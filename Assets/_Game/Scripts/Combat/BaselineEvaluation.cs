using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Unity.InferenceEngine;
using Unity.MLAgents;
using UnityEngine;

[RequireComponent(typeof(ArenaRound))]
public sealed class BaselineEvaluation : MonoBehaviour
{
    private const string CheckpointPath = "Assets/_Game/LocalEvaluation/ArenaDuel.onnx";
    private const int DecisionPeriod = 5;
    [SerializeField] private MeleeFighter policyFighter;
    [SerializeField] private MeleeFighter baselineFighter;
    [SerializeField] private SpriteRenderer floor;
    [SerializeField, Min(1)] private int pairs = 50;
    [SerializeField] private int seed = 2027;
    [SerializeField, Range(1f, 20f)] private float simulationSpeed = 10f;

    private ArenaRound round;
    private Academy academy;
    private SelfPlayFighterAgent agent;
    private Rigidbody2D policyBody;
    private Rigidbody2D baselineBody;
    private System.Random random;
    private StreamWriter results;
    private string outputPath;
    private string checkpointHash;
    private float originalTimeScale;
    private int ticksUntilDecision;
    private int matches;
    private int wins;
    private int losses;
    private int draws;
    private float policyY;
    private float baselineY;

    private void Start()
    {
        round = GetComponent<ArenaRound>();
        string checkpointPath = CheckpointPath;
        ModelAsset model = null;
#if UNITY_EDITOR
        if (ArenaTrainingPlayMode.IsDashboardEvaluation)
            checkpointPath = ArenaTrainingPlayMode.EvaluationModel;
        model = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(checkpointPath);
#endif
        if (model == null)
        {
            round.enabled = false;
            Debug.LogError($"Open this scene in the Unity Editor and import the checkpoint at {checkpointPath}.");
            ExitDashboardPlayMode();
            return;
        }

        academy = Academy.Instance;
        if (academy.IsCommunicatorOn)
        {
            round.enabled = false;
            Debug.LogError("Stop mlagents-learn before evaluating a frozen checkpoint.");
            ExitDashboardPlayMode();
            return;
        }

        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", "baseline-evaluations"));
        string checkpointFile = Path.Combine(Application.dataPath, "..", checkpointPath);
        using (var sha = SHA256.Create())
            checkpointHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(checkpointFile))).Replace("-", "");

        Directory.CreateDirectory(directory);
        outputPath = Path.Combine(directory, $"baseline-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-seed{seed}.csv");
        results = new StreamWriter(outputPath) { AutoFlush = true };
        results.WriteLine("match,pair,seed,checkpoint_sha256,policy_side,policy_start_y,baseline_start_y,outcome");
        policyBody = policyFighter.GetComponent<Rigidbody2D>();
        baselineBody = baselineFighter.GetComponent<Rigidbody2D>();
        random = new System.Random(seed);
        agent = SelfPlayFighterAgent.Attach(policyFighter, baselineFighter, round, floor.bounds, 0, model);
        academy.AgentPreStep += RequestDecision;
        round.RoundEnded += RecordMatch;
        originalTimeScale = Time.timeScale;
        Time.timeScale = simulationSpeed;
        PrepareMatch();
        Debug.Log($"Evaluating {model.name} against Baseline Bot for {2 * pairs} matches. CSV: {outputPath}");
    }

    private static void ExitDashboardPlayMode()
    {
#if UNITY_EDITOR
        if (ArenaTrainingPlayMode.IsDashboardEvaluation)
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
    }

    private void RequestDecision(int step)
    {
        if (ticksUntilDecision > 0)
        {
            ticksUntilDecision--;
            return;
        }

        agent.RequestDecision();
        ticksUntilDecision = DecisionPeriod - 1;
    }

    private void PrepareMatch()
    {
        if (matches % 2 == 0)
        {
            policyY = NextStartY();
            baselineY = NextStartY();
        }

        round.ResetRound();
        float side = matches % 2 == 0 ? -1f : 1f;
        policyBody.position = new Vector2(side * 3.4f, policyY);
        baselineBody.position = new Vector2(-side * 3.4f, baselineY);
        policyFighter.Face(baselineBody.position - policyBody.position);
        baselineFighter.Face(policyBody.position - baselineBody.position);
        ticksUntilDecision = 0;
    }

    private float NextStartY() => (float)(random.NextDouble() * 3.6 - 1.8);

    private void RecordMatch(ArenaRound.Outcome outcome)
    {
        if (outcome == ArenaRound.Outcome.PlayerWin) wins++;
        else if (outcome == ArenaRound.Outcome.OpponentWin) losses++;
        else draws++;

        string side = matches % 2 == 0 ? "left" : "right";
        results.WriteLine(FormattableString.Invariant(
            $"{matches + 1},{matches / 2 + 1},{seed},{checkpointHash},{side},{policyY:F6},{baselineY:F6},{outcome}"));
        matches++;
        agent.EndEpisode();

        if (matches < 2 * pairs)
        {
            PrepareMatch();
            return;
        }

        float winRate = (float)wins / matches;
        Debug.Log($"Baseline evaluation complete. {wins} wins, {losses} losses, {draws} draws. "
            + $"Win rate {winRate.ToString("P1", CultureInfo.InvariantCulture)}. CSV: {outputPath}");
        StopEvaluation();
#if UNITY_EDITOR
        if (ArenaTrainingPlayMode.IsDashboardEvaluation)
            ArenaTrainingPlayMode.FinishDashboardEvaluation();
#endif
    }

    private void StopEvaluation()
    {
        if (results == null)
            return;

        academy.AgentPreStep -= RequestDecision;
        round.RoundEnded -= RecordMatch;
        Time.timeScale = originalTimeScale;
        results.Dispose();
        results = null;
    }

    private void OnDestroy() => StopEvaluation();
}

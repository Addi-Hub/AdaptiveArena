using Unity.MLAgents;
using UnityEngine;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(ArenaRound))]
public sealed class SelfPlayArena : MonoBehaviour
{
    [SerializeField] private MeleeFighter first;
    [SerializeField] private MeleeFighter second;
    [SerializeField] private SpriteRenderer floor;

    private const int DecisionPeriod = 5;
    private const int MaxArenas = 16;
    private const float ArenaSpacing = 16f;
    private const float CloseStartDistance = 1.4f;
    private static bool cloningArenas;
    private Academy academy;
    private ArenaRound round;
    private SelfPlayFighterAgent firstAgent;
    private SelfPlayFighterAgent secondAgent;
    private Rigidbody2D firstBody;
    private Rigidbody2D secondBody;
    private const float FacingRewardCap = 0.05f;
    private const float FacingDotThreshold = 0.8660254f; // cos(30 degrees)
    private float firstFacingReward;
    private float secondFacingReward;
    private bool hasRequestedDecision;
    private int completedRounds;
    private bool closeStart;
    private int ticksUntilDecision;
    private int lightAttempts;
    private int heavyAttempts;
    private int lightHits;
    private int heavyHits;

    private void Awake()
    {
        if (cloningArenas)
            return;

        var sharedAcademy = Academy.Instance;
        if (!sharedAcademy.IsCommunicatorOn)
            return;

        int arenaCount = Mathf.Clamp(sharedAcademy.NumAreas, 1, MaxArenas);
        Transform arenaRoot = floor.transform.parent;
        // Clone before the fighters and arena dressing create their runtime visuals.
        cloningArenas = true;
        try
        {
            for (int index = 1; index < arenaCount; index++)
            {
                Vector3 offset = new Vector3(index % 4, index / 4, 0f) * ArenaSpacing;
                var copy = Instantiate(arenaRoot, arenaRoot.position + offset, arenaRoot.rotation);
                copy.name = $"Arena {index + 1}";
            }
        }
        finally
        {
            cloningArenas = false;
        }
        Debug.Log($"Self-play ready: {arenaCount} parallel arena(s).");
    }

    private void Start()
    {
        round = GetComponent<ArenaRound>();
        academy = Academy.Instance;
        if (!academy.IsCommunicatorOn)
        {
            round.enabled = false;
            Debug.LogWarning("Start mlagents-learn before entering Play Mode in SelfPlayArena.");
            return;
        }

        bool observeReach = false;
        bool observeAim = false;
#if UNITY_EDITOR
        observeReach = ArenaTrainingPlayMode.ObserveReach;
        observeAim = ArenaTrainingPlayMode.ObserveAim;
#endif
        firstAgent = SelfPlayFighterAgent.Attach(first, second, round, floor.bounds, 0, observeReach: observeReach, observeAim: observeAim);
        secondAgent = SelfPlayFighterAgent.Attach(second, first, round, floor.bounds, 1, observeReach: observeReach, observeAim: observeAim);
        firstBody = first.GetComponent<Rigidbody2D>();
        secondBody = second.GetComponent<Rigidbody2D>();
        academy.AgentPreStep += RequestDecisions;
        academy.OnEnvironmentReset += ResetRound;
        round.RoundEnded += FinishRound;
        first.AttackStarted += CountAttempt;
        second.AttackStarted += CountAttempt;
        first.HitLanded += RewardFirstHit;
        second.HitLanded += RewardSecondHit;
    }

    private void RequestDecisions(int step)
    {
        if (ticksUntilDecision > 0)
        {
            ticksUntilDecision--;
            return;
        }

        float facingReward = academy.EnvironmentParameters.GetWithDefault("reward_face_opponent", 0f);
        if (hasRequestedDecision && facingReward > 0f)
        {
            Vector2 offset = secondBody.position - firstBody.position;
            RewardFacing(first, firstAgent, offset, facingReward, ref firstFacingReward);
            RewardFacing(second, secondAgent, -offset, facingReward, ref secondFacingReward);
        }

        hasRequestedDecision = true;
        firstAgent.RequestDecision();
        secondAgent.RequestDecision();
        ticksUntilDecision = DecisionPeriod - 1;
    }

    private static void RewardFacing(MeleeFighter fighter, SelfPlayFighterAgent agent,
        Vector2 toOpponent, float reward, ref float earned)
    {
        float reach = fighter.MaxAttackReach;
        if (earned >= FacingRewardCap || toOpponent.sqrMagnitude > reach * reach
            || Vector2.Dot(fighter.FacingDirection, toOpponent.normalized) < FacingDotThreshold)
            return;

        float bonus = Mathf.Min(reward, FacingRewardCap - earned);
        agent.AddReward(bonus);
        earned += bonus;
    }

    private void CountAttempt(bool isHeavy)
    {
        if (isHeavy) heavyAttempts++;
        else lightAttempts++;
    }

    private void RewardFirstHit(bool isHeavy, int damage) => RewardHit(isHeavy, damage, firstAgent, secondAgent);

    private void RewardSecondHit(bool isHeavy, int damage) => RewardHit(isHeavy, damage, secondAgent, firstAgent);

    private void RewardHit(bool isHeavy, int damage, SelfPlayFighterAgent attacker, SelfPlayFighterAgent defender)
    {
        if (isHeavy) heavyHits++;
        else lightHits++;

        var rewards = academy.EnvironmentParameters;
        attacker.AddReward(damage * rewards.GetWithDefault("reward_damage_dealt", 0.003f));
        defender.AddReward(damage * rewards.GetWithDefault("reward_damage_taken", -0.003f));
    }

    private void FinishRound(ArenaRound.Outcome outcome)
    {
        var rewards = academy.EnvironmentParameters;
        float win = rewards.GetWithDefault("reward_win", 1f);
        float loss = rewards.GetWithDefault("reward_loss", -1f);
        float draw = rewards.GetWithDefault("reward_draw", 0f);
        (float firstReward, float secondReward) = outcome switch
        {
            ArenaRound.Outcome.PlayerWin => (win, loss),
            ArenaRound.Outcome.OpponentWin => (loss, win),
            _ => (draw, draw)
        };
        var stats = academy.StatsRecorder;
        if (rewards.GetWithDefault("reward_face_opponent", 0f) > 0f)
            stats.Add("Arena/Facing reward per fighter per round", (firstFacingReward + secondFacingReward) * 0.5f);
        stats.Add("Arena/Light attempts per round", lightAttempts);
        stats.Add("Arena/Heavy attempts per round", heavyAttempts);
        stats.Add("Arena/Light hits per round", lightHits);
        stats.Add("Arena/Heavy hits per round", heavyHits);
        float knockout = outcome == ArenaRound.Outcome.Timeout ? 0f : 1f;
        stats.Add("Arena/Knockout rate", knockout);
        string start = closeStart ? "Close starts" : "Normal starts";
        stats.Add($"Arena/{start}/Hits per round", lightHits + heavyHits);
        stats.Add($"Arena/{start}/Knockout rate", knockout);
        firstAgent.AddReward(firstReward);
        secondAgent.AddReward(secondReward);
        firstAgent.EndEpisode();
        secondAgent.EndEpisode();
        completedRounds++;
        ResetRound();
    }

    private void ResetRound()
    {
        lightAttempts = heavyAttempts = lightHits = heavyHits = 0;
        ticksUntilDecision = 0;
        firstFacingReward = secondFacingReward = 0f;
        hasRequestedDecision = false;
        round.ResetRound();
        closeStart = academy.EnvironmentParameters.GetWithDefault("alternate_close_starts", 0f) > 0.5f
            && completedRounds % 2 == 1;
        if (closeStart)
        {
            Vector2 midpoint = (firstBody.position + secondBody.position) * 0.5f;
            Vector2 halfSpacing = Vector2.right * (CloseStartDistance * 0.5f);
            firstBody.position = midpoint - halfSpacing;
            secondBody.position = midpoint + halfSpacing;
        }

        // Each team gets both distances on each side over four rounds.
        bool swapSides = academy.EnvironmentParameters.GetWithDefault("alternate_start_sides", 0f) > 0.5f
            && completedRounds % 4 >= 2;
        if (swapSides)
        {
            (firstBody.position, secondBody.position) = (secondBody.position, firstBody.position);
            first.Face(secondBody.position - firstBody.position);
            second.Face(firstBody.position - secondBody.position);
        }
    }

    private void OnDestroy()
    {
        if (academy == null)
            return;

        academy.AgentPreStep -= RequestDecisions;
        academy.OnEnvironmentReset -= ResetRound;
        round.RoundEnded -= FinishRound;
        first.AttackStarted -= CountAttempt;
        second.AttackStarted -= CountAttempt;
        first.HitLanded -= RewardFirstHit;
        second.HitLanded -= RewardSecondHit;
    }
}

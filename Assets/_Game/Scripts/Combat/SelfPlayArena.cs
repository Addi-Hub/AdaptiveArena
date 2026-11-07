using Unity.MLAgents;
using UnityEngine;

[RequireComponent(typeof(ArenaRound))]
public sealed class SelfPlayArena : MonoBehaviour
{
    [SerializeField] private MeleeFighter first;
    [SerializeField] private MeleeFighter second;
    [SerializeField] private SpriteRenderer floor;

    private const int DecisionPeriod = 5;
    private const float CloseStartDistance = 1.4f;
    private Academy academy;
    private ArenaRound round;
    private SelfPlayFighterAgent firstAgent;
    private SelfPlayFighterAgent secondAgent;
    private Rigidbody2D firstBody;
    private Rigidbody2D secondBody;
    private int completedRounds;
    private bool closeStart;
    private int ticksUntilDecision;
    private int lightAttempts;
    private int heavyAttempts;
    private int lightHits;
    private int heavyHits;

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

        firstAgent = SelfPlayFighterAgent.Attach(first, second, round, floor.bounds, 0);
        secondAgent = SelfPlayFighterAgent.Attach(second, first, round, floor.bounds, 1);
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

        firstAgent.RequestDecision();
        secondAgent.RequestDecision();
        ticksUntilDecision = DecisionPeriod - 1;
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

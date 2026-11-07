using Unity.MLAgents;
using UnityEngine;

[RequireComponent(typeof(ArenaRound))]
public sealed class SelfPlayArena : MonoBehaviour
{
    [SerializeField] private MeleeFighter first;
    [SerializeField] private MeleeFighter second;
    [SerializeField] private SpriteRenderer floor;

    private const int DecisionPeriod = 5;
    private const float KnockoutReward = 200f;
    private const float DrawReward = -100f;
    private const float HitReward = 1f;
    private Academy academy;
    private ArenaRound round;
    private SelfPlayFighterAgent firstAgent;
    private SelfPlayFighterAgent secondAgent;
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

    private void RewardFirstHit(bool isHeavy) => RewardHit(isHeavy, firstAgent, secondAgent);

    private void RewardSecondHit(bool isHeavy) => RewardHit(isHeavy, secondAgent, firstAgent);

    private void RewardHit(bool isHeavy, SelfPlayFighterAgent attacker, SelfPlayFighterAgent defender)
    {
        if (isHeavy) heavyHits++;
        else lightHits++;

        attacker.AddReward(HitReward);
        defender.AddReward(-HitReward);
    }

    private void FinishRound(ArenaRound.Outcome outcome)
    {
        (float firstReward, float secondReward) = outcome switch
        {
            ArenaRound.Outcome.PlayerWin => (KnockoutReward, -KnockoutReward),
            ArenaRound.Outcome.OpponentWin => (-KnockoutReward, KnockoutReward),
            _ => (DrawReward, DrawReward)
        };
        var stats = academy.StatsRecorder;
        stats.Add("Arena/Light attempts per round", lightAttempts);
        stats.Add("Arena/Heavy attempts per round", heavyAttempts);
        stats.Add("Arena/Light hits per round", lightHits);
        stats.Add("Arena/Heavy hits per round", heavyHits);
        stats.Add("Arena/Knockout rate", outcome == ArenaRound.Outcome.Timeout ? 0f : 1f);
        firstAgent.AddReward(firstReward);
        secondAgent.AddReward(secondReward);
        firstAgent.EndEpisode();
        secondAgent.EndEpisode();
        ResetRound();
    }

    private void ResetRound()
    {
        lightAttempts = heavyAttempts = lightHits = heavyHits = 0;
        ticksUntilDecision = 0;
        round.ResetRound();
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

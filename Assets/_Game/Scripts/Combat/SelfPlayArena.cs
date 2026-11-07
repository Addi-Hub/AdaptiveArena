using Unity.MLAgents;
using UnityEngine;

[RequireComponent(typeof(ArenaRound))]
public sealed class SelfPlayArena : MonoBehaviour
{
    [SerializeField] private MeleeFighter first;
    [SerializeField] private MeleeFighter second;
    [SerializeField] private SpriteRenderer floor;

    private const int DecisionPeriod = 5;
    private Academy academy;
    private ArenaRound round;
    private SelfPlayFighterAgent firstAgent;
    private SelfPlayFighterAgent secondAgent;
    private int ticksUntilDecision;

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

    private void FinishRound(ArenaRound.Outcome outcome)
    {
        float reward = outcome switch
        {
            ArenaRound.Outcome.PlayerWin => 1f,
            ArenaRound.Outcome.OpponentWin => -1f,
            _ => 0f
        };
        firstAgent.AddReward(reward);
        secondAgent.AddReward(-reward);
        firstAgent.EndEpisode();
        secondAgent.EndEpisode();
        ResetRound();
    }

    private void ResetRound()
    {
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
    }
}

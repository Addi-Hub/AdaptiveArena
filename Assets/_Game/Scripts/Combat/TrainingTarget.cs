using UnityEngine;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(MeleeFighter))]
public sealed class TrainingTarget : MonoBehaviour
{
    [SerializeField] private Rigidbody2D player;
    [SerializeField, Min(0.1f)] private float attackInterval = 3f;
    [SerializeField] private bool forceBaseline;
    [SerializeField] private SpriteRenderer arenaFloor;

    private MeleeFighter fighter;
    private MeleeFighter playerFighter;
    private Rigidbody2D body;
    private ArenaRound round;
    private float nextAttackAt;
    private bool baselineBot;
    private bool sparringDodges;
    private bool nextAttackIsHeavy;
    private SelfPlayFighterAgent trainedAgent;
    private int ticksUntilDecision;
    public bool IsBaseline => baselineBot;

    private void Awake()
    {
        fighter = GetComponent<MeleeFighter>();
        playerFighter = player.GetComponent<MeleeFighter>();
        round = player.GetComponent<ArenaRound>();
        body = GetComponent<Rigidbody2D>();
        baselineBot = forceBaseline || ArenaMainMenu.UseBaselineBot;
        ResetTactics();
    }

    private void Start()
    {
        if (arenaFloor != null && ArenaMainMenu.TrainedOpponent != null)
        {
            trainedAgent = SelfPlayFighterAgent.Attach(fighter, playerFighter, round,
                arenaFloor.bounds, 1, ArenaMainMenu.TrainedOpponent);
        }
    }

    private void OnEnable() => round.RoundReset += ResetTactics;
    private void OnDisable() => round.RoundReset -= ResetTactics;

    public void EnableTrainingOpponent(bool canDodge, bool useBaseline)
    {
        baselineBot = useBaseline;
        sparringDodges = canDodge;
        ResetTactics();
        enabled = true;
    }

    private void ResetTactics()
    {
        ticksUntilDecision = 0;
        trainedAgent?.EndEpisode();
        nextAttackAt = 0f;
        nextAttackIsHeavy = !baselineBot;
    }

    private void FixedUpdate()
    {
        if (trainedAgent != null)
        {
            if (round.Result == ArenaRound.Outcome.InProgress && ticksUntilDecision-- <= 0)
            {
                trainedAgent.RequestDecision();
                ticksUntilDecision = SelfPlayFighterAgent.DecisionPeriod - 1;
            }
            return;
        }

        Vector2 direction = player.position - body.position;
        fighter.Face(direction);

        if ((baselineBot || sparringDodges) && !fighter.IsHealing
            && playerFighter.IsAboutToHit(body.position, 0.12f))
            fighter.TryDodge(-direction);

        if (baselineBot)
        {
            bool wantsHeal = fighter.Health <= fighter.MaxHealth * 0.5f && fighter.HealCharges > 0;
            if (wantsHeal && direction.sqrMagnitude > playerFighter.MaxAttackReach * playerFighter.MaxAttackReach)
                fighter.TryHeal();

            if (fighter.IsHealing || (wantsHeal && !fighter.IsWindingUp))
            {
                body.linearVelocity = fighter.GetMovementVelocity(-direction.normalized);
                return;
            }
        }

        float reach = fighter.GetAttackReach(nextAttackIsHeavy);
        float stopDistance = reach * 0.85f;
        bool shouldApproach = !fighter.IsWindingUp && direction.sqrMagnitude > stopDistance * stopDistance;
        body.linearVelocity = fighter.GetMovementVelocity(
            shouldApproach ? direction.normalized : Vector2.zero);

        if (Time.fixedTime >= nextAttackAt
            && direction.sqrMagnitude <= reach * reach
            && fighter.TryAttack(nextAttackIsHeavy, direction))
        {
            body.linearVelocity = Vector2.zero;
            nextAttackAt = Time.fixedTime + (baselineBot ? 1.35f : attackInterval);
            if (baselineBot)
                nextAttackIsHeavy = !nextAttackIsHeavy;
        }
    }
}

using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

public sealed class SelfPlayFighterAgent : Agent
{
    private const int ObservationSize = 25;
    private bool observeReach;
    private bool observeAim;

    private MeleeFighter fighter;
    private MeleeFighter opponent;
    private Rigidbody2D body;
    private Rigidbody2D opponentBody;
    private ArenaRound round;
    private Bounds arenaBounds;
    private Vector2 movement;

    public static SelfPlayFighterAgent Attach(MeleeFighter fighter, MeleeFighter opponent,
        ArenaRound round, Bounds bounds, int team, ModelAsset model = null, bool observeReach = false, bool observeAim = false)
    {
        if (model != null)
        {
            foreach (var input in ModelLoader.Load(model).inputs)
            {
                if (input.name == "obs_0" && input.shape.rank == 2)
                {
                    int extraInputs = input.shape.Get(1) - ObservationSize;
                    observeReach = extraInputs == 1 || extraInputs == 3;
                    observeAim = extraInputs == 2 || extraInputs == 3;
                    break;
                }
            }
        }

        var parameters = fighter.gameObject.AddComponent<BehaviorParameters>();
        parameters.BehaviorName = "ArenaDuel";
        parameters.TeamId = team;
        parameters.BrainParameters.VectorObservationSize = ObservationSize + (observeReach ? 1 : 0) + (observeAim ? 2 : 0);
        parameters.BrainParameters.ActionSpec = new ActionSpec(4, new[] { 5 });
        if (model != null)
        {
            parameters.Model = model;
            parameters.BehaviorType = BehaviorType.InferenceOnly;
            parameters.DeterministicInference = true;
        }

        var agent = fighter.gameObject.AddComponent<SelfPlayFighterAgent>();
        agent.observeReach = observeReach;
        agent.observeAim = observeAim;
        agent.Configure(opponent, round, bounds);
        return agent;
    }

    private void Configure(MeleeFighter other, ArenaRound arenaRound, Bounds bounds)
    {
        fighter = GetComponent<MeleeFighter>();
        body = GetComponent<Rigidbody2D>();
        opponent = other;
        opponentBody = other.GetComponent<Rigidbody2D>();
        round = arenaRound;
        arenaBounds = bounds;
    }

    public override void OnEpisodeBegin() => movement = Vector2.zero;

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(Vector2.Scale(body.position - (Vector2)arenaBounds.center,
            new Vector2(1f / arenaBounds.extents.x, 1f / arenaBounds.extents.y)));
        sensor.AddObservation(Vector2.Scale(opponentBody.position - body.position,
            new Vector2(1f / arenaBounds.size.x, 1f / arenaBounds.size.y)));
        sensor.AddObservation(Vector2.ClampMagnitude(body.linearVelocity / 12f, 1f));
        sensor.AddObservation(Vector2.ClampMagnitude(opponentBody.linearVelocity / 12f, 1f));
        ObserveFighter(sensor, fighter);
        ObserveFighter(sensor, opponent);
        sensor.AddObservation(round.TimeFractionRemaining);
        if (observeReach)
            sensor.AddObservation(Vector2.Distance(body.position, opponentBody.position)
                / Mathf.Max(0.01f, fighter.MaxAttackReach));
        if (observeAim)
        {
            Vector2 toOpponent = opponentBody.position - body.position;
            float angle = Vector2.SignedAngle(fighter.FacingDirection, toOpponent) * Mathf.Deg2Rad;
            sensor.AddObservation(Mathf.Sin(angle));
            sensor.AddObservation(Mathf.Cos(angle));
        }
    }

    private static void ObserveFighter(VectorSensor sensor, MeleeFighter target)
    {
        sensor.AddObservation(target.FacingDirection);
        sensor.AddObservation((float)target.Health / target.MaxHealth);
        sensor.AddObservation(target.HealCharges / 2f);
        sensor.AddObservation(target.IsWindingUp);
        sensor.AddObservation(target.IsHealing);
        sensor.AddObservation(target.IsDodging);
        sensor.AddObservation(target.IsDodgeReady);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (round.Result != ArenaRound.Outcome.InProgress)
            return;

        var continuous = actions.ContinuousActions;
        movement = Vector2.ClampMagnitude(new Vector2(continuous[0], continuous[1]), 1f);
        fighter.Face(new Vector2(continuous[2], continuous[3]));
        switch (actions.DiscreteActions[0])
        {
            case 1: fighter.TryAttack(false, fighter.FacingDirection); break;
            case 2: fighter.TryAttack(true, fighter.FacingDirection); break;
            case 3:
                fighter.TryDodge(movement.sqrMagnitude > 0.0001f ? movement : fighter.FacingDirection);
                break;
            case 4: fighter.TryHeal(); break;
        }
    }

    private void FixedUpdate() => body.linearVelocity = fighter.GetMovementVelocity(movement);
}

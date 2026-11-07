using UnityEngine;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(MeleeFighter))]
public sealed class TrainingTarget : MonoBehaviour
{
    [SerializeField] private Rigidbody2D player;
    [SerializeField, Min(0.1f)] private float attackInterval = 3f;

    private MeleeFighter fighter;
    private MeleeFighter playerFighter;
    private Rigidbody2D body;
    private float nextAttackAt;
    private bool baselineBot;
    private bool nextAttackIsHeavy;

    private void Awake()
    {
        fighter = GetComponent<MeleeFighter>();
        playerFighter = player.GetComponent<MeleeFighter>();
        body = GetComponent<Rigidbody2D>();
        baselineBot = ArenaMainMenu.UseBaselineBot;
        nextAttackIsHeavy = !baselineBot;
    }

    private void FixedUpdate()
    {
        Vector2 direction = player.position - body.position;
        fighter.Face(direction);

        if (baselineBot)
        {
            if (!fighter.IsHealing && playerFighter.IsAboutToHit(body.position, 0.12f))
                fighter.TryDodge(-direction);

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

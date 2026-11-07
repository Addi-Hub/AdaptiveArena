using UnityEngine;

[RequireComponent(typeof(MeleeFighter))]
public sealed class TrainingTarget : MonoBehaviour
{
    [SerializeField] private Rigidbody2D player;
    [SerializeField, Min(0.1f)] private float attackInterval = 3f;
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;

    private MeleeFighter fighter;
    private Rigidbody2D body;
    private float nextAttackAt;

    private void Awake()
    {
        fighter = GetComponent<MeleeFighter>();
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        Vector2 direction = player.position - body.position;
        fighter.Face(direction);
        float stopDistance = fighter.HeavyReach * 0.85f;
        bool shouldApproach = !fighter.IsWindingUp && direction.sqrMagnitude > stopDistance * stopDistance;
        body.linearVelocity = fighter.GetMovementVelocity(
            shouldApproach ? direction.normalized : Vector2.zero, moveSpeed);

        if (Time.fixedTime >= nextAttackAt
            && direction.sqrMagnitude <= fighter.HeavyReach * fighter.HeavyReach
            && fighter.TryAttack(isHeavy: true, direction: direction))
        {
            body.linearVelocity = Vector2.zero;
            nextAttackAt = Time.fixedTime + attackInterval;
        }
    }
}

using UnityEngine;

[RequireComponent(typeof(MeleeFighter))]
public sealed class TrainingTarget : MonoBehaviour
{
    [SerializeField] private Rigidbody2D player;
    [SerializeField, Min(0.1f)] private float attackInterval = 3f;

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
        if (Time.fixedTime < nextAttackAt)
            return;

        Vector2 direction = player.position - body.position;
        if (direction.sqrMagnitude <= fighter.HeavyReach * fighter.HeavyReach
            && fighter.TryAttack(isHeavy: true, direction: direction))
            nextAttackAt = Time.fixedTime + attackInterval;
    }
}

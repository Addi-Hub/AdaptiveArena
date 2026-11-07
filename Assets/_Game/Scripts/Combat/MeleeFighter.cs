using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public sealed class MeleeFighter : MonoBehaviour
{
    [System.Serializable]
    private struct Attack
    {
        [Min(1)] public int damage;
        [Min(0.1f)] public float reach;
        [Range(1f, 180f)] public float coneAngle;
        [Min(0.02f)] public float windup;
        [Min(0f)] public float recovery;
    }

    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private Attack light = new Attack
    {
        damage = 15, reach = 1.75f, coneAngle = 55f, windup = 0.15f, recovery = 0.25f
    };
    [SerializeField] private Attack heavy = new Attack
    {
        damage = 30, reach = 1.75f, coneAngle = 110f, windup = 0.45f, recovery = 0.55f
    };
    [SerializeField] private MeleeFighter opponent;
    [SerializeField, Min(0.02f)] private float dodgeDuration = 0.2f;
    [SerializeField, Min(0f)] private float dodgeCooldown = 1f;
    [SerializeField, Min(0f)] private float dodgeSpeed = 12f;

    private const int HealAmount = 50;
    private const int MaxHealCharges = 2;
    private const float HealDuration = 1.25f;
    private const float HealSpeedMultiplier = 0.5f;
    private const float MoveSpeed = 5f;
    private const float KnockbackDuration = 0.18f;

    private Rigidbody2D body;
    private FighterEffects effects;
    private Attack activeAttack;
    private float windupRemaining;
    private float recoveryRemaining;
    private float dodgeEndsAt;
    private float dodgeReadyAt;
    private Vector2 dodgeDirection;
    private float healRemaining;
    private bool roundEnded;
    private Vector2 knockbackVelocity;
    private float knockbackEndsAt;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public int HealCharges { get; private set; } = MaxHealCharges;
    public bool IsAlive => Health > 0;
    public bool IsWindingUp => windupRemaining > 0f;
    public bool IsDodging => Time.time < dodgeEndsAt;
    public bool IsHealing => healRemaining > 0f;
    public bool IsDodgeReady => !roundEnded && Time.time >= dodgeReadyAt;
    public float GetAttackReach(bool isHeavy) => isHeavy ? heavy.reach : light.reach;
    public float MaxAttackReach => Mathf.Max(light.reach, heavy.reach);
    public Vector2 FacingDirection { get; private set; } = Vector2.right;

    public void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
            FacingDirection = direction.normalized;
    }

    private bool CanAct => !roundEnded && IsAlive && opponent.IsAlive && !IsWindingUp
        && recoveryRemaining <= 0f && !IsDodging && !IsHealing;

    private void Awake()
    {
        Health = maxHealth;
        body = GetComponent<Rigidbody2D>();
        effects = gameObject.AddComponent<FighterEffects>();
        effects.Initialize(GetComponent<SpriteRenderer>());
    }

    public bool TryAttack(bool isHeavy, Vector2 direction)
    {
        if (!CanAct || direction.sqrMagnitude < 0.0001f)
            return false;

        activeAttack = isHeavy ? heavy : light;
        Face(direction);
        windupRemaining = activeAttack.windup;
        effects.WindUp(activeAttack.reach, activeAttack.coneAngle,
            activeAttack.windup, isHeavy);
        return true;
    }

    public bool TryDodge(Vector2 direction)
    {
        if (!IsAlive || !opponent.IsAlive || !IsDodgeReady || IsDodging
            || direction.sqrMagnitude < 0.0001f)
            return false;

        windupRemaining = 0f;
        recoveryRemaining = 0f;
        healRemaining = 0f;
        effects.CancelAttack();
        dodgeDirection = direction.normalized;
        knockbackEndsAt = 0f;
        dodgeEndsAt = Time.time + dodgeDuration;
        dodgeReadyAt = dodgeEndsAt + dodgeCooldown;
        return true;
    }

    public bool TryHeal()
    {
        if (!CanAct || HealCharges == 0 || Health == maxHealth)
            return false;

        HealCharges--;
        healRemaining = HealDuration;
        return true;
    }

    public bool IsAboutToHit(Vector2 position, float reactionWindow)
    {
        return IsWindingUp && windupRemaining <= reactionWindow
            && IsInsideAttack(position - body.position);
    }

    public Vector2 GetMovementVelocity(Vector2 input)
    {
        if (Time.time < knockbackEndsAt)
            return knockbackVelocity * Mathf.Clamp01((knockbackEndsAt - Time.time) / KnockbackDuration);
        if (roundEnded || !IsAlive)
            return Vector2.zero;
        if (IsDodging)
            return dodgeDirection * dodgeSpeed;

        float speed = IsHealing ? MoveSpeed * HealSpeedMultiplier : MoveSpeed;
        return Vector2.ClampMagnitude(input, 1f) * speed;
    }

    private void FixedUpdate()
    {
        if (roundEnded)
            return;

        if (IsHealing)
        {
            healRemaining = Mathf.Max(0f, healRemaining - Time.fixedDeltaTime);
            if (!IsHealing)
            {
                Health = Mathf.Min(maxHealth, Health + HealAmount);
                effects.HealComplete();
            }
        }

        if (!IsWindingUp)
        {
            recoveryRemaining = Mathf.Max(0f, recoveryRemaining - Time.fixedDeltaTime);
            return;
        }

        windupRemaining = Mathf.Max(0f, windupRemaining - Time.fixedDeltaTime);
        if (IsWindingUp)
            return;

        effects.Swing();
        recoveryRemaining = activeAttack.recovery;
        Vector2 offset = opponent.body.position - body.position;
        if (opponent.IsAlive && IsInsideAttack(offset))
        {
            Vector2 pushDirection = offset.sqrMagnitude > 0.0001f ? offset.normalized : FacingDirection;
            opponent.TakeDamage(activeAttack.damage, pushDirection);
        }
    }

    private bool IsInsideAttack(Vector2 offset)
    {
        return offset.sqrMagnitude <= activeAttack.reach * activeAttack.reach
            && Vector2.Angle(FacingDirection, offset) <= activeAttack.coneAngle * 0.5f;
    }

    public void EndRound()
    {
        roundEnded = true;
        windupRemaining = 0f;
        healRemaining = 0f;
        dodgeEndsAt = 0f;
        effects.CancelWindup();
        body.linearVelocity = Vector2.zero;
    }

    private void TakeDamage(int damage, Vector2 pushDirection)
    {
        if (IsDodging)
            return;

        Health = Mathf.Max(0, Health - damage);
        knockbackVelocity = pushDirection * (4f + damage * 0.12f);
        knockbackEndsAt = Time.time + KnockbackDuration;
        effects.Hit();
        if (IsAlive)
            return;

        windupRemaining = 0f;
        healRemaining = 0f;
        effects.CancelWindup();
        body.linearVelocity = Vector2.zero;
    }
}

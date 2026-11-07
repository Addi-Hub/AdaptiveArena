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
    private Vector2 spawnPosition;
    private float spawnRotation;
    private FighterEffects effects;
    private Attack activeAttack;
    private bool activeAttackIsHeavy;
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
    public event System.Action<bool> AttackStarted;
    public enum WindupCancellation { Dodge, Knockout, RoundEnd }
    public event System.Action AttackRejected;
    public event System.Action<WindupCancellation> WindupCancelled;
    public event System.Action<bool, int> HitLanded;
    public event System.Action<bool, float, float, float, float, string, int> SwingResolved;

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
        spawnPosition = body.position;
        spawnRotation = body.rotation;
        effects = gameObject.AddComponent<FighterEffects>();
        effects.Initialize(GetComponent<SpriteRenderer>());
    }

    public bool TryAttack(bool isHeavy, Vector2 direction)
    {
        if (!CanAct || direction.sqrMagnitude < 0.0001f)
        {
            AttackRejected?.Invoke();
            return false;
        }

        activeAttack = isHeavy ? heavy : light;
        activeAttackIsHeavy = isHeavy;
        Face(direction);
        windupRemaining = activeAttack.windup;
        effects.WindUp(activeAttack.reach, activeAttack.coneAngle,
            activeAttack.windup, isHeavy);
        AttackStarted?.Invoke(isHeavy);
        return true;
    }

    public bool TryDodge(Vector2 direction)
    {
        if (!IsAlive || !opponent.IsAlive || !IsDodgeReady || IsDodging
            || direction.sqrMagnitude < 0.0001f)
            return false;

        CancelWindup(WindupCancellation.Dodge);
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
        bool inRange = offset.sqrMagnitude <= activeAttack.reach * activeAttack.reach;
        float angle = inRange || SwingResolved != null ? Vector2.Angle(FacingDirection, offset) : 0f;
        float halfCone = activeAttack.coneAngle * 0.5f;
        bool inCone = angle <= halfCone;
        bool targetAlive = opponent.IsAlive;
        int damageDealt = 0;
        if (targetAlive && inRange && inCone)
        {
            Vector2 pushDirection = offset.sqrMagnitude > 0.0001f ? offset.normalized : FacingDirection;
            damageDealt = opponent.TakeDamage(activeAttack.damage, pushDirection);
            if (damageDealt > 0)
                HitLanded?.Invoke(activeAttackIsHeavy, damageDealt);
        }

        if (SwingResolved != null)
        {
            string outcome = (targetAlive, inRange, inCone, damageDealt) switch
            {
                (false, _, _, _) => "target_defeated",
                (_, false, false, _) => "range_and_aim",
                (_, false, _, _) => "out_of_range",
                (_, _, false, _) => "outside_cone",
                (_, _, _, 0) => "dodged",
                _ => "hit"
            };
            SwingResolved.Invoke(activeAttackIsHeavy, offset.magnitude, activeAttack.reach,
                angle, halfCone, outcome, damageDealt);
        }
    }

    private bool IsInsideAttack(Vector2 offset)
    {
        return offset.sqrMagnitude <= activeAttack.reach * activeAttack.reach
            && Vector2.Angle(FacingDirection, offset) <= activeAttack.coneAngle * 0.5f;
    }

    public void ResetForRound(Vector2 facing)
    {
        Health = maxHealth;
        HealCharges = MaxHealCharges;
        roundEnded = false;
        activeAttack = default;
        windupRemaining = recoveryRemaining = healRemaining = 0f;
        dodgeEndsAt = dodgeReadyAt = knockbackEndsAt = 0f;
        dodgeDirection = knockbackVelocity = Vector2.zero;
        FacingDirection = facing;
        body.position = spawnPosition;
        body.rotation = spawnRotation;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        effects.ResetForRound();
    }

    public void EndRound()
    {
        roundEnded = true;
        CancelWindup(WindupCancellation.RoundEnd);
        healRemaining = 0f;
        dodgeEndsAt = 0f;
        effects.CancelWindup();
        body.linearVelocity = Vector2.zero;
    }

    private void CancelWindup(WindupCancellation reason)
    {
        bool wasWindingUp = IsWindingUp;
        windupRemaining = 0f;
        if (wasWindingUp)
            WindupCancelled?.Invoke(reason);
    }

    private int TakeDamage(int damage, Vector2 pushDirection)
    {
        if (IsDodging)
            return 0;

        int damageDealt = Mathf.Min(Health, damage);
        Health -= damageDealt;
        knockbackVelocity = pushDirection * (4f + damage * 0.12f);
        knockbackEndsAt = Time.time + KnockbackDuration;
        effects.Hit();
        if (IsAlive)
            return damageDealt;

        CancelWindup(WindupCancellation.Knockout);
        healRemaining = 0f;
        effects.CancelWindup();
        body.linearVelocity = Vector2.zero;
        return damageDealt;
    }
}

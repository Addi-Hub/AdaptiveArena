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
    private const float HealDuration = 2f;
    private const float HealSpeedMultiplier = 0.5f;

    private const int ArcSegments = 16;
    private readonly Vector3[] outline = new Vector3[ArcSegments + 3];
    private Rigidbody2D body;
    private SpriteRenderer fighterSprite;
    private LineRenderer telegraph;
    private Color healthyColor;
    private Attack activeAttack;
    private Vector2 attackDirection;
    private float windupRemaining;
    private float recoveryRemaining;
    private float dodgeEndsAt;
    private float dodgeReadyAt;
    private Vector2 dodgeDirection;
    private float healRemaining;
    private bool roundEnded;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public int HealCharges { get; private set; } = MaxHealCharges;
    public bool IsAlive => Health > 0;
    public bool IsWindingUp => windupRemaining > 0f;
    public bool IsDodging => Time.time < dodgeEndsAt;
    public bool IsHealing => healRemaining > 0f;
    public bool IsDodgeReady => !roundEnded && Time.time >= dodgeReadyAt;
    public Vector2 AttackDirection => attackDirection;
    public float HeavyReach => heavy.reach;

    private bool CanAct => !roundEnded && IsAlive && opponent.IsAlive && !IsWindingUp
        && recoveryRemaining <= 0f && !IsDodging && !IsHealing;

    private void Awake()
    {
        Health = maxHealth;
        body = GetComponent<Rigidbody2D>();
        fighterSprite = GetComponent<SpriteRenderer>();
        healthyColor = fighterSprite.color;

        telegraph = gameObject.AddComponent<LineRenderer>();
        telegraph.enabled = false;
        telegraph.sharedMaterial = fighterSprite.sharedMaterial;
        telegraph.useWorldSpace = false;
        telegraph.positionCount = outline.Length;
        telegraph.widthMultiplier = 0.045f;
        telegraph.sortingLayerID = fighterSprite.sortingLayerID;
        telegraph.sortingOrder = fighterSprite.sortingOrder + 2;
    }

    public bool TryAttack(bool isHeavy, Vector2 direction)
    {
        if (!CanAct || direction.sqrMagnitude < 0.0001f)
            return false;

        activeAttack = isHeavy ? heavy : light;
        attackDirection = direction.normalized;
        windupRemaining = activeAttack.windup;
        ShowTelegraph(isHeavy ? new Color(1f, 0.35f, 0.20f) : new Color(1f, 0.85f, 0.35f));
        return true;
    }

    public bool TryDodge(Vector2 direction)
    {
        if (!CanAct || !IsDodgeReady || direction.sqrMagnitude < 0.0001f)
            return false;

        dodgeDirection = direction.normalized;
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

    public Vector2 GetMovementVelocity(Vector2 input, float moveSpeed)
    {
        if (roundEnded || !IsAlive)
            return Vector2.zero;
        if (IsDodging)
            return dodgeDirection * dodgeSpeed;

        float speed = IsHealing ? moveSpeed * HealSpeedMultiplier : moveSpeed;
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
                UpdateHealthColor();
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

        telegraph.enabled = false;
        recoveryRemaining = activeAttack.recovery;
        Vector2 offset = opponent.body.position - body.position;
        if (opponent.IsAlive && offset.sqrMagnitude <= activeAttack.reach * activeAttack.reach
            && Vector2.Angle(attackDirection, offset) <= activeAttack.coneAngle * 0.5f)
            opponent.TakeDamage(activeAttack.damage);
    }

    public void EndRound()
    {
        roundEnded = true;
        windupRemaining = 0f;
        healRemaining = 0f;
        dodgeEndsAt = 0f;
        telegraph.enabled = false;
        body.linearVelocity = Vector2.zero;
    }

    private void TakeDamage(int damage)
    {
        if (IsDodging)
            return;

        Health = Mathf.Max(0, Health - damage);
        UpdateHealthColor();
        if (IsAlive)
            return;

        windupRemaining = 0f;
        healRemaining = 0f;
        telegraph.enabled = false;
        body.linearVelocity = Vector2.zero;
    }

    private void UpdateHealthColor()
    {
        fighterSprite.color = Color.Lerp(new Color(0.25f, 0.25f, 0.28f), healthyColor,
            (float)Health / maxHealth);
    }

    private void ShowTelegraph(Color color)
    {
        float facingAngle = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg;
        float halfCone = activeAttack.coneAngle * 0.5f;
        outline[0] = Vector3.zero;
        outline[outline.Length - 1] = Vector3.zero;
        for (int step = 0; step <= ArcSegments; step++)
        {
            float angle = (facingAngle + Mathf.Lerp(-halfCone, halfCone, (float)step / ArcSegments))
                * Mathf.Deg2Rad;
            outline[step + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * activeAttack.reach;
        }

        telegraph.startColor = color;
        telegraph.endColor = color;
        telegraph.SetPositions(outline);
        telegraph.enabled = true;
    }
}

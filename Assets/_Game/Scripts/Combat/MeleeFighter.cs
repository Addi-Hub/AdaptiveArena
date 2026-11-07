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

    public int Health { get; private set; }
    public bool IsAlive => Health > 0;
    public bool IsWindingUp => windupRemaining > 0f;
    public Vector2 AttackDirection => attackDirection;

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
        if (!IsAlive || !opponent.IsAlive || IsWindingUp || recoveryRemaining > 0f
            || direction.sqrMagnitude < 0.0001f)
            return false;

        activeAttack = isHeavy ? heavy : light;
        attackDirection = direction.normalized;
        windupRemaining = activeAttack.windup;
        ShowTelegraph(isHeavy ? new Color(1f, 0.35f, 0.20f) : new Color(1f, 0.85f, 0.35f));
        return true;
    }

    private void FixedUpdate()
    {
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

    private void TakeDamage(int damage)
    {
        Health = Mathf.Max(0, Health - damage);
        fighterSprite.color = Color.Lerp(new Color(0.25f, 0.25f, 0.28f), healthyColor,
            (float)Health / maxHealth);
        if (IsAlive)
            return;

        windupRemaining = 0f;
        telegraph.enabled = false;
        body.linearVelocity = Vector2.zero;
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

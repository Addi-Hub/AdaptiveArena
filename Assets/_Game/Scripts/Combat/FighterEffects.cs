using UnityEngine;

public sealed class FighterEffects : MonoBehaviour
{
    private const int RingSegments = 48;
    private readonly Vector3[] ringPoints = new Vector3[RingSegments + 1];
    private static readonly Color HealGreen = new Color(0.31f, 1f, 0.60f);

    private MeleeFighter fighter;
    private SpriteRenderer sprite;
    private Color fighterColor;
    private LineRenderer blade;
    private LineRenderer guard;
    private LineRenderer ring;
    private TrailRenderer dodgeTrail;
    private Transform sword;
    private float halfCone;
    private float reach = 1.45f;
    private float windupStart;
    private float windupDuration;
    private float swingStart = -1f;
    private float swingDuration;
    private float hitUntil;
    private float healUntil;

    public void Initialize(SpriteRenderer bodySprite)
    {
        fighter = GetComponent<MeleeFighter>();
        sprite = bodySprite;
        fighterColor = sprite.color;

        sword = new GameObject("Sword").transform;
        sword.SetParent(transform, false);
        blade = CreateLine("Blade", sword, 3, 0.13f);
        blade.widthCurve = new AnimationCurve(new Keyframe(0f, 1f),
            new Keyframe(0.82f, 0.8f), new Keyframe(1f, 0.06f));
        guard = CreateLine("Crossguard", sword, 2, 0.08f);
        guard.SetPosition(0, new Vector3(0.60f, -0.17f));
        guard.SetPosition(1, new Vector3(0.60f, 0.17f));
        SetColor(guard, new Color(0.96f, 0.72f, 0.32f));
        ring = CreateLine("Heal ring", transform, ringPoints.Length, 0.06f);
        ring.enabled = false;

        var trailObject = new GameObject("Dodge trail");
        trailObject.transform.SetParent(transform, false);
        dodgeTrail = trailObject.AddComponent<TrailRenderer>();
        dodgeTrail.sharedMaterial = sprite.sharedMaterial;
        dodgeTrail.sortingLayerID = sprite.sortingLayerID;
        dodgeTrail.sortingOrder = sprite.sortingOrder - 1;
        dodgeTrail.time = 0.18f;
        dodgeTrail.minVertexDistance = 0.04f;
        dodgeTrail.startWidth = 0.88f;
        dodgeTrail.endWidth = 0.05f;
        dodgeTrail.startColor = fighterColor;
        dodgeTrail.endColor = new Color(fighterColor.r, fighterColor.g, fighterColor.b, 0f);
        dodgeTrail.emitting = false;
    }

    public void WindUp(float attackReach, float cone, float duration, bool heavy)
    {
        halfCone = cone * 0.5f;
        reach = attackReach;
        windupStart = Time.time;
        windupDuration = duration;
        swingDuration = heavy ? 0.22f : 0.13f;
        swingStart = -1f;
    }

    public void Swing()
    {
        windupDuration = 0f;
        swingStart = Time.time;
    }

    public void CancelWindup() => windupDuration = 0f;

    public void CancelAttack()
    {
        CancelWindup();
        swingStart = -1f;
    }
    public void Hit() => hitUntil = Time.time + 0.12f;
    public void HealComplete() => healUntil = Time.time + 0.45f;

    private void LateUpdate()
    {
        AnimateSword();
        AnimateHeal();
        dodgeTrail.emitting = fighter.IsDodging;

        Color bodyColor = fighter.IsAlive ? fighterColor : fighterColor * new Color(0.4f, 0.4f, 0.4f, 1f);
        if (fighter.IsHealing)
            bodyColor = Color.Lerp(bodyColor, HealGreen, 0.35f + 0.2f * Mathf.Sin(Time.time * 16f));
        if (fighter.IsDodging)
            bodyColor = Color.Lerp(bodyColor, Color.white, 0.45f);
        sprite.color = Time.time < hitUntil ? Color.white : bodyColor;
    }

    private void AnimateSword()
    {
        sword.gameObject.SetActive(fighter.IsAlive);
        float angle = Angle(fighter.FacingDirection);
        float tip = 1.15f;
        Color color = new Color(0.75f, 0.87f, 0.95f);

        if (windupDuration > 0f)
        {
            float progress = Mathf.Clamp01((Time.time - windupStart) / windupDuration);
            angle -= halfCone * Mathf.Sin(progress * Mathf.PI);
            tip = Mathf.Lerp(tip, reach, progress);
            color = Color.Lerp(color, new Color(1f, 0.62f, 0.20f), progress);
        }
        else if (swingStart >= 0f && Time.time - swingStart < swingDuration)
        {
            float progress = Mathf.Clamp01((Time.time - swingStart) / swingDuration);
            angle += halfCone * Mathf.Sin(progress * Mathf.PI * 0.5f);
            tip = Mathf.Lerp(reach, 1.15f, progress);
            color = Color.Lerp(new Color(1f, 0.91f, 0.55f), color, progress);
        }

        sword.localRotation = Quaternion.Euler(0f, 0f, angle);
        blade.SetPosition(0, new Vector3(0.60f, 0f));
        blade.SetPosition(1, new Vector3(Mathf.Lerp(0.60f, tip, 0.82f), 0f));
        blade.SetPosition(2, new Vector3(tip, 0f));
        SetColor(blade, color);
    }

    private void AnimateHeal()
    {
        float burst = Mathf.Clamp01((healUntil - Time.time) / 0.45f);
        ring.enabled = fighter.IsHealing || burst > 0f;
        if (!ring.enabled)
            return;

        float radius = fighter.IsHealing ? 0.68f + 0.10f * Mathf.Sin(Time.time * 13f)
            : Mathf.Lerp(1.45f, 0.6f, burst);
        for (int i = 0; i < ringPoints.Length; i++)
        {
            float angle = i * (2f * Mathf.PI / RingSegments);
            ringPoints[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        Color color = HealGreen;
        color.a = fighter.IsHealing ? 0.8f : burst;
        SetColor(ring, color);
        ring.SetPositions(ringPoints);
    }

    private LineRenderer CreateLine(string objectName, Transform parent, int pointCount, float width)
    {
        var line = new GameObject(objectName).AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.sharedMaterial = sprite.sharedMaterial;
        line.sortingLayerID = sprite.sortingLayerID;
        line.sortingOrder = sprite.sortingOrder + 2;
        line.useWorldSpace = false;
        line.positionCount = pointCount;
        line.widthMultiplier = width;
        return line;
    }

    private static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

    private static void SetColor(LineRenderer line, Color color)
    {
        line.startColor = color;
        line.endColor = color;
    }
}

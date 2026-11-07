using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ArenaRound : MonoBehaviour
{
    public enum Outcome { InProgress, PlayerWin, OpponentWin, DoubleKnockout, Timeout }

    [SerializeField] private MeleeFighter player;
    [SerializeField] private MeleeFighter opponent;
    [SerializeField, Min(1f)] private float duration = 60f;
    [SerializeField] private bool showHud = true;

    private float remaining;
    public Outcome Result { get; private set; }
    public float TimeFractionRemaining => remaining / duration;
    public event System.Action<Outcome> RoundEnded;
    public event System.Action RoundReset;
    private GUIStyle label;
    private GUIStyle number;
    private GUIStyle timer;

    private void Awake()
    {
        remaining = duration;
    }

    private void FixedUpdate()
    {
        if (Result != Outcome.InProgress)
            return;

        remaining = Mathf.Max(0f, remaining - Time.fixedDeltaTime);
        if (!player.IsAlive || !opponent.IsAlive)
        {
            if (player.IsAlive == opponent.IsAlive)
                Result = Outcome.DoubleKnockout;
            else
                Result = player.IsAlive ? Outcome.PlayerWin : Outcome.OpponentWin;
        }
        else if (remaining <= 0f)
        {
            Result = Outcome.Timeout;
        }

        if (Result != Outcome.InProgress)
        {
            player.EndRound();
            opponent.EndRound();
            RoundEnded?.Invoke(Result);
        }
    }

    public void ResetRound()
    {
        player.ResetForRound(Vector2.right);
        opponent.ResetForRound(Vector2.left);
        remaining = duration;
        Result = Outcome.InProgress;
        RoundReset?.Invoke();
    }

    private void OnGUI()
    {
        if (!showHud)
            return;

        label ??= MakeStyle(15);
        number ??= MakeStyle(20);
        timer ??= MakeStyle(38);

        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        if (scale <= 0f)
            return;

        float width = Screen.width / scale;
        float height = Screen.height / scale;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f)) * previousMatrix;

        float barWidth = (width - 160f) / 2f;
        DrawFighter(new Rect(24, 34, barWidth, 28), player, "PLAYER", false,
            new Color(0.18f, 0.83f, 0.75f));
        DrawFighter(new Rect(width - 24f - barWidth, 34, barWidth, 28), opponent, "OPPONENT", true,
            new Color(0.98f, 0.40f, 0.37f));

        Rect clock = new Rect(width / 2f - 46f, 17, 92, 63);
        Fill(clock, new Color(0.06f, 0.08f, 0.12f, 0.97f));
        Fill(new Rect(clock.x, clock.yMax - 3f, clock.width, 3f), new Color(0.91f, 0.68f, 0.34f));
        GUI.Label(clock, Mathf.CeilToInt(remaining).ToString("00"), timer);

        if (Result != Outcome.InProgress)
        {
            Rect panel = new Rect(width / 2f - 180f, height / 2f - 49f, 360, 98);
            Fill(panel, new Color(0.04f, 0.06f, 0.09f, 0.96f));
            string message = Result switch
            {
                Outcome.PlayerWin => "You win",
                Outcome.OpponentWin => "Opponent wins",
                Outcome.DoubleKnockout => "Draw — double knockout",
                _ => "Draw — time expired"
            };
            GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 45), message, number);
            GUI.Label(new Rect(panel.x, panel.y + 54f, panel.width, 27), "R   RESTART", label);
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawFighter(Rect bar, MeleeFighter fighter, string title, bool right, Color accent)
    {
        label.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        GUI.Label(new Rect(bar.x, bar.y - 24, bar.width, 22), title, label);
        Fill(bar, new Color(0.06f, 0.08f, 0.11f, 0.97f));
        Rect inner = new Rect(bar.x + 3f, bar.y + 3f, bar.width - 6f, bar.height - 6f);
        Fill(inner, new Color(0.19f, 0.13f, 0.17f));
        float fillWidth = inner.width * fighter.Health / fighter.MaxHealth;
        Fill(new Rect(right ? inner.xMax - fillWidth : inner.x, inner.y, fillWidth, inner.height), accent);
        Rect score = new Rect(bar.center.x - 54f, bar.y + 3f, 108f, bar.height - 6f);
        Fill(score, new Color(0.04f, 0.06f, 0.09f, 0.84f));
        GUI.Label(score, $"{fighter.Health} / {fighter.MaxHealth}", number);

        string dodge = fighter.IsDodging ? "DODGING" : fighter.IsDodgeReady ? "DODGE READY" : "DODGE COOLDOWN";
        if (Result != Outcome.InProgress)
            dodge = "ROUND OVER";
        string heal = fighter.IsHealing ? "HEALING" : $"HEALS {fighter.HealCharges}";
        GUI.Label(new Rect(bar.x, bar.yMax + 3, bar.width, 21), $"{heal}     {dodge}", label);
        label.alignment = TextAnchor.MiddleCenter;
    }

    private static GUIStyle MakeStyle(int size)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        return style;
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}

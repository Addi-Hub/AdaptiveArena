using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ArenaRound : MonoBehaviour
{
    [SerializeField] private MeleeFighter player;
    [SerializeField] private MeleeFighter opponent;
    [SerializeField, Min(1f)] private float duration = 60f;

    private float remaining;
    private string result;

    private void Awake()
    {
        remaining = duration;
    }

    private void FixedUpdate()
    {
        if (result != null)
            return;

        remaining = Mathf.Max(0f, remaining - Time.fixedDeltaTime);
        if (!player.IsAlive || !opponent.IsAlive)
        {
            if (player.IsAlive == opponent.IsAlive)
                result = "Draw — double knockout";
            else
                result = player.IsAlive ? "You win" : "Opponent wins";
        }
        else if (remaining <= 0f)
        {
            result = "Draw — time expired";
        }

        if (result != null)
        {
            player.EndRound();
            opponent.EndRound();
        }
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12, 54, 280, 36),
            $"Time {Mathf.CeilToInt(remaining)}s    Opponent HP {opponent.Health}/{opponent.MaxHealth}");

        if (result != null)
            GUI.Box(new Rect((Screen.width - 280) / 2f, (Screen.height - 50) / 2f, 280, 50),
                $"{result}\nPress R to restart");
    }
}

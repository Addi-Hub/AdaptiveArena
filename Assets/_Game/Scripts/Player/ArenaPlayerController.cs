using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D), typeof(MeleeFighter))]
public sealed class ArenaPlayerController : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera arenaCamera;
    [SerializeField] private Transform facingMarker;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;

    private Rigidbody2D body;
    private MeleeFighter fighter;
    private InputAction move;
    private Vector2 aimDirection = Vector2.right;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        fighter = GetComponent<MeleeFighter>();
        move = inputActions.FindAction("Player/Move", throwIfNotFound: true).Clone();
    }

    private void OnEnable() => move.Enable();

    private void OnDisable()
    {
        move.Disable();
        body.linearVelocity = Vector2.zero;
    }

    private void OnDestroy() => move.Dispose();

    private void FixedUpdate()
    {
        body.linearVelocity = fighter.GetMovementVelocity(move.ReadValue<Vector2>(), moveSpeed);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard?.rKey.wasPressedThisFrame == true)
        {
            SceneManager.LoadScene(gameObject.scene.buildIndex);
            return;
        }

        Mouse mouse = Mouse.current;
        if (!fighter.IsAlive)
            return;

        if (mouse != null)
            UpdateAim(mouse.position.ReadValue());

        if (keyboard?.spaceKey.wasPressedThisFrame == true)
        {
            Vector2 direction = move.ReadValue<Vector2>();
            fighter.TryDodge(direction.sqrMagnitude > 0.0001f ? direction : aimDirection);
            return;
        }

        if (keyboard?.eKey.wasPressedThisFrame == true)
        {
            fighter.TryHeal();
            return;
        }

        if (mouse == null)
            return;
        if (mouse.rightButton.wasPressedThisFrame)
            fighter.TryAttack(isHeavy: true, direction: aimDirection);
        else if (mouse.leftButton.wasPressedThisFrame)
            fighter.TryAttack(isHeavy: false, direction: aimDirection);
    }

    private void OnGUI()
    {
        string heal = fighter.IsHealing ? " (healing)" : "";
        string dodge = fighter.IsDodging ? "Dodging" : fighter.IsDodgeReady ? "Ready" : "Cooling down";
        GUI.Box(new Rect(12, 12, 280, 36),
            $"HP {fighter.Health}/{fighter.MaxHealth}    Heals {fighter.HealCharges}{heal}    {dodge}");
    }

    private void LateUpdate()
    {
        if (fighter.IsAlive)
            facingMarker.right = fighter.IsWindingUp ? fighter.AttackDirection : aimDirection;
    }

    private void UpdateAim(Vector2 cursorPosition)
    {
        Ray cursorRay = arenaCamera.ScreenPointToRay(cursorPosition);
        Plane arenaPlane = new Plane(Vector3.forward, transform.position);
        if (!arenaPlane.Raycast(cursorRay, out float distance))
            return;

        Vector2 aim = cursorRay.GetPoint(distance) - transform.position;
        if (aim.sqrMagnitude > 0.0001f)
            aimDirection = aim.normalized;
    }
}

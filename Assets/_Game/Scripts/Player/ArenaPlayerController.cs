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
        body.linearVelocity = fighter.IsAlive
            ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) * moveSpeed
            : Vector2.zero;
    }

    private void Update()
    {
        if (Keyboard.current?.rKey.wasPressedThisFrame == true)
        {
            SceneManager.LoadScene(gameObject.scene.buildIndex);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !fighter.IsAlive)
            return;

        UpdateAim(mouse.position.ReadValue());
        if (mouse.rightButton.wasPressedThisFrame)
            fighter.TryAttack(isHeavy: true, direction: aimDirection);
        else if (mouse.leftButton.wasPressedThisFrame)
            fighter.TryAttack(isHeavy: false, direction: aimDirection);
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

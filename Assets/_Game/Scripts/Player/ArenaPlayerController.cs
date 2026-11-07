using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class ArenaPlayerController : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera arenaCamera;
    [SerializeField] private Transform facingMarker;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;

    private Rigidbody2D body;
    private InputAction move;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
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
        body.linearVelocity = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) * moveSpeed;
    }

    private void LateUpdate()
    {
        if (Mouse.current == null)
            return;

        Ray cursorRay = arenaCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane arenaPlane = new Plane(Vector3.forward, transform.position);
        if (!arenaPlane.Raycast(cursorRay, out float distance))
            return;

        Vector2 aim = cursorRay.GetPoint(distance) - transform.position;
        if (aim.sqrMagnitude > 0.0001f)
            facingMarker.right = aim;
    }
}

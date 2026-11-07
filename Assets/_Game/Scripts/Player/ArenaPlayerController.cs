using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D), typeof(MeleeFighter))]
public sealed class ArenaPlayerController : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Camera arenaCamera;

    private Rigidbody2D body;
    private MeleeFighter fighter;
    private ArenaRound round;
    private InputAction move;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        fighter = GetComponent<MeleeFighter>();
        round = GetComponent<ArenaRound>();
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
        body.linearVelocity = fighter.GetMovementVelocity(move.ReadValue<Vector2>());
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard?.escapeKey.wasPressedThisFrame == true)
        {
            SceneManager.LoadScene(ArenaMainMenu.SceneName);
            return;
        }

        if (keyboard?.rKey.wasPressedThisFrame == true)
        {
            round.ResetRound();
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
            fighter.TryDodge(direction.sqrMagnitude > 0.0001f ? direction : fighter.FacingDirection);
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
            fighter.TryAttack(isHeavy: true, direction: fighter.FacingDirection);
        else if (mouse.leftButton.wasPressedThisFrame)
            fighter.TryAttack(isHeavy: false, direction: fighter.FacingDirection);
    }

    private void UpdateAim(Vector2 cursorPosition)
    {
        Ray cursorRay = arenaCamera.ScreenPointToRay(cursorPosition);
        Plane arenaPlane = new Plane(Vector3.forward, transform.position);
        if (!arenaPlane.Raycast(cursorRay, out float distance))
            return;

        Vector2 aim = cursorRay.GetPoint(distance) - transform.position;
        fighter.Face(aim);
    }
}

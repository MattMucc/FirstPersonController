using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReader : MonoBehaviour
{
    [SerializeField] private InputActionAsset input;
    [SerializeField] private string gameplayMapName = "Gameplay";
    private InputActionMap inputMap;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction crouchAction;
    private InputAction sprintAction;
    private InputAction interactAction;

    // Control Scheme
    public enum ControlScheme { Keyboard, Gamepad}
    public ControlScheme CurrentControlScheme { get; private set; } = ControlScheme.Keyboard;
    public event Action<ControlScheme> OnControlSchemeChanged;

    // Input Actions
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool CrouchPressed { get; private set; }
    public bool CrouchHeld { get; private set; }
    public bool SprintPressed { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool InteractPressed { get; private set; }
    public bool InteractHeld { get; private set; }

    private void Awake()
    {
        if (!input)
        {
            Debug.LogError($"{nameof(PlayerInputReader)}: No InputActionAsset assigned!", this);
            return;
        }

        inputMap = input.FindActionMap(gameplayMapName, true);
        moveAction = inputMap.FindAction("Move", true);
        lookAction = inputMap.FindAction("Look", true);
        jumpAction = inputMap.FindAction("Jump", true);
        crouchAction = inputMap.FindAction("Crouch", true);
        sprintAction = inputMap.FindAction("Sprint", true);
        interactAction = inputMap.FindAction("Interact", true);
    }

    private void OnEnable()
    {
        inputMap?.Enable();
        InputSystem.onActionChange += OnActionChange;
    }

    private void OnDisable()
    {
        inputMap?.Disable();
        InputSystem.onActionChange -= OnActionChange;
    }

    private void Update()
    {
        // Vectors
        Move = moveAction.ReadValue<Vector2>();
        Look = lookAction.ReadValue<Vector2>();

        // Pressed
        JumpPressed = jumpAction.WasPressedThisFrame();
        CrouchPressed = crouchAction.WasPressedThisFrame();
        SprintPressed = sprintAction.WasPressedThisFrame();
        InteractPressed = interactAction.WasPressedThisFrame();

        // Held
        JumpHeld = jumpAction.IsPressed();
        CrouchHeld = crouchAction.IsPressed();
        SprintHeld = sprintAction.IsPressed();
        InteractHeld = interactAction.IsPressed();
    }

    public bool IsGamepad()
    {
        if (CurrentControlScheme == ControlScheme.Gamepad)
            return true;
        else
            return false;
    }

    // Detects control scheme changes based on the device used for the performed action
    private void OnActionChange(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed) return;

        InputAction action = obj as InputAction;
        if (action == null) return;

        // Only responds to GAMEPLAY actions, not UI actions
        if (action.actionMap != inputMap) return;

        InputDevice device = action.activeControl?.device;
        if (device == null) return;

        ControlScheme newScheme = device is Gamepad ? ControlScheme.Gamepad : (device is Keyboard || device is Mouse) ? ControlScheme.Keyboard : CurrentControlScheme;
        if (newScheme != CurrentControlScheme)
        {
            CurrentControlScheme = newScheme;
            OnControlSchemeChanged?.Invoke(CurrentControlScheme);
        }
    }
}
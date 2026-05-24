using UnityEngine;


[RequireComponent(typeof(CharacterController))]
public class CharacterMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float crouchMultiplier = 0.5f;
    [Tooltip("15 = slower acontrollereleration, 120 = instant movement.")]
    [SerializeField] private float groundAcontrollereleration = 60f;
    [Tooltip("15 = slower braking, 120 = instant stop.")]
    [SerializeField] private float groundDeceleration = 60f;
    [Tooltip("0 = no air steering, 1 = full ground-level control.")]
    [SerializeField][Range(0f, 1f)] private float airControlMultiplier = 0.25f;
    [Tooltip("Keyboard-only. Gamepad always uses toggle.")]
    [SerializeField] private bool toggleSprint = false;

    [Header("Jump Settings")]
    [SerializeField] private float jumpHeight = 1.5f;
    [Tooltip("Multiplied on top of gravity while rising. Increase to reduce floatiness.")]
    [SerializeField] private float riseGravityMultiplier = 2f;
    [Tooltip("Multiplied on top of gravity while falling.")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [Tooltip("How long you have to jump after leaving a ledge.")]
    [SerializeField] private float coyoteTime = 0.2f;
    [Tooltip("How long you can queue another jump before landing.")]
    [SerializeField] private float jumpBufferTime = 0.2f;

    [Header("Crouch Settings")]
    [Tooltip("CharacterController height is multiplied by this when crouching.")]
    [SerializeField] private float crouchHeightMultiplier = 0.5f;
    [Tooltip("CameraRoot local Y is multiplied by this when crouching.")]
    [SerializeField] private float crouchCameraYMultiplier = 0.5f;
    [SerializeField] private float crouchTransitionSpeed = 12f;
    [Tooltip("Keyboard-only. Gamepad always uses toggle.")]
    [SerializeField] private bool toggleCrouch = false;
    [SerializeField] private bool displayStandCheckDebug = false;

    [Header("Ceiling Check Settings")]
    [Tooltip("Used only for the stand-up ceiling check.")]
    [SerializeField] private LayerMask environmentLayer = ~0;

    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    private PlayerInputReader input;
    private CharacterController controller;

    // Velocities
    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    // Sprint / Crouch
    private bool isSprinting;
    private bool isCrouching;
    private bool pendingStandUp;

    // Ground / Air
    private bool isGrounded;
    private bool wasGrounded;
    private float coyoteTimer;
    private float jumpBufferTimer;

    // Standing Defaults
    private float standingHeight;
    private float standingCameraY;
    private float targetHeight;
    private float targetCameraY;

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        standingHeight = controller.height;
        standingCameraY = cameraRoot.localPosition.y;
        targetHeight = standingHeight;
        targetCameraY = standingCameraY;
    }

    private void Update()
    {
        if (coyoteTimer > 0f)
            coyoteTimer -= Time.deltaTime;

        if (jumpBufferTimer > 0f)
            jumpBufferTimer -= Time.deltaTime;

        GroundCheck();
        SprintInput();
        JumpInput();
        CrouchInput();
        Move();
        UpdateCrouchTransition();
    }

    private void GroundCheck()
    {
        wasGrounded = isGrounded;
        isGrounded = controller.isGrounded;
        if (wasGrounded && !isGrounded)
            coyoteTimer = coyoteTime;
        else if (isGrounded)
            coyoteTimer = 0f;
    }

    private void Move()
    {
        Vector2 rawInput = input.Move;
        Vector3 moveDir = transform.right * rawInput.x + transform.forward * rawInput.y;

        float speed = walkSpeed;
        bool canSprint = isSprinting && !isCrouching && isGrounded && rawInput.sqrMagnitude > 0.01f;
        if (canSprint)
            speed *= sprintMultiplier;
        else if (isCrouching)
            speed *= crouchMultiplier;

        Vector3 targetVelocity = moveDir * speed;
        float acontrollereleration = moveDir.sqrMagnitude > 0.01f ? groundAcontrollereleration : groundDeceleration;
        if (!isGrounded)
            acontrollereleration *= airControlMultiplier;

        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acontrollereleration * Time.deltaTime);
        ApplyGravity();
        controller.Move(new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z) * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            return;
        }

        float gravMultiplier = verticalVelocity < 0f ? fallGravityMultiplier : riseGravityMultiplier;
        verticalVelocity += Physics.gravity.y * gravMultiplier * Time.deltaTime;
    }

    private void SprintInput()
    {
        bool useToggle = input.IsGamepad() || toggleSprint;
        if (useToggle)
        {
            if (input.SprintPressed)
                isSprinting = !isSprinting;
        }
        else
            isSprinting = input.SprintHeld;
    }

    private void JumpInput()
    {
        if (input.JumpPressed)
            jumpBufferTimer = jumpBufferTime;

        bool canJumpNow = jumpBufferTimer > 0f && (isGrounded || coyoteTimer > 0f);
        if (canJumpNow)
            Jump();
    }

    private void Jump()
    {
        float effectiveGravity = Mathf.Abs(Physics.gravity.y) * riseGravityMultiplier;
        verticalVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpHeight);
        isGrounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
    }

    private void CrouchInput()
    {
        bool useToggle = input.IsGamepad() || toggleCrouch;
        if (useToggle)
        {
            if (input.CrouchPressed)
            {
                if (!isCrouching)
                {
                    pendingStandUp = false;
                    BeginCrouch();
                }
                else if (CanStandUp())
                {
                    pendingStandUp = false;
                    EndCrouch();
                }
                else
                    pendingStandUp = true; // Toggled off but ceiling blocked, wait for clearance
            }
        }
        else
        {
            if (input.CrouchHeld && !isCrouching)
            {
                pendingStandUp = false;
                BeginCrouch();
            }
            else if (!input.CrouchHeld && isCrouching)
            {
                if (CanStandUp())
                {
                    pendingStandUp = false;
                    EndCrouch();
                }
                else
                    pendingStandUp = true; // Released but ceiling blocked, wait for clearance
            }
        }

        // Resolve the pending stand-up the moment ceiling clears
        if (pendingStandUp && CanStandUp())
        {
            pendingStandUp = false;
            EndCrouch();
        }
    }

    private void BeginCrouch()
    {
        isCrouching = true;
        isSprinting = false;
        targetHeight = standingHeight * crouchHeightMultiplier;
        targetCameraY = standingCameraY * crouchCameraYMultiplier;
    }

    private void EndCrouch()
    {
        isCrouching = false;
        targetHeight = standingHeight;
        targetCameraY = standingCameraY;
    }

    private void UpdateCrouchTransition()
    {
        float t = crouchTransitionSpeed * Time.deltaTime;
        float newHeight = Mathf.Lerp(controller.height, targetHeight, t);
        controller.height = newHeight;
        controller.center = new Vector3(0f, newHeight * 0.5f, 0f);
        if (cameraRoot)
        {
            Vector3 camLocal = cameraRoot.localPosition;
            camLocal.y = Mathf.Lerp(camLocal.y, targetCameraY, t);
            cameraRoot.localPosition = camLocal;
        }
    }

    private bool CanStandUp()
    {
        float requiredClearance = standingHeight - controller.height;
        float castRadius = controller.radius * 0.2f;
        Vector3 origin = transform.position + Vector3.up * (controller.center.y + controller.height * 0.5f);
        return !Physics.SphereCast(origin, castRadius, Vector3.up, out _, requiredClearance, environmentLayer, QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmos()
    {
        if (displayStandCheckDebug && controller)
        {
            float castRadius = controller.radius * 0.2f;
            Vector3 origin = transform.position + Vector3.up * (controller.center.y + controller.height * 0.5f);
            Gizmos.color = CanStandUp() ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin, castRadius);
        }
    }

    public bool IsGrounded => isGrounded;
    public bool IsCrouching => isCrouching;
    public bool IsSprinting => isSprinting && !isCrouching && isGrounded && input.Move.sqrMagnitude > 0.01f;
    public float HorizontalSpeed => new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;
    public Vector3 Velocity => new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
}
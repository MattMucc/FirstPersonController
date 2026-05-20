using UnityEngine;

public class RigidbodyMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float crouchMultiplier = 0.5f;
    [Tooltip("15 = slower acceleration, 120 = instant movement.")]
    [SerializeField] private float groundAcceleration = 60f;
    [Tooltip("15 = slower braking, 120 = instant stop.")]
    [SerializeField] private float groundDeceleration = 60f;
    [Tooltip("0 = no air steering, 1 = full control.")]
    [SerializeField] [Range(0f, 1f)] private float airControlMultiplier = 0.25f;
    [Tooltip("This value prevents runaway horizontal speed while airborne.")]
    [SerializeField] private float airDrag = 1.5f;
    [Tooltip("Keyboard-only option. Gamepad always uses toggle.")]
    [SerializeField] private bool toggleSprint = false;

    [Header("Jump Settings")]
    [SerializeField] private float jumpHeight = 1.5f;
    [Tooltip("This value is multiplied on top of Physics.gravity.y while rising. Increase to reduce floatiness.")]
    [SerializeField] private float riseGravityMultiplier = 2f;
    [Tooltip("This value is multiplied on top of Physics.gravity while falling.")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [Tooltip("The amount of time you still have to jump after leaving a ledge.")]
    [SerializeField] private float coyoteTime = 0.2f;
    [Tooltip("The amount of time you have before landing to queue another jump.")]
    [SerializeField] private float jumpBufferTime = 0.2f;

    [Header("Crouch Settings")]
    [Tooltip("The CapsuleCollider height is multiplied by this value when crouching.")]
    [SerializeField] private float crouchHeightMultiplier = 0.5f;
    [Tooltip("The CameraRoot local Y is multiplied by this value when crouching.")]
    [SerializeField] private float crouchCameraYMultiplier = 0.5f;
    [SerializeField] private float crouchTransitionSpeed = 12f;
    [Tooltip("Keyboard-only option. Gamepad always uses toggle.")]
    [SerializeField] private bool toggleCrouch = false;
    [SerializeField] private bool displayStandCheckDebug = false;
    private float standingHeight;
    private float standingCameraY;

    [Header("Ground Check Settings")]
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private bool displayGroundCheckDebug = false;

    [Header("References")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private Transform groundCheck;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PlayerInputReader input;

    private bool isSprinting;
    private bool isCrouching;
    private bool pendingStandUp;
    private bool isGrounded;
    private bool wasGrounded;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float targetCapsuleHeight;
    private float targetCameraY;

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!capsule) capsule = GetComponent<CapsuleCollider>();
        if (!input) input = GetComponent<PlayerInputReader>();
    }

    private void Start()
    {
        // Rigidbody setup (just in case it wasn't set before)
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.useGravity = true;
        rb.freezeRotation = true;

        // Default standing variables
        standingHeight = capsule.height;
        standingCameraY = cameraRoot.localPosition.y;

        // Set collider to standing state
        SetColliderHeight(standingHeight);
        targetCapsuleHeight = standingHeight;
        targetCameraY = standingCameraY;
    }

    private void Update()
    {
        if (coyoteTimer > 0f) coyoteTimer -= Time.deltaTime;
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        SprintInput();
        JumpInput();
        CrouchInput();
        UpdateCrouchTransition();
    }

    private void FixedUpdate()
    {
        GroundCheck();
        Move();
        ApplyGravity();
    }

    private void GroundCheck()
    {
        wasGrounded = isGrounded;

        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);
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
        if (isSprinting)
            speed *= sprintMultiplier;
        else if (isCrouching)
            speed *= crouchMultiplier;

        Vector3 targetVelocity = moveDir * speed;
        Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float acceleration = moveDir.sqrMagnitude > 0.01f ? groundAcceleration : groundDeceleration;
        if (!isGrounded)
        {
            // Manual horizontal air drag, y is never touched when airborne so it doesn't affect jump height
            Vector3 horizontalVelcoity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(-horizontalVelcoity * airDrag * Time.fixedDeltaTime, ForceMode.VelocityChange);
            acceleration *= airControlMultiplier;
        }

        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, targetVelocity, acceleration * Time.fixedDeltaTime);
        rb.AddForce(newHorizontal - currentHorizontal, ForceMode.VelocityChange);
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
                    pendingStandUp = true; // Toggled off but blocked, will automatically stand when there's enough room
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
                    pendingStandUp = true; // Released but blocked, will automatically stand when there's enough room
            }
        }

        // Resolve pending stand up the moment space clears up
        if (pendingStandUp && CanStandUp())
        {
            pendingStandUp = false;
            EndCrouch();
        }
    }

    private void Jump()
    {
        float effectiveGravity = Mathf.Abs(Physics.gravity.y) * riseGravityMultiplier; // Prevents the rise gravity multiplier affecting jump hieght, but affects the feel of the jump (weightiness versus floatiness)
        float jumpVelocity = Mathf.Sqrt(2f * effectiveGravity * jumpHeight);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z); // Set y to 0 so that the jump is the same at any fall speed
        rb.AddForce(jumpVelocity * Vector3.up, ForceMode.VelocityChange);
        isGrounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
    }

    private void BeginCrouch()
    {
        isCrouching = true;
        isSprinting = false;
        targetCapsuleHeight = standingHeight * crouchHeightMultiplier;
        targetCameraY = standingCameraY * crouchCameraYMultiplier;
    }

    private void EndCrouch()
    {
        isCrouching = false;
        targetCapsuleHeight = standingHeight;
        targetCameraY = standingCameraY;
    }

    private void UpdateCrouchTransition()
    {
        float t = crouchTransitionSpeed * Time.deltaTime;
        float newHeight = Mathf.Lerp(capsule.height, targetCapsuleHeight, t);
        SetColliderHeight(newHeight);
        if (cameraRoot)
        {
            Vector3 camLocal = cameraRoot.localPosition;
            camLocal.y = Mathf.Lerp(camLocal.y, targetCameraY, t);
            cameraRoot.localPosition = camLocal;
        }
    }

    private void ApplyGravity()
    {
        if (IsGrounded) return;

        if (rb.linearVelocity.y < 0f)
            rb.AddForce(Physics.gravity * (fallGravityMultiplier - 1f), ForceMode.Acceleration);
        else
            rb.AddForce(Physics.gravity * (riseGravityMultiplier - 1f), ForceMode.Acceleration);
    }

    private void SetColliderHeight(float height)
    {
        capsule.height = height;
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
    }

    private bool CanStandUp()
    {
        float requiredClearance = standingHeight - capsule.height;
        Vector3 origin = transform.position + Vector3.up * (capsule.center.y + capsule.height * 0.5f);
        return !Physics.SphereCast(origin, groundCheckRadius, Vector3.up, out _, requiredClearance, groundLayer, QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmos()
    {
        if (displayGroundCheckDebug && groundCheck)
        {
            Gizmos.color = isGrounded ? Color.red : Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        if (displayStandCheckDebug && capsule)
        {
            Vector3 origin = transform.position + Vector3.up * (capsule.center.y + capsule.height * 0.5f);
            Gizmos.color = CanStandUp() ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin, groundCheckRadius);
        }
    }

    public bool IsGrounded => isGrounded;
    public bool IsCrouching => isCrouching;
    public bool IsSprinting => isSprinting && !isCrouching && isGrounded && input.Move.sqrMagnitude > 0.01f;
    public float HorizontalSpeed => new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
    public Vector3 Velocity => rb.linearVelocity;
}
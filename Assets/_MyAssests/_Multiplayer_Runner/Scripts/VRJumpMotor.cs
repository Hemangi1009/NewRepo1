//using UnityEngine;
//using UnityEngine.InputSystem;

///// <summary>
///// Put this on the XR Origin (the same object that has the CharacterController).
///// Adds jumping + gravity. Only exists on the local rig, so nothing here needs networking:
///// the avatar follows your head target, so the jump replicates automatically.
///// </summary>
//[RequireComponent(typeof(CharacterController))]
//public class VRJumpMotor : MonoBehaviour
//{
//    [Header("Jump")]
//    [SerializeField] private float jumpHeight = 1.0f;
//    [SerializeField] private float gravity = -15f;

//    [Tooltip("Optional: assign your own action. If empty, the right controller A/primary button is used.")]
//    [SerializeField] private InputActionProperty jumpAction;

//    [Header("Gravity")]
//    [Tooltip("ON: this script applies gravity (turn OFF 'Use Gravity' on your Move Provider to avoid double gravity). " +
//             "OFF: the Move Provider handles gravity and this script only adds the jump impulse.")]
//    [SerializeField] private bool handleGravity = true;

//    private CharacterController _cc;
//    private InputAction _jump;
//    private bool _ownsAction;
//    private float _velY;

//    private void Awake()
//    {
//        _cc = GetComponent<CharacterController>();

//        bool hasAssigned = jumpAction.reference != null || jumpAction.action.bindings.Count > 0;
//        if (hasAssigned)
//        {
//            _jump = jumpAction.action;
//        }
//        else
//        {
//            _jump = new InputAction("Jump", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
//            _ownsAction = true;
//        }
//    }

//    private void OnEnable() => _jump?.Enable();

//    private void OnDisable() => _jump?.Disable();

//    private void OnDestroy()
//    {
//        if (_ownsAction) _jump?.Dispose();
//    }

//    /// <summary>Launches the player up as if jumping with this many times the normal jump height (used by spring pads).</summary>
//    public void LaunchWithMultiplier(float multiplier) => Launch(jumpHeight * multiplier);

//    /// <summary>Launches the player up to roughly this height (in meters).</summary>
//    public void Launch(float height)
//    {
//        _velY = Mathf.Sqrt(Mathf.Max(0.01f, height) * -2f * gravity);
//    }

//    /// <summary>Called after teleporting/respawning so old fall speed is discarded.</summary>
//    public void ResetVelocity() => _velY = 0f;

//    private void Update()
//    {
//        bool grounded = _cc.isGrounded;

//        if (grounded && _velY < 0f)
//            _velY = -2f; // small push keeps the controller stuck to the ground

//        if (grounded && _jump.WasPressedThisFrame())
//            _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);

//        if (handleGravity || _velY > 0f)
//        {
//            _velY += gravity * Time.deltaTime;
//            _cc.Move(Vector3.up * (_velY * Time.deltaTime));
//        }
//    }
//}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the XR Origin (the same object that has the CharacterController).
/// Adds jumping + gravity. Only exists on the local rig, so nothing here needs networking:
/// the avatar follows your head target, so the jump replicates automatically.
///
/// "Game feel" jump:
///   - Hold the button = higher jump, tap = small hop (variable height)
///   - Falling is faster than rising (snappy, not floaty) + a little hang time at the top
///   - Air Speed Multiplier: you travel further forward / sideways while in the air
///   - Coyote time: you can still jump a split second after walking off an edge
///   - Jump buffer: pressing jump slightly BEFORE landing still jumps when you land
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VRJumpMotor : MonoBehaviour
{
    [Header("Jump")]
    [Tooltip("How high a full jump goes (meters).")]
    [SerializeField] private float jumpHeight = 1.2f;
    [Tooltip("Base gravity (negative). Used while rising.")]
    [SerializeField] private float gravity = -15f;

    [Tooltip("Optional: assign your own action. If empty, the right controller A/primary button is used.")]
    [SerializeField] private InputActionProperty jumpAction;

    [Header("Jump feel")]
    [Tooltip("Gravity is multiplied by this while FALLING. 1 = floaty, 1.5 - 2 = snappy like most games.")]
    [SerializeField] private float fallMultiplier = 1.6f;
    [Tooltip("Gravity is multiplied by this while rising AFTER you let go of the button. Makes a quick tap a small hop. 1 = off.")]
    [SerializeField] private float lowJumpMultiplier = 2.2f;
    [Tooltip("Near the top of the jump (vertical speed below this, m/s) gravity is reduced a little for a short hang time. 0 = off.")]
    [SerializeField] private float apexHangSpeed = 1.5f;
    [Tooltip("Gravity multiplier at the top of the jump (0.5 = half gravity).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float apexGravityMultiplier = 0.6f;
    [Tooltip("Fastest possible fall speed (negative, m/s).")]
    [SerializeField] private float terminalVelocity = -30f;

    [Header("Air movement")]
    [Tooltip("How much FASTER you move forward / sideways while in the air compared to walking. " +
             "1 = same as walking (feels short), 1.5 = good jump distance, 2 = very long jumps.")]
    [SerializeField] private float airSpeedMultiplier = 1.5f;
    [Tooltip("Ignore sudden movement faster than this (m/s) when measuring your walking speed (teleports, knockback).")]
    [SerializeField] private float maxMeasuredSpeed = 8f;

    [Header("Forgiveness")]
    [Tooltip("Seconds after leaving the ground in which you can still jump.")]
    [SerializeField] private float coyoteTime = 0.12f;
    [Tooltip("Seconds before landing in which a jump press is remembered and used on landing.")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Gravity")]
    [Tooltip("ON: this script applies gravity (turn OFF 'Use Gravity' on your Move Provider to avoid double gravity). " +
             "OFF: the Move Provider handles gravity and this script only adds the jump impulse.")]
    [SerializeField] private bool handleGravity = true;

    /// <summary>
    /// While true the jump button does nothing (no jump, no remembered press, no coyote jump).
    /// Spring pads (Launch) still work. SlowSurface sets this while the player stands on mud / sand / water.
    /// </summary>
    public bool JumpBlocked { get; set; }

    /// <summary>Raised the moment a button jump starts (not for spring pad launches). The jump sound listens to this.</summary>
    public event System.Action Jumped;

    private CharacterController _cc;
    private InputAction _jump;
    private bool _ownsAction;
    private float _velY;

    private float _lastGroundedTime = -10f;
    private float _jumpBufferedUntil = -10f;
    private bool _jumpedByButton;          // true only for a normal button jump (not for spring pads)

    // air speed boost
    private Vector3 _lastLatePos;
    private bool _hasLastLatePos;
    private Vector3 _appliedHorizontal;    // horizontal movement THIS script applied this frame
    private Vector3 _airExtra;             // extra horizontal movement to add next frame while in the air

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();

        bool hasAssigned = jumpAction.reference != null || jumpAction.action.bindings.Count > 0;
        if (hasAssigned)
        {
            _jump = jumpAction.action;
        }
        else
        {
            _jump = new InputAction("Jump", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            _ownsAction = true;
        }
    }

    private void OnEnable()
    {
        _jump?.Enable();
        _hasLastLatePos = false;
        _airExtra = Vector3.zero;
    }

    private void OnDisable() => _jump?.Disable();

    private void OnDestroy()
    {
        if (_ownsAction) _jump?.Dispose();
    }

    /// <summary>Launches the player up as if jumping with this many times the normal jump height (used by spring pads).</summary>
    public void LaunchWithMultiplier(float multiplier) => Launch(jumpHeight * multiplier);

    /// <summary>Launches the player up to roughly this height (in meters).</summary>
    public void Launch(float height)
    {
        _velY = Mathf.Sqrt(Mathf.Max(0.01f, height) * -2f * gravity);
        _jumpedByButton = false;           // a launch always goes the full height, releasing the button does not cut it
        _lastGroundedTime = -10f;
    }

    /// <summary>Called after teleporting/respawning so old fall speed is discarded.</summary>
    public void ResetVelocity()
    {
        _velY = 0f;
        _airExtra = Vector3.zero;
        _appliedHorizontal = Vector3.zero;
        _hasLastLatePos = false;
        _jumpedByButton = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool grounded = _cc.isGrounded;

        if (grounded)
        {
            _lastGroundedTime = Time.time;
            _jumpedByButton = false;
            if (_velY < 0f) _velY = -2f;   // small push keeps the controller stuck to the ground
        }

        // ---- jump (with buffer + coyote time)
        if (JumpBlocked)
            _jumpBufferedUntil = -10f;     // a press while blocked is NOT remembered, so no surprise jump when the block ends
        else if (_jump.WasPressedThisFrame())
            _jumpBufferedUntil = Time.time + jumpBufferTime;

        bool canJump = !JumpBlocked && Time.time - _lastGroundedTime <= coyoteTime;
        if (canJump && Time.time <= _jumpBufferedUntil)
        {
            _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpedByButton = true;
            _jumpBufferedUntil = -10f;
            _lastGroundedTime = -10f;      // used up, no double jump
            Jumped?.Invoke();
        }

        // ---- gravity + movement
        _appliedHorizontal = Vector3.zero;

        if (handleGravity || _velY > 0f)
        {
            float g = gravity;
            if (_velY > 0f)
            {
                if (_jumpedByButton && !_jump.IsPressed()) g *= lowJumpMultiplier;   // let go early = smaller jump
            }
            else
            {
                g *= fallMultiplier;                                                  // fall faster than you rise
            }
            if (apexHangSpeed > 0f && Mathf.Abs(_velY) < apexHangSpeed) g *= apexGravityMultiplier;

            _velY = Mathf.Max(_velY + g * dt, terminalVelocity);

            // more distance while in the air
            Vector3 extra = grounded ? Vector3.zero : _airExtra;

            Vector3 before = transform.position;
            _cc.Move(new Vector3(extra.x, _velY * dt, extra.z));

            // bumped the head on a ceiling: stop going up
            if (_velY > 0f && (_cc.collisionFlags & CollisionFlags.Above) != 0) _velY = 0f;

            Vector3 moved = transform.position - before;
            _appliedHorizontal = new Vector3(moved.x, 0f, moved.z);
        }
    }

    private void LateUpdate()
    {
        // Measure how far the Move Provider (thumbstick walking) moved the rig this frame,
        // then next frame add a bit more of the same while in the air.
        Vector3 pos = transform.position;

        if (_hasLastLatePos && airSpeedMultiplier > 1.001f && Time.deltaTime > 0.0001f)
        {
            Vector3 total = pos - _lastLatePos;
            total.y = 0f;

            Vector3 providerMove = total - _appliedHorizontal;
            providerMove.y = 0f;

            float maxStep = maxMeasuredSpeed * Time.deltaTime;
            if (providerMove.magnitude > maxStep) providerMove = Vector3.zero;   // teleport / knockback, ignore

            _airExtra = providerMove * (airSpeedMultiplier - 1f);
        }
        else
        {
            _airExtra = Vector3.zero;
        }

        _lastLatePos = pos;
        _hasLastLatePos = true;
        _appliedHorizontal = Vector3.zero;
    }
}
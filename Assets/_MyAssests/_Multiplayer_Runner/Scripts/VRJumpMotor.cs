using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the XR Origin (the same object that has the CharacterController).
/// Adds jumping + gravity. Only exists on the local rig, so nothing here needs networking:
/// the avatar follows your head target, so the jump replicates automatically.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VRJumpMotor : MonoBehaviour
{
    [Header("Jump")]
    [SerializeField] private float jumpHeight = 1.0f;
    [SerializeField] private float gravity = -15f;

    [Tooltip("Optional: assign your own action. If empty, the right controller A/primary button is used.")]
    [SerializeField] private InputActionProperty jumpAction;

    [Header("Gravity")]
    [Tooltip("ON: this script applies gravity (turn OFF 'Use Gravity' on your Move Provider to avoid double gravity). " +
             "OFF: the Move Provider handles gravity and this script only adds the jump impulse.")]
    [SerializeField] private bool handleGravity = true;

    private CharacterController _cc;
    private InputAction _jump;
    private bool _ownsAction;
    private float _velY;

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

    private void OnEnable() => _jump?.Enable();

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
    }

    /// <summary>Called after teleporting/respawning so old fall speed is discarded.</summary>
    public void ResetVelocity() => _velY = 0f;

    private void Update()
    {
        bool grounded = _cc.isGrounded;

        if (grounded && _velY < 0f)
            _velY = -2f; // small push keeps the controller stuck to the ground

        if (grounded && _jump.WasPressedThisFrame())
            _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);

        if (handleGravity || _velY > 0f)
        {
            _velY += gravity * Time.deltaTime;
            _cc.Move(Vector3.up * (_velY * Time.deltaTime));
        }
    }
}
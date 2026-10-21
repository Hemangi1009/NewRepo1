//using System;
//using Fusion;
//using UnityEngine;
//using UnityEngine.InputSystem;

///// <summary>
///// Put this on the XR Origin. Each slot is one ball with its own button, hand and prefab.
/////   Press and HOLD the button : the ball appears on the hand and grows.
/////   RELEASE the button        : the ball is thrown in a straight line.
/////
///// Default setup:
/////   Ball X : left controller X button (primaryButton)
/////   Ball Y : left controller Y button (secondaryButton)
/////
///// POSITIONING: every slot creates its own "anchor" under the hand. Hand Offset is in REAL METERS
///// (hand scale is cancelled out) and Rotation Offset is in degrees. Both update live in Play mode.
///// </summary>
//public class HandBallLauncher : MonoBehaviour
//{
//    public enum Hand { Left, Right }
//    public enum ThrowDirection { Head, Hand }

//    [Serializable]
//    public class BallSlot
//    {
//        public string name = "Ball";
//        [Tooltip("Ball prefab: NetworkObject + NetworkTransform + HandBall on the root, VFX as a child.")]
//        public NetworkObject ballPrefab;
//        [Tooltip("Which hand the ball appears on and grows on.")]
//        public Hand hand = Hand.Left;
//        [Tooltip("Optional but recommended: drag the REAL controller transform here (for example 'Left Controller' under the XR Origin). Overrides the hand from LocalXROriginProvider.")]
//        public Transform handOverride;
//        [Tooltip("Optional: an Input Action (for example from your XRI input actions asset). If empty, the Default Binding below is used.")]
//        public InputActionProperty button;
//        [Tooltip("Used only when 'Button' above is empty.")]
//        public string defaultBinding = "<XRController>{LeftHand}/primaryButton";

//        [Header("Position of the ball (live in Play mode)")]
//        [Tooltip("Meters, in hand space: X = right, Y = up, Z = forward of the hand.")]
//        public Vector3 handOffset = new Vector3(0f, 0f, 0.1f);
//        [Tooltip("Degrees, extra rotation of the ball relative to the hand.")]
//        public Vector3 rotationOffset = Vector3.zero;

//        [NonSerialized] public InputAction action;
//        [NonSerialized] public bool ownsAction;
//        [NonSerialized] public HandBall active;
//        [NonSerialized] public float readyAt;
//        [NonSerialized] public Transform anchor;
//    }

//    [SerializeField]
//    private BallSlot[] slots =
//    {
//        new BallSlot { name = "Ball X", hand = Hand.Left, defaultBinding = "<XRController>{LeftHand}/primaryButton" },
//        new BallSlot { name = "Ball Y", hand = Hand.Left, defaultBinding = "<XRController>{LeftHand}/secondaryButton",
//                       handOffset = new Vector3(0f, 0.1f, 0.1f) }
//    };

//    [Header("Throwing")]
//    [SerializeField] private ThrowDirection throwDirection = ThrowDirection.Head;
//    [Tooltip("Used when Throw Direction = Hand: which local axis of the hand points forward.")]
//    [SerializeField] private Vector3 handForwardAxis = Vector3.forward;
//    [Tooltip("If you let go before the ball is fully grown: false = the ball is cancelled, true = it is thrown small.")]
//    [SerializeField] private bool allowEarlyThrow = false;
//    [SerializeField] private float cooldownAfterThrow = 0.4f;

//    [Header("Debug")]
//    [Tooltip("Draws a small sphere at each slot's ball position while this object is selected.")]
//    [SerializeField] private bool drawGizmos = true;
//    [SerializeField] private bool logPositions = false;

//    private NetworkRunner _runner;
//    private Camera _cam;

//    private void Awake()
//    {
//        foreach (var s in slots)
//        {
//            InputAction assigned = s.button.action;
//            bool hasAssigned = s.button.reference != null || (assigned != null && assigned.bindings.Count > 0);

//            if (hasAssigned)
//            {
//                s.action = assigned;
//                s.ownsAction = false;
//            }
//            else
//            {
//                s.action = new InputAction(s.name, InputActionType.Button, s.defaultBinding);
//                s.ownsAction = true;
//            }

//            Debug.Log($"[HandBallLauncher] Slot '{s.name}': hand={s.hand}, binding={(s.ownsAction ? s.defaultBinding : "assigned action '" + s.action.name + "'")}, prefab={(s.ballPrefab != null ? s.ballPrefab.name : "NONE")}.");
//        }
//    }

//    private void OnEnable()
//    {
//        foreach (var s in slots) s.action?.Enable();
//    }

//    private void OnDisable()
//    {
//        foreach (var s in slots) s.action?.Disable();
//    }

//    private void OnDestroy()
//    {
//        foreach (var s in slots)
//        {
//            if (s.ownsAction) s.action?.Dispose();
//            if (s.anchor != null) Destroy(s.anchor.gameObject);
//        }
//    }

//    private void Update()
//    {
//        foreach (var s in slots)
//        {
//            // keep the anchor on the hand and apply the offsets every frame (so tweaking in Play mode works)
//            RefreshAnchor(s);

//            if (s.action == null) continue;

//            if (s.action.WasPressedThisFrame()) TryStartCharging(s);

//            if (s.action.WasReleasedThisFrame() && s.active != null) ReleaseBall(s);

//            // the ball may have been removed by the network in the meantime
//            if (s.active != null && s.active.Object == null) s.active = null;
//        }
//    }

//    // ------------------------------------------------------------------ anchor (where the ball sits)

//    /// <summary>Creates / updates the anchor object of this slot. Returns it, or null if there is no hand yet.</summary>
//    private Transform RefreshAnchor(BallSlot s)
//    {
//        Transform hand = GetHand(s);
//        if (hand == null) return null;

//        if (s.anchor == null)
//        {
//            var go = new GameObject("BallAnchor_" + s.name);
//            s.anchor = go.transform;
//        }

//        if (s.anchor.parent != hand)
//            s.anchor.SetParent(hand, false);

//        // cancel the hand's scale so the offset is in real meters
//        Vector3 ls = hand.lossyScale;
//        Vector3 inv = new Vector3(
//            Mathf.Abs(ls.x) > 0.00001f ? 1f / ls.x : 1f,
//            Mathf.Abs(ls.y) > 0.00001f ? 1f / ls.y : 1f,
//            Mathf.Abs(ls.z) > 0.00001f ? 1f / ls.z : 1f);

//        s.anchor.localPosition = Vector3.Scale(s.handOffset, inv);
//        s.anchor.localRotation = Quaternion.Euler(s.rotationOffset);
//        s.anchor.localScale = Vector3.one;
//        return s.anchor;
//    }

//    // ------------------------------------------------------------------ charging

//    private void TryStartCharging(BallSlot s)
//    {
//        if (s.active != null) return;
//        if (Time.time < s.readyAt) return;

//        if (s.ballPrefab == null)
//        {
//            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}' has no ball prefab assigned.");
//            return;
//        }

//        if (_runner == null) _runner = FindObjectOfType<NetworkRunner>();
//        if (_runner == null || !_runner.IsRunning)
//        {
//            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}': the network runner is not running yet, cannot spawn.");
//            return;
//        }

//        Transform hand = GetHand(s);
//        Transform anchor = RefreshAnchor(s);
//        if (hand == null || anchor == null)
//        {
//            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}': no {s.hand} hand transform found (assign it in LocalXROriginProvider or 'Hand Override').");
//            return;
//        }

//        NetworkObject obj = _runner.Spawn(s.ballPrefab, anchor.position, anchor.rotation, _runner.LocalPlayer);
//        var ball = obj != null ? obj.GetComponent<HandBall>() : null;
//        if (ball == null)
//        {
//            Debug.LogError($"[HandBallLauncher] Slot '{s.name}': the spawned prefab has no HandBall component on its root.");
//            if (obj != null) _runner.Despawn(obj);
//            return;
//        }

//        // the ball follows the anchor, so the offset is already included -> pass zero offset
//        ball.Begin(anchor, Vector3.zero);
//        s.active = ball;

//        Debug.Log($"[HandBallLauncher] Slot '{s.name}': button PRESSED. Hand='{hand.name}' (scale {hand.lossyScale}), ball spawned at {anchor.position}.");
//    }

//    private void ReleaseBall(BallSlot s)
//    {
//        HandBall ball = s.active;
//        s.active = null;
//        s.readyAt = Time.time + cooldownAfterThrow;

//        if (ball == null || ball.Object == null) return;

//        if (!ball.IsFullyCharged && !allowEarlyThrow)
//        {
//            Debug.Log($"[HandBallLauncher] Slot '{s.name}': button RELEASED before the ball was fully grown, cancelling it.");
//            ball.Cancel("released too early");
//            return;
//        }

//        Vector3 dir = GetThrowDirection(s);
//        Debug.Log($"[HandBallLauncher] Slot '{s.name}': button RELEASED, throwing in direction {dir}.");
//        ball.Throw(dir);
//    }

//    // ------------------------------------------------------------------ helpers

//    private Transform GetHand(BallSlot s)
//    {
//        if (s.handOverride != null) return s.handOverride;
//        var xr = LocalXROriginProvider.Instance;
//        if (xr == null) return null;
//        return s.hand == Hand.Right ? xr.RightHandVRTarget : xr.LeftHandVRTarget;
//    }

//    private Vector3 GetThrowDirection(BallSlot s)
//    {
//        if (throwDirection == ThrowDirection.Hand)
//        {
//            Transform hand = GetHand(s);
//            if (hand != null) return hand.TransformDirection(handForwardAxis.normalized);
//        }

//        if (_cam == null)
//        {
//            _cam = GetComponentInChildren<Camera>(true);
//            if (_cam == null) _cam = Camera.main;
//        }
//        return _cam != null ? _cam.transform.forward : transform.forward;
//    }

//    private void OnDrawGizmosSelected()
//    {
//        if (!drawGizmos || slots == null) return;
//        foreach (var s in slots)
//        {
//            if (s.anchor == null) continue;
//            Gizmos.color = Color.cyan;
//            Gizmos.DrawWireSphere(s.anchor.position, 0.05f);
//            Gizmos.color = Color.blue;
//            Gizmos.DrawLine(s.anchor.position, s.anchor.position + s.anchor.forward * 0.15f);
//        }
//    }
//}

using System;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the XR Origin. Each slot is one ball with its own button, hand and prefab.
///   Press and HOLD the button : the ball appears on the hand and grows.
///   RELEASE the button        : the ball is thrown in a straight line.
///
/// Default setup:
///   Ball X : left controller X button (primaryButton)
///   Ball Y : left controller Y button (secondaryButton)
///
/// POSITIONING: every slot creates its own "anchor" under the hand. Hand Offset is in REAL METERS
/// (hand scale is cancelled out) and Rotation Offset is in degrees. Both update live in Play mode.
///
/// THROW DIRECTION (like a gun):
///   Throw Direction = Hand : the ball flies where the hand / controller is pointing. Turn your wrist and the
///                            aim line + throw direction turn with it. The camera is ignored.
///   Throw Direction = Head : old behaviour, the ball flies where the camera looks.
///
/// In Hand mode the direction is taken from the slot's ANCHOR (the same object the ball sits on), so the ball,
/// the aim line and the throw always agree. If the direction is slightly off, fix it live in Play mode with
/// the slot's "Rotation Offset" (or change "Hand Forward Axis").
/// </summary>
public class HandBallLauncher : MonoBehaviour
{
    public enum Hand { Left, Right }
    public enum ThrowDirection { Head, Hand }

    [Serializable]
    public class BallSlot
    {
        public string name = "Ball";
        [Tooltip("Ball prefab: NetworkObject + NetworkTransform + HandBall on the root, VFX as a child.")]
        public NetworkObject ballPrefab;
        [Tooltip("Which hand the ball appears on and grows on.")]
        public Hand hand = Hand.Left;
        [Tooltip("Optional but recommended: drag the REAL controller transform here (for example 'Left Controller' under the XR Origin). Overrides the hand from LocalXROriginProvider.")]
        public Transform handOverride;
        [Tooltip("Optional: an Input Action (for example from your XRI input actions asset). If empty, the Default Binding below is used.")]
        public InputActionProperty button;
        [Tooltip("Used only when 'Button' above is empty.")]
        public string defaultBinding = "<XRController>{LeftHand}/primaryButton";

        [Header("Position of the ball (live in Play mode)")]
        [Tooltip("Meters, in hand space: X = right, Y = up, Z = forward of the hand.")]
        public Vector3 handOffset = new Vector3(0f, 0f, 0.1f);
        [Tooltip("Degrees, extra rotation relative to the hand. In Hand throw mode this also rotates the throw direction, " +
                 "so use it to line the 'gun barrel' up with where your hand really points.")]
        public Vector3 rotationOffset = Vector3.zero;

        [NonSerialized] public InputAction action;
        [NonSerialized] public bool ownsAction;
        [NonSerialized] public HandBall active;
        [NonSerialized] public float readyAt;
        [NonSerialized] public Transform anchor;
    }

    [SerializeField]
    private BallSlot[] slots =
    {
        new BallSlot { name = "Ball X", hand = Hand.Left, defaultBinding = "<XRController>{LeftHand}/primaryButton" },
        new BallSlot { name = "Ball Y", hand = Hand.Left, defaultBinding = "<XRController>{LeftHand}/secondaryButton",
                       handOffset = new Vector3(0f, 0.1f, 0.1f) }
    };

    [Header("Throwing")]
    [Tooltip("Hand = the ball is fired where your hand / controller points (like a gun). Head = where the camera looks.")]
    [SerializeField] private ThrowDirection throwDirection = ThrowDirection.Hand;
    [Tooltip("Used when Throw Direction = Hand: which local axis of the hand points forward (the barrel). " +
             "(0,0,1) = the hand's forward. If the ball flies sideways or up, try (1,0,0), (0,1,0) or their negatives, " +
             "or use the slot's Rotation Offset instead.")]
    [SerializeField] private Vector3 handForwardAxis = Vector3.forward;
    [Tooltip("If you let go before the ball is fully grown: false = the ball is cancelled, true = it is thrown small.")]
    [SerializeField] private bool allowEarlyThrow = false;
    [SerializeField] private float cooldownAfterThrow = 0.4f;

    [Header("Debug")]
    [Tooltip("Draws a small sphere at each slot's ball position while this object is selected.")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logPositions = false;

    private NetworkRunner _runner;
    private Camera _cam;

    private void Awake()
    {
        foreach (var s in slots)
        {
            InputAction assigned = s.button.action;
            bool hasAssigned = s.button.reference != null || (assigned != null && assigned.bindings.Count > 0);

            if (hasAssigned)
            {
                s.action = assigned;
                s.ownsAction = false;
            }
            else
            {
                s.action = new InputAction(s.name, InputActionType.Button, s.defaultBinding);
                s.ownsAction = true;
            }

            Debug.Log($"[HandBallLauncher] Slot '{s.name}': hand={s.hand}, binding={(s.ownsAction ? s.defaultBinding : "assigned action '" + s.action.name + "'")}, prefab={(s.ballPrefab != null ? s.ballPrefab.name : "NONE")}.");
        }
    }

    private void OnEnable()
    {
        foreach (var s in slots) s.action?.Enable();
    }

    private void OnDisable()
    {
        foreach (var s in slots) s.action?.Disable();
    }

    private void OnDestroy()
    {
        foreach (var s in slots)
        {
            if (s.ownsAction) s.action?.Dispose();
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        }
    }

    private void Update()
    {
        foreach (var s in slots)
        {
            // keep the anchor on the hand and apply the offsets every frame (so tweaking in Play mode works)
            RefreshAnchor(s);

            if (s.action == null) continue;

            if (s.action.WasPressedThisFrame()) TryStartCharging(s);

            if (s.action.WasReleasedThisFrame() && s.active != null) ReleaseBall(s);

            // the ball may have been removed by the network in the meantime
            if (s.active != null && s.active.Object == null) s.active = null;

            // tell the ball where it will be thrown, so it can draw its aim line
            if (s.active != null) s.active.SetAimDirection(GetThrowDirection(s));
        }
    }

    // ------------------------------------------------------------------ anchor (where the ball sits)

    /// <summary>Creates / updates the anchor object of this slot. Returns it, or null if there is no hand yet.</summary>
    private Transform RefreshAnchor(BallSlot s)
    {
        Transform hand = GetHand(s);
        if (hand == null) return null;

        if (s.anchor == null)
        {
            var go = new GameObject("BallAnchor_" + s.name);
            s.anchor = go.transform;
        }

        if (s.anchor.parent != hand)
            s.anchor.SetParent(hand, false);

        // cancel the hand's scale so the offset is in real meters
        Vector3 ls = hand.lossyScale;
        Vector3 inv = new Vector3(
            Mathf.Abs(ls.x) > 0.00001f ? 1f / ls.x : 1f,
            Mathf.Abs(ls.y) > 0.00001f ? 1f / ls.y : 1f,
            Mathf.Abs(ls.z) > 0.00001f ? 1f / ls.z : 1f);

        s.anchor.localPosition = Vector3.Scale(s.handOffset, inv);
        s.anchor.localRotation = Quaternion.Euler(s.rotationOffset);
        s.anchor.localScale = Vector3.one;
        return s.anchor;
    }

    // ------------------------------------------------------------------ charging

    private void TryStartCharging(BallSlot s)
    {
        if (s.active != null) return;
        if (Time.time < s.readyAt) return;

        if (s.ballPrefab == null)
        {
            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}' has no ball prefab assigned.");
            return;
        }

        if (_runner == null) _runner = FindObjectOfType<NetworkRunner>();
        if (_runner == null || !_runner.IsRunning)
        {
            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}': the network runner is not running yet, cannot spawn.");
            return;
        }

        Transform hand = GetHand(s);
        Transform anchor = RefreshAnchor(s);
        if (hand == null || anchor == null)
        {
            Debug.LogWarning($"[HandBallLauncher] Slot '{s.name}': no {s.hand} hand transform found (assign it in LocalXROriginProvider or 'Hand Override').");
            return;
        }

        NetworkObject obj = _runner.Spawn(s.ballPrefab, anchor.position, anchor.rotation, _runner.LocalPlayer);
        var ball = obj != null ? obj.GetComponent<HandBall>() : null;
        if (ball == null)
        {
            Debug.LogError($"[HandBallLauncher] Slot '{s.name}': the spawned prefab has no HandBall component on its root.");
            if (obj != null) _runner.Despawn(obj);
            return;
        }

        // the ball follows the anchor, so the offset is already included -> pass zero offset
        ball.Begin(anchor, Vector3.zero);
        s.active = ball;

        Debug.Log($"[HandBallLauncher] Slot '{s.name}': button PRESSED. Hand='{hand.name}' (scale {hand.lossyScale}), ball spawned at {anchor.position}.");
    }

    private void ReleaseBall(BallSlot s)
    {
        HandBall ball = s.active;
        s.active = null;
        s.readyAt = Time.time + cooldownAfterThrow;

        if (ball == null || ball.Object == null) return;

        if (!ball.IsFullyCharged && !allowEarlyThrow)
        {
            Debug.Log($"[HandBallLauncher] Slot '{s.name}': button RELEASED before the ball was fully grown, cancelling it.");
            ball.Cancel("released too early");
            return;
        }

        RefreshAnchor(s); // make sure the direction uses the hand's latest rotation
        Vector3 dir = GetThrowDirection(s);
        Debug.Log($"[HandBallLauncher] Slot '{s.name}': button RELEASED, throwing in direction {dir}.");
        ball.Throw(dir);
    }

    // ------------------------------------------------------------------ helpers

    private Transform GetHand(BallSlot s)
    {
        if (s.handOverride != null) return s.handOverride;
        var xr = LocalXROriginProvider.Instance;
        if (xr == null) return null;
        return s.hand == Hand.Right ? xr.RightHandVRTarget : xr.LeftHandVRTarget;
    }

    /// <summary>
    /// The direction the ball will fly.
    /// Hand mode: follows the hand / controller rotation (through the slot's anchor, so Rotation Offset counts too).
    /// Head mode: follows the camera.
    /// </summary>
    private Vector3 GetThrowDirection(BallSlot s)
    {
        if (throwDirection == ThrowDirection.Hand)
        {
            // prefer the anchor: it is the object the ball sits on, so aim line, ball and throw always match
            Transform source = s.anchor != null ? s.anchor : GetHand(s);
            if (source != null)
            {
                Vector3 axis = handForwardAxis.sqrMagnitude > 0.0001f ? handForwardAxis.normalized : Vector3.forward;
                return source.TransformDirection(axis).normalized;
            }
        }

        if (_cam == null)
        {
            _cam = GetComponentInChildren<Camera>(true);
            if (_cam == null) _cam = Camera.main;
        }
        return _cam != null ? _cam.transform.forward : transform.forward;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || slots == null) return;
        foreach (var s in slots)
        {
            if (s.anchor == null) continue;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(s.anchor.position, 0.05f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(s.anchor.position, s.anchor.position + GetThrowDirection(s) * 0.3f);
        }
    }
}
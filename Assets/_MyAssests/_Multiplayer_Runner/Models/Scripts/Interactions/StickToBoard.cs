//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.XR.Interaction.Toolkit.Interactables;
//using UnityEngine.XR.Interaction.Toolkit.Interactors;

//[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
//public class StickToBoard : MonoBehaviour
//{
//    public enum Axis { Auto, X, Y, Z }
//    struct Contact
//    {
//        public Vector3 point, normal, center, axU, axV;
//        public float gap, halfU, halfV;
//    }

//    [Header("Board")]
//    public BoxCollider boardCollider;        // the wall's Box Collider
//    public Axis boardFaceAxis = Axis.Auto;   // flat-face axis of the board (Auto = thinnest)
//    public float edgeMargin = 0.03f;         // how far past the board's edge the letter's center may be and still stick
//    public float edgeInset = 0.02f;          // border kept clear: the whole letter is pushed inside this

//    [Header("Sticking")]
//    public float stickDistance = 0.04f;      // gap to the wall at which it snaps (speed does not matter)
//    public float rearmDistance = 0.10f;      // after grabbing off the wall, move this far away before it can stick again
//    public float maxPenetration = 0.15f;     // hand pushed it into the wall by this much: still sticks
//    public bool stickWhileHeld = true;       // snap as soon as it comes near, without releasing
//    public float freeWatchTime = 2f;         // after a release, keep watching this long: if it enters range it sticks
//    public bool alignFlat = true;            // turn the letter's flat face onto the wall
//    public float surfaceGap = 0.0005f;       // tiny gap to avoid flickering
//    public float grabDelay = 0.3f;           // grab is disabled this long after sticking

//    [Header("Stuck rotation (any direction in, correct rotation out)")]
//    [Tooltip("ON: the letter may be stuck at ANY rotation/side. The moment it sticks it is turned to the correct pose you saved with 'Calibrate' (front to the player, upright).")]
//    public bool snapToCorrectRotation = true;
//    [Tooltip("Filled by Calibrate. The letter's correct rotation, stored relative to the board.")]
//    public Quaternion correctRotationInBoard = Quaternion.identity;
//    [Tooltip("Filled by Calibrate. Which side of the board the correct pose was saved on.")]
//    public Vector3 calibratedNormalInBoard = Vector3.forward;
//    [Tooltip("Filled by Calibrate. Tick means a correct rotation is saved.")]
//    public bool hasCorrectRotation = false;

//    XRGrabInteractable grab;
//    Rigidbody rb;
//    Collider[] cols;
//    Vector3 flatAxis = Vector3.forward;
//    bool hasFlatAxis;
//    bool held, armed, busy;
//    float watchUntil;

//    // ---- word-puzzle hooks (used by NetworkedLetter) ----
//    public event System.Action Stuck;
//    public event System.Action Unstuck;
//    public bool IsStuck { get; private set; }

//    [Header("Stuck verification")]
//    [Tooltip("A letter only counts as stuck while it is within this distance of the board surface. If it ends up further away it is rejected / released and falls.")]
//    public float verifyTolerance = 0.02f;
//    public bool IsSettled => !busy && !held;   // not being held and not in the middle of a snap

//    [Header("Letter spacing")]
//    [Tooltip("Minimum free space between two letters on the board. A snap that would overlap (or come closer than this to) another stuck letter is refused.")]
//    public float minLetterGap = 0.002f;
//    float nextStickTry, nextBlockLog;

//    Vector3 homePos;
//    Quaternion homeRot;
//    bool homeKinematic, homeGravity;

//    void Awake()
//    {
//        grab = GetComponent<XRGrabInteractable>();
//        rb = GetComponent<Rigidbody>();
//        cols = GetComponentsInChildren<Collider>();
//        DetectFlatAxis();

//        // remember where this letter started so it can be put back after the board resets
//        homePos = transform.position;
//        homeRot = transform.rotation;
//        homeKinematic = rb.isKinematic;
//        homeGravity = rb.useGravity;
//        if (boardCollider == null) Debug.LogWarning("StickToBoard: assign Board Collider", this);
//        if (snapToCorrectRotation && !hasCorrectRotation)
//            Debug.LogWarning($"[StickToBoard] '{name}': no correct rotation saved yet. Right-click this component > Calibrate Correct Rotation (letter readable + upright in front of the board).", this);

//        grab.selectEntered.AddListener(args => OnGrabbed());
//        grab.selectExited.AddListener(args => OnReleased());
//    }

//    // ---------------- state changes ----------------

//    void OnGrabbed()
//    {
//        StopAllCoroutines();
//        SetStuck(false);   // picked up off the board
//        busy = false;
//        held = true;
//        watchUntil = 0f;
//        armed = !(Measure(out Contact c) && c.gap <= rearmDistance);
//    }

//    void OnReleased()
//    {
//        held = false;
//        if (busy) return; // this release was triggered by our own snap

//        if (Measure(out Contact c) && CanStick(c) && BeginStick(c, false))
//        {
//            // sticking - nothing else to do here
//        }
//        else
//        {
//            watchUntil = Time.time + freeWatchTime;   // keep watching: it may drift into range
//            StartCoroutine(Fall());
//        }
//    }

//    void Update()
//    {
//        if (!held || !stickWhileHeld || busy) return;

//        if (!Measure(out Contact c) || c.gap > rearmDistance) { armed = true; return; }
//        if (armed && CanStick(c)) BeginStick(c, true);
//    }

//    // released but not stuck: if it enters range at ANY speed, it sticks
//    void FixedUpdate()
//    {
//        if (held || busy || Time.time > watchUntil) return;
//        if (Measure(out Contact c) && CanStick(c)) BeginStick(c, false);
//    }

//    bool SnapsToCorrectRotation => snapToCorrectRotation && hasCorrectRotation && boardCollider != null;

//    bool CanStick(Contact c) => c.gap <= stickDistance && c.gap >= -maxPenetration;

//    // Right-click the component header > "Calibrate Correct Rotation".
//    // Put the letter in the scene IN FRONT of the board (over it, on the side the player stands),
//    // readable and upright. Its current rotation is saved as the correct one.
//    [ContextMenu("Calibrate Correct Rotation (letter readable + upright, in front of board)")]
//    void CalibrateCorrectRotation()
//    {
//        cols = GetComponentsInChildren<Collider>();
//        if (!Measure(out Contact c))
//        {
//            Debug.LogWarning("[StickToBoard] Calibrate failed: assign Board Collider and place the letter in front of / over the board.", this);
//            return;
//        }

//        Quaternion boardRot = boardCollider.transform.rotation;
//        correctRotationInBoard = Quaternion.Inverse(boardRot) * transform.rotation;
//        calibratedNormalInBoard = Quaternion.Inverse(boardRot) * c.normal;
//        hasCorrectRotation = true;
//#if UNITY_EDITOR
//        UnityEditor.EditorUtility.SetDirty(this);
//#endif
//        Debug.Log($"[StickToBoard] '{name}': correct rotation SAVED - every stick will now snap to this pose.", this);
//    }

//    // ---------------- puzzle helpers ----------------

//    void SetStuck(bool value)
//    {
//        if (IsStuck == value) return;
//        IsStuck = value;
//        if (value) Stuck?.Invoke(); else Unstuck?.Invoke();
//    }

//    // Would this letter, placed at 'pose', overlap (or come closer than minLetterGap to) a letter that is already stuck?
//    // Uses the networked list, so letters placed by OTHER players count too.
//    bool OverlapsOtherLetter(Pose pose, Contact c, out string blocker)
//    {
//        blocker = null;

//        // measure where this letter would sit on the board, in the board's own left/right + up/down directions
//        Vector3 p0 = transform.position;
//        Quaternion r0 = transform.rotation;
//        transform.SetPositionAndRotation(pose.position, pose.rotation);
//        Physics.SyncTransforms();
//        Extent(c.axU, c.center, out float u0, out float u1);
//        Extent(c.axV, c.center, out float v0, out float v1);
//        transform.SetPositionAndRotation(p0, r0);
//        Physics.SyncTransforms();

//        foreach (var l in NetworkedLetter.All)
//        {
//            if (l == null || !l.IsStuck) continue;
//            StickToBoard o = l.Stick;
//            if (o == null || o == this) continue;

//            o.Extent(c.axU, c.center, out float ou0, out float ou1);
//            o.Extent(c.axV, c.center, out float ov0, out float ov1);

//            bool overlapU = u0 < ou1 + minLetterGap && ou0 < u1 + minLetterGap;
//            bool overlapV = v0 < ov1 + minLetterGap && ov0 < v1 + minLetterGap;
//            if (overlapU && overlapV)
//            {
//                blocker = l.Letter;
//                return true;
//            }
//        }
//        return false;
//    }

//    // True if the letter is over the board AND within verifyTolerance of its surface.
//    public bool IsTouchingBoard(out float gap)
//    {
//        gap = float.MaxValue;
//        if (!Measure(out Contact c)) return false;
//        gap = c.gap;
//        return Mathf.Abs(c.gap) <= verifyTolerance;
//    }

//    // Un-sticks the letter and lets it fall (used when it turns out not to be on the board).
//    public void DropFromBoard()
//    {
//        StopAllCoroutines();
//        busy = false; held = false; watchUntil = 0f;
//        grab.enabled = true;
//        SetStuck(false);
//        StartCoroutine(Fall());
//    }

//    // Puts the letter back where it started (call on the client that has state authority).
//    public void ResetToHome()
//    {
//        StopAllCoroutines();

//        var selecting = new List<IXRSelectInteractor>(grab.interactorsSelecting);
//        foreach (var i in selecting) grab.interactionManager.SelectExit(i, grab);

//        busy = false; held = false; watchUntil = 0f;
//        grab.enabled = true;

//        if (!rb.isKinematic)
//        {
//            rb.linearVelocity = Vector3.zero;
//            rb.angularVelocity = Vector3.zero;
//        }
//        rb.isKinematic = homeKinematic;
//        rb.useGravity = homeGravity;
//        rb.position = homePos;
//        rb.rotation = homeRot;
//        transform.SetPositionAndRotation(homePos, homeRot);
//        Physics.SyncTransforms();

//        SetStuck(false);
//    }

//    // ---------------- sticking ----------------

//    bool BeginStick(Contact c, bool releaseFromHand)
//    {
//        if (Time.time < nextStickTry) return false;   // recently blocked, don't recompute every frame

//        Pose pose = ComputeSnapPose(c);   // decided ONCE, right now

//        // Don't stick on top of another letter
//        if (OverlapsOtherLetter(pose, c, out string blocker))
//        {
//            nextStickTry = Time.time + 0.1f;
//            if (Time.time >= nextBlockLog)
//            {
//                nextBlockLog = Time.time + 0.5f;
//                Debug.Log($"[StickToBoard] '{name}' can't stick here - it would overlap letter '{blocker}'. Move it to a free spot.", this);
//            }
//            return false;
//        }

//        busy = true;
//        watchUntil = 0f;
//        held = false;

//        if (releaseFromHand)
//        {
//            var list = new List<IXRSelectInteractor>(grab.interactorsSelecting);
//            foreach (var i in list) grab.interactionManager.SelectExit(i, grab);
//        }
//        StartCoroutine(FinishStick(pose));
//        return true;
//    }

//    IEnumerator FinishStick(Pose pose)
//    {
//        yield return new WaitForFixedUpdate(); // let XRI finish its release / throw logic
//        yield return null;

//        if (!rb.isKinematic)
//        {
//            rb.linearVelocity = Vector3.zero;
//            rb.angularVelocity = Vector3.zero;
//            rb.isKinematic = true;
//        }
//        rb.position = pose.position;
//        rb.rotation = pose.rotation;
//        transform.SetPositionAndRotation(pose.position, pose.rotation);
//        Physics.SyncTransforms();

//        // Only count it as stuck if it REALLY ended up touching the board
//        if (!IsTouchingBoard(out float finalGap))
//        {
//            Debug.LogWarning($"[StickToBoard] '{name}' snap REJECTED - it ended up {finalGap:F3} m from the board surface (allowed {verifyTolerance:F3} m). Letting it fall instead.", this);
//            busy = false;
//            StartCoroutine(Fall());
//            yield break;
//        }

//        Debug.Log($"[StickToBoard] '{name}' stuck to the board. Gap to surface = {finalGap:F4} m", this);
//        SetStuck(true);    // now officially on the board

//        grab.enabled = false;

//        // Keep it pinned for a moment: XRI can restore the Rigidbody / apply throw velocity a few frames after release
//        float end = Time.time + Mathf.Max(grabDelay, 0.3f);
//        while (Time.time < end)
//        {
//            yield return new WaitForFixedUpdate();
//            if (!rb.isKinematic)
//            {
//                rb.linearVelocity = Vector3.zero;
//                rb.angularVelocity = Vector3.zero;
//                rb.isKinematic = true;
//            }
//            rb.position = pose.position;
//            rb.rotation = pose.rotation;
//            transform.SetPositionAndRotation(pose.position, pose.rotation);
//        }

//        grab.enabled = true;
//        busy = false;
//    }

//    // XRI restores the Rigidbody a few frames after release, so keep forcing gravity on for a moment
//    IEnumerator Fall()
//    {
//        for (int i = 0; i < 25; i++)
//        {
//            yield return new WaitForFixedUpdate();
//            if (held || busy) yield break;
//            rb.isKinematic = false;
//            rb.useGravity = true;
//        }
//    }

//    // Works out where the letter should end up, without leaving it moved.
//    Pose ComputeSnapPose(Contact c)
//    {
//        Vector3 origPos = transform.position;
//        Quaternion origRot = transform.rotation;
//        Vector3 into = -c.normal;

//        // 0. saved correct rotation: turn the letter to it, whatever way it came in
//        bool orientedBySavedPose = false;
//        if (SnapsToCorrectRotation)
//        {
//            Transform bt = boardCollider.transform;
//            Quaternion target = bt.rotation * correctRotationInBoard;

//            // stuck from the other side of the board than the one calibrated on: mirror the pose around the board's vertical
//            Vector3 curNormalInBoard = Quaternion.Inverse(bt.rotation) * c.normal;
//            if (Vector3.Dot(curNormalInBoard, calibratedNormalInBoard) < 0f)
//            {
//                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, c.normal);
//                if (up.sqrMagnitude < 1e-4f) up = c.axV;
//                target = Quaternion.AngleAxis(180f, up.normalized) * target;
//            }

//            Vector3 pivot0 = GetBounds().center;
//            Quaternion dq = target * Quaternion.Inverse(transform.rotation);
//            transform.position = pivot0 + dq * (transform.position - pivot0);
//            transform.rotation = target;
//            orientedBySavedPose = true;
//        }

//        // 1. turn the flat face onto the wall
//        if (alignFlat && !orientedBySavedPose)
//        {
//            Vector3 face;
//            if (hasFlatAxis)
//            {
//                // old behaviour: whichever side is closer to the wall
//                Vector3 a = transform.TransformDirection(flatAxis);
//                face = Vector3.Dot(a, into) >= 0 ? a : -a;
//            }
//            else
//            {
//                Vector3[] f = { transform.right, -transform.right, transform.up,
//                                -transform.up, transform.forward, -transform.forward };
//                face = f[0];
//                foreach (var v in f) if (Vector3.Dot(v, into) > Vector3.Dot(face, into)) face = v;
//            }
//            Quaternion q = Quaternion.FromToRotation(face, into);
//            Vector3 pivot = GetBounds().center;
//            transform.position = pivot + q * (transform.position - pivot);
//            transform.rotation = q * transform.rotation;
//        }

//        // 2. touch the surface
//        Physics.SyncTransforms();
//        float h = DeepestHeight(c.normal, c.point);
//        transform.position += c.normal * (surfaceGap - h);

//        // 3. slide along the face so the WHOLE letter stays inside the board
//        Physics.SyncTransforms();
//        Extent(c.axU, c.center, out float u0, out float u1);
//        Extent(c.axV, c.center, out float v0, out float v1);
//        transform.position += c.axU * ClampShift(u0, u1, c.halfU) + c.axV * ClampShift(v0, v1, c.halfV);

//        Pose result = new Pose(transform.position, transform.rotation);
//        transform.SetPositionAndRotation(origPos, origRot);
//        Physics.SyncTransforms();
//        return result;
//    }

//    float ClampShift(float min, float max, float half)
//    {
//        float lo = -half + edgeInset, hi = half - edgeInset;
//        if (max - min >= hi - lo) return (lo + hi) * 0.5f - (min + max) * 0.5f; // bigger than the face: center it
//        if (min < lo) return lo - min;
//        if (max > hi) return hi - max;
//        return 0f;
//    }

//    // ---------------- measuring ----------------

//    bool Measure(out Contact c)
//    {
//        c = default;
//        if (boardCollider == null) return false;
//        Physics.SyncTransforms();
//        Bounds b = GetBounds();
//        if (b.size == Vector3.zero) return false;

//        Transform bt = boardCollider.transform;
//        Vector3 center = bt.TransformPoint(boardCollider.center);
//        Vector3 ls = bt.lossyScale;
//        Vector3 half = Vector3.Scale(boardCollider.size,
//            new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))) * 0.5f;

//        int n = boardFaceAxis == Axis.Auto
//            ? (half.x <= half.y && half.x <= half.z ? 0 : (half.y <= half.z ? 1 : 2))
//            : (int)boardFaceAxis - 1;
//        int u = (n + 1) % 3, v = (n + 2) % 3;
//        Vector3[] axes = { bt.right, bt.up, bt.forward };

//        Vector3 d = b.center - center;
//        float side = Vector3.Dot(d, axes[n]) >= 0 ? 1f : -1f;
//        Vector3 normal = axes[n] * side;

//        if (Mathf.Abs(Vector3.Dot(d, axes[u])) > half[u] + edgeMargin) return false;
//        if (Mathf.Abs(Vector3.Dot(d, axes[v])) > half[v] + edgeMargin) return false;

//        c.point = center + normal * half[n];
//        c.normal = normal;
//        c.center = center;
//        c.axU = axes[u];
//        c.axV = axes[v];
//        c.halfU = half[u];
//        c.halfV = half[v];
//        c.gap = DeepestHeight(normal, c.point);
//        return true;
//    }

//    float DeepestHeight(Vector3 normal, Vector3 planePoint)
//    {
//        Vector3 far = GetBounds().center - normal * 100f;
//        float min = float.MaxValue;
//        foreach (var col in cols)
//        {
//            if (col == null || !col.enabled || col.isTrigger) continue;
//            float h = Vector3.Dot(col.ClosestPoint(far) - planePoint, normal);
//            if (h < min) min = h;
//        }
//        return min;
//    }

//    void Extent(Vector3 axis, Vector3 origin, out float min, out float max)
//    {
//        Vector3 c = GetBounds().center;
//        min = float.MaxValue; max = float.MinValue;
//        foreach (var col in cols)
//        {
//            if (col == null || !col.enabled || col.isTrigger) continue;
//            float hi = Vector3.Dot(col.ClosestPoint(c + axis * 100f) - origin, axis);
//            float lo = Vector3.Dot(col.ClosestPoint(c - axis * 100f) - origin, axis);
//            if (hi > max) max = hi;
//            if (lo < min) min = lo;
//        }
//    }

//    Bounds GetBounds()
//    {
//        Bounds b = default; bool first = true;
//        foreach (var col in cols)
//        {
//            if (col == null || !col.enabled || col.isTrigger) continue;
//            if (first) { b = col.bounds; first = false; } else b.Encapsulate(col.bounds);
//        }
//        return b;
//    }

//    void DetectFlatAxis()
//    {
//        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
//        bool any = false;
//        foreach (var mf in GetComponentsInChildren<MeshFilter>())
//        {
//            if (mf.sharedMesh == null) continue;
//            Bounds mb = mf.sharedMesh.bounds;
//            Matrix4x4 m = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
//            for (int i = 0; i < 8; i++)
//            {
//                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
//                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
//                Vector3 p = m.MultiplyPoint3x4(corner);
//                min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true;
//            }
//        }
//        if (!any) return;
//        Vector3 s = max - min;
//        flatAxis = s.x <= s.y && s.x <= s.z ? Vector3.right : (s.y <= s.z ? Vector3.up : Vector3.forward);
//        hasFlatAxis = true;
//    }
//}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class StickToBoard : MonoBehaviour
{
    public enum Axis { Auto, X, Y, Z }
    struct Contact
    {
        public Vector3 point, normal, center, axU, axV;
        public float gap, halfU, halfV;
    }

    [Header("Board")]
    public BoxCollider boardCollider;        // the wall's Box Collider
    public Axis boardFaceAxis = Axis.Auto;   // flat-face axis of the board (Auto = thinnest)
    public float edgeMargin = 0.03f;         // how far past the board's edge the letter's center may be and still stick
    public float edgeInset = 0.02f;          // border kept clear: the whole letter is pushed inside this

    [Header("Sticking")]
    public float stickDistance = 0.04f;      // gap to the wall at which it snaps (speed does not matter)
    public float rearmDistance = 0.10f;      // after grabbing off the wall, move this far away before it can stick again
    public float maxPenetration = 0.15f;     // hand pushed it into the wall by this much: still sticks
    public bool stickWhileHeld = true;       // snap as soon as it comes near, without releasing
    public float freeWatchTime = 2f;         // after a release, keep watching this long: if it enters range it sticks
    public bool alignFlat = true;            // turn the letter's flat face onto the wall
    public float surfaceGap = 0.0005f;       // tiny gap to avoid flickering
    public float grabDelay = 0.3f;           // grab is disabled this long after sticking

    [Header("Stuck rotation (any direction in, correct rotation out)")]
    [Tooltip("ON: the letter may be stuck at ANY rotation/side. The moment it sticks it is turned to the correct pose you saved with 'Calibrate' (front to the player, upright).")]
    public bool snapToCorrectRotation = true;
    [Tooltip("Filled by Calibrate. The letter's correct rotation, stored relative to the board.")]
    public Quaternion correctRotationInBoard = Quaternion.identity;
    [Tooltip("Filled by Calibrate. Which side of the board the correct pose was saved on.")]
    public Vector3 calibratedNormalInBoard = Vector3.forward;
    [Tooltip("Filled by Calibrate. Tick means a correct rotation is saved.")]
    public bool hasCorrectRotation = false;

    XRGrabInteractable grab;
    Rigidbody rb;
    Collider[] cols;
    Vector3 flatAxis = Vector3.forward;
    bool hasFlatAxis;
    bool held, armed, busy;
    float watchUntil;

    // ---- word-puzzle hooks (used by NetworkedLetter) ----
    public event System.Action Stuck;
    public event System.Action Unstuck;
    public bool IsStuck { get; private set; }

    [Header("Stuck verification")]
    [Tooltip("A letter only counts as stuck while it is within this distance of the board surface. If it ends up further away it is rejected / released and falls.")]
    public float verifyTolerance = 0.02f;
    public bool IsSettled => !busy && !held;   // not being held and not in the middle of a snap

    [Header("Letter spacing")]
    [Tooltip("Minimum free space between two letters on the board. A snap that would overlap (or come closer than this to) another stuck letter is refused.")]
    public float minLetterGap = 0.002f;
    float nextStickTry, nextBlockLog;

    Vector3 homePos;
    Quaternion homeRot;
    bool homeKinematic, homeGravity;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        cols = GetComponentsInChildren<Collider>();
        DetectFlatAxis();

        // remember where this letter started so it can be put back after the board resets
        homePos = transform.position;
        homeRot = transform.rotation;
        homeKinematic = rb.isKinematic;
        homeGravity = rb.useGravity;
        if (boardCollider == null) Debug.LogWarning("StickToBoard: assign Board Collider", this);
        if (snapToCorrectRotation && !hasCorrectRotation)
            Debug.LogWarning($"[StickToBoard] '{name}': no correct rotation saved yet. Right-click this component > Calibrate Correct Rotation (letter readable + upright in front of the board).", this);

        grab.selectEntered.AddListener(args => OnGrabbed());
        grab.selectExited.AddListener(args => OnReleased());
    }

    // ---------------- state changes ----------------

    void OnGrabbed()
    {
        StopAllCoroutines();
        SetStuck(false);   // picked up off the board
        busy = false;
        held = true;
        watchUntil = 0f;
        armed = !(Measure(out Contact c) && c.gap <= rearmDistance);
    }

    void OnReleased()
    {
        held = false;
        if (busy) return; // this release was triggered by our own snap

        if (Measure(out Contact c) && CanStick(c) && BeginStick(c, false))
        {
            // sticking - nothing else to do here
        }
        else
        {
            watchUntil = Time.time + freeWatchTime;   // keep watching: it may drift into range
            StartCoroutine(Fall());
        }
    }

    void Update()
    {
        if (!held || !stickWhileHeld || busy) return;

        if (!Measure(out Contact c) || c.gap > rearmDistance) { armed = true; return; }
        if (armed && CanStick(c)) BeginStick(c, true);
    }

    // released but not stuck: if it enters range at ANY speed, it sticks
    void FixedUpdate()
    {
        if (held || busy || Time.time > watchUntil) return;
        if (Measure(out Contact c) && CanStick(c)) BeginStick(c, false);
    }

    bool SnapsToCorrectRotation => snapToCorrectRotation && hasCorrectRotation && boardCollider != null;

    bool CanStick(Contact c) => c.gap <= stickDistance && c.gap >= -maxPenetration;

    // Right-click the component header > "Calibrate Correct Rotation".
    // Put the letter in the scene IN FRONT of the board (over it, on the side the player stands),
    // readable and upright. Its current rotation is saved as the correct one.
    [ContextMenu("Calibrate Correct Rotation (letter readable + upright, in front of board)")]
    void CalibrateCorrectRotation()
    {
        cols = GetComponentsInChildren<Collider>();
        if (!Measure(out Contact c))
        {
            Debug.LogWarning("[StickToBoard] Calibrate failed: assign Board Collider and place the letter in front of / over the board.", this);
            return;
        }

        Quaternion boardRot = boardCollider.transform.rotation;
        correctRotationInBoard = Quaternion.Inverse(boardRot) * transform.rotation;
        calibratedNormalInBoard = Quaternion.Inverse(boardRot) * c.normal;
        hasCorrectRotation = true;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[StickToBoard] '{name}': correct rotation SAVED - every stick will now snap to this pose.", this);
    }

    // ---------------- puzzle helpers ----------------

    void SetStuck(bool value)
    {
        if (IsStuck == value) return;
        IsStuck = value;
        if (value) Stuck?.Invoke(); else Unstuck?.Invoke();
    }

    // Would this letter, placed at 'pose', overlap (or come closer than minLetterGap to) a letter that is already stuck?
    // Uses the networked list, so letters placed by OTHER players count too.
    bool OverlapsOtherLetter(Pose pose, Contact c, out string blocker)
    {
        blocker = null;

        // measure where this letter would sit on the board, in the board's own left/right + up/down directions
        Vector3 p0 = transform.position;
        Quaternion r0 = transform.rotation;
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        Physics.SyncTransforms();
        Extent(c.axU, c.center, out float u0, out float u1);
        Extent(c.axV, c.center, out float v0, out float v1);
        transform.SetPositionAndRotation(p0, r0);
        Physics.SyncTransforms();

        foreach (var l in NetworkedLetter.All)
        {
            if (l == null || !l.IsStuck) continue;
            StickToBoard o = l.Stick;
            if (o == null || o == this) continue;

            o.Extent(c.axU, c.center, out float ou0, out float ou1);
            o.Extent(c.axV, c.center, out float ov0, out float ov1);

            bool overlapU = u0 < ou1 + minLetterGap && ou0 < u1 + minLetterGap;
            bool overlapV = v0 < ov1 + minLetterGap && ov0 < v1 + minLetterGap;
            if (overlapU && overlapV)
            {
                blocker = l.Letter;
                return true;
            }
        }
        return false;
    }

    // True if the letter is over the board AND within verifyTolerance of its surface.
    public bool IsTouchingBoard(out float gap)
    {
        gap = float.MaxValue;
        if (!Measure(out Contact c)) return false;
        gap = c.gap;
        return Mathf.Abs(c.gap) <= verifyTolerance;
    }

    // Un-sticks the letter and lets it fall (used when it turns out not to be on the board).
    public void DropFromBoard()
    {
        StopAllCoroutines();
        busy = false; held = false; watchUntil = 0f;
        grab.enabled = true;
        SetStuck(false);
        StartCoroutine(Fall());
    }

    public bool IsHeld => held;
    public Vector3 HomePosition => homePos;

    // Puts the letter back where it started (call on the client that has state authority).
    public void ResetToHome()
    {
        StopAllCoroutines();

        // busy = true FIRST: releasing the hand below would otherwise trigger OnReleased,
        // which could re-stick the letter to the board or start Fall() (letter drops with gravity).
        busy = true;
        held = false;
        watchUntil = 0f;

        var selecting = new List<IXRSelectInteractor>(grab.interactorsSelecting);
        foreach (var i in selecting) grab.interactionManager.SelectExit(i, grab);

        grab.enabled = true;
        PlaceAtHome();
        SetStuck(false);

        // XRI can restore the Rigidbody / apply throw velocity a few frames after release,
        // so keep the letter pinned to its home pose for a moment.
        StartCoroutine(HoldAtHome());
    }

    void PlaceAtHome()
    {
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = homeKinematic;
        rb.useGravity = homeGravity;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.position = homePos;
        rb.rotation = homeRot;
        transform.SetPositionAndRotation(homePos, homeRot);
        Physics.SyncTransforms();
    }

    IEnumerator HoldAtHome()
    {
        float end = Time.time + 0.3f;
        while (Time.time < end)
        {
            yield return new WaitForFixedUpdate();
            PlaceAtHome();
        }
        busy = false;
    }

    // ---------------- sticking ----------------

    bool BeginStick(Contact c, bool releaseFromHand)
    {
        if (Time.time < nextStickTry) return false;   // recently blocked, don't recompute every frame

        Pose pose = ComputeSnapPose(c);   // decided ONCE, right now

        // Don't stick on top of another letter
        if (OverlapsOtherLetter(pose, c, out string blocker))
        {
            nextStickTry = Time.time + 0.1f;
            if (Time.time >= nextBlockLog)
            {
                nextBlockLog = Time.time + 0.5f;
                Debug.Log($"[StickToBoard] '{name}' can't stick here - it would overlap letter '{blocker}'. Move it to a free spot.", this);
            }
            return false;
        }

        busy = true;
        watchUntil = 0f;
        held = false;

        if (releaseFromHand)
        {
            var list = new List<IXRSelectInteractor>(grab.interactorsSelecting);
            foreach (var i in list) grab.interactionManager.SelectExit(i, grab);
        }
        StartCoroutine(FinishStick(pose));
        return true;
    }

    IEnumerator FinishStick(Pose pose)
    {
        yield return new WaitForFixedUpdate(); // let XRI finish its release / throw logic
        yield return null;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        rb.position = pose.position;
        rb.rotation = pose.rotation;
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        Physics.SyncTransforms();

        // Only count it as stuck if it REALLY ended up touching the board
        if (!IsTouchingBoard(out float finalGap))
        {
            Debug.LogWarning($"[StickToBoard] '{name}' snap REJECTED - it ended up {finalGap:F3} m from the board surface (allowed {verifyTolerance:F3} m). Letting it fall instead.", this);
            busy = false;
            StartCoroutine(Fall());
            yield break;
        }

        Debug.Log($"[StickToBoard] '{name}' stuck to the board. Gap to surface = {finalGap:F4} m", this);
        SetStuck(true);    // now officially on the board

        grab.enabled = false;

        // Keep it pinned for a moment: XRI can restore the Rigidbody / apply throw velocity a few frames after release
        float end = Time.time + Mathf.Max(grabDelay, 0.3f);
        while (Time.time < end)
        {
            yield return new WaitForFixedUpdate();
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            rb.position = pose.position;
            rb.rotation = pose.rotation;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        grab.enabled = true;
        busy = false;
    }

    // XRI restores the Rigidbody a few frames after release, so keep forcing gravity on for a moment
    IEnumerator Fall()
    {
        for (int i = 0; i < 25; i++)
        {
            yield return new WaitForFixedUpdate();
            if (held || busy) yield break;
            rb.isKinematic = false;
            rb.useGravity = true;
        }
    }

    // Works out where the letter should end up, without leaving it moved.
    Pose ComputeSnapPose(Contact c)
    {
        Vector3 origPos = transform.position;
        Quaternion origRot = transform.rotation;
        Vector3 into = -c.normal;

        // 0. saved correct rotation: turn the letter to it, whatever way it came in
        bool orientedBySavedPose = false;
        if (SnapsToCorrectRotation)
        {
            Transform bt = boardCollider.transform;
            Quaternion target = bt.rotation * correctRotationInBoard;

            // stuck from the other side of the board than the one calibrated on: mirror the pose around the board's vertical
            Vector3 curNormalInBoard = Quaternion.Inverse(bt.rotation) * c.normal;
            if (Vector3.Dot(curNormalInBoard, calibratedNormalInBoard) < 0f)
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, c.normal);
                if (up.sqrMagnitude < 1e-4f) up = c.axV;
                target = Quaternion.AngleAxis(180f, up.normalized) * target;
            }

            Vector3 pivot0 = GetBounds().center;
            Quaternion dq = target * Quaternion.Inverse(transform.rotation);
            transform.position = pivot0 + dq * (transform.position - pivot0);
            transform.rotation = target;
            orientedBySavedPose = true;
        }

        // 1. turn the flat face onto the wall
        if (alignFlat && !orientedBySavedPose)
        {
            Vector3 face;
            if (hasFlatAxis)
            {
                // old behaviour: whichever side is closer to the wall
                Vector3 a = transform.TransformDirection(flatAxis);
                face = Vector3.Dot(a, into) >= 0 ? a : -a;
            }
            else
            {
                Vector3[] f = { transform.right, -transform.right, transform.up,
                                -transform.up, transform.forward, -transform.forward };
                face = f[0];
                foreach (var v in f) if (Vector3.Dot(v, into) > Vector3.Dot(face, into)) face = v;
            }
            Quaternion q = Quaternion.FromToRotation(face, into);
            Vector3 pivot = GetBounds().center;
            transform.position = pivot + q * (transform.position - pivot);
            transform.rotation = q * transform.rotation;
        }

        // 2. touch the surface
        Physics.SyncTransforms();
        float h = DeepestHeight(c.normal, c.point);
        transform.position += c.normal * (surfaceGap - h);

        // 3. slide along the face so the WHOLE letter stays inside the board
        Physics.SyncTransforms();
        Extent(c.axU, c.center, out float u0, out float u1);
        Extent(c.axV, c.center, out float v0, out float v1);
        transform.position += c.axU * ClampShift(u0, u1, c.halfU) + c.axV * ClampShift(v0, v1, c.halfV);

        Pose result = new Pose(transform.position, transform.rotation);
        transform.SetPositionAndRotation(origPos, origRot);
        Physics.SyncTransforms();
        return result;
    }

    float ClampShift(float min, float max, float half)
    {
        float lo = -half + edgeInset, hi = half - edgeInset;
        if (max - min >= hi - lo) return (lo + hi) * 0.5f - (min + max) * 0.5f; // bigger than the face: center it
        if (min < lo) return lo - min;
        if (max > hi) return hi - max;
        return 0f;
    }

    // ---------------- measuring ----------------

    bool Measure(out Contact c)
    {
        c = default;
        if (boardCollider == null) return false;
        Physics.SyncTransforms();
        Bounds b = GetBounds();
        if (b.size == Vector3.zero) return false;

        Transform bt = boardCollider.transform;
        Vector3 center = bt.TransformPoint(boardCollider.center);
        Vector3 ls = bt.lossyScale;
        Vector3 half = Vector3.Scale(boardCollider.size,
            new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))) * 0.5f;

        int n = boardFaceAxis == Axis.Auto
            ? (half.x <= half.y && half.x <= half.z ? 0 : (half.y <= half.z ? 1 : 2))
            : (int)boardFaceAxis - 1;
        int u = (n + 1) % 3, v = (n + 2) % 3;
        Vector3[] axes = { bt.right, bt.up, bt.forward };

        Vector3 d = b.center - center;
        float side = Vector3.Dot(d, axes[n]) >= 0 ? 1f : -1f;
        Vector3 normal = axes[n] * side;

        if (Mathf.Abs(Vector3.Dot(d, axes[u])) > half[u] + edgeMargin) return false;
        if (Mathf.Abs(Vector3.Dot(d, axes[v])) > half[v] + edgeMargin) return false;

        c.point = center + normal * half[n];
        c.normal = normal;
        c.center = center;
        c.axU = axes[u];
        c.axV = axes[v];
        c.halfU = half[u];
        c.halfV = half[v];
        c.gap = DeepestHeight(normal, c.point);
        return true;
    }

    float DeepestHeight(Vector3 normal, Vector3 planePoint)
    {
        Vector3 far = GetBounds().center - normal * 100f;
        float min = float.MaxValue;
        foreach (var col in cols)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            float h = Vector3.Dot(col.ClosestPoint(far) - planePoint, normal);
            if (h < min) min = h;
        }
        return min;
    }

    void Extent(Vector3 axis, Vector3 origin, out float min, out float max)
    {
        Vector3 c = GetBounds().center;
        min = float.MaxValue; max = float.MinValue;
        foreach (var col in cols)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            float hi = Vector3.Dot(col.ClosestPoint(c + axis * 100f) - origin, axis);
            float lo = Vector3.Dot(col.ClosestPoint(c - axis * 100f) - origin, axis);
            if (hi > max) max = hi;
            if (lo < min) min = lo;
        }
    }

    Bounds GetBounds()
    {
        Bounds b = default; bool first = true;
        foreach (var col in cols)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            if (first) { b = col.bounds; first = false; } else b.Encapsulate(col.bounds);
        }
        return b;
    }

    void DetectFlatAxis()
    {
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        bool any = false;
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true;
            }
        }
        if (!any) return;
        Vector3 s = max - min;
        flatAxis = s.x <= s.y && s.x <= s.z ? Vector3.right : (s.y <= s.z ? Vector3.up : Vector3.forward);
        hasFlatAxis = true;
    }
}
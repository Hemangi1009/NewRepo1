//THIS IS DEFAULT OF MULTIPLAYER SYNC SCENE
//using UnityEngine;

//public class LocalXROriginProvider : MonoBehaviour
//{
//    public static LocalXROriginProvider Instance { get; private set; }

//    [SerializeField] private Transform headVRTarget;
//    [SerializeField] private Transform leftHandVRTarget;
//    [SerializeField] private Transform rightHandVRTarget;

//    public Transform HeadVRTarget => headVRTarget;
//    public Transform LeftHandVRTarget => leftHandVRTarget;
//    public Transform RightHandVRTarget => rightHandVRTarget;

//    private void Awake() => Instance = this;
//}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Fusion;
using Behaviour = UnityEngine.Behaviour; // Fusion also defines a "Behaviour" type
using UnityEngine;

public class LocalXROriginProvider : MonoBehaviour
{
    public static LocalXROriginProvider Instance { get; private set; }

    [SerializeField] private Transform headVRTarget;
    [SerializeField] private Transform leftHandVRTarget;
    [SerializeField] private Transform rightHandVRTarget;

    [Header("Used for spawn teleport (leave empty to use this object's transform)")]
    [SerializeField] private Transform xrOriginRoot;

    [Header("Spawn ground snapping")]
    [Tooltip("Drops the spawn position onto the first solid ground below it, so the player never spawns in the air.")]
    [SerializeField] private bool snapSpawnToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckStartHeight = 1f;
    [SerializeField] private float groundCheckDistance = 50f;
    [SerializeField] private float groundOffset = 0.05f;

    [Header("Fall respawn (runs here, local player only)")]
    [SerializeField] private bool fallRespawnEnabled = true;
    [Tooltip("Respawn when the rig is this many meters below its spawn point height.")]
    [SerializeField] private float fallDistance = 8f;
    [SerializeField] private float totalDuration = 3f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float fadeInDuration = 0.5f;

    [Header("Optional: extra components to switch off while respawning (movement providers are found automatically)")]
    [SerializeField] private Behaviour[] disableDuringRespawn;

    public Transform HeadVRTarget => headVRTarget;
    public Transform LeftHandVRTarget => leftHandVRTarget;
    public Transform RightHandVRTarget => rightHandVRTarget;

    public Transform Root => xrOriginRoot != null ? xrOriginRoot : transform;

    /// <summary>This player's spawn pose. SpawnPosition = where the player's HEAD/body should stand (on the ground).</summary>
    public bool HasSpawn { get; private set; }
    public Vector3 SpawnPosition { get; private set; }
    public Quaternion SpawnRotation { get; private set; }

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private readonly List<Behaviour> _locked = new List<Behaviour>();

    private bool _pinned;
    private Vector3 _pinPosition;
    private bool _ccWasEnabled;

    private Camera _cam;

    // fall respawn state
    private NetworkAvatarSetup _avatar;
    private NetworkTransform _avatarNetTransform;
    private float _nextAvatarSearch;
    private bool _respawning;
    private bool _settling;

    private void Awake()
    {
        Instance = this;
        _initialPosition = Root.position;
        _initialRotation = Root.rotation;
    }

    private Camera RigCamera
    {
        get
        {
            if (_cam == null)
            {
                _cam = Root.GetComponentInChildren<Camera>(true);
                if (_cam == null) _cam = Camera.main;
            }
            return _cam;
        }
    }

    private void Update()
    {
        if (!fallRespawnEnabled || !HasSpawn) return;

        if (_avatar == null)
        {
            if (Time.unscaledTime >= _nextAvatarSearch)
            {
                _nextAvatarSearch = Time.unscaledTime + 0.5f;
                TryFindLocalAvatar();
            }
            return;
        }

        if (_respawning || _settling) return;

        if (Root.position.y < SpawnPosition.y - fallDistance)
            StartCoroutine(FallRespawnRoutine());
    }

    private void LateUpdate()
    {
        // While locked the rig is held exactly in place, no matter what else tries to move it
        if (_pinned) Root.position = _pinPosition;
    }

    // ------------------------------------------------------------------ spawn

    /// <summary>
    /// Puts the player's HEAD (and therefore the CharacterController body that follows it) on the spawn point
    /// and remembers it as this player's spawn.
    /// </summary>
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        Vector3 safe = ResolveSafePosition(position, out bool usedFallback);
        if (usedFallback) rotation = _initialRotation;

        SpawnPosition = safe;
        SpawnRotation = rotation;
        HasSpawn = true;

        StartCoroutine(InitialSpawnRoutine());
    }

    private IEnumerator InitialSpawnRoutine()
    {
        _settling = true;

        // Freeze so the body cannot fall while the head position settles
        SetMovementLocked(true);
        PlaceHeadAt(SpawnPosition, SpawnRotation);   // runs immediately, before the avatar is spawned

        yield return new WaitForSeconds(0.3f);

        CorrectHeadToSpawn();
        LogDiagnostics("after spawn teleport");

        ResetFallVelocity();
        SetMovementLocked(false);
        _settling = false;
    }

    /// <summary>Sends the player's head back to this player's spawn point.</summary>
    public void RespawnAtSpawn()
    {
        if (HasSpawn) PlaceHeadAt(SpawnPosition, SpawnRotation);
    }

    /// <summary>Respawn at the XR Origin's original scene position from now on.</summary>
    public bool UseInitialAsSpawn()
    {
        SpawnPosition = _initialPosition;
        SpawnRotation = _initialRotation;
        HasSpawn = true;
        return true;
    }

    /// <summary>
    /// The CharacterController body follows the HEAD, and the head can be several meters away from the
    /// XR Origin object. So the origin is moved such that the head ends up on the target.
    /// </summary>
    public void PlaceHeadAt(Vector3 target, Quaternion rotation)
    {
        Transform root = Root;
        Quaternion newRot = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);

        Vector3 headLocal = Vector3.zero;
        var cam = RigCamera;
        if (cam != null)
        {
            headLocal = Quaternion.Inverse(root.rotation) * (cam.transform.position - root.position);
            headLocal.y = 0f;
        }

        Vector3 newPos = target - newRot * headLocal;
        newPos.y = target.y;

        Debug.Log($"[LocalXROriginProvider] Placing head at {target} (head is {headLocal.magnitude:F2} m from the rig origin).");
        MoveRoot(newPos, newRot);
    }

    /// <summary>Second pass: tracking may have updated the head position, so nudge the rig until the head is on the spawn point.</summary>
    private void CorrectHeadToSpawn()
    {
        var cam = RigCamera;
        if (cam == null || !HasSpawn) return;

        Vector3 err = SpawnPosition - cam.transform.position;
        err.y = 0f;
        if (err.magnitude < 0.1f) return;

        Debug.Log($"[LocalXROriginProvider] Head was {err.magnitude:F2} m away from the spawn point, shifting the rig.");
        MoveRoot(Root.position + err, Root.rotation);
    }

    public void MoveRoot(Vector3 position, Quaternion rotation)
    {
        Transform root = Root;
        Debug.Log($"[LocalXROriginProvider] Moving '{root.name}' from {root.position} to {position}");

        var cc = root.GetComponent<CharacterController>();
        bool wasEnabled = cc != null && cc.enabled;
        if (wasEnabled) cc.enabled = false; // CharacterController would otherwise fight the teleport

        // Only take the yaw so the rig never tilts
        root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
        Physics.SyncTransforms();

        if (wasEnabled) cc.enabled = true;
        if (_pinned) _pinPosition = position;

        var motor = root.GetComponent<VRJumpMotor>();
        if (motor != null) motor.ResetVelocity();
    }

    // ------------------------------------------------------------------ fall respawn

    private void TryFindLocalAvatar()
    {
        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
        {
            if (a.Object == null || !a.Object.IsValid || !a.Object.HasStateAuthority) continue;

            _avatar = a;
            _avatarNetTransform = a.GetComponent<NetworkTransform>();

            // The fall respawn lives here. Switch off any older NetworkFallRespawn on the avatar.
            foreach (var b in a.GetComponents<Behaviour>())
            {
                if (b != null && b.GetType().Name == "NetworkFallRespawn" && b.enabled)
                {
                    b.enabled = false;
                    Debug.Log("[LocalXROriginProvider] Disabled the old NetworkFallRespawn component on the avatar.");
                }
            }

            Debug.Log("[LocalXROriginProvider] Fall respawn is active (v6, handled here).");
            return;
        }
    }

    private IEnumerator FallRespawnRoutine()
    {
        _respawning = true;
        Debug.Log($"[LocalXROriginProvider] Fall detected. Rig={Root.position}, spawn={SpawnPosition}");

        var fader = VRScreenFader.Instance;
        float hold = Mathf.Max(0f, totalDuration - fadeOutDuration - fadeInDuration);

        // 1) Keep falling naturally while the screen fades to black (the player never sees the fall stop)
        if (fader != null) yield return fader.FadeTo(1f, fadeOutDuration);

        // 1b) Screen is fully black now: freeze everything so nothing moves during the respawn
        SetMovementLocked(true);

        // 2) Black screen: clear the leftover fall speed and put the HEAD on this player's own spawn point
        ResetFallVelocity();
        RespawnAtSpawn();

        // 3) Tracking settles, fix any head offset, let the avatar's IK follow, then snap the networked avatar
        yield return new WaitForSeconds(0.15f);
        CorrectHeadToSpawn();
        yield return new WaitForSeconds(0.1f);
        LogDiagnostics("after respawn");
        if (_avatar != null && _avatarNetTransform != null)
            _avatarNetTransform.Teleport(_avatar.transform.position, _avatar.transform.rotation);

        // 4) Stay black for the rest of the time
        yield return new WaitForSeconds(Mathf.Max(0f, hold - 0.25f));

        // 5) Last head check, clear speed again, unfreeze and fade in
        CorrectHeadToSpawn();
        ResetFallVelocity();
        SetMovementLocked(false);
        StartCoroutine(DiagnosticsLater());
        if (fader != null) yield return fader.FadeTo(0f, fadeInDuration);

        _respawning = false;
    }

    private IEnumerator DiagnosticsLater()
    {
        yield return new WaitForSeconds(1f);
        LogDiagnostics("1s after unlock");
        if (Root.position.y < SpawnPosition.y - 1.5f)
            Debug.LogWarning("[LocalXROriginProvider] Rig is still sinking after the respawn. See the [Diag] line: 'Ground below BODY'.");
    }

    /// <summary>
    /// XRI keeps the current fall speed inside its gravity/move providers and does not clear it when the rig is
    /// teleported. This zeroes those runtime speed fields on the components we switched off.
    /// </summary>
    private void ResetFallVelocity()
    {
        var zeroed = new List<string>();
        foreach (var b in _locked)
            if (b != null) ZeroRuntimeVelocity(b, zeroed);

        var motor = Root.GetComponent<VRJumpMotor>();
        if (motor != null) motor.ResetVelocity();

        Debug.Log($"[LocalXROriginProvider] Reset {zeroed.Count} leftover velocity field(s): {string.Join(", ", zeroed)}");
    }

    private static void ZeroRuntimeVelocity(Behaviour b, List<string> zeroed)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (Type t = b.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour); t = t.BaseType)
        {
            foreach (var f in t.GetFields(flags))
            {
                if (f.IsPublic || f.IsDefined(typeof(SerializeField), true)) continue; // settings, not runtime state
                if (f.Name.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (f.FieldType == typeof(Vector3)) f.SetValue(b, Vector3.zero);
                else if (f.FieldType == typeof(float)) f.SetValue(b, 0f);
                else continue;

                zeroed.Add(t.Name + "." + f.Name);
            }
        }
    }

    // ------------------------------------------------------------------ lock

    /// <summary>
    /// Locks/unlocks everything that moves the rig (move/turn/gravity providers, VRJumpMotor, CharacterController),
    /// and holds the rig in place, so the player cannot fall or walk while the screen is black.
    /// Only components that were enabled before are switched back on.
    /// </summary>
    public void SetMovementLocked(bool locked)
    {
        if (locked)
        {
            if (_locked.Count > 0 || _pinned) return;

            foreach (var b in Root.root.GetComponentsInChildren<Behaviour>(false))
            {
                if (ShouldLock(b))
                {
                    b.enabled = false;
                    _locked.Add(b);
                }
            }

            if (disableDuringRespawn != null)
            {
                foreach (var b in disableDuringRespawn)
                {
                    if (b != null && b != this && b.enabled)
                    {
                        b.enabled = false;
                        _locked.Add(b);
                    }
                }
            }

            var cc = Root.GetComponent<CharacterController>();
            _ccWasEnabled = cc != null && cc.enabled;
            if (_ccWasEnabled) cc.enabled = false;

            _pinPosition = Root.position;
            _pinned = true;

            Debug.Log($"[LocalXROriginProvider] Movement locked ({_locked.Count} component(s) disabled).");
        }
        else
        {
            _pinned = false;

            var cc = Root.GetComponent<CharacterController>();
            if (_ccWasEnabled && cc != null) cc.enabled = true;
            _ccWasEnabled = false;

            foreach (var b in _locked)
                if (b != null) b.enabled = true;
            _locked.Clear();

            Debug.Log("[LocalXROriginProvider] Movement unlocked.");
        }
    }

    private static bool ShouldLock(Behaviour b)
    {
        if (b == null || !b.enabled) return false;
        if (b is VRJumpMotor) return true;

        var t = b.GetType();
        string ns = t.Namespace ?? string.Empty;
        // XR Interaction Toolkit locomotion: MoveProvider, TurnProvider, GravityProvider, ... (works for XRI 2 and 3)
        return ns.StartsWith("UnityEngine.XR.Interaction.Toolkit") && t.Name.EndsWith("Provider");
    }

    // ------------------------------------------------------------------ ground / diagnostics

    private bool CharacterControllerCollidesWith(Collider c)
    {
        // Physics queries ignore the layer collision matrix, a CharacterController does not.
        return !Physics.GetIgnoreLayerCollision(Root.gameObject.layer, c.gameObject.layer);
    }

    private Vector3 ResolveSafePosition(Vector3 position, out bool usedFallback)
    {
        usedFallback = false;
        if (!snapSpawnToGround) return position;

        Vector3 origin = position + Vector3.up * groundCheckStartHeight;
        float maxDist = groundCheckStartHeight + groundCheckDistance;

        var hits = Physics.RaycastAll(origin, Vector3.down, maxDist, groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float best = float.MaxValue;
        Vector3 point = position;
        string hitName = "";

        foreach (var h in hits)
        {
            if (h.collider is CharacterController) continue;      // never count the player
            if (h.transform.IsChildOf(Root)) continue;             // nor any part of the rig
            if (!CharacterControllerCollidesWith(h.collider))      // the rig could not stand on it
            {
                Debug.LogWarning($"[LocalXROriginProvider] Ignoring '{h.collider.name}' under spawn point: its layer " +
                                 $"'{LayerMask.LayerToName(h.collider.gameObject.layer)}' does not collide with the rig's layer " +
                                 $"'{LayerMask.LayerToName(Root.gameObject.layer)}'.");
                continue;
            }
            if (h.distance < best)
            {
                best = h.distance;
                point = h.point;
                hitName = h.collider.name;
                found = true;
            }
        }

        if (found)
        {
            Vector3 snapped = point + Vector3.up * groundOffset;
            Debug.Log($"[LocalXROriginProvider] Spawn snapped to ground '{hitName}': {position} -> {snapped}");
            return snapped;
        }

        usedFallback = true;
        Debug.LogWarning($"[LocalXROriginProvider] NO WALKABLE GROUND found under spawn point {position}. " +
                         $"Using the XR Origin's original scene position {_initialPosition} instead. " +
                         "Move your spawn point so it sits above a collider.");
        return _initialPosition;
    }

    private string DescribeGroundBelow(Vector3 from, Transform root)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, 100f, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        var sb = new StringBuilder();
        int shown = 0;
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(root)) continue;
            sb.Append($"[{h.collider.name} y={h.point.y:F2} layer='{LayerMask.LayerToName(h.collider.gameObject.layer)}' " +
                      $"trigger={h.collider.isTrigger} rigCollides={CharacterControllerCollidesWith(h.collider)}] ");
            if (++shown >= 3) break;
        }
        return shown == 0 ? "NOTHING" : sb.ToString().TrimEnd();
    }

    /// <summary>One compact log line: where the head/body really are and what is under the BODY (not just under the origin).</summary>
    public void LogDiagnostics(string tag)
    {
        Transform root = Root;
        var cc = root.GetComponent<CharacterController>();
        var cam = RigCamera;

        var sb = new StringBuilder();
        sb.Append($"[Diag] {tag}: root={root.position}. ");

        Vector3 bodyCenter = root.position + Vector3.up;
        if (cc == null) sb.Append("NO CharacterController. ");
        else
        {
            bodyCenter = root.TransformPoint(cc.center);
            sb.Append($"CC enabled={cc.enabled}, grounded={cc.isGrounded}, height={cc.height:F2}, radius={cc.radius:F2}, bodyWorld={bodyCenter}. ");
        }

        if (cam != null)
            sb.Append($"head={cam.transform.position} (local to root {root.InverseTransformPoint(cam.transform.position)}). ");

        sb.Append("Ground below BODY: ").Append(DescribeGroundBelow(bodyCenter, root)).Append(". ");
        sb.Append("Ground below ROOT: ").Append(DescribeGroundBelow(root.position + Vector3.up, root)).Append(".");

        Debug.Log(sb.ToString());
    }
}
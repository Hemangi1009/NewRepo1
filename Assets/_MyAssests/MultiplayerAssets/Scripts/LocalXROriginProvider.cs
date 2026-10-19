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

    [Header("Sounds: fall")]
    [Tooltip("Played once when the player falls and is about to respawn. Leave empty for no sound.")]
    [SerializeField] private AudioClip fallSfx;
    [Range(0f, 1f)]
    [SerializeField] private float fallSfxVolume = 1f;

    [Header("Sounds: jump")]
    [Tooltip("Played when the player jumps. Leave empty for no sound.")]
    [SerializeField] private AudioClip jumpSfx;
    [Range(0f, 1f)]
    [SerializeField] private float jumpSfxVolume = 1f;
    [Tooltip("Random pitch change so the sound does not feel the same every time. 0 = always the same, 0.08 = a little variety.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float jumpPitchVariation = 0.06f;

    [Tooltip("Minimum seconds between two jump sounds.")]
    [SerializeField] private float jumpSfxCooldown = 0.2f;

    [Header("Checkpoints")]
    [Tooltip("ON: after a fall, the player respawns at the last checkpoint THEY reached (if any). OFF: always at the start spawn.")]
    [SerializeField] private bool useCheckpointRespawn = true;

    [Header("Optional: extra components to switch off while respawning (movement providers are found automatically)")]
    [SerializeField] private Behaviour[] disableDuringRespawn;

    [Header("Debug")]
    [Tooltip("Print debug messages (editor only). Keep OFF while testing performance: Debug.Log is slow, especially on Quest.")]
    [SerializeField] private bool verboseLogs = false;

    // Calls to this are removed completely from builds, including the work of building the message text.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void Log(string msg)
    {
        if (Instance != null && Instance.verboseLogs) Debug.Log(msg);
    }

    public Transform HeadVRTarget => headVRTarget;
    public Transform LeftHandVRTarget => leftHandVRTarget;
    public Transform RightHandVRTarget => rightHandVRTarget;

    public Transform Root => xrOriginRoot != null ? xrOriginRoot : transform;

    /// <summary>
    /// This player's CURRENT respawn pose. SpawnPosition = where the player's HEAD/body should stand (on the ground).
    /// It starts as the start spawn and becomes the last checkpoint after the first respawn at a checkpoint.
    /// </summary>
    public bool HasSpawn { get; private set; }
    public Vector3 SpawnPosition { get; private set; }
    public Quaternion SpawnRotation { get; private set; }

    /// <summary>True while the first spawn or a fall respawn is running.</summary>
    public bool IsRespawning => _respawning || _settling;

    /// <summary>True while the player is frozen by a ball.</summary>
    public bool IsFrozen => _freezeRoutine != null;

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

    // jump sound state
    private VRJumpMotor _jumpMotor;
    private float _nextJumpSfxTime;

    // hit effects state (knockback / freeze / haptics)
    private Coroutine _knockbackRoutine;
    private Coroutine _freezeRoutine;
    private Coroutine _hapticRoutine;
    private float _freezeUntil;

    private void OnEnable()
    {
        // the jump sound plays exactly when VRJumpMotor starts a jump
        _jumpMotor = Root.GetComponent<VRJumpMotor>();
        if (_jumpMotor != null) _jumpMotor.Jumped += OnJumped;
        else Debug.LogWarning("[LocalXROriginProvider] No VRJumpMotor found on the XR Origin, so no jump sound can play.");
    }

    private void OnDisable()
    {
        if (_jumpMotor != null) _jumpMotor.Jumped -= OnJumped;
        _jumpMotor = null;
    }

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

    // ------------------------------------------------------------------ jump sound

    /// <summary>Called by VRJumpMotor at the moment a jump starts.</summary>
    private void OnJumped()
    {
        if (jumpSfx == null) return;
        if (_respawning || _settling || _pinned) return;   // never while frozen / respawning
        if (Time.time < _nextJumpSfxTime) return;

        _nextJumpSfxTime = Time.time + jumpSfxCooldown;
        float pitch = 1f + UnityEngine.Random.Range(-jumpPitchVariation, jumpPitchVariation);
        PlaySfx2D(jumpSfx, jumpSfxVolume, pitch, "jump");
    }

    // ------------------------------------------------------------------ hit effects (called by HandBall on the VICTIM's own machine)

    /// <summary>
    /// Pushes the player back like a stumble: horizontally, in the given world direction, over 'duration' seconds
    /// (fast at the start, slowing down). Uses the CharacterController, so walls still stop the player.
    /// </summary>
    public void Knockback(Vector3 worldDirection, float distance, float duration)
    {
        if (IsRespawning || _pinned)
        {
            Log("[LocalXROriginProvider] Knockback skipped: the player is respawning or frozen.");
            return;
        }

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            Debug.LogWarning("[LocalXROriginProvider] Knockback skipped: the push direction is straight up or down.");
            return;
        }

        if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);
        _knockbackRoutine = StartCoroutine(KnockbackRoutine(worldDirection.normalized, Mathf.Max(0f, distance), Mathf.Max(0.05f, duration)));
    }

    private IEnumerator KnockbackRoutine(Vector3 dir, float distance, float duration)
    {
        Log($"[LocalXROriginProvider] KNOCKBACK started: direction {dir}, {distance:F2} m over {duration:F2}s.");

        Transform root = Root;
        var cc = root.GetComponent<CharacterController>();
        Vector3 startPos = root.position;

        float t = 0f;
        float previous = 0f;
        while (t < duration)
        {
            if (_pinned || _respawning || _settling)
            {
                Log("[LocalXROriginProvider] Knockback stopped early: the player was frozen or respawned.");
                break;
            }

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float eased = 1f - (1f - k) * (1f - k);       // fast at first, then slowing down
            float current = distance * eased;
            Vector3 step = dir * (current - previous);
            previous = current;

            if (cc != null && cc.enabled) cc.Move(step);   // respects walls
            else root.position += step;

            yield return null;
        }

        Log($"[LocalXROriginProvider] KNOCKBACK finished: moved {Vector3.Distance(startPos, root.position):F2} m.");
        _knockbackRoutine = null;
    }

    /// <summary>
    /// Freezes the player on the spot for 'seconds' (no walking, turning or jumping), then gives control back.
    /// Hitting an already frozen player extends the freeze.
    /// </summary>
    public void Freeze(float seconds)
    {
        if (seconds <= 0f) return;

        if (IsRespawning)
        {
            Log("[LocalXROriginProvider] Freeze skipped: the player is respawning (already held in place).");
            return;
        }

        _freezeUntil = Mathf.Max(_freezeUntil, Time.time + seconds);

        if (_freezeRoutine != null)
        {
            Log($"[LocalXROriginProvider] Already frozen, freeze extended to {_freezeUntil - Time.time:F1}s from now.");
            return;
        }

        _freezeRoutine = StartCoroutine(FreezeRoutine());
    }

    private IEnumerator FreezeRoutine()
    {
        if (_knockbackRoutine != null)
        {
            StopCoroutine(_knockbackRoutine);
            _knockbackRoutine = null;
        }

        SetMovementLocked(true);
        Log($"[LocalXROriginProvider] FROZEN for {_freezeUntil - Time.time:F1}s.");

        while (Time.time < _freezeUntil)
        {
            if (_respawning || _settling) break;    // a respawn takes over the lock and releases it itself
            yield return null;
        }

        if (_respawning || _settling)
        {
            Log("[LocalXROriginProvider] Freeze ended early because a respawn started (the respawn releases the lock).");
        }
        else if (_pinned)
        {
            ResetFallVelocity();
            SetMovementLocked(false);
            Log("[LocalXROriginProvider] UNFROZEN: the player can move again.");
        }

        _freezeUntil = 0f;
        _freezeRoutine = null;
    }

    /// <summary>
    /// Vibrates both controllers, strongest at the start and fading out, like something hit the player.
    /// strength 0..1, duration in seconds.
    /// </summary>
    public void PlayHaptics(float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f) return;

        // ONE call per controller. No coroutine, no repeated pulses.
        strength = Mathf.Clamp01(strength);
        SendHaptic(UnityEngine.XR.XRNode.LeftHand, strength, duration);
        SendHaptic(UnityEngine.XR.XRNode.RightHand, strength, duration);
    }

    private static void SendHaptic(UnityEngine.XR.XRNode node, float amplitude, float seconds)
    {
        var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid
            && device.TryGetHapticCapabilities(out UnityEngine.XR.HapticCapabilities caps)
            && caps.supportsImpulse)
            device.SendHapticImpulse(0u, amplitude, seconds);
    }

    [ContextMenu("Debug: Test knockback (backwards)")]
    private void DebugKnockback()
    {
        var cam = RigCamera;
        Vector3 back = cam != null ? -cam.transform.forward : -Root.forward;
        Knockback(back, 1.5f, 0.35f);
    }

    [ContextMenu("Debug: Test freeze (3 s)")]
    private void DebugFreeze()
    {
        Freeze(3f);
    }

    [ContextMenu("Debug: Test haptics")]
    private void DebugHaptics()
    {
        PlayHaptics(1f, 0.6f);
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

    /// <summary>
    /// Sends the player's head back to this player's respawn point:
    /// the last checkpoint this player reached, or the start spawn if there is none.
    /// </summary>
    public void RespawnAtSpawn()
    {
        if (!HasSpawn) return;

        ApplyCheckpointAsSpawn();
        PlaceHeadAt(SpawnPosition, SpawnRotation);
    }

    /// <summary>
    /// If the local player has reached a checkpoint, makes it the current spawn (snapped to the ground).
    /// The checkpoint is stored per player (per machine), so another player's checkpoint is never used.
    /// </summary>
    private void ApplyCheckpointAsSpawn()
    {
        if (!useCheckpointRespawn)
        {
            Log("[LocalXROriginProvider] Checkpoint respawn is switched off, using the normal spawn.");
            return;
        }

        if (!Checkpoint.TryGetRespawnPose(out Vector3 cpPos, out Quaternion cpRot))
        {
            Log($"[LocalXROriginProvider] No checkpoint reached yet, respawning at the normal spawn {SpawnPosition}.");
            return;
        }

        Vector3 safe = ResolveSafePosition(cpPos, out bool usedFallback);
        if (usedFallback)
        {
            Debug.LogWarning($"[LocalXROriginProvider] The checkpoint at {cpPos} has NO WALKABLE GROUND below it, so it cannot be used. " +
                             $"Respawning at the normal spawn {SpawnPosition} instead. Move the checkpoint (or its Respawn Point) above a collider.");
            return;
        }

        SpawnPosition = safe;
        SpawnRotation = cpRot;
        Log($"[LocalXROriginProvider] Respawning at the CHECKPOINT: {cpPos} -> {safe} (yaw {cpRot.eulerAngles.y:F0}).");
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

        Log($"[LocalXROriginProvider] Placing head at {target} (head is {headLocal.magnitude:F2} m from the rig origin).");
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

        Log($"[LocalXROriginProvider] Head was {err.magnitude:F2} m away from the spawn point, shifting the rig.");
        MoveRoot(Root.position + err, Root.rotation);
    }

    public void MoveRoot(Vector3 position, Quaternion rotation)
    {
        Transform root = Root;
        Log($"[LocalXROriginProvider] Moving '{root.name}' from {root.position} to {position}");

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
                    Log("[LocalXROriginProvider] Disabled the old NetworkFallRespawn component on the avatar.");
                }
            }

            Log("[LocalXROriginProvider] Fall respawn is active (v10, checkpoint aware, fall + jump sounds, hit effects).");
            return;
        }
    }

    private IEnumerator FallRespawnRoutine()
    {
        _respawning = true;
        Log($"[LocalXROriginProvider] Fall detected. Rig={Root.position}, current spawn={SpawnPosition}, hasCheckpoint={Checkpoint.HasCheckpoint}");

        if (fallSfx != null) PlaySfx2D(fallSfx, fallSfxVolume, 1f, "fall");
        else Log("[LocalXROriginProvider] No fall sound assigned (Fall Sfx is empty).");

        var fader = VRScreenFader.Instance;
        float hold = Mathf.Max(0f, totalDuration - fadeOutDuration - fadeInDuration);

        // 1) Keep falling naturally while the screen fades to black (the player never sees the fall stop)
        if (fader != null) yield return fader.FadeTo(1f, fadeOutDuration);

        // 1b) Screen is fully black now: freeze everything so nothing moves during the respawn
        SetMovementLocked(true);

        // 2) Black screen: clear the leftover fall speed and put the HEAD on this player's own respawn point
        //    (their last checkpoint, or the start spawn when they have none)
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

    /// <summary>Plays a clip as a flat (2D) sound, so it is heard fully and is not left behind when the rig teleports.</summary>
    private static void PlaySfx2D(AudioClip clip, float volume, float pitch, string label)
    {
        if (clip == null) return;

        var go = new GameObject("SFX_" + clip.name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 0f;
        src.playOnAwake = false;
        src.Play();
        UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.25f);

        Log($"[LocalXROriginProvider] Played {label} sound '{clip.name}' (volume {volume:F2}, pitch {pitch:F2}).");
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

        Log($"[LocalXROriginProvider] Reset {zeroed.Count} leftover velocity field(s): {string.Join(", ", zeroed)}");
    }

    // Looking up fields with reflection is slow, so it is done once per component type.
    private static readonly Dictionary<Type, FieldInfo[]> VelocityFieldCache = new Dictionary<Type, FieldInfo[]>();

    private static FieldInfo[] GetVelocityFields(Type type)
    {
        if (VelocityFieldCache.TryGetValue(type, out var cached)) return cached;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var result = new List<FieldInfo>();

        for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour); t = t.BaseType)
        {
            foreach (var f in t.GetFields(flags))
            {
                if (f.IsPublic || f.IsDefined(typeof(SerializeField), true)) continue; // settings, not runtime state
                if (f.Name.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (f.FieldType != typeof(Vector3) && f.FieldType != typeof(float)) continue;
                result.Add(f);
            }
        }

        var array = result.ToArray();
        VelocityFieldCache[type] = array;
        return array;
    }

    private static void ZeroRuntimeVelocity(Behaviour b, List<string> zeroed)
    {
        foreach (var f in GetVelocityFields(b.GetType()))
        {
            if (f.FieldType == typeof(Vector3)) f.SetValue(b, Vector3.zero);
            else f.SetValue(b, 0f);

            zeroed.Add(f.DeclaringType.Name + "." + f.Name);
        }
    }

    // ------------------------------------------------------------------ lock

    /// <summary>
    /// Locks/unlocks everything that moves the rig (move/turn/gravity providers, VRJumpMotor, CharacterController),
    /// and holds the rig in place, so the player cannot fall or walk while the screen is black (or while frozen).
    /// Only components that were enabled before are switched back on.
    /// </summary>
    public void SetMovementLocked(bool locked)
    {
        if (locked)
        {
            if (_locked.Count > 0 || _pinned) return;

            foreach (var b in GetLockCandidates())
            {
                if (b != null && b.isActiveAndEnabled)
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

            Log($"[LocalXROriginProvider] Movement locked ({_locked.Count} component(s) disabled).");
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

            Log("[LocalXROriginProvider] Movement unlocked.");
        }
    }

    // The movement components of the rig are searched ONCE and remembered, so freezing a player
    // (which happens on every hit) no longer scans the whole rig each time.
    private Behaviour[] _lockCandidates;

    private Behaviour[] GetLockCandidates()
    {
        bool stale = _lockCandidates == null;
        if (!stale)
        {
            for (int i = 0; i < _lockCandidates.Length; i++)
                if (_lockCandidates[i] == null) { stale = true; break; }   // something was destroyed: search again
        }

        if (stale)
        {
            var list = new List<Behaviour>();
            foreach (var b in Root.root.GetComponentsInChildren<Behaviour>(true))
                if (IsLockType(b)) list.Add(b);
            _lockCandidates = list.ToArray();
        }
        return _lockCandidates;
    }

    private static bool IsLockType(Behaviour b)
    {
        if (b == null) return false;
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
            Log($"[LocalXROriginProvider] Spawn snapped to ground '{hitName}': {position} -> {snapped}");
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

        Log(sb.ToString());
    }
}
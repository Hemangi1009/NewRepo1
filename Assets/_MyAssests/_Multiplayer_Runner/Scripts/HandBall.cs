//using System;
//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

///// <summary>
///// The networked ball. Put this on the ROOT of the ball prefab together with:
/////   - NetworkObject
/////   - NetworkTransform
///// and put your VFX as a CHILD of the root.
/////
///// Life of a ball:
/////   1) Charging : spawned on the player's hand, starts tiny and grows to its end scale (everyone sees this).
/////   2) Thrown   : flies straight (launch sound plays on every machine), hits players / surfaces,
/////                 or disappears after maxDistance.
/////   3) Hit      : visuals hide, every machine plays the hit VFX prefab and the blast sound at the hit point,
/////                 and the player who was hit gets the effect chosen below (push back / freeze + haptics).
/////
///// The player who throws the ball is the state authority: that client moves the ball and does all hit tests.
///// Hits are announced to every client with an RPC. The effect on a hit player is applied by THAT player's own
///// machine (each player controls their own rig), so nothing needs to be synced.
/////
///// PERFORMANCE VERSION: hit VFX and sounds are pooled (nothing is created or destroyed on a hit),
///// logging is off by default, and the flight / hit test code no longer allocates memory every frame.
///// </summary>
//public class HandBall : NetworkBehaviour
//{
//    public enum HitType { Player = 0, Surface = 1 }
//    public enum PlayerHitEffect { None = 0, Knockback = 1, Freeze = 2 }
//    public enum EffectAlignment { UpIsSurfaceNormal = 0, ForwardIsSurfaceNormal = 1, NoRotation = 2 }
//    private enum BallState { Charging = 0, Thrown = 1, Hit = 2 }

//    [Header("Info")]
//    [SerializeField] private string ballName = "Ball";
//    [Tooltip("Print debug messages (editor only). Keep OFF while testing performance: Debug.Log is slow, especially on Quest.")]
//    [SerializeField] private bool verboseLogs = false;

//    [Header("Growing on the hand")]
//    [SerializeField] private float startScale = 0.05f;
//    [SerializeField] private float endScale = 1f;
//    [Tooltip("Seconds the ball needs to grow from start scale to end scale (use 1 - 2).")]
//    [SerializeField] private float growTime = 1.5f;

//    [Header("Throw")]
//    [SerializeField] private float throwSpeed = 12f;
//    [Tooltip("The ball disappears after flying this far without hitting anything.")]
//    [SerializeField] private float maxDistance = 15f;

//    [Header("Hit detection")]
//    [Tooltip("Size of the ball for hit tests (meters).")]
//    [SerializeField] private float hitRadius = 0.25f;
//    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
//    [Tooltip("How long the object stays alive after a hit so every client receives the hit message.")]
//    [SerializeField] private float hitLingerTime = 1.5f;

//    [Header("Optional: effect spawned on every client at the hit point")]
//    [Tooltip("A normal prefab (no NetworkObject needed). It is pooled and reused on EVERY machine where the ball hits.")]
//    [SerializeField] private GameObject hitEffectPrefab;
//    [Tooltip("Seconds until the hit effect is switched off again. Make it at least as long as the effect.")]
//    [SerializeField] private float hitEffectLifetime = 3f;
//    [Tooltip("UpIsSurfaceNormal: the effect's up axis points away from the surface (good for ground blasts). " +
//             "ForwardIsSurfaceNormal: its forward axis points away from the surface. NoRotation: it keeps the prefab's rotation.")]
//    [SerializeField] private EffectAlignment effectAlignment = EffectAlignment.UpIsSurfaceNormal;

//    [Header("Hit effect for the player who GOT HIT (what they see on their own screen)")]
//    [Tooltip("ON: the hit player also sees a blast. It is kept light so Quest does not lag (see the 3 settings below). " +
//             "OFF: the hit player sees no blast at all.")]
//    [SerializeField] private bool showHitEffectToHitPlayer = true;
//    [Tooltip("Optional: a lighter version of the blast (fewer / smaller particles) used ONLY for the hit player. " +
//             "Leave empty to use the normal Hit Effect Prefab.")]
//    [SerializeField] private GameObject victimEffectPrefab;
//    [Tooltip("The blast is placed at least this far (meters) from the hit player's eyes, toward the thrower, so particles do not fill the whole view. " +
//             "Bigger = less lag. Smaller = blast closer to the face.")]
//    [SerializeField] private float victimEffectDistance = 1.5f;
//    [Tooltip("Size of the blast for the hit player. 1 = normal, 0.5 = half size. Smaller = less lag.")]
//    [Range(0.1f, 1f)]
//    [SerializeField] private float victimEffectScale = 0.5f;

//    [Header("Sounds")]
//    [Tooltip("Played at the ball when it is thrown, on every machine. Leave empty for no sound.")]
//    [SerializeField] private AudioClip launchSfx;
//    [Range(0f, 1f)]
//    [SerializeField] private float launchSfxVolume = 1f;
//    [Tooltip("Played at the hit point when the ball blasts, on every machine. Leave empty for no sound.")]
//    [SerializeField] private AudioClip hitSfx;
//    [Range(0f, 1f)]
//    [SerializeField] private float hitSfxVolume = 1f;
//    [Tooltip("The sounds are 3D: players farther away than this (meters) do not hear them.")]
//    [SerializeField] private float sfxMaxDistance = 30f;

//    [Header("What happens to a player this ball hits (runs on that player's own machine)")]
//    [Tooltip("None: nothing. Knockback: the player is pushed back (like a stumble). Freeze: the player cannot move for 'Freeze Seconds'.")]
//    [SerializeField] private PlayerHitEffect playerHitEffect = PlayerHitEffect.Knockback;
//    [Tooltip("Knockback: how far the player is pushed (meters), in the direction the ball was flying.")]
//    [SerializeField] private float knockbackDistance = 1.5f;
//    [Tooltip("Knockback: seconds the push lasts.")]
//    [SerializeField] private float knockbackDuration = 0.35f;
//    [Tooltip("Freeze: seconds the player stays frozen on the spot.")]
//    [SerializeField] private float freezeSeconds = 3f;
//    [Tooltip("Controller vibration for the player who is hit. 0 = none, 1 = strongest.")]
//    [Range(0f, 1f)]
//    [SerializeField] private float hitHapticStrength = 1f;
//    [Tooltip("Seconds the vibration lasts (it fades out).")]
//    [SerializeField] private float hitHapticDuration = 0.6f;

//    // -------- networked state (written by the owner, read by everyone)
//    [Networked] public int NetState { get; set; }
//    [Networked] public float Charge { get; set; }

//    // -------- owner-only values
//    private BallState _state = BallState.Charging;
//    private float _charge;
//    private bool _fullChargeLogged;
//    private Transform _hand;
//    private Vector3 _handOffset;
//    private Vector3 _dir;
//    private float _traveled;
//    private float _despawnAt;
//    private Vector3 _pos;          // the REAL position of the ball while flying (owner only)

//    private bool _hidden;
//    private int _prevState = -1;   // last state seen by Render(), used to play the launch sound once

//    // -------- shared buffers (no memory is allocated per frame)
//    private const int CastBufferSize = 32;
//    private static readonly RaycastHit[] CastBuffer = new RaycastHit[CastBufferSize];
//    private static readonly RaycastDistanceComparer DistanceComparer = new RaycastDistanceComparer();

//    private class RaycastDistanceComparer : IComparer<RaycastHit>
//    {
//        public int Compare(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); }
//    }

//    private class AvatarEntry
//    {
//        public NetworkAvatarSetup avatar;
//        public Renderer[] renderers;
//    }
//    // one avatar list shared by all balls, so several balls do not each search the scene
//    private static readonly List<AvatarEntry> SharedAvatars = new List<AvatarEntry>();
//    private static float _nextAvatarRefresh;
//    private readonly List<NetworkAvatarSetup> _targetAvatars = new List<NetworkAvatarSetup>(8);
//    private readonly List<Bounds> _targetBounds = new List<Bounds>(8);

//    public bool IsFullyCharged => _charge >= 1f;
//    public string BallName => ballName;

//    [System.Diagnostics.Conditional("UNITY_EDITOR")]
//    private void Log(string msg)
//    {
//        if (verboseLogs) Debug.Log(msg);
//    }

//    // ------------------------------------------------------------------ spawn

//    public override void Spawned()
//    {
//        ApplyScale(0f);
//        _pos = transform.position;

//        // get everything ready now (while the ball is only growing on the hand), so the hit itself costs nothing
//        if (hitEffectPrefab != null) HandBallFxPool.Prewarm(hitEffectPrefab, hitEffectLifetime);
//        if (victimEffectPrefab != null) HandBallFxPool.Prewarm(victimEffectPrefab, hitEffectLifetime);
//        if (hitSfx != null) hitSfx.LoadAudioData();
//        if (launchSfx != null) launchSfx.LoadAudioData();
//        HandBallSfxPool.EnsureCreated();

//        if (Object.HasStateAuthority)
//        {
//            _state = BallState.Charging;
//            _charge = 0f;
//            Log($"[HandBall:{ballName}] Spawned on the OWNER client (owner={Object.StateAuthority}, id={Object.Id}).");
//        }
//        else
//        {
//            Log($"[HandBall:{ballName}] Spawned on a REMOTE client (owner={Object.StateAuthority}, me={Runner.LocalPlayer}, id={Object.Id}).");
//        }
//    }

//    /// <summary>Called by the launcher right after spawning (owner only).</summary>
//    public void Begin(Transform hand, Vector3 handOffset)
//    {
//        _hand = hand;
//        _handOffset = handOffset;
//        FollowHand();
//        Log($"[HandBall:{ballName}] Charging started on hand '{(hand != null ? hand.name : "NONE")}'. Grows from {startScale} to {endScale} in {growTime}s.");
//    }

//    // ------------------------------------------------------------------ owner API

//    /// <summary>Throws the ball in a straight line (owner only).</summary>
//    public void Throw(Vector3 direction)
//    {
//        if (!Object.HasStateAuthority || _state != BallState.Charging) return;

//        _pos = transform.position;
//        _dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
//        _traveled = 0f;
//        _state = BallState.Thrown;
//        Log($"[HandBall:{ballName}] THROWN from {_pos} dir={_dir} speed={throwSpeed} maxDistance={maxDistance} scale={_charge:F2}.");
//    }

//    /// <summary>Removes the ball without throwing it (owner only).</summary>
//    public void Cancel(string reason)
//    {
//        if (!Object.HasStateAuthority) return;
//        Log($"[HandBall:{ballName}] CANCELLED ({reason}) at charge {_charge:F2}.");
//        DespawnSelf();
//    }

//    // ------------------------------------------------------------------ per frame

//    private void Update()
//    {
//        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

//        switch (_state)
//        {
//            case BallState.Charging: UpdateCharging(Time.deltaTime); break;
//            case BallState.Thrown: UpdateThrown(Time.deltaTime); break;
//            case BallState.Hit:
//                if (Time.time >= _despawnAt) DespawnSelf();
//                break;
//        }
//    }

//    private void LateUpdate()
//    {
//        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

//        if (_state == BallState.Charging)
//        {
//            FollowHand(); // after hand tracking has been applied this frame
//        }
//        else
//        {
//            // Thrown / Hit: force the real position AFTER NetworkTransform has done its work this frame
//            transform.position = _pos;
//        }
//    }

//    // Copies the owner's values into the network state.
//    public override void FixedUpdateNetwork()
//    {
//        if (!Object.HasStateAuthority) return;
//        NetState = (int)_state;
//        Charge = _charge;
//    }

//    // Runs on every client: scale, launch sound and visibility follow the networked values.
//    public override void Render()
//    {
//        bool owner = Object.HasStateAuthority;
//        ApplyScale(owner ? _charge : Charge);

//        int s = owner ? (int)_state : NetState;

//        // launch sound: exactly once, when the state changes from Charging to Thrown (not for balls seen already flying)
//        if (s == (int)BallState.Thrown && _prevState == (int)BallState.Charging)
//        {
//            if (launchSfx != null)
//                HandBallSfxPool.PlayAt(launchSfx, transform.position, launchSfxVolume, sfxMaxDistance);
//        }
//        _prevState = s;

//        if (s == (int)BallState.Hit && !_hidden) HideVisuals();
//    }

//    // ------------------------------------------------------------------ charging

//    private void UpdateCharging(float dt)
//    {
//        if (_hand == null)
//        {
//            Debug.LogWarning($"[HandBall:{ballName}] No hand assigned while charging, cancelling.");
//            DespawnSelf();
//            return;
//        }

//        _charge = Mathf.Clamp01(_charge + dt / Mathf.Max(0.05f, growTime));

//        if (_charge >= 1f && !_fullChargeLogged)
//        {
//            _fullChargeLogged = true;
//            Log($"[HandBall:{ballName}] FULLY CHARGED, ready to throw.");
//        }
//    }

//    private void FollowHand()
//    {
//        if (_hand == null) return;
//        _pos = _hand.TransformPoint(_handOffset);
//        transform.SetPositionAndRotation(_pos, _hand.rotation);
//    }

//    private void ApplyScale(float charge01)
//    {
//        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(charge01));
//        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);
//    }

//    // ------------------------------------------------------------------ flying + hit detection

//    private void UpdateThrown(float dt)
//    {
//        Vector3 from = _pos;
//        float stepLen = Mathf.Min(throwSpeed * dt, Mathf.Max(0f, maxDistance - _traveled));

//        // 1) nearest solid thing in the way
//        bool envFound = false;
//        float envDist = float.MaxValue;
//        RaycastHit envHit = default;
//        NetworkAvatarSetup envAvatar = null;

//        int count = Physics.SphereCastNonAlloc(from, hitRadius, _dir, CastBuffer, stepLen + 0.01f, hitMask, QueryTriggerInteraction.Ignore);
//        if (count > 1) Array.Sort(CastBuffer, 0, count, DistanceComparer);

//        for (int i = 0; i < count; i++)
//        {
//            RaycastHit h = CastBuffer[i];
//            if (h.distance <= 0.0001f && h.point == Vector3.zero) continue;     // started inside this collider, ignore
//            if (IsOwnRig(h.collider)) continue;                                  // never hit the thrower
//            if (h.collider.GetComponentInParent<HandBall>() != null) continue;   // other balls

//            var av = h.collider.GetComponentInParent<NetworkAvatarSetup>();
//            if (av != null && av.Object != null && av.Object.HasStateAuthority) continue; // own avatar

//            envFound = true;
//            envDist = h.distance;
//            envHit = h;
//            envAvatar = av;
//            break;
//        }

//        // 2) other players (their avatars may have no colliders, so test against their body bounds)
//        float limit = envFound ? Mathf.Min(stepLen, envDist) : stepLen;
//        if (TryHitAvatarBounds(from, _dir, limit, out NetworkAvatarSetup victim, out float victimDist))
//        {
//            Vector3 point = from + _dir * victimDist;
//            _pos = point;
//            transform.position = point;
//            RegisterHit(HitType.Player, point, -_dir, victim.Object.StateAuthority,
//                        "avatar '" + victim.name + "' (bounds test)");
//            return;
//        }

//        if (envFound)
//        {
//            _pos = from + _dir * envDist;
//            transform.position = _pos;

//            if (envAvatar != null)
//            {
//                RegisterHit(HitType.Player, envHit.point, envHit.normal, envAvatar.Object.StateAuthority,
//                            "avatar collider '" + envHit.collider.name + "'");
//            }
//            else
//            {
//                RegisterHit(HitType.Surface, envHit.point, envHit.normal, PlayerRef.None,
//                            "collider '" + envHit.collider.name + "'");
//            }
//            return;
//        }

//        // 3) nothing hit: move on
//        _pos = from + _dir * stepLen;
//        transform.position = _pos;
//        _traveled += stepLen;

//        if (_traveled >= maxDistance - 0.0001f)
//        {
//            Log($"[HandBall:{ballName}] EXPIRED after {_traveled:F1} m without hitting anything, removing it.");
//            DespawnSelf();
//        }
//    }

//    private bool IsOwnRig(Collider c)
//    {
//        var xr = LocalXROriginProvider.Instance;
//        return xr != null && c.transform.IsChildOf(xr.Root);
//    }

//    private static void RefreshAvatars()
//    {
//        if (Time.time < _nextAvatarRefresh) return;
//        _nextAvatarRefresh = Time.time + 1f;

//        SharedAvatars.Clear();
//        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
//        {
//            if (a.Object == null || !a.Object.IsValid) continue;

//            var list = new List<Renderer>();
//            foreach (var r in a.GetComponentsInChildren<Renderer>())
//                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);

//            SharedAvatars.Add(new AvatarEntry { avatar = a, renderers = list.ToArray() });
//        }
//    }

//    private bool TryHitAvatarBounds(Vector3 from, Vector3 dir, float length, out NetworkAvatarSetup victim, out float distance)
//    {
//        victim = null;
//        distance = 0f;
//        RefreshAvatars();

//        // body bounds of every OTHER player
//        _targetAvatars.Clear();
//        _targetBounds.Clear();
//        for (int k = 0; k < SharedAvatars.Count; k++)
//        {
//            var e = SharedAvatars[k];
//            if (e.avatar == null || e.avatar.Object == null || !e.avatar.Object.IsValid) continue;
//            if (e.avatar.Object.HasStateAuthority) continue;               // the thrower's own avatar
//            if (e.renderers.Length == 0 || e.renderers[0] == null) continue;

//            Bounds b = e.renderers[0].bounds;
//            for (int i = 1; i < e.renderers.Length; i++)
//                if (e.renderers[i] != null) b.Encapsulate(e.renderers[i].bounds);

//            _targetAvatars.Add(e.avatar);
//            _targetBounds.Add(b);
//        }
//        if (_targetAvatars.Count == 0) return false;

//        // walk along the path in small steps so a fast ball cannot skip through a player
//        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.1f));
//        float rSqr = hitRadius * hitRadius;
//        for (int i = 0; i <= steps; i++)
//        {
//            float d = length * i / steps;
//            Vector3 p = from + dir * d;
//            for (int t = 0; t < _targetBounds.Count; t++)
//            {
//                if (_targetBounds[t].SqrDistance(p) <= rSqr)
//                {
//                    victim = _targetAvatars[t];
//                    distance = d;
//                    return true;
//                }
//            }
//        }
//        return false;
//    }

//    // ------------------------------------------------------------------ hit handling

//    private void RegisterHit(HitType type, Vector3 point, Vector3 normal, PlayerRef victim, string what)
//    {
//        _state = BallState.Hit;
//        _despawnAt = Time.time + hitLingerTime;

//        Log($"[HandBall:{ballName}] HIT {type} -> {what}. point={point} victim={victim}.");

//        RPC_BallHit((int)type, point, normal, _dir, victim);
//        HideVisuals();
//    }

//    /// <summary>Sent by the owner. Runs on EVERY client (including the owner).</summary>
//    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
//    public void RPC_BallHit(int hitType, Vector3 point, Vector3 normal, Vector3 direction, PlayerRef victim)
//    {
//        HitType type = (HitType)hitType;
//        bool victimIsMe = victim != PlayerRef.None && Runner != null && victim == Runner.LocalPlayer;

//        // 1) hit VFX (taken from the pool, nothing is created)
//        if (victimIsMe)
//        {
//            // the hit player sees a lighter blast, pushed away from their eyes
//            GameObject prefab = victimEffectPrefab != null ? victimEffectPrefab : hitEffectPrefab;
//            if (showHitEffectToHitPlayer && prefab != null)
//            {
//                Vector3 pos = PushAwayFromHead(point, direction);
//                HandBallFxPool.Play(prefab, pos, EffectRotation(normal), hitEffectLifetime, victimEffectScale);
//            }
//        }
//        else if (hitEffectPrefab != null)
//        {
//            HandBallFxPool.Play(hitEffectPrefab, point, EffectRotation(normal), hitEffectLifetime, 1f);
//        }

//        // 2) blast sound at the hit point (taken from the pool, nothing is created)
//        if (hitSfx != null)
//            HandBallSfxPool.PlayAt(hitSfx, point, hitSfxVolume, sfxMaxDistance);

//        // 3) gameplay
//        if (type == HitType.Player) OnPlayerHit(victim, victimIsMe, point, direction);
//        else OnSurfaceHit(point, normal);
//    }

//    // ================================================================== WHAT HAPPENS ON A HIT
//    // These run on EVERY client when the ball hits something.

//    /// <summary>
//    /// The ball hit another player. victimIsMe = true ONLY on the machine of the player who was hit,
//    /// so the effect is applied there, to that player's own rig.
//    /// </summary>
//    private void OnPlayerHit(PlayerRef victim, bool victimIsMe, Vector3 point, Vector3 direction)
//    {
//        if (!victimIsMe) return; // other machines only show the VFX and sound

//        var xr = LocalXROriginProvider.Instance;
//        if (xr == null)
//        {
//            Debug.LogWarning($"[HandBall:{ballName}] You were hit, but LocalXROriginProvider.Instance is missing, so the hit effect cannot be applied.");
//            return;
//        }

//        if (hitHapticStrength > 0f)
//            xr.PlayHaptics(hitHapticStrength, hitHapticDuration);

//        switch (playerHitEffect)
//        {
//            case PlayerHitEffect.Knockback:
//                xr.Knockback(direction, knockbackDistance, knockbackDuration);
//                break;

//            case PlayerHitEffect.Freeze:
//                xr.Freeze(freezeSeconds);
//                break;
//        }
//    }

//    /// <summary>The ball hit a wall, floor or any other object.</summary>
//    private void OnSurfaceHit(Vector3 point, Vector3 normal)
//    {
//        // add what should happen when the ball hits the world (decals, shaking, ...)
//    }

//    // ------------------------------------------------------------------ helpers

//    /// <summary>Moves the blast so it is at least victimEffectDistance from the local player's eyes, on the side the ball came from.</summary>
//    private Vector3 PushAwayFromHead(Vector3 point, Vector3 flyDirection)
//    {
//        Camera cam = Camera.main;
//        Vector3 head = cam != null ? cam.transform.position : point;

//        Vector3 toPoint = point - head;
//        float dist = toPoint.magnitude;
//        if (dist >= victimEffectDistance) return point;

//        Vector3 dir = dist > 0.05f ? toPoint / dist : -flyDirection;   // away from the eyes, toward where the ball came from
//        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
//        return head + dir.normalized * victimEffectDistance;
//    }

//    private Quaternion EffectRotation(Vector3 normal)
//    {
//        if (normal.sqrMagnitude < 0.0001f) return Quaternion.identity;

//        switch (effectAlignment)
//        {
//            case EffectAlignment.UpIsSurfaceNormal: return Quaternion.FromToRotation(Vector3.up, normal.normalized);
//            case EffectAlignment.ForwardIsSurfaceNormal: return Quaternion.LookRotation(normal);
//            default: return Quaternion.identity;
//        }
//    }

//    private void HideVisuals()
//    {
//        if (_hidden) return;
//        _hidden = true;
//        foreach (Transform child in transform)
//            child.gameObject.SetActive(false);
//    }

//    private void DespawnSelf()
//    {
//        if (Runner != null && Object != null && Object.IsValid)
//            Runner.Despawn(Object);
//    }
//}

//// ====================================================================== pools (same file, no setup needed)

///// <summary>Reuses hit effect objects instead of creating and destroying one on every hit.</summary>
//public static class HandBallFxPool
//{
//    private static readonly Dictionary<GameObject, Queue<HandBallPooledFx>> Pools =
//        new Dictionary<GameObject, Queue<HandBallPooledFx>>();

//    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
//    private static void ResetStatics() { Pools.Clear(); }

//    private static Queue<HandBallPooledFx> GetQueue(GameObject prefab)
//    {
//        if (!Pools.TryGetValue(prefab, out var q))
//        {
//            q = new Queue<HandBallPooledFx>();
//            Pools[prefab] = q;
//        }
//        return q;
//    }

//    private static HandBallPooledFx Create(GameObject prefab, Queue<HandBallPooledFx> home)
//    {
//        var go = UnityEngine.Object.Instantiate(prefab);
//        var fx = go.GetComponent<HandBallPooledFx>();
//        if (fx == null) fx = go.AddComponent<HandBallPooledFx>();
//        fx.Setup(home);
//        go.SetActive(false);
//        return fx;
//    }

//    /// <summary>Makes sure a couple of effect objects exist already. Called while the ball is still charging.</summary>
//    public static void Prewarm(GameObject prefab, float lifetime)
//    {
//        var q = GetQueue(prefab);
//        while (q.Count < 2) q.Enqueue(Create(prefab, q));
//    }

//    public static void Play(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, float scale)
//    {
//        var q = GetQueue(prefab);

//        HandBallPooledFx fx = null;
//        while (q.Count > 0 && fx == null) fx = q.Dequeue();   // skips objects destroyed by a scene change
//        if (fx == null) fx = Create(prefab, q);

//        fx.Play(position, rotation, lifetime, scale);
//    }
//}

///// <summary>Added automatically to pooled hit effects. Switches the effect off after its lifetime and returns it to the pool.</summary>
//public class HandBallPooledFx : MonoBehaviour
//{
//    private Queue<HandBallPooledFx> _home;
//    private ParticleSystem[] _systems;
//    private float _returnAt;
//    private bool _playing;
//    private Vector3 _baseScale = Vector3.one;

//    public void Setup(Queue<HandBallPooledFx> home)
//    {
//        _home = home;
//        _baseScale = transform.localScale;
//        _systems = GetComponentsInChildren<ParticleSystem>(true);
//    }

//    public void Play(Vector3 position, Quaternion rotation, float lifetime, float scale)
//    {
//        transform.SetPositionAndRotation(position, rotation);
//        transform.localScale = _baseScale * scale;
//        gameObject.SetActive(true);
//        for (int i = 0; i < _systems.Length; i++)
//        {
//            _systems[i].Clear(true);
//            _systems[i].Play(true);
//        }
//        _returnAt = Time.time + Mathf.Max(0.1f, lifetime);
//        _playing = true;
//    }

//    private void Update()
//    {
//        if (_playing && Time.time >= _returnAt) Release();
//    }

//    private void Release()
//    {
//        _playing = false;
//        for (int i = 0; i < _systems.Length; i++)
//            _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
//        gameObject.SetActive(false);
//        _home.Enqueue(this);
//    }
//}

///// <summary>A small fixed set of 3D audio sources that are reused for every ball sound.</summary>
//public static class HandBallSfxPool
//{
//    private const int PoolSize = 8;
//    private static AudioSource[] _sources;
//    private static int _next;

//    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
//    private static void ResetStatics() { _sources = null; _next = 0; }

//    public static void EnsureCreated()
//    {
//        if (_sources != null && _sources[0] != null) return;

//        var root = new GameObject("HandBallSfxPool");
//        UnityEngine.Object.DontDestroyOnLoad(root);

//        _sources = new AudioSource[PoolSize];
//        for (int i = 0; i < PoolSize; i++)
//        {
//            var child = new GameObject("Sfx_" + i);
//            child.transform.SetParent(root.transform, false);
//            var src = child.AddComponent<AudioSource>();
//            src.playOnAwake = false;
//            src.loop = false;
//            src.spatialBlend = 1f;                      // 3D: louder when close, quieter when far
//            src.rolloffMode = AudioRolloffMode.Linear;
//            src.minDistance = 2f;
//            _sources[i] = src;
//        }
//    }

//    public static void PlayAt(AudioClip clip, Vector3 position, float volume, float maxDistance)
//    {
//        if (clip == null) return;
//        EnsureCreated();

//        // prefer a source that is free, otherwise take over the oldest one
//        AudioSource src = null;
//        for (int i = 0; i < PoolSize; i++)
//        {
//            var s = _sources[(_next + i) % PoolSize];
//            if (!s.isPlaying) { src = s; _next = (_next + i + 1) % PoolSize; break; }
//        }
//        if (src == null)
//        {
//            src = _sources[_next];
//            _next = (_next + 1) % PoolSize;
//        }

//        // every source sits on its own child object, so each sound has its own 3D position
//        src.transform.position = position;
//        src.maxDistance = Mathf.Max(3f, maxDistance);
//        src.volume = volume;
//        src.clip = clip;
//        src.Play();
//    }
//}

//using System;
//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

///// <summary>
///// The networked ball. Put this on the ROOT of the ball prefab together with:
/////   - NetworkObject
/////   - NetworkTransform
///// and put your VFX as a CHILD of the root.
/////
///// Life of a ball:
/////   1) Charging : spawned on the player's hand, starts tiny and grows to its end scale (everyone sees this).
/////   2) Thrown   : flies straight, hits players / surfaces, or disappears after maxDistance.
/////   3) Hit      : visuals hide, every client gets a callback, the object is removed a moment later.
/////
///// The player who throws the ball is the state authority: that client moves the ball and does all hit tests.
///// Hits are announced to every client with an RPC.
/////
///// FIX: the owner keeps the real ball position in _pos and writes it to the transform in LateUpdate,
///// so NetworkTransform can no longer pull the ball back to where it was released.
///// </summary>
//public class HandBall : NetworkBehaviour
//{
//    public enum HitType { Player = 0, Surface = 1 }
//    private enum BallState { Charging = 0, Thrown = 1, Hit = 2 }

//    [Header("Info")]
//    [SerializeField] private string ballName = "Ball";

//    [Header("Growing on the hand")]
//    [SerializeField] private float startScale = 0.05f;
//    [SerializeField] private float endScale = 1f;
//    [Tooltip("Seconds the ball needs to grow from start scale to end scale (use 1 - 2).")]
//    [SerializeField] private float growTime = 1.5f;

//    [Header("Throw")]
//    [SerializeField] private float throwSpeed = 12f;
//    [Tooltip("The ball disappears after flying this far without hitting anything.")]
//    [SerializeField] private float maxDistance = 15f;

//    [Header("Hit detection")]
//    [Tooltip("Size of the ball for hit tests (meters).")]
//    [SerializeField] private float hitRadius = 0.25f;
//    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
//    [Tooltip("How long the object stays alive after a hit so every client receives the hit message.")]
//    [SerializeField] private float hitLingerTime = 1.5f;

//    [Header("Optional: effect spawned on every client at the hit point")]
//    [SerializeField] private GameObject hitEffectPrefab;
//    [SerializeField] private float hitEffectLifetime = 3f;

//    // -------- networked state (written by the owner, read by everyone)
//    [Networked] public int NetState { get; set; }
//    [Networked] public float Charge { get; set; }

//    // -------- owner-only values
//    private BallState _state = BallState.Charging;
//    private float _charge;
//    private bool _fullChargeLogged;
//    private Transform _hand;
//    private Vector3 _handOffset;
//    private Vector3 _dir;
//    private float _traveled;
//    private float _despawnAt;
//    private Vector3 _pos;          // the REAL position of the ball while flying (owner only)

//    private bool _hidden;

//    private class AvatarEntry
//    {
//        public NetworkAvatarSetup avatar;
//        public Renderer[] renderers;
//    }
//    private readonly List<AvatarEntry> _avatars = new List<AvatarEntry>();
//    private float _nextAvatarRefresh;

//    public bool IsFullyCharged => _charge >= 1f;
//    public string BallName => ballName;

//    // ------------------------------------------------------------------ spawn

//    public override void Spawned()
//    {
//        ApplyScale(0f);
//        _pos = transform.position;

//        if (Object.HasStateAuthority)
//        {
//            _state = BallState.Charging;
//            _charge = 0f;
//            Debug.Log($"[HandBall:{ballName}] Spawned on the OWNER client (owner={Object.StateAuthority}, id={Object.Id}).");
//        }
//        else
//        {
//            Debug.Log($"[HandBall:{ballName}] Spawned on a REMOTE client (owner={Object.StateAuthority}, me={Runner.LocalPlayer}, id={Object.Id}).");
//        }
//    }

//    /// <summary>Called by the launcher right after spawning (owner only).</summary>
//    public void Begin(Transform hand, Vector3 handOffset)
//    {
//        _hand = hand;
//        _handOffset = handOffset;
//        FollowHand();
//        Debug.Log($"[HandBall:{ballName}] Charging started on hand '{(hand != null ? hand.name : "NONE")}'. Grows from {startScale} to {endScale} in {growTime}s.");
//    }

//    // ------------------------------------------------------------------ owner API

//    /// <summary>Throws the ball in a straight line (owner only).</summary>
//    public void Throw(Vector3 direction)
//    {
//        if (!Object.HasStateAuthority || _state != BallState.Charging) return;

//        _pos = transform.position;
//        _dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
//        _traveled = 0f;
//        _state = BallState.Thrown;
//        Debug.Log($"[HandBall:{ballName}] THROWN from {_pos} dir={_dir} speed={throwSpeed} maxDistance={maxDistance} scale={_charge:F2}.");
//    }

//    /// <summary>Removes the ball without throwing it (owner only).</summary>
//    public void Cancel(string reason)
//    {
//        if (!Object.HasStateAuthority) return;
//        Debug.Log($"[HandBall:{ballName}] CANCELLED ({reason}) at charge {_charge:F2}.");
//        DespawnSelf();
//    }

//    // ------------------------------------------------------------------ per frame

//    private void Update()
//    {
//        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

//        switch (_state)
//        {
//            case BallState.Charging: UpdateCharging(Time.deltaTime); break;
//            case BallState.Thrown: UpdateThrown(Time.deltaTime); break;
//            case BallState.Hit:
//                if (Time.time >= _despawnAt) DespawnSelf();
//                break;
//        }
//    }

//    private void LateUpdate()
//    {
//        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

//        if (_state == BallState.Charging)
//        {
//            FollowHand(); // after hand tracking has been applied this frame
//        }
//        else
//        {
//            // Thrown / Hit: force the real position AFTER NetworkTransform has done its work this frame
//            transform.position = _pos;
//        }
//    }

//    // Copies the owner's values into the network state.
//    public override void FixedUpdateNetwork()
//    {
//        if (!Object.HasStateAuthority) return;
//        NetState = (int)_state;
//        Charge = _charge;
//    }

//    // Runs on every client: scale and visibility follow the networked values.
//    public override void Render()
//    {
//        bool owner = Object.HasStateAuthority;
//        ApplyScale(owner ? _charge : Charge);

//        BallState s = owner ? _state : (BallState)NetState;
//        if (s == BallState.Hit && !_hidden) HideVisuals();
//    }

//    // ------------------------------------------------------------------ charging

//    private void UpdateCharging(float dt)
//    {
//        if (_hand == null)
//        {
//            Debug.LogWarning($"[HandBall:{ballName}] No hand assigned while charging, cancelling.");
//            DespawnSelf();
//            return;
//        }

//        _charge = Mathf.Clamp01(_charge + dt / Mathf.Max(0.05f, growTime));

//        if (_charge >= 1f && !_fullChargeLogged)
//        {
//            _fullChargeLogged = true;
//            Debug.Log($"[HandBall:{ballName}] FULLY CHARGED, ready to throw.");
//        }
//    }

//    private void FollowHand()
//    {
//        if (_hand == null) return;
//        _pos = _hand.TransformPoint(_handOffset);
//        transform.SetPositionAndRotation(_pos, _hand.rotation);
//    }

//    private void ApplyScale(float charge01)
//    {
//        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(charge01));
//        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);
//    }

//    // ------------------------------------------------------------------ flying + hit detection

//    private void UpdateThrown(float dt)
//    {
//        Vector3 from = _pos;
//        float stepLen = Mathf.Min(throwSpeed * dt, Mathf.Max(0f, maxDistance - _traveled));

//        // 1) nearest solid thing in the way
//        bool envFound = false;
//        float envDist = float.MaxValue;
//        RaycastHit envHit = default;
//        NetworkAvatarSetup envAvatar = null;

//        var hits = Physics.SphereCastAll(from, hitRadius, _dir, stepLen + 0.01f, hitMask, QueryTriggerInteraction.Ignore);
//        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

//        foreach (var h in hits)
//        {
//            if (h.distance <= 0.0001f && h.point == Vector3.zero) continue;     // started inside this collider, ignore
//            if (IsOwnRig(h.collider)) continue;                                  // never hit the thrower
//            if (h.collider.GetComponentInParent<HandBall>() != null) continue;   // other balls

//            var av = h.collider.GetComponentInParent<NetworkAvatarSetup>();
//            if (av != null && av.Object != null && av.Object.HasStateAuthority) continue; // own avatar

//            envFound = true;
//            envDist = h.distance;
//            envHit = h;
//            envAvatar = av;
//            break;
//        }

//        // 2) other players (their avatars may have no colliders, so test against their body bounds)
//        float limit = envFound ? Mathf.Min(stepLen, envDist) : stepLen;
//        if (TryHitAvatarBounds(from, _dir, limit, out NetworkAvatarSetup victim, out float victimDist))
//        {
//            Vector3 point = from + _dir * victimDist;
//            _pos = point;
//            transform.position = point;
//            RegisterHit(HitType.Player, point, -_dir, victim.Object.StateAuthority,
//                        $"avatar '{victim.name}' (bounds test)");
//            return;
//        }

//        if (envFound)
//        {
//            _pos = from + _dir * envDist;
//            transform.position = _pos;

//            if (envAvatar != null)
//            {
//                RegisterHit(HitType.Player, envHit.point, envHit.normal, envAvatar.Object.StateAuthority,
//                            $"avatar collider '{envHit.collider.name}'");
//            }
//            else
//            {
//                RegisterHit(HitType.Surface, envHit.point, envHit.normal, PlayerRef.None,
//                            $"collider '{envHit.collider.name}' on object '{envHit.collider.gameObject.name}' (layer {LayerMask.LayerToName(envHit.collider.gameObject.layer)})");
//            }
//            return;
//        }

//        // 3) nothing hit: move on
//        _pos = from + _dir * stepLen;
//        transform.position = _pos;
//        _traveled += stepLen;

//        if (_traveled >= maxDistance - 0.0001f)
//        {
//            Debug.Log($"[HandBall:{ballName}] EXPIRED after {_traveled:F1} m without hitting anything, removing it.");
//            DespawnSelf();
//        }
//    }

//    private bool IsOwnRig(Collider c)
//    {
//        var xr = LocalXROriginProvider.Instance;
//        return xr != null && c.transform.IsChildOf(xr.Root);
//    }

//    private void RefreshAvatars()
//    {
//        if (Time.time < _nextAvatarRefresh) return;
//        _nextAvatarRefresh = Time.time + 0.5f;

//        _avatars.Clear();
//        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
//        {
//            if (a.Object == null || !a.Object.IsValid) continue;

//            var list = new List<Renderer>();
//            foreach (var r in a.GetComponentsInChildren<Renderer>())
//                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);

//            _avatars.Add(new AvatarEntry { avatar = a, renderers = list.ToArray() });
//        }
//    }

//    private bool TryHitAvatarBounds(Vector3 from, Vector3 dir, float length, out NetworkAvatarSetup victim, out float distance)
//    {
//        victim = null;
//        distance = 0f;
//        RefreshAvatars();

//        // body bounds of every OTHER player
//        var targets = new List<KeyValuePair<NetworkAvatarSetup, Bounds>>();
//        foreach (var e in _avatars)
//        {
//            if (e.avatar == null || e.avatar.Object == null || !e.avatar.Object.IsValid) continue;
//            if (e.avatar.Object.HasStateAuthority) continue;               // the thrower's own avatar
//            if (e.renderers.Length == 0 || e.renderers[0] == null) continue;

//            Bounds b = e.renderers[0].bounds;
//            for (int i = 1; i < e.renderers.Length; i++)
//                if (e.renderers[i] != null) b.Encapsulate(e.renderers[i].bounds);
//            targets.Add(new KeyValuePair<NetworkAvatarSetup, Bounds>(e.avatar, b));
//        }
//        if (targets.Count == 0) return false;

//        // walk along the path in small steps so a fast ball cannot skip through a player
//        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.1f));
//        float rSqr = hitRadius * hitRadius;
//        for (int i = 0; i <= steps; i++)
//        {
//            float d = length * i / steps;
//            Vector3 p = from + dir * d;
//            foreach (var t in targets)
//            {
//                if (t.Value.SqrDistance(p) <= rSqr)
//                {
//                    victim = t.Key;
//                    distance = d;
//                    return true;
//                }
//            }
//        }
//        return false;
//    }

//    // ------------------------------------------------------------------ hit handling

//    private void RegisterHit(HitType type, Vector3 point, Vector3 normal, PlayerRef victim, string what)
//    {
//        _state = BallState.Hit;
//        _despawnAt = Time.time + hitLingerTime;

//        Debug.Log($"[HandBall:{ballName}] HIT {type.ToString().ToUpper()} -> {what}. point={point} normal={normal} traveled={_traveled:F1} m thrower={Object.StateAuthority} victim={victim}.");

//        RPC_BallHit((int)type, point, normal, victim);
//        HideVisuals();
//    }

//    /// <summary>Sent by the owner. Runs on EVERY client (including the owner).</summary>
//    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
//    public void RPC_BallHit(int hitType, Vector3 point, Vector3 normal, PlayerRef victim)
//    {
//        HitType type = (HitType)hitType;
//        bool victimIsMe = victim != PlayerRef.None && Runner != null && victim == Runner.LocalPlayer;

//        Debug.Log($"[HandBall:{ballName}] (all clients) hit message received: type={type}, point={point}, victim={victim}, victimIsMe={victimIsMe}, thrower={Object.StateAuthority}.");

//        if (hitEffectPrefab != null)
//        {
//            Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
//            var fx = Instantiate(hitEffectPrefab, point, rot);
//            Destroy(fx, hitEffectLifetime);
//        }

//        if (type == HitType.Player) OnPlayerHit(victim, victimIsMe, point);
//        else OnSurfaceHit(point, normal);
//    }

//    // ================================================================== YOUR LOGIC GOES HERE
//    // These run on EVERY client when the ball hits something. Add what should happen later.

//    /// <summary>The ball hit another player. victimIsMe = true on the client of the player who was hit.</summary>
//    private void OnPlayerHit(PlayerRef victim, bool victimIsMe, Vector3 point)
//    {
//        Debug.Log($"[HandBall:{ballName}] OnPlayerHit -> victim={victim}, victimIsMe={victimIsMe}, point={point}. (nothing implemented yet)");
//        // TODO: what should happen to the hit player
//    }

//    /// <summary>The ball hit a wall, floor or any other object.</summary>
//    private void OnSurfaceHit(Vector3 point, Vector3 normal)
//    {
//        Debug.Log($"[HandBall:{ballName}] OnSurfaceHit -> point={point}, normal={normal}. (nothing implemented yet)");
//        // TODO: what should happen when the ball hits the world
//    }

//    // ------------------------------------------------------------------ helpers

//    private void HideVisuals()
//    {
//        if (_hidden) return;
//        _hidden = true;
//        foreach (Transform child in transform)
//            child.gameObject.SetActive(false);
//    }

//    private void DespawnSelf()
//    {
//        if (Runner != null && Object != null && Object.IsValid)
//            Runner.Despawn(Object);
//    }
//}

using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// The networked ball. Put this on the ROOT of the ball prefab together with:
///   - NetworkObject
///   - NetworkTransform
/// and put your VFX as a CHILD of the root.
///
/// Life of a ball:
///   1) Charging : spawned on the player's hand, starts tiny and grows to its end scale (everyone sees this).
///   2) Thrown   : flies straight (launch sound plays on every machine), hits players / surfaces,
///                 or disappears after maxDistance.
///   3) Hit      : visuals hide, every machine plays the hit VFX prefab and the blast sound at the hit point,
///                 and the player who was hit gets the effect chosen below (push back / freeze + haptics).
///
/// The player who throws the ball is the state authority: that client moves the ball and does all hit tests.
/// Hits are announced to every client with an RPC. The effect on a hit player is applied by THAT player's own
/// machine (each player controls their own rig), so nothing needs to be synced.
///
/// PERFORMANCE VERSION: hit VFX and sounds are pooled (nothing is created or destroyed on a hit),
/// logging is off by default, and the flight / hit test code no longer allocates memory every frame.
/// </summary>
public class HandBall : NetworkBehaviour
{
    public enum HitType { Player = 0, Surface = 1 }
    public enum PlayerHitEffect { None = 0, Knockback = 1, Freeze = 2 }
    public enum EffectAlignment { UpIsSurfaceNormal = 0, ForwardIsSurfaceNormal = 1, NoRotation = 2 }
    private enum BallState { Charging = 0, Thrown = 1, Hit = 2 }

    [Header("Info")]
    [SerializeField] private string ballName = "Ball";
    [Tooltip("Print debug messages (editor only). Keep OFF while testing performance: Debug.Log is slow, especially on Quest.")]
    [SerializeField] private bool verboseLogs = false;

    [Header("Growing on the hand")]
    [SerializeField] private float startScale = 0.05f;
    [SerializeField] private float endScale = 1f;
    [Tooltip("Seconds the ball needs to grow from start scale to end scale (use 1 - 2).")]
    [SerializeField] private float growTime = 1.5f;

    [Header("Throw")]
    [SerializeField] private float throwSpeed = 12f;
    [Tooltip("The ball disappears after flying this far without hitting anything.")]
    [SerializeField] private float maxDistance = 15f;

    [Header("Hit detection")]
    [Tooltip("Size of the ball for hit tests (meters).")]
    [SerializeField] private float hitRadius = 0.25f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [Tooltip("How long the object stays alive after a hit so every client receives the hit message.")]
    [SerializeField] private float hitLingerTime = 1.5f;

    [Header("Optional: effect spawned on every client at the hit point")]
    [Tooltip("A normal prefab (no NetworkObject needed). It is pooled and reused on EVERY machine where the ball hits.")]
    [SerializeField] private GameObject hitEffectPrefab;
    [Tooltip("Seconds until the hit effect is switched off again. Make it at least as long as the effect.")]
    [SerializeField] private float hitEffectLifetime = 3f;
    [Tooltip("UpIsSurfaceNormal: the effect's up axis points away from the surface (good for ground blasts). " +
             "ForwardIsSurfaceNormal: its forward axis points away from the surface. NoRotation: it keeps the prefab's rotation.")]
    [SerializeField] private EffectAlignment effectAlignment = EffectAlignment.UpIsSurfaceNormal;

    [Header("Hit effect for the player who GOT HIT (what they see on their own screen)")]
    [Tooltip("ON: the hit player also sees a blast. It is kept light so Quest does not lag (see the 3 settings below). " +
             "OFF: the hit player sees no blast at all.")]
    [SerializeField] private bool showHitEffectToHitPlayer = true;
    [Tooltip("Optional: a lighter version of the blast (fewer / smaller particles) used ONLY for the hit player. " +
             "Leave empty to use the normal Hit Effect Prefab.")]
    [SerializeField] private GameObject victimEffectPrefab;
    [Tooltip("The blast is placed at least this far (meters) from the hit player's eyes, toward the thrower, so particles do not fill the whole view. " +
             "Bigger = less lag. Smaller = blast closer to the face.")]
    [SerializeField] private float victimEffectDistance = 1.5f;
    [Tooltip("Size of the blast for the hit player. 1 = normal, 0.5 = half size. Smaller = less lag.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float victimEffectScale = 0.5f;

    [Header("Sounds")]
    [Tooltip("Played at the ball when it is thrown, on every machine. Leave empty for no sound.")]
    [SerializeField] private AudioClip launchSfx;
    [Range(0f, 1f)]
    [SerializeField] private float launchSfxVolume = 1f;
    [Tooltip("Played at the hit point when the ball blasts, on every machine. Leave empty for no sound.")]
    [SerializeField] private AudioClip hitSfx;
    [Range(0f, 1f)]
    [SerializeField] private float hitSfxVolume = 1f;
    [Tooltip("The sounds are 3D: players farther away than this (meters) do not hear them.")]
    [SerializeField] private float sfxMaxDistance = 30f;

    [Header("What happens to a player this ball hits (runs on that player's own machine)")]
    [Tooltip("None: nothing. Knockback: the player is pushed back (like a stumble). Freeze: the player cannot move for 'Freeze Seconds'.")]
    [SerializeField] private PlayerHitEffect playerHitEffect = PlayerHitEffect.Knockback;
    [Tooltip("Knockback: how far the player is pushed (meters), in the direction the ball was flying.")]
    [SerializeField] private float knockbackDistance = 1.5f;
    [Tooltip("Knockback: seconds the push lasts.")]
    [SerializeField] private float knockbackDuration = 0.35f;
    [Tooltip("Freeze: seconds the player stays frozen on the spot.")]
    [SerializeField] private float freezeSeconds = 3f;
    [Tooltip("Controller vibration for the player who is hit. 0 = none, 1 = strongest.")]
    [Range(0f, 1f)]
    [SerializeField] private float hitHapticStrength = 1f;
    [Tooltip("Seconds the vibration lasts (it fades out).")]
    [SerializeField] private float hitHapticDuration = 0.6f;

    [Header("Aim line (only YOU see it, while the ball is in your hand)")]
    [Tooltip("A straight line from the front of the ball in the direction it will fly. It stops at the first thing the ball would hit " +
             "(wall, floor, another player) and disappears the moment the ball is thrown.")]
    [SerializeField] private bool showAimLine = true;
    [SerializeField] private Color aimLineColor = new Color(0.2f, 0.9f, 1f, 1f);
    [Tooltip("Thickness of the line in meters (0.01 = 1 cm).")]
    [SerializeField] private float aimLineWidth = 0.01f;
    [Tooltip("Meters in front of the ball's centre where the line starts, measured at full ball size (it shrinks with the ball while it grows).")]
    [SerializeField] private float aimStartOffset = 0.2f;
    [Tooltip("Optional. Leave empty to use a simple built-in unlit material.")]
    [SerializeField] private Material aimLineMaterial;

    // -------- networked state (written by the owner, read by everyone)
    [Networked] public int NetState { get; set; }
    [Networked] public float Charge { get; set; }

    // -------- owner-only values
    private BallState _state = BallState.Charging;
    private float _charge;
    private bool _fullChargeLogged;
    private Transform _hand;
    private Vector3 _handOffset;
    private Vector3 _dir;
    private float _traveled;
    private float _despawnAt;
    private Vector3 _pos;          // the REAL position of the ball while flying (owner only)

    private LineRenderer _aimLine;
    private Material _aimLineOwnMaterial;
    private Vector3 _aimDir;
    private bool _hasAimDir;

    private bool _hidden;
    private int _prevState = -1;   // last state seen by Render(), used to play the launch sound once

    // -------- shared buffers (no memory is allocated per frame)
    private const int CastBufferSize = 32;
    private static readonly RaycastHit[] CastBuffer = new RaycastHit[CastBufferSize];
    private static readonly RaycastDistanceComparer DistanceComparer = new RaycastDistanceComparer();

    private class RaycastDistanceComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); }
    }

    private class AvatarEntry
    {
        public NetworkAvatarSetup avatar;
        public Renderer[] renderers;
    }
    // one avatar list shared by all balls, so several balls do not each search the scene
    private static readonly List<AvatarEntry> SharedAvatars = new List<AvatarEntry>();
    private static float _nextAvatarRefresh;
    private readonly List<NetworkAvatarSetup> _targetAvatars = new List<NetworkAvatarSetup>(8);
    private readonly List<Bounds> _targetBounds = new List<Bounds>(8);

    public bool IsFullyCharged => _charge >= 1f;
    public string BallName => ballName;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string msg)
    {
        if (verboseLogs) Debug.Log(msg);
    }

    // ------------------------------------------------------------------ spawn

    public override void Spawned()
    {
        ApplyScale(0f);
        _pos = transform.position;

        // get everything ready now (while the ball is only growing on the hand), so the hit itself costs nothing
        if (hitEffectPrefab != null) HandBallFxPool.Prewarm(hitEffectPrefab, hitEffectLifetime);
        if (victimEffectPrefab != null) HandBallFxPool.Prewarm(victimEffectPrefab, hitEffectLifetime);
        if (hitSfx != null) hitSfx.LoadAudioData();
        if (launchSfx != null) launchSfx.LoadAudioData();
        HandBallSfxPool.EnsureCreated();

        if (Object.HasStateAuthority)
        {
            _state = BallState.Charging;
            _charge = 0f;
            Log($"[HandBall:{ballName}] Spawned on the OWNER client (owner={Object.StateAuthority}, id={Object.Id}).");
        }
        else
        {
            Log($"[HandBall:{ballName}] Spawned on a REMOTE client (owner={Object.StateAuthority}, me={Runner.LocalPlayer}, id={Object.Id}).");
        }
    }

    /// <summary>Called by the launcher right after spawning (owner only).</summary>
    public void Begin(Transform hand, Vector3 handOffset)
    {
        _hand = hand;
        _handOffset = handOffset;
        FollowHand();
        Log($"[HandBall:{ballName}] Charging started on hand '{(hand != null ? hand.name : "NONE")}'. Grows from {startScale} to {endScale} in {growTime}s.");
    }

    // ------------------------------------------------------------------ owner API

    /// <summary>Throws the ball in a straight line (owner only).</summary>
    public void Throw(Vector3 direction)
    {
        if (!Object.HasStateAuthority || _state != BallState.Charging) return;

        _pos = transform.position;
        _dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        _traveled = 0f;
        _state = BallState.Thrown;
        DestroyAimLine();                       // the aim line disappears the moment the ball is thrown
        Log($"[HandBall:{ballName}] THROWN from {_pos} dir={_dir} speed={throwSpeed} maxDistance={maxDistance} scale={_charge:F2}.");
    }

    /// <summary>Removes the ball without throwing it (owner only).</summary>
    public void Cancel(string reason)
    {
        if (!Object.HasStateAuthority) return;
        Log($"[HandBall:{ballName}] CANCELLED ({reason}) at charge {_charge:F2}.");
        DestroyAimLine();
        DespawnSelf();
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

        switch (_state)
        {
            case BallState.Charging: UpdateCharging(Time.deltaTime); break;
            case BallState.Thrown: UpdateThrown(Time.deltaTime); break;
            case BallState.Hit:
                if (Time.time >= _despawnAt) DespawnSelf();
                break;
        }
    }

    private void LateUpdate()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

        if (_state == BallState.Charging)
        {
            FollowHand(); // after hand tracking has been applied this frame
            UpdateAimLine();
        }
        else
        {
            // Thrown / Hit: force the real position AFTER NetworkTransform has done its work this frame
            transform.position = _pos;
        }
    }

    // Copies the owner's values into the network state.
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        NetState = (int)_state;
        Charge = _charge;
    }

    // Runs on every client: scale, launch sound and visibility follow the networked values.
    public override void Render()
    {
        bool owner = Object.HasStateAuthority;
        ApplyScale(owner ? _charge : Charge);

        int s = owner ? (int)_state : NetState;

        // launch sound: exactly once, when the state changes from Charging to Thrown (not for balls seen already flying)
        if (s == (int)BallState.Thrown && _prevState == (int)BallState.Charging)
        {
            if (launchSfx != null)
                HandBallSfxPool.PlayAt(launchSfx, transform.position, launchSfxVolume, sfxMaxDistance);
        }
        _prevState = s;

        if (s == (int)BallState.Hit && !_hidden) HideVisuals();
    }

    // ------------------------------------------------------------------ aim line

    /// <summary>Called every frame by the launcher while the ball is in the hand: the direction the ball will be thrown in.</summary>
    public void SetAimDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        _aimDir = direction.normalized;
        _hasAimDir = true;
    }

    private void UpdateAimLine()
    {
        if (!showAimLine || _state != BallState.Charging)
        {
            DestroyAimLine();
            return;
        }

        Vector3 dir = _hasAimDir ? _aimDir : (_hand != null ? _hand.forward : transform.forward);
        float length = Mathf.Max(0.1f, maxDistance);

        float endDist = ComputeAimEndDistance(_pos, dir, length);
        float startDist = aimStartOffset * transform.localScale.x;

        EnsureAimLine();

        if (endDist <= startDist + 0.005f)
        {
            _aimLine.enabled = false;           // something is touching the ball already, nothing to draw
            return;
        }

        _aimLine.enabled = true;
        _aimLine.SetPosition(0, _pos + dir * startDist);
        _aimLine.SetPosition(1, _pos + dir * endDist);
    }

    /// <summary>
    /// How far the line reaches. Uses the same rules as the real flight (same mask, ignores your own rig / avatar / other balls),
    /// including the body bounds of other players, so the line stops exactly where the ball would stop.
    /// </summary>
    private float ComputeAimEndDistance(Vector3 from, Vector3 dir, float length)
    {
        bool envFound = false;
        float envDist = float.MaxValue;
        float envEnd = length;

        int count = Physics.SphereCastNonAlloc(from, hitRadius, dir, CastBuffer, length + 0.01f, hitMask, QueryTriggerInteraction.Ignore);
        if (count > 1) Array.Sort(CastBuffer, 0, count, DistanceComparer);

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = CastBuffer[i];
            if (h.distance <= 0.0001f && h.point == Vector3.zero) continue;
            if (IsOwnRig(h.collider)) continue;
            if (h.collider.GetComponentInParent<HandBall>() != null) continue;

            var av = h.collider.GetComponentInParent<NetworkAvatarSetup>();
            if (av != null && av.Object != null && av.Object.HasStateAuthority) continue;

            envFound = true;
            envDist = h.distance;
            // the ball stops at 'distance'; the surface it touches is up to one radius further
            envEnd = Mathf.Clamp(Vector3.Dot(h.point - from, dir), h.distance, h.distance + hitRadius);
            break;
        }

        float limit = envFound ? envDist : length;
        if (TryHitAvatarBounds(from, dir, limit, out NetworkAvatarSetup victim, out float victimDist))
            return Mathf.Min(victimDist + hitRadius, length);

        return envFound ? Mathf.Min(envEnd, length) : length;
    }

    private void EnsureAimLine()
    {
        if (_aimLine != null) return;

        var go = new GameObject("BallAimLine");
        _aimLine = go.AddComponent<LineRenderer>();
        _aimLine.useWorldSpace = true;
        _aimLine.positionCount = 2;
        _aimLine.numCapVertices = 4;
        _aimLine.alignment = LineAlignment.View;
        _aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _aimLine.receiveShadows = false;
        _aimLine.widthMultiplier = Mathf.Max(0.001f, aimLineWidth);

        if (aimLineMaterial != null)
        {
            _aimLine.sharedMaterial = aimLineMaterial;
        }
        else
        {
            Shader sh = Shader.Find("Sprites/Default");   // always included in builds
            if (sh != null)
            {
                _aimLineOwnMaterial = new Material(sh);
                _aimLine.sharedMaterial = _aimLineOwnMaterial;
            }
        }

        Color faded = aimLineColor;
        faded.a *= 0.35f;
        _aimLine.startColor = aimLineColor;
        _aimLine.endColor = faded;
    }

    private void DestroyAimLine()
    {
        if (_aimLine != null) Destroy(_aimLine.gameObject);
        _aimLine = null;
        if (_aimLineOwnMaterial != null) Destroy(_aimLineOwnMaterial);
        _aimLineOwnMaterial = null;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        DestroyAimLine();
    }

    private void OnDestroy()
    {
        DestroyAimLine();
    }

    // ------------------------------------------------------------------ charging

    private void UpdateCharging(float dt)
    {
        if (_hand == null)
        {
            Debug.LogWarning($"[HandBall:{ballName}] No hand assigned while charging, cancelling.");
            DespawnSelf();
            return;
        }

        _charge = Mathf.Clamp01(_charge + dt / Mathf.Max(0.05f, growTime));

        if (_charge >= 1f && !_fullChargeLogged)
        {
            _fullChargeLogged = true;
            Log($"[HandBall:{ballName}] FULLY CHARGED, ready to throw.");
        }
    }

    private void FollowHand()
    {
        if (_hand == null) return;
        _pos = _hand.TransformPoint(_handOffset);
        transform.SetPositionAndRotation(_pos, _hand.rotation);
    }

    private void ApplyScale(float charge01)
    {
        float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(charge01));
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);
    }

    // ------------------------------------------------------------------ flying + hit detection

    private void UpdateThrown(float dt)
    {
        Vector3 from = _pos;
        float stepLen = Mathf.Min(throwSpeed * dt, Mathf.Max(0f, maxDistance - _traveled));

        // 1) nearest solid thing in the way
        bool envFound = false;
        float envDist = float.MaxValue;
        RaycastHit envHit = default;
        NetworkAvatarSetup envAvatar = null;

        int count = Physics.SphereCastNonAlloc(from, hitRadius, _dir, CastBuffer, stepLen + 0.01f, hitMask, QueryTriggerInteraction.Ignore);
        if (count > 1) Array.Sort(CastBuffer, 0, count, DistanceComparer);

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = CastBuffer[i];
            if (h.distance <= 0.0001f && h.point == Vector3.zero) continue;     // started inside this collider, ignore
            if (IsOwnRig(h.collider)) continue;                                  // never hit the thrower
            if (h.collider.GetComponentInParent<HandBall>() != null) continue;   // other balls

            var av = h.collider.GetComponentInParent<NetworkAvatarSetup>();
            if (av != null && av.Object != null && av.Object.HasStateAuthority) continue; // own avatar

            envFound = true;
            envDist = h.distance;
            envHit = h;
            envAvatar = av;
            break;
        }

        // 2) other players (their avatars may have no colliders, so test against their body bounds)
        float limit = envFound ? Mathf.Min(stepLen, envDist) : stepLen;
        if (TryHitAvatarBounds(from, _dir, limit, out NetworkAvatarSetup victim, out float victimDist))
        {
            Vector3 point = from + _dir * victimDist;
            _pos = point;
            transform.position = point;
            RegisterHit(HitType.Player, point, -_dir, victim.Object.StateAuthority,
                        "avatar '" + victim.name + "' (bounds test)");
            return;
        }

        if (envFound)
        {
            _pos = from + _dir * envDist;
            transform.position = _pos;

            if (envAvatar != null)
            {
                RegisterHit(HitType.Player, envHit.point, envHit.normal, envAvatar.Object.StateAuthority,
                            "avatar collider '" + envHit.collider.name + "'");
            }
            else
            {
                RegisterHit(HitType.Surface, envHit.point, envHit.normal, PlayerRef.None,
                            "collider '" + envHit.collider.name + "'");
            }
            return;
        }

        // 3) nothing hit: move on
        _pos = from + _dir * stepLen;
        transform.position = _pos;
        _traveled += stepLen;

        if (_traveled >= maxDistance - 0.0001f)
        {
            Log($"[HandBall:{ballName}] EXPIRED after {_traveled:F1} m without hitting anything, removing it.");
            DespawnSelf();
        }
    }

    private bool IsOwnRig(Collider c)
    {
        var xr = LocalXROriginProvider.Instance;
        return xr != null && c.transform.IsChildOf(xr.Root);
    }

    private static void RefreshAvatars()
    {
        if (Time.time < _nextAvatarRefresh) return;
        _nextAvatarRefresh = Time.time + 1f;

        SharedAvatars.Clear();
        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
        {
            if (a.Object == null || !a.Object.IsValid) continue;

            var list = new List<Renderer>();
            foreach (var r in a.GetComponentsInChildren<Renderer>())
                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);

            SharedAvatars.Add(new AvatarEntry { avatar = a, renderers = list.ToArray() });
        }
    }

    private bool TryHitAvatarBounds(Vector3 from, Vector3 dir, float length, out NetworkAvatarSetup victim, out float distance)
    {
        victim = null;
        distance = 0f;
        RefreshAvatars();

        // body bounds of every OTHER player
        _targetAvatars.Clear();
        _targetBounds.Clear();
        for (int k = 0; k < SharedAvatars.Count; k++)
        {
            var e = SharedAvatars[k];
            if (e.avatar == null || e.avatar.Object == null || !e.avatar.Object.IsValid) continue;
            if (e.avatar.Object.HasStateAuthority) continue;               // the thrower's own avatar
            if (e.renderers.Length == 0 || e.renderers[0] == null) continue;

            Bounds b = e.renderers[0].bounds;
            for (int i = 1; i < e.renderers.Length; i++)
                if (e.renderers[i] != null) b.Encapsulate(e.renderers[i].bounds);

            _targetAvatars.Add(e.avatar);
            _targetBounds.Add(b);
        }
        if (_targetAvatars.Count == 0) return false;

        // walk along the path in small steps so a fast ball cannot skip through a player
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.1f));
        float rSqr = hitRadius * hitRadius;
        for (int i = 0; i <= steps; i++)
        {
            float d = length * i / steps;
            Vector3 p = from + dir * d;
            for (int t = 0; t < _targetBounds.Count; t++)
            {
                if (_targetBounds[t].SqrDistance(p) <= rSqr)
                {
                    victim = _targetAvatars[t];
                    distance = d;
                    return true;
                }
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ hit handling

    private void RegisterHit(HitType type, Vector3 point, Vector3 normal, PlayerRef victim, string what)
    {
        _state = BallState.Hit;
        _despawnAt = Time.time + hitLingerTime;

        Log($"[HandBall:{ballName}] HIT {type} -> {what}. point={point} victim={victim}.");

        RPC_BallHit((int)type, point, normal, _dir, victim);
        HideVisuals();
    }

    /// <summary>Sent by the owner. Runs on EVERY client (including the owner).</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BallHit(int hitType, Vector3 point, Vector3 normal, Vector3 direction, PlayerRef victim)
    {
        HitType type = (HitType)hitType;
        bool victimIsMe = victim != PlayerRef.None && Runner != null && victim == Runner.LocalPlayer;

        // 1) hit VFX (taken from the pool, nothing is created)
        if (victimIsMe)
        {
            // the hit player sees a lighter blast, pushed away from their eyes
            GameObject prefab = victimEffectPrefab != null ? victimEffectPrefab : hitEffectPrefab;
            if (showHitEffectToHitPlayer && prefab != null)
            {
                Vector3 pos = PushAwayFromHead(point, direction);
                HandBallFxPool.Play(prefab, pos, EffectRotation(normal), hitEffectLifetime, victimEffectScale);
            }
        }
        else if (hitEffectPrefab != null)
        {
            HandBallFxPool.Play(hitEffectPrefab, point, EffectRotation(normal), hitEffectLifetime, 1f);
        }

        // 2) blast sound at the hit point (taken from the pool, nothing is created)
        if (hitSfx != null)
            HandBallSfxPool.PlayAt(hitSfx, point, hitSfxVolume, sfxMaxDistance);

        // 3) gameplay
        if (type == HitType.Player) OnPlayerHit(victim, victimIsMe, point, direction);
        else OnSurfaceHit(point, normal);
    }

    // ================================================================== WHAT HAPPENS ON A HIT
    // These run on EVERY client when the ball hits something.

    /// <summary>
    /// The ball hit another player. victimIsMe = true ONLY on the machine of the player who was hit,
    /// so the effect is applied there, to that player's own rig.
    /// </summary>
    private void OnPlayerHit(PlayerRef victim, bool victimIsMe, Vector3 point, Vector3 direction)
    {
        if (!victimIsMe) return; // other machines only show the VFX and sound

        var xr = LocalXROriginProvider.Instance;
        if (xr == null)
        {
            Debug.LogWarning($"[HandBall:{ballName}] You were hit, but LocalXROriginProvider.Instance is missing, so the hit effect cannot be applied.");
            return;
        }

        if (hitHapticStrength > 0f)
            xr.PlayHaptics(hitHapticStrength, hitHapticDuration);

        switch (playerHitEffect)
        {
            case PlayerHitEffect.Knockback:
                xr.Knockback(direction, knockbackDistance, knockbackDuration);
                break;

            case PlayerHitEffect.Freeze:
                xr.Freeze(freezeSeconds);
                break;
        }
    }

    /// <summary>The ball hit a wall, floor or any other object.</summary>
    private void OnSurfaceHit(Vector3 point, Vector3 normal)
    {
        // add what should happen when the ball hits the world (decals, shaking, ...)
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Moves the blast so it is at least victimEffectDistance from the local player's eyes, on the side the ball came from.</summary>
    private Vector3 PushAwayFromHead(Vector3 point, Vector3 flyDirection)
    {
        Camera cam = Camera.main;
        Vector3 head = cam != null ? cam.transform.position : point;

        Vector3 toPoint = point - head;
        float dist = toPoint.magnitude;
        if (dist >= victimEffectDistance) return point;

        Vector3 dir = dist > 0.05f ? toPoint / dist : -flyDirection;   // away from the eyes, toward where the ball came from
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        return head + dir.normalized * victimEffectDistance;
    }

    private Quaternion EffectRotation(Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f) return Quaternion.identity;

        switch (effectAlignment)
        {
            case EffectAlignment.UpIsSurfaceNormal: return Quaternion.FromToRotation(Vector3.up, normal.normalized);
            case EffectAlignment.ForwardIsSurfaceNormal: return Quaternion.LookRotation(normal);
            default: return Quaternion.identity;
        }
    }

    private void HideVisuals()
    {
        if (_hidden) return;
        _hidden = true;
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);
    }

    private void DespawnSelf()
    {
        if (Runner != null && Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }
}

// ====================================================================== pools (same file, no setup needed)

/// <summary>Reuses hit effect objects instead of creating and destroying one on every hit.</summary>
public static class HandBallFxPool
{
    private static readonly Dictionary<GameObject, Queue<HandBallPooledFx>> Pools =
        new Dictionary<GameObject, Queue<HandBallPooledFx>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Pools.Clear(); }

    private static Queue<HandBallPooledFx> GetQueue(GameObject prefab)
    {
        if (!Pools.TryGetValue(prefab, out var q))
        {
            q = new Queue<HandBallPooledFx>();
            Pools[prefab] = q;
        }
        return q;
    }

    private static HandBallPooledFx Create(GameObject prefab, Queue<HandBallPooledFx> home)
    {
        var go = UnityEngine.Object.Instantiate(prefab);
        var fx = go.GetComponent<HandBallPooledFx>();
        if (fx == null) fx = go.AddComponent<HandBallPooledFx>();
        fx.Setup(home);
        go.SetActive(false);
        return fx;
    }

    /// <summary>Makes sure a couple of effect objects exist already. Called while the ball is still charging.</summary>
    public static void Prewarm(GameObject prefab, float lifetime)
    {
        var q = GetQueue(prefab);
        while (q.Count < 2) q.Enqueue(Create(prefab, q));
    }

    public static void Play(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, float scale)
    {
        var q = GetQueue(prefab);

        HandBallPooledFx fx = null;
        while (q.Count > 0 && fx == null) fx = q.Dequeue();   // skips objects destroyed by a scene change
        if (fx == null) fx = Create(prefab, q);

        fx.Play(position, rotation, lifetime, scale);
    }
}

/// <summary>Added automatically to pooled hit effects. Switches the effect off after its lifetime and returns it to the pool.</summary>
public class HandBallPooledFx : MonoBehaviour
{
    private Queue<HandBallPooledFx> _home;
    private ParticleSystem[] _systems;
    private float _returnAt;
    private bool _playing;
    private Vector3 _baseScale = Vector3.one;

    public void Setup(Queue<HandBallPooledFx> home)
    {
        _home = home;
        _baseScale = transform.localScale;
        _systems = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void Play(Vector3 position, Quaternion rotation, float lifetime, float scale)
    {
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = _baseScale * scale;
        gameObject.SetActive(true);
        for (int i = 0; i < _systems.Length; i++)
        {
            _systems[i].Clear(true);
            _systems[i].Play(true);
        }
        _returnAt = Time.time + Mathf.Max(0.1f, lifetime);
        _playing = true;
    }

    private void Update()
    {
        if (_playing && Time.time >= _returnAt) Release();
    }

    private void Release()
    {
        _playing = false;
        for (int i = 0; i < _systems.Length; i++)
            _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
        _home.Enqueue(this);
    }
}

/// <summary>A small fixed set of 3D audio sources that are reused for every ball sound.</summary>
public static class HandBallSfxPool
{
    private const int PoolSize = 8;
    private static AudioSource[] _sources;
    private static int _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _sources = null; _next = 0; }

    public static void EnsureCreated()
    {
        if (_sources != null && _sources[0] != null) return;

        var root = new GameObject("HandBallSfxPool");
        UnityEngine.Object.DontDestroyOnLoad(root);

        _sources = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var child = new GameObject("Sfx_" + i);
            child.transform.SetParent(root.transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 1f;                      // 3D: louder when close, quieter when far
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            _sources[i] = src;
        }
    }

    public static void PlayAt(AudioClip clip, Vector3 position, float volume, float maxDistance)
    {
        if (clip == null) return;
        EnsureCreated();

        // prefer a source that is free, otherwise take over the oldest one
        AudioSource src = null;
        for (int i = 0; i < PoolSize; i++)
        {
            var s = _sources[(_next + i) % PoolSize];
            if (!s.isPlaying) { src = s; _next = (_next + i + 1) % PoolSize; break; }
        }
        if (src == null)
        {
            src = _sources[_next];
            _next = (_next + 1) % PoolSize;
        }

        // every source sits on its own child object, so each sound has its own 3D position
        src.transform.position = position;
        src.maxDistance = Mathf.Max(3f, maxDistance);
        src.volume = volume;
        src.clip = clip;
        src.Play();
    }
}
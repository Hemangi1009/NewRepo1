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
///   2) Thrown   : flies straight, hits players / surfaces, or disappears after maxDistance.
///   3) Hit      : visuals hide, every client gets a callback, the object is removed a moment later.
///
/// The player who throws the ball is the state authority: that client moves the ball and does all hit tests.
/// Hits are announced to every client with an RPC.
///
/// FIX: the owner keeps the real ball position in _pos and writes it to the transform in LateUpdate,
/// so NetworkTransform can no longer pull the ball back to where it was released.
/// </summary>
public class HandBall : NetworkBehaviour
{
    public enum HitType { Player = 0, Surface = 1 }
    private enum BallState { Charging = 0, Thrown = 1, Hit = 2 }

    [Header("Info")]
    [SerializeField] private string ballName = "Ball";

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
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private float hitEffectLifetime = 3f;

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

    private bool _hidden;

    private class AvatarEntry
    {
        public NetworkAvatarSetup avatar;
        public Renderer[] renderers;
    }
    private readonly List<AvatarEntry> _avatars = new List<AvatarEntry>();
    private float _nextAvatarRefresh;

    public bool IsFullyCharged => _charge >= 1f;
    public string BallName => ballName;

    // ------------------------------------------------------------------ spawn

    public override void Spawned()
    {
        ApplyScale(0f);
        _pos = transform.position;

        if (Object.HasStateAuthority)
        {
            _state = BallState.Charging;
            _charge = 0f;
            Debug.Log($"[HandBall:{ballName}] Spawned on the OWNER client (owner={Object.StateAuthority}, id={Object.Id}).");
        }
        else
        {
            Debug.Log($"[HandBall:{ballName}] Spawned on a REMOTE client (owner={Object.StateAuthority}, me={Runner.LocalPlayer}, id={Object.Id}).");
        }
    }

    /// <summary>Called by the launcher right after spawning (owner only).</summary>
    public void Begin(Transform hand, Vector3 handOffset)
    {
        _hand = hand;
        _handOffset = handOffset;
        FollowHand();
        Debug.Log($"[HandBall:{ballName}] Charging started on hand '{(hand != null ? hand.name : "NONE")}'. Grows from {startScale} to {endScale} in {growTime}s.");
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
        Debug.Log($"[HandBall:{ballName}] THROWN from {_pos} dir={_dir} speed={throwSpeed} maxDistance={maxDistance} scale={_charge:F2}.");
    }

    /// <summary>Removes the ball without throwing it (owner only).</summary>
    public void Cancel(string reason)
    {
        if (!Object.HasStateAuthority) return;
        Debug.Log($"[HandBall:{ballName}] CANCELLED ({reason}) at charge {_charge:F2}.");
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

    // Runs on every client: scale and visibility follow the networked values.
    public override void Render()
    {
        bool owner = Object.HasStateAuthority;
        ApplyScale(owner ? _charge : Charge);

        BallState s = owner ? _state : (BallState)NetState;
        if (s == BallState.Hit && !_hidden) HideVisuals();
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
            Debug.Log($"[HandBall:{ballName}] FULLY CHARGED, ready to throw.");
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

        var hits = Physics.SphereCastAll(from, hitRadius, _dir, stepLen + 0.01f, hitMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
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
                        $"avatar '{victim.name}' (bounds test)");
            return;
        }

        if (envFound)
        {
            _pos = from + _dir * envDist;
            transform.position = _pos;

            if (envAvatar != null)
            {
                RegisterHit(HitType.Player, envHit.point, envHit.normal, envAvatar.Object.StateAuthority,
                            $"avatar collider '{envHit.collider.name}'");
            }
            else
            {
                RegisterHit(HitType.Surface, envHit.point, envHit.normal, PlayerRef.None,
                            $"collider '{envHit.collider.name}' on object '{envHit.collider.gameObject.name}' (layer {LayerMask.LayerToName(envHit.collider.gameObject.layer)})");
            }
            return;
        }

        // 3) nothing hit: move on
        _pos = from + _dir * stepLen;
        transform.position = _pos;
        _traveled += stepLen;

        if (_traveled >= maxDistance - 0.0001f)
        {
            Debug.Log($"[HandBall:{ballName}] EXPIRED after {_traveled:F1} m without hitting anything, removing it.");
            DespawnSelf();
        }
    }

    private bool IsOwnRig(Collider c)
    {
        var xr = LocalXROriginProvider.Instance;
        return xr != null && c.transform.IsChildOf(xr.Root);
    }

    private void RefreshAvatars()
    {
        if (Time.time < _nextAvatarRefresh) return;
        _nextAvatarRefresh = Time.time + 0.5f;

        _avatars.Clear();
        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
        {
            if (a.Object == null || !a.Object.IsValid) continue;

            var list = new List<Renderer>();
            foreach (var r in a.GetComponentsInChildren<Renderer>())
                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);

            _avatars.Add(new AvatarEntry { avatar = a, renderers = list.ToArray() });
        }
    }

    private bool TryHitAvatarBounds(Vector3 from, Vector3 dir, float length, out NetworkAvatarSetup victim, out float distance)
    {
        victim = null;
        distance = 0f;
        RefreshAvatars();

        // body bounds of every OTHER player
        var targets = new List<KeyValuePair<NetworkAvatarSetup, Bounds>>();
        foreach (var e in _avatars)
        {
            if (e.avatar == null || e.avatar.Object == null || !e.avatar.Object.IsValid) continue;
            if (e.avatar.Object.HasStateAuthority) continue;               // the thrower's own avatar
            if (e.renderers.Length == 0 || e.renderers[0] == null) continue;

            Bounds b = e.renderers[0].bounds;
            for (int i = 1; i < e.renderers.Length; i++)
                if (e.renderers[i] != null) b.Encapsulate(e.renderers[i].bounds);
            targets.Add(new KeyValuePair<NetworkAvatarSetup, Bounds>(e.avatar, b));
        }
        if (targets.Count == 0) return false;

        // walk along the path in small steps so a fast ball cannot skip through a player
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.1f));
        float rSqr = hitRadius * hitRadius;
        for (int i = 0; i <= steps; i++)
        {
            float d = length * i / steps;
            Vector3 p = from + dir * d;
            foreach (var t in targets)
            {
                if (t.Value.SqrDistance(p) <= rSqr)
                {
                    victim = t.Key;
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

        Debug.Log($"[HandBall:{ballName}] HIT {type.ToString().ToUpper()} -> {what}. point={point} normal={normal} traveled={_traveled:F1} m thrower={Object.StateAuthority} victim={victim}.");

        RPC_BallHit((int)type, point, normal, victim);
        HideVisuals();
    }

    /// <summary>Sent by the owner. Runs on EVERY client (including the owner).</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BallHit(int hitType, Vector3 point, Vector3 normal, PlayerRef victim)
    {
        HitType type = (HitType)hitType;
        bool victimIsMe = victim != PlayerRef.None && Runner != null && victim == Runner.LocalPlayer;

        Debug.Log($"[HandBall:{ballName}] (all clients) hit message received: type={type}, point={point}, victim={victim}, victimIsMe={victimIsMe}, thrower={Object.StateAuthority}.");

        if (hitEffectPrefab != null)
        {
            Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            var fx = Instantiate(hitEffectPrefab, point, rot);
            Destroy(fx, hitEffectLifetime);
        }

        if (type == HitType.Player) OnPlayerHit(victim, victimIsMe, point);
        else OnSurfaceHit(point, normal);
    }

    // ================================================================== YOUR LOGIC GOES HERE
    // These run on EVERY client when the ball hits something. Add what should happen later.

    /// <summary>The ball hit another player. victimIsMe = true on the client of the player who was hit.</summary>
    private void OnPlayerHit(PlayerRef victim, bool victimIsMe, Vector3 point)
    {
        Debug.Log($"[HandBall:{ballName}] OnPlayerHit -> victim={victim}, victimIsMe={victimIsMe}, point={point}. (nothing implemented yet)");
        // TODO: what should happen to the hit player
    }

    /// <summary>The ball hit a wall, floor or any other object.</summary>
    private void OnSurfaceHit(Vector3 point, Vector3 normal)
    {
        Debug.Log($"[HandBall:{ballName}] OnSurfaceHit -> point={point}, normal={normal}. (nothing implemented yet)");
        // TODO: what should happen when the ball hits the world
    }

    // ------------------------------------------------------------------ helpers

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
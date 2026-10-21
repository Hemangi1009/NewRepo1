using Fusion;
using UnityEngine;

// Put this on the ROOT of your NetworkAvatar prefab (same GameObject as NetworkObject + VRAvatarSync).
//
// Shared mode has no InputAuthority, so the avatar remembers who spawned it in a networked field.
// If that player is no longer in the session, the client that holds state authority despawns the avatar.
// If nobody holds authority (orphaned object), the session's master client takes it over first.
public class AvatarOwnerCleanup : NetworkBehaviour
{
    [SerializeField] private float checkInterval = 0.5f;
    [SerializeField] private bool debugLogs = true;

    // Set once by the player who spawned this avatar.
    [Networked] public PlayerRef OwnerPlayer { get; private set; }

    private float _nextCheck;
    private bool _requestedAuthority;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
            OwnerPlayer = Runner.LocalPlayer;

        if (debugLogs)
            Debug.Log($"[AvatarOwnerCleanup] '{name}' spawned. Owner = Player {OwnerPlayer.PlayerId}, authority = Player {Object.StateAuthority.PlayerId}, I am Player {Runner.LocalPlayer.PlayerId}.", this);
    }

    private bool IsActive(PlayerRef p)
    {
        if (p.IsNone) return false;
        foreach (PlayerRef a in Runner.ActivePlayers)
            if (a == p) return true;
        return false;
    }

    private void Update()
    {
        if (Object == null || Runner == null || !Runner.IsRunning) return;
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + checkInterval;

        PlayerRef owner = OwnerPlayer;
        if (owner.IsNone) return;          // owner not replicated yet
        if (IsActive(owner)) return;       // owner is still in the session

        // Owner has left.
        if (Object.HasStateAuthority)
        {
            if (debugLogs) Debug.Log($"[AvatarOwnerCleanup] Owner Player {owner.PlayerId} left - despawning avatar '{name}'.", this);
            Runner.Despawn(Object);
            return;
        }

        // Nobody (still connected) holds authority: the master client takes it, then despawns on the next check.
        bool orphaned = !IsActive(Object.StateAuthority);
        if (orphaned && Runner.IsSharedModeMasterClient && !_requestedAuthority)
        {
            _requestedAuthority = true;
            if (debugLogs) Debug.Log($"[AvatarOwnerCleanup] Avatar '{name}' is orphaned - master client requesting authority to remove it.", this);
            Object.RequestStateAuthority();
        }
    }
}
using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// WIN ZONE. Put it on the win object (the one with the win VFX) together with a TRIGGER collider and a NetworkObject.
///
/// The FIRST player to reach the zone wins. Every player who touches it asks the room's state authority
/// ("can I win?"); the state authority accepts only the first request and announces the winner to everybody.
///   - The winner hears the Winner Sfx, everyone else hears the Someone Else Won Sfx (optional).
///   - The win VFX is switched on for everybody.
///   - Two UnityEvents (On I Won / On Someone Else Won) let you hook up UI or other logic.
///
/// The player number is the Fusion player number (the same number that appears in logs like [Player:1]).
/// </summary>
[RequireComponent(typeof(Collider))]
public class WinZone : NetworkBehaviour
{
    [Header("Win VFX")]
    [Tooltip("Switched on for EVERY player when the winner is decided. Optional.")]
    [SerializeField] private GameObject winVfx;
    [Tooltip("ON: the win VFX is hidden until somebody wins. OFF: it stays as it is and is only switched on when somebody wins.")]
    [SerializeField] private bool hideVfxAtStart = false;

    [Header("Sounds")]
    [Tooltip("Heard by the player who reached the zone first.")]
    [SerializeField] private AudioClip winnerSfx;
    [Tooltip("Heard by every other player when somebody else won. Optional.")]
    [SerializeField] private AudioClip someoneElseWonSfx;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Events (hook up your UI or game logic)")]
    [SerializeField] private UnityEvent onIWon;
    [SerializeField] private UnityEvent onSomeoneElseWon;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    // -------- networked state: 0 = nobody has won yet, otherwise the Fusion player number of the winner
    [Networked] public int WinnerPlayerId { get; set; }

    private bool _claimSent;      // this machine already asked to win
    private bool _decided;        // the result is already applied on this machine
    private bool _warnedNotNetworked;
    private Collider _col;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        _col = GetComponent<Collider>();

        if (!_col.isTrigger)
            Debug.LogWarning($"[WinZone:{name}] The collider is NOT a trigger. Tick 'Is Trigger' on it, otherwise the player is blocked and nothing is detected.", this);

        if (hideVfxAtStart && winVfx != null) winVfx.SetActive(false);

        Log($"Ready. VFX='{(winVfx != null ? winVfx.name : "NONE")}', winner sound='{(winnerSfx != null ? winnerSfx.name : "NONE")}', other-player sound='{(someoneElseWonSfx != null ? someoneElseWonSfx.name : "NONE")}'.");
    }

    public override void Spawned()
    {
        int me = Runner != null ? Runner.LocalPlayer.PlayerId : -1;
        Debug.Log($"[WinZone:{name}] Networked OK. You are Player {me}, state authority = Player {Object.StateAuthority.PlayerId}, winner so far = {(WinnerPlayerId == 0 ? "nobody" : "Player " + WinnerPlayerId)}.", this);

        // somebody joined late and the game was already won: show the result silently (no sounds, no events)
        if (WinnerPlayerId != 0) ApplyResult(WinnerPlayerId, true);
    }

    // ------------------------------------------------------------------ touching

    private void OnTriggerEnter(Collider other)
    {
        if (_decided || _claimSent) return;

        if (!IsLocalPlayer(other, out string why))
        {
            Log($"Touched by '{other.name}' but ignored ({why}).");
            return;
        }

        bool networked = Object != null && Object.IsValid && Runner != null;

        if (!networked)
        {
            // This object was not spawned by Fusion, so there is no shared state to decide a winner.
            if (!_warnedNotNetworked)
            {
                _warnedNotNetworked = true;
                Debug.LogWarning($"[WinZone:{name}] This win zone is NOT networked (no NetworkObject on it, or Fusion has not spawned it). " +
                                 "The 'first player' check cannot work, so you are treated as the winner on THIS machine only. " +
                                 "Add a NetworkObject component to this object and make sure your NetworkLauncher uses a scene manager.", this);
            }
            _claimSent = true;
            ApplyResult(-1, false);
            return;
        }

        _claimSent = true;
        int me = Runner.LocalPlayer.PlayerId;
        Debug.Log($"[WinZone:{name}] Local player (Player {me}) reached the win zone, asking the state authority if I am first...", this);
        RPC_ClaimWin(me);
    }

    /// <summary>True only for the player controlled on THIS machine.</summary>
    private static bool IsLocalPlayer(Collider c, out string reason)
    {
        var xr = LocalXROriginProvider.Instance;
        if (xr != null && xr.Root != null && c.transform.IsChildOf(xr.Root))
        {
            reason = "local XR rig";
            return true;
        }

        var avatar = c.GetComponentInParent<NetworkAvatarSetup>();
        if (avatar != null && avatar.Object != null && avatar.Object.IsValid)
        {
            if (avatar.Object.HasStateAuthority)
            {
                reason = "local networked avatar";
                return true;
            }
            reason = "another player's avatar";
            return false;
        }

        reason = "not a player";
        return false;
    }

    // ------------------------------------------------------------------ deciding the winner (network)

    /// <summary>Sent by every player who reaches the zone. Runs ONLY on the state authority, which accepts the first request.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ClaimWin(int playerId)
    {
        if (WinnerPlayerId != 0)
        {
            Debug.Log($"[WinZone:{name}] (state authority) Player {playerId} was too late, Player {WinnerPlayerId} already won.", this);
            return;
        }

        WinnerPlayerId = playerId;
        Debug.Log($"[WinZone:{name}] (state authority) Player {playerId} reached the zone FIRST and wins.", this);
        RPC_AnnounceWinner(playerId);
    }

    /// <summary>Sent by the state authority. Runs on EVERY machine.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceWinner(int playerId)
    {
        ApplyResult(playerId, false);
    }

    // ------------------------------------------------------------------ result (runs on every machine)

    private void ApplyResult(int winnerId, bool silent)
    {
        if (_decided) return;
        _decided = true;

        int me = Runner != null ? Runner.LocalPlayer.PlayerId : -1;
        bool iWon = winnerId == me || winnerId == -1;   // -1 = not networked fallback

        Debug.Log($"[WinZone:{name}] RESULT: Player {winnerId} won. {(iWon ? "YOU WON!" : "Somebody else won (you are Player " + me + ").")}{(silent ? " (late join, shown silently)" : "")}", this);

        if (winVfx != null)
        {
            winVfx.SetActive(true);
            Log($"Win VFX '{winVfx.name}' switched ON.");
        }

        if (_col != null) _col.enabled = false;

        if (silent) return;

        if (iWon)
        {
            PlaySfx2D(winnerSfx, sfxVolume, "winner");
            onIWon?.Invoke();
        }
        else
        {
            PlaySfx2D(someoneElseWonSfx, sfxVolume, "someone-else-won");
            onSomeoneElseWon?.Invoke();
        }
    }

    // ------------------------------------------------------------------ helpers

    private void PlaySfx2D(AudioClip clip, float volume, string label)
    {
        if (clip == null)
        {
            Log($"No {label} sound assigned, nothing played.");
            return;
        }

        var go = new GameObject("SFX_" + clip.name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 0f;
        src.playOnAwake = false;
        src.Play();
        UnityEngine.Object.Destroy(go, clip.length + 0.25f);

        Log($"Played {label} sound '{clip.name}' (volume {volume:F2}).");
    }

    private void Log(string msg)
    {
        if (verboseLogs) Debug.Log($"[WinZone:{name}] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.6f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
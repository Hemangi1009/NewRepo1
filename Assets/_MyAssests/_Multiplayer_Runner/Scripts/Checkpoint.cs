using System.Collections;
using System.Text;
using Fusion;
using UnityEngine;

/// <summary>
/// CHECKPOINT (one script for everything). Put it on EVERY checkpoint object that has a TRIGGER collider.
///
///   - Each checkpoint can belong to one player (Owner). Player 1's checkpoints are only collected by Player 1,
///     Player 2's only by Player 2. If another player touches it, NOTHING happens (ignored).
///     Owner = Anyone means every player can collect it.
///   - When the OWNER touches it: a sound plays, the VFX scales up a little for ~0.5 s, then is disabled,
///     and this checkpoint becomes that player's respawn point.
///   - The saved respawn point is static: every player runs the game in their own process,
///     so it is automatically "per player" and never shared with the other player.
///   - Give each checkpoint an Order number (1, 2, 3 ...) within its player's route so touching an
///     EARLIER one later does not replace a later one.
///   - Respawning:  Checkpoint.RespawnLocalPlayer()  or, inside LocalXROriginProvider,
///                  Checkpoint.TryGetRespawnPose(out pos, out rot)
///
/// The owner number is the Fusion player number (the same number that appears in logs like [Player:1]).
/// It is read from the running NetworkRunner, or, if that is not found, from the avatar this player controls.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    public enum CheckpointOwner { Anyone = 0, Player1 = 1, Player2 = 2, Player3 = 3, Player4 = 4 }

    // ------------------------------------------------------------------ inspector

    [Header("Owner (which player can collect this checkpoint)")]
    [Tooltip("Anyone = every player can collect it. Player1 / Player2 = only that player (Fusion PlayerId) can. Others touching it is ignored.")]
    [SerializeField] private CheckpointOwner owner = CheckpointOwner.Anyone;
    [Tooltip("If ON, the VFX of a checkpoint that belongs to ANOTHER player is hidden on your screen.")]
    [SerializeField] private bool hideVfxForOtherPlayers = false;

    [Header("Order")]
    [Tooltip("1 for the first checkpoint, 2 for the second, and so on. Leave all at 0 if you do not care about order.")]
    [SerializeField] private int order = 0;
    [Tooltip("If ON, touching a checkpoint with a LOWER order than the one already saved will not replace the respawn point (the VFX still plays).")]
    [SerializeField] private bool ignoreEarlierCheckpoints = true;

    [Header("VFX")]
    [Tooltip("The checkpoint VFX object (usually a child). It is scaled up a little and then disabled.")]
    [SerializeField] private GameObject vfxObject;
    [Tooltip("How much bigger the VFX gets before it disappears (1.3 = 30 percent bigger).")]
    [SerializeField] private float scaleMultiplier = 1.3f;
    [Tooltip("Seconds the scale-up takes before the VFX is disabled.")]
    [SerializeField] private float scaleDuration = 0.5f;

    [Header("Sound")]
    [Tooltip("Played once when the local player steps on this checkpoint. Leave empty for no sound.")]
    [SerializeField] private AudioClip stepOnSfx;
    [Range(0f, 1f)]
    [SerializeField] private float stepOnSfxVolume = 1f;

    [Header("Respawn")]
    [Tooltip("Where the player appears after dying. If empty, this checkpoint object's own position is used. The height is snapped to the ground by LocalXROriginProvider.")]
    [SerializeField] private Transform respawnPoint;
    [Tooltip("Also turn the player to face the respawn point's forward direction.")]
    [SerializeField] private bool matchFacing = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    // ------------------------------------------------------------------ per player (this machine only)

    private static bool s_hasCheckpoint;
    private static Vector3 s_position;
    private static Quaternion s_rotation = Quaternion.identity;
    private static bool s_matchFacing = true;
    private static int s_order = int.MinValue;
    private static string s_name = "NONE";
    private static NetworkRunner s_runner;

    /// <summary>True when the local player has reached at least one checkpoint.</summary>
    public static bool HasCheckpoint => s_hasCheckpoint;

    // ------------------------------------------------------------------ instance state

    private bool _activated;
    private Vector3 _originalScale;
    private Collider _col;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_hasCheckpoint = false;
        s_position = Vector3.zero;
        s_rotation = Quaternion.identity;
        s_matchFacing = true;
        s_order = int.MinValue;
        s_name = "NONE";
        s_runner = null;
    }

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        _col = GetComponent<Collider>();

        if (!_col.isTrigger)
            Debug.LogWarning($"[Checkpoint:{name}] The collider is NOT a trigger. Tick 'Is Trigger' on it, otherwise the player is blocked and nothing is detected.", this);

        if (vfxObject == null)
            Debug.LogWarning($"[Checkpoint:{name}] No VFX object assigned. The checkpoint will still work, but nothing will be scaled or disabled.", this);
        else
            _originalScale = vfxObject.transform.localScale;

        Log($"Ready. Owner={owner}, Order={order}, VFX='{(vfxObject != null ? vfxObject.name : "NONE")}', sound='{(stepOnSfx != null ? stepOnSfx.name : "NONE")}', scale x{scaleMultiplier} over {scaleDuration}s, respawn point={(respawnPoint != null ? respawnPoint.name : "this object")}.");
    }

    private void Start()
    {
        if (hideVfxForOtherPlayers && owner != CheckpointOwner.Anyone && vfxObject != null)
            StartCoroutine(HideIfNotMine());
    }

    private IEnumerator HideIfNotMine()
    {
        int me = -1;
        float waited = 0f;
        while (!TryGetLocalPlayerNumber(out me, out _))
        {
            waited += 0.25f;
            if (waited > 30f)
            {
                Debug.LogWarning($"[Checkpoint:{name}] Could not find the local player number within 30 s, VFX left visible. {DescribeNetworkState()}", this);
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }

        if (me != (int)owner)
        {
            vfxObject.SetActive(false);
            if (_col != null) _col.enabled = false;
            Log($"Belongs to {owner} but you are Player {me}: VFX hidden and trigger disabled on this machine.");
        }
    }

    // ------------------------------------------------------------------ who am I

    /// <summary>
    /// The Fusion player number (1, 2, ...) of the player on THIS machine.
    /// 1) from the running NetworkRunner, 2) from the avatar this machine controls.
    /// </summary>
    private static bool TryGetLocalPlayerNumber(out int number, out string source)
    {
        number = -1;
        source = "";

        // 1) the running NetworkRunner (looks at ALL runners and takes the one that is running)
        if (s_runner == null || !s_runner.IsRunning)
        {
            s_runner = null;
            foreach (var r in FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r != null && r.IsRunning) { s_runner = r; break; }
            }
        }

        if (s_runner != null && s_runner.LocalPlayer != PlayerRef.None)
        {
            number = s_runner.LocalPlayer.PlayerId;
            source = "network runner '" + s_runner.name + "'";
            return true;
        }

        // 2) the avatar this machine has authority over: its owner is the local player
        foreach (var a in FindObjectsByType<NetworkAvatarSetup>(FindObjectsSortMode.None))
        {
            if (a == null || a.Object == null || !a.Object.IsValid || !a.Object.HasStateAuthority) continue;

            number = a.Object.StateAuthority.PlayerId;
            source = "local avatar '" + a.name + "'";
            return true;
        }

        return false;
    }

    /// <summary>One line describing what the network looks like, for the warning when the player number cannot be found.</summary>
    private static string DescribeNetworkState()
    {
        var sb = new StringBuilder();
        var runners = FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.Append($"NetworkRunners found: {runners.Length}");
        foreach (var r in runners)
            if (r != null) sb.Append($" ['{r.name}' IsRunning={r.IsRunning}, active={r.gameObject.activeInHierarchy}]");

        var avatars = FindObjectsByType<NetworkAvatarSetup>(FindObjectsSortMode.None);
        int local = 0;
        foreach (var a in avatars)
            if (a != null && a.Object != null && a.Object.IsValid && a.Object.HasStateAuthority) local++;
        sb.Append($". NetworkAvatarSetup found: {avatars.Length} (controlled by this machine: {local}).");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ touching

    private void OnTriggerEnter(Collider other)
    {
        if (_activated) return;

        if (!IsLocalPlayer(other, out string why))
        {
            Log($"Touched by '{other.name}' but ignored ({why}).");
            return;
        }

        if (owner != CheckpointOwner.Anyone)
        {
            if (!TryGetLocalPlayerNumber(out int me, out string source))
            {
                Debug.LogWarning($"[Checkpoint:{name}] Touched by '{other.name}' but ignored: cannot tell which player you are. {DescribeNetworkState()}", this);
                return;
            }

            if (me != (int)owner)
            {
                Log($"Touched by '{other.name}' but ignored: this checkpoint belongs to {owner}, you are Player {me} (from {source}).");
                return;
            }

            Log($"Owner check passed: you are Player {me} (from {source}).");
        }

        Activate(other.name);
    }

    /// <summary>True only for the player controlled on THIS machine.</summary>
    private static bool IsLocalPlayer(Collider c, out string reason)
    {
        // 1) the local XR rig
        var xr = LocalXROriginProvider.Instance;
        if (xr != null && xr.Root != null && c.transform.IsChildOf(xr.Root))
        {
            reason = "local XR rig";
            return true;
        }

        // 2) the local networked avatar (the one this client has authority over)
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

    private void Activate(string touchedBy)
    {
        _activated = true;

        bool saveAsRespawn = !(ignoreEarlierCheckpoints && s_hasCheckpoint && order < s_order);

        if (saveAsRespawn)
        {
            Transform point = respawnPoint != null ? respawnPoint : transform;
            s_hasCheckpoint = true;
            s_position = point.position;
            s_rotation = point.rotation;
            s_matchFacing = matchFacing;
            s_order = order;
            s_name = name;

            Debug.Log($"[Checkpoint:{name}] REACHED by local player (touched by '{touchedBy}'). Respawn point saved at {s_position} (order {order}).", this);
        }
        else
        {
            Debug.Log($"[Checkpoint:{name}] REACHED by local player (touched by '{touchedBy}') but it is EARLIER (order {order}) than the saved checkpoint '{s_name}' (order {s_order}). Respawn point NOT changed.", this);
        }

        // sound for the player who stepped on it
        if (stepOnSfx != null)
        {
            PlaySfx2D(stepOnSfx, stepOnSfxVolume);
            Log($"Step-on sound played ('{stepOnSfx.name}', volume {stepOnSfxVolume:F2}).");
        }
        else
        {
            Log("No step-on sound assigned (Step On Sfx is empty).");
        }

        // do not detect this player again; the checkpoint is used up on this machine
        if (_col != null) _col.enabled = false;

        if (vfxObject != null) StartCoroutine(ScaleThenDisable());
    }

    private IEnumerator ScaleThenDisable()
    {
        Transform t = vfxObject.transform;
        Vector3 from = _originalScale;
        Vector3 to = _originalScale * scaleMultiplier;
        float duration = Mathf.Max(0.01f, scaleDuration);

        Log($"VFX scaling {from} -> {to} over {duration}s.");

        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / duration));
            t.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }

        t.localScale = to;
        vfxObject.SetActive(false);
        t.localScale = _originalScale; // so it looks normal if you ever enable it again

        Log("VFX finished and DISABLED.");
    }

    /// <summary>Plays a clip as a flat (2D) sound. A temporary object plays it and removes itself afterwards.</summary>
    private static void PlaySfx2D(AudioClip clip, float volume)
    {
        if (clip == null) return;

        var go = new GameObject("SFX_" + clip.name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 0f;      // 2D: heard the same everywhere, even after the rig teleports
        src.playOnAwake = false;
        src.Play();
        UnityEngine.Object.Destroy(go, clip.length + 0.25f);
    }

    // ------------------------------------------------------------------ respawn API

    /// <summary>
    /// Gives the local player's last checkpoint as a position + rotation.
    /// Returns false if the player has not reached any checkpoint yet (use your normal spawn then).
    /// LocalXROriginProvider calls this inside its fall respawn.
    /// </summary>
    public static bool TryGetRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!s_hasCheckpoint) return false;

        position = s_position;

        if (s_matchFacing)
        {
            rotation = Quaternion.Euler(0f, s_rotation.eulerAngles.y, 0f);
        }
        else
        {
            var xr = LocalXROriginProvider.Instance;
            float yaw = (xr != null && xr.Root != null) ? xr.Root.eulerAngles.y : 0f;
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        Debug.Log($"[Checkpoint] Respawn pose requested: checkpoint '{s_name}' (order {s_order}) position={position}, yaw={rotation.eulerAngles.y:F0}.");
        return true;
    }

    /// <summary>
    /// Moves the local player to their last checkpoint using LocalXROriginProvider.TeleportTo
    /// (ground snapping, movement lock and velocity reset are handled there).
    /// Returns false if the player has not reached any checkpoint yet.
    /// </summary>
    public static bool RespawnLocalPlayer()
    {
        if (!TryGetRespawnPose(out Vector3 pos, out Quaternion rot))
        {
            Debug.LogWarning("[Checkpoint] RespawnLocalPlayer called but the local player has no checkpoint yet. Use your normal start position.");
            return false;
        }

        var xr = LocalXROriginProvider.Instance;
        if (xr == null)
        {
            Debug.LogError("[Checkpoint] RespawnLocalPlayer: LocalXROriginProvider.Instance is missing, cannot move the player.");
            return false;
        }

        Debug.Log($"[Checkpoint] RESPAWNING local player at checkpoint '{s_name}' (order {s_order}) via TeleportTo({pos}).");
        xr.TeleportTo(pos, rot);
        return true;
    }

    // ------------------------------------------------------------------ editor helpers

    [ContextMenu("Debug: Respawn local player now")]
    private void DebugRespawn()
    {
        RespawnLocalPlayer();
    }

    [ContextMenu("Debug: Print saved checkpoint")]
    private void DebugPrint()
    {
        Debug.Log(s_hasCheckpoint
            ? $"[Checkpoint] Saved checkpoint = '{s_name}' (order {s_order}) at {s_position}."
            : "[Checkpoint] No checkpoint saved yet.");
    }

    [ContextMenu("Debug: Print which player I am")]
    private void DebugWhoAmI()
    {
        if (TryGetLocalPlayerNumber(out int me, out string source))
            Debug.Log($"[Checkpoint] You are Player {me} (from {source}).");
        else
            Debug.LogWarning($"[Checkpoint] Cannot tell which player you are. {DescribeNetworkState()}");
    }

    [ContextMenu("Debug: Play step-on sound")]
    private void DebugPlaySfx()
    {
        PlaySfx2D(stepOnSfx, stepOnSfxVolume);
    }

    private void Log(string msg)
    {
        if (verboseLogs) Debug.Log($"[Checkpoint:{name}] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        Transform p = respawnPoint != null ? respawnPoint : transform;
        Gizmos.color = _activated ? Color.gray : Color.green;
        Gizmos.DrawWireSphere(p.position, 0.25f);
        Gizmos.DrawLine(p.position, p.position + p.forward * 0.6f);
    }
}
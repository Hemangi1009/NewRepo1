//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

//// Put this on every letter, on the SAME GameObject as NetworkObject + StickToBoard.
//// It shares "is this letter stuck on the board right now?" with all players, so the
//// board (LetterBoardFade) can read it no matter who placed the letter.
//[RequireComponent(typeof(StickToBoard))]
//public class NetworkedLetter : NetworkBehaviour
//{
//    // Every letter that is currently spawned in the network session.
//    public static readonly List<NetworkedLetter> All = new List<NetworkedLetter>();

//    [Tooltip("The character this letter shows, e.g. L, I, V or E.")]
//    [SerializeField] private string letter = "A";
//    public string Letter => string.IsNullOrEmpty(letter) ? "" : letter.Substring(0, 1);
//    public StickToBoard Stick => stick;

//    [Header("Debug")]
//    [SerializeField] private bool debugLogs = true;

//    // Set by whoever holds state authority of this letter (the last player who grabbed it).
//    [Networked] public NetworkBool IsStuck { get; private set; }

//    StickToBoard stick;
//    Collider[] cols;

//    // StickToBoard reports on the local client; we only write the networked value once we
//    // actually hold state authority (which arrives a moment after a grab in Shared mode).
//    bool? pending;
//    float _nextWaitLog;
//    float _nextVerify;
//    bool _lastSeenStuck;

//    void Log(string msg)
//    {
//        if (debugLogs) Debug.Log($"[Letter '{Letter}'] {msg}", this);
//    }

//    void Awake()
//    {
//        stick = GetComponent<StickToBoard>();
//        cols = GetComponentsInChildren<Collider>();
//    }

//    void OnEnable()
//    {
//        stick.Stuck += OnStuck;
//        stick.Unstuck += OnUnstuck;
//    }

//    void OnDisable()
//    {
//        stick.Stuck -= OnStuck;
//        stick.Unstuck -= OnUnstuck;
//    }

//    void OnStuck()
//    {
//        pending = true;
//        Log("Locally STUCK to the board (this player attached it). Waiting to publish to the network.");
//    }

//    void OnUnstuck()
//    {
//        pending = false;
//        Log("Locally UNSTUCK (picked up or reset). Waiting to publish to the network.");
//    }

//    public override void Spawned()
//    {
//        if (!All.Contains(this)) All.Add(this);
//        _lastSeenStuck = IsStuck;
//        Log($"Spawned. Registered letters now: {All.Count}. State authority = Player {Object.StateAuthority.PlayerId} (I am Player {Runner.LocalPlayer.PlayerId}, IsMine = {Object.HasStateAuthority}). IsStuck = {IsStuck}");
//    }

//    public override void Despawned(NetworkRunner runner, bool hasState)
//    {
//        All.Remove(this);
//        Log($"Despawned. Registered letters now: {All.Count}");
//    }

//    void Update()
//    {
//        if (Object == null || Runner == null || !Runner.IsRunning) return;

//        // Log the stuck state as seen on THIS client, whoever changed it (shows remote players' letters too).
//        if (IsStuck != _lastSeenStuck)
//        {
//            _lastSeenStuck = IsStuck;
//            Log($"Network state changed on this client: IsStuck = {IsStuck} (authority = Player {Object.StateAuthority.PlayerId})");
//        }

//        VerifyStillOnBoard();

//        if (!pending.HasValue) return;

//        if (!Object.HasStateAuthority)
//        {
//            // In Shared mode authority arrives a moment after the grab
//            if (Time.time >= _nextWaitLog)
//            {
//                _nextWaitLog = Time.time + 1f;
//                Log($"Waiting for state authority before publishing IsStuck = {pending.Value} (currently held by Player {Object.StateAuthority.PlayerId}).");
//            }
//            return;
//        }

//        IsStuck = pending.Value;
//        Log($"Published to network: IsStuck = {IsStuck}");
//        pending = null;
//    }

//    // Safety net: a letter may only stay 'stuck' while it is really touching the board.
//    // If it drifted away (thrown off, moved by something else...) it is released and falls.
//    void VerifyStillOnBoard()
//    {
//        if (!IsStuck || pending.HasValue || !Object.HasStateAuthority) return;
//        if (!stick.IsSettled || Time.time < _nextVerify) return;
//        _nextVerify = Time.time + 0.2f;

//        if (stick.IsTouchingBoard(out float gap)) return;

//        Debug.LogWarning($"[Letter '{Letter}'] Marked as stuck but it is NOT touching the board (gap = {gap:F3} m). Un-sticking it so it doesn't count.", this);
//        stick.DropFromBoard();
//        IsStuck = false;
//        pending = null;
//        Log("Published to network: IsStuck = False (was floating).");
//    }

//    // World-space middle of the letter (used to work out left-to-right order).
//    public Vector3 Center
//    {
//        get
//        {
//            bool first = true;
//            Bounds b = default;
//            foreach (var c in cols)
//            {
//                if (c == null || !c.enabled || c.isTrigger) continue;
//                if (first) { b = c.bounds; first = false; } else b.Encapsulate(c.bounds);
//            }
//            return first ? transform.position : b.center;
//        }
//    }

//    // Send the letter back to where it started. Safe to call from any client.
//    public void RequestReset()
//    {
//        if (Object == null) return;

//        if (Object.HasStateAuthority)
//        {
//            Log("Reset requested - I hold authority, resetting directly.");
//            DoReset();
//        }
//        else
//        {
//            Log($"Reset requested - asking authority holder Player {Object.StateAuthority.PlayerId} via RPC.");
//            RPC_ResetToHome();
//        }
//    }

//    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
//    void RPC_ResetToHome()
//    {
//        Log("Reset RPC received on the authority client.");
//        DoReset();
//    }

//    void DoReset()
//    {
//        stick.ResetToHome();   // also raises Unstuck
//        IsStuck = false;
//        pending = null;
//        Log("Reset DONE - letter is back at its starting position and IsStuck = false.");
//    }
//}
using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Put this on every letter, on the SAME GameObject as NetworkObject + StickToBoard.
// It shares "is this letter stuck on the board right now?" with all players, so the
// board (LetterBoardFade) can read it no matter who placed the letter.
[RequireComponent(typeof(StickToBoard))]
public class NetworkedLetter : NetworkBehaviour
{
    // Every letter that is currently spawned in the network session.
    public static readonly List<NetworkedLetter> All = new List<NetworkedLetter>();

    [Tooltip("The character this letter shows, e.g. L, I, V or E.")]
    [SerializeField] private string letter = "A";
    public string Letter => string.IsNullOrEmpty(letter) ? "" : letter.Substring(0, 1);
    public StickToBoard Stick => stick;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    // Set by whoever holds state authority of this letter (the last player who grabbed it).
    [Networked] public NetworkBool IsStuck { get; private set; }

    StickToBoard stick;
    Collider[] cols;

    // StickToBoard reports on the local client; we only write the networked value once we
    // actually hold state authority (which arrives a moment after a grab in Shared mode).
    bool? pending;
    float _nextWaitLog;
    float _nextVerify;
    bool _lastSeenStuck;
    float _verifyResetUntil = -1f;
    float _nextResetVerify;

    void Log(string msg)
    {
        if (debugLogs) Debug.Log($"[Letter '{Letter}'] {msg}", this);
    }

    void Awake()
    {
        stick = GetComponent<StickToBoard>();
        cols = GetComponentsInChildren<Collider>();
    }

    void OnEnable()
    {
        stick.Stuck += OnStuck;
        stick.Unstuck += OnUnstuck;
    }

    void OnDisable()
    {
        stick.Stuck -= OnStuck;
        stick.Unstuck -= OnUnstuck;
    }

    void OnStuck()
    {
        pending = true;
        Log("Locally STUCK to the board (this player attached it). Waiting to publish to the network.");
    }

    void OnUnstuck()
    {
        pending = false;
        Log("Locally UNSTUCK (picked up or reset). Waiting to publish to the network.");
    }

    public override void Spawned()
    {
        if (!All.Contains(this)) All.Add(this);
        _lastSeenStuck = IsStuck;
        Log($"Spawned. Registered letters now: {All.Count}. State authority = Player {Object.StateAuthority.PlayerId} (I am Player {Runner.LocalPlayer.PlayerId}, IsMine = {Object.HasStateAuthority}). IsStuck = {IsStuck}");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        All.Remove(this);
        Log($"Despawned. Registered letters now: {All.Count}");
    }

    void Update()
    {
        if (Object == null || Runner == null || !Runner.IsRunning) return;

        // Log the stuck state as seen on THIS client, whoever changed it (shows remote players' letters too).
        if (IsStuck != _lastSeenStuck)
        {
            _lastSeenStuck = IsStuck;
            Log($"Network state changed on this client: IsStuck = {IsStuck} (authority = Player {Object.StateAuthority.PlayerId})");
        }

        VerifyStillOnBoard();
        VerifyReset();

        if (!pending.HasValue) return;

        if (!Object.HasStateAuthority)
        {
            // In Shared mode authority arrives a moment after the grab
            if (Time.time >= _nextWaitLog)
            {
                _nextWaitLog = Time.time + 1f;
                Log($"Waiting for state authority before publishing IsStuck = {pending.Value} (currently held by Player {Object.StateAuthority.PlayerId}).");
            }
            return;
        }

        IsStuck = pending.Value;
        Log($"Published to network: IsStuck = {IsStuck}");
        pending = null;
    }

    // Safety net: a letter may only stay 'stuck' while it is really touching the board.
    // If it drifted away (thrown off, moved by something else...) it is released and falls.
    void VerifyStillOnBoard()
    {
        if (!IsStuck || pending.HasValue || !Object.HasStateAuthority) return;
        if (!stick.IsSettled || Time.time < _nextVerify) return;
        _nextVerify = Time.time + 0.2f;

        if (stick.IsTouchingBoard(out float gap)) return;

        Debug.LogWarning($"[Letter '{Letter}'] Marked as stuck but it is NOT touching the board (gap = {gap:F3} m). Un-sticking it so it doesn't count.", this);
        stick.DropFromBoard();
        IsStuck = false;
        pending = null;
        Log("Published to network: IsStuck = False (was floating).");
    }

    // After a reset, make sure the letter really is back at its start position for a short while.
    // If it isn't (dropped, pushed back onto the board...) it is reset again. Stops as soon as someone grabs it.
    void VerifyReset()
    {
        if (_verifyResetUntil < 0f || !Object.HasStateAuthority) return;
        if (Time.time > _verifyResetUntil || stick.IsHeld) { _verifyResetUntil = -1f; return; }
        if (Time.time < _nextResetVerify) return;
        _nextResetVerify = Time.time + 0.25f;

        if (!stick.IsSettled) return;   // still being pinned at home by the reset

        float d = Vector3.Distance(transform.position, stick.HomePosition);
        if (d <= 0.25f) return;

        Debug.LogWarning($"[Letter '{Letter}'] Reset did not hold - letter is {d:F2} m from its start position. Resetting again.", this);
        stick.ResetToHome();
        IsStuck = false;
        pending = null;
    }

    // World-space middle of the letter (used to work out left-to-right order).
    public Vector3 Center
    {
        get
        {
            bool first = true;
            Bounds b = default;
            foreach (var c in cols)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (first) { b = c.bounds; first = false; } else b.Encapsulate(c.bounds);
            }
            return first ? transform.position : b.center;
        }
    }

    // Send the letter back to where it started. Safe to call from any client.
    public void RequestReset()
    {
        if (Object == null) return;

        if (Object.HasStateAuthority)
        {
            Log("Reset requested - I hold authority, resetting directly.");
            DoReset();
        }
        else
        {
            Log($"Reset requested - asking authority holder Player {Object.StateAuthority.PlayerId} via RPC.");
            RPC_ResetToHome();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_ResetToHome()
    {
        Log("Reset RPC received on the authority client.");
        DoReset();
    }

    void DoReset()
    {
        stick.ResetToHome();   // also raises Unstuck
        IsStuck = false;
        pending = null;
        _verifyResetUntil = Time.time + 2.5f;
        _nextResetVerify = Time.time + 0.6f;
        Log("Reset DONE - letter is back at its starting position and IsStuck = false.");
    }
}
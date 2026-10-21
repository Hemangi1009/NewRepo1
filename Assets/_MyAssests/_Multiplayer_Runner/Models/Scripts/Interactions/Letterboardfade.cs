//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

//// Put this on the board, together with a NetworkObject.
////
//// The word is checked by where the stuck letters sit on the board (left to right),
//// NOT by the order they were attached. When they spell the target word:
////   fade out  ->  stay hidden (3 s)  ->  fade in
//// The state is networked, so every player sees the same fade.
//public class LetterBoardFade : NetworkBehaviour
//{
//    public enum BoardPhase { Idle, FadingOut, Hidden, FadingIn }

//    [Header("Word")]
//    [SerializeField] private string targetWord = "LIVE";
//    [SerializeField] private bool ignoreCase = true;

//    [Header("Reading direction")]
//    [Tooltip("Empty child of the board. Its +X must point LEFT -> RIGHT as the players see the board, and +Y must point up. Falls back to this object's own axes if empty.")]
//    [SerializeField] private Transform readingFrame;
//    [Tooltip("Optional. All letters must sit within this height of each other (same row). 0 = don't check.")]
//    [SerializeField] private float maxRowSpread = 0f;

//    [Header("Fade")]
//    [SerializeField] private float fadeOutSeconds = 2f;
//    [SerializeField] private float hiddenSeconds = 3f;
//    [SerializeField] private float fadeInSeconds = 2f;
//    [Tooltip("The word must stay correct this long before the fade starts (lets remote positions settle).")]
//    [SerializeField] private float confirmSeconds = 0.3f;

//    [Header("What fades")]
//    [Tooltip("Leave empty to use every Renderer under this object.")]
//    [SerializeField] private Renderer[] boardRenderers;
//    [SerializeField] private bool fadeLettersToo = true;
//    [Tooltip("While the board is hidden, send every letter back to where it started so the puzzle can be played again.")]
//    [SerializeField] private bool resetLettersWhenHidden = true;

//    [Header("Effect")]
//    [Tooltip("The effect object (with your particle systems on it or its children). It is kept DISABLED, enabled on EVERY player's screen when the word is solved, then disabled again. Can be a scene object or a prefab from the Project window. Needs no NetworkObject or NetworkTransform.")]
//    [SerializeField] private GameObject celebrationEffectRoot;
//    [Tooltip("How long the effect stays enabled before it is disabled again.")]
//    [SerializeField] private float effectActiveSeconds = 10f;
//    [Tooltip("Only used when the effect is a prefab asset: where it is spawned, relative to this board.")]
//    [SerializeField] private Vector3 effectSpawnOffset = Vector3.zero;

//    GameObject _effectObject;
//    ParticleSystem[] _effects;
//    float _effectOffTime = -1f;

//    [Header("Debug")]
//    [SerializeField] private bool debugLogs = true;
//    [Tooltip("While fading, print the fade progress this often (seconds).")]
//    [SerializeField] private float fadeLogInterval = 0.5f;

//    [Networked] public BoardPhase Phase { get; private set; }
//    [Networked] private TickTimer PhaseTimer { get; set; }

//    float _stableTime;
//    float _lastAlpha = 1f;

//    // logging state
//    string _lastCheckKey = null;
//    bool _confirming;
//    BoardPhase _lastLoggedPhase = BoardPhase.Idle;
//    float _nextFadeLog;

//    // ---- fading helpers ----
//    class FadeTarget
//    {
//        public Renderer renderer;
//        public Material[] mats;
//        public int[] colorIds;
//        public Color[] baseColors;
//    }

//    readonly List<FadeTarget> _targets = new List<FadeTarget>();
//    readonly HashSet<Renderer> _known = new HashSet<Renderer>();
//    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
//    static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

//    void Log(string msg)
//    {
//        if (debugLogs) Debug.Log($"[LetterBoard] {msg}", this);
//    }

//    public override void Spawned()
//    {
//        if (boardRenderers == null || boardRenderers.Length == 0)
//            boardRenderers = GetComponentsInChildren<Renderer>(true);

//        foreach (var r in boardRenderers) AddTarget(r);

//        _lastLoggedPhase = Phase;
//        SetupEffect();

//        int noFade = 0;
//        foreach (var t in _targets)
//            for (int i = 0; i < t.colorIds.Length; i++)
//                if (t.colorIds[i] == -1) noFade++;

//        Log($"Spawned. Target word = \"{targetWord}\" | board renderers = {boardRenderers.Length} | " +
//            $"fade {fadeOutSeconds}s -> hidden {hiddenSeconds}s -> fade {fadeInSeconds}s | " +
//            $"reading frame = {(readingFrame != null ? readingFrame.name : "NONE (using board's own axes)")} | " +
//            $"authority = Player {Object.StateAuthority.PlayerId} (I am Player {Runner.LocalPlayer.PlayerId}, IsMine = {Object.HasStateAuthority}) | " +
//            $"current phase = {Phase}");

//        if (noFade > 0)
//            Debug.LogWarning($"[LetterBoard] {noFade} material slot(s) have no _BaseColor/_Color property, so they cannot fade (they will just disappear at the end).", this);
//    }

//    // ------------------------------------------------------------------
//    // Game logic - runs only on whoever holds state authority of the board
//    // ------------------------------------------------------------------
//    public override void FixedUpdateNetwork()
//    {
//        if (!Object.HasStateAuthority) return;

//        switch (Phase)
//        {
//            case BoardPhase.Idle:
//                if (IsWordCorrect())
//                {
//                    if (!_confirming)
//                    {
//                        _confirming = true;
//                        Log($"Word looks CORRECT - confirming for {confirmSeconds}s before starting the fade...");
//                    }

//                    _stableTime += Runner.DeltaTime;
//                    if (_stableTime >= confirmSeconds)
//                    {
//                        _stableTime = 0f;
//                        _confirming = false;
//                        Log($"WORD \"{targetWord}\" CONFIRMED! Starting fade out.");
//                        SetPhase(BoardPhase.FadingOut, fadeOutSeconds);
//                    }
//                }
//                else
//                {
//                    if (_confirming)
//                        Log("Word stopped being correct before it was confirmed - cancelled.");
//                    _confirming = false;
//                    _stableTime = 0f;
//                }
//                break;

//            case BoardPhase.FadingOut:
//                if (PhaseTimer.Expired(Runner))
//                {
//                    if (resetLettersWhenHidden)
//                    {
//                        Log($"Fade out finished - resetting {NetworkedLetter.All.Count} letter(s) to their start positions.");
//                        foreach (var l in NetworkedLetter.All)
//                            if (l != null) l.RequestReset();
//                    }
//                    else
//                    {
//                        Log("Fade out finished - letters are NOT being reset (resetLettersWhenHidden is off).");
//                    }
//                    SetPhase(BoardPhase.Hidden, hiddenSeconds);
//                }
//                break;

//            case BoardPhase.Hidden:
//                if (PhaseTimer.Expired(Runner))
//                    SetPhase(BoardPhase.FadingIn, fadeInSeconds);
//                break;

//            case BoardPhase.FadingIn:
//                if (PhaseTimer.Expired(Runner))
//                    SetPhase(BoardPhase.Idle, 0f);
//                break;
//        }
//    }

//    void SetPhase(BoardPhase phase, float seconds)
//    {
//        Log($"(authority) Changing phase {Phase} -> {phase}" + (seconds > 0f ? $" for {seconds}s" : ""));
//        Phase = phase;
//        PhaseTimer = seconds > 0f ? TickTimer.CreateFromSeconds(Runner, seconds) : default;
//    }

//    // Reads the letters that are stuck, orders them left -> right, and compares to the target word.
//    // Prints a log line only when the situation changes, so the console isn't flooded.
//    bool IsWordCorrect()
//    {
//        if (string.IsNullOrEmpty(targetWord)) return false;

//        var stuck = new List<NetworkedLetter>();
//        foreach (var l in NetworkedLetter.All)
//            if (l != null && l.IsStuck) stuck.Add(l);

//        Transform frame = readingFrame != null ? readingFrame : transform;
//        Vector3 right = frame.right;
//        Vector3 up = frame.up;

//        stuck.Sort((a, b) => Vector3.Dot(a.Center, right).CompareTo(Vector3.Dot(b.Center, right)));

//        var sb = new System.Text.StringBuilder();
//        foreach (var l in stuck) sb.Append(l.Letter);
//        string arrangement = sb.ToString();

//        bool ok = false;
//        string reason;

//        if (stuck.Count != targetWord.Length)
//        {
//            reason = $"only {stuck.Count}/{targetWord.Length} letters are on the board ({NetworkedLetter.All.Count} letters registered in total)";
//        }
//        else
//        {
//            float spread = 0f;
//            if (maxRowSpread > 0f)
//            {
//                float min = float.MaxValue, max = float.MinValue;
//                foreach (var l in stuck)
//                {
//                    float h = Vector3.Dot(l.Center, up);
//                    if (h < min) min = h;
//                    if (h > max) max = h;
//                }
//                spread = max - min;
//            }

//            if (maxRowSpread > 0f && spread > maxRowSpread)
//            {
//                reason = $"letters are not in one row (height spread {spread:F3} > allowed {maxRowSpread:F3})";
//            }
//            else
//            {
//                ok = string.Equals(arrangement, targetWord,
//                    ignoreCase ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal);
//                reason = ok ? "MATCH" : "wrong order";
//            }
//        }

//        string key = arrangement + "|" + reason;
//        if (key != _lastCheckKey)
//        {
//            _lastCheckKey = key;
//            string shown = arrangement.Length > 0 ? arrangement : "(nothing)";
//            Log($"Board check: left-to-right = \"{shown}\" | target = \"{targetWord}\" | {reason}");
//        }

//        return ok;
//    }

//    // ------------------------------------------------------------------
//    // Visuals - runs on every client, driven by the networked phase
//    // ------------------------------------------------------------------
//    public override void Render()
//    {
//        CheckEffectTimeout();

//        // Every client (not just the authority) reports phase changes it sees
//        if (Phase != _lastLoggedPhase)
//        {
//            Log($"Phase is now {Phase} (seen on this client, Player {Runner.LocalPlayer.PlayerId}).");
//            _lastLoggedPhase = Phase;
//            _nextFadeLog = 0f;
//            OnPhaseChanged(Phase);
//        }

//        float alpha = 1f;
//        switch (Phase)
//        {
//            case BoardPhase.FadingOut: alpha = 1f - Progress(fadeOutSeconds); break;
//            case BoardPhase.Hidden: alpha = 0f; break;
//            case BoardPhase.FadingIn: alpha = Progress(fadeInSeconds); break;
//        }
//        alpha = Mathf.SmoothStep(0f, 1f, alpha);

//        if (fadeLettersToo && alpha < 1f)
//        {
//            // pick up letters that spawned after this board did
//            foreach (var l in NetworkedLetter.All)
//                if (l != null) foreach (var r in l.GetComponentsInChildren<Renderer>(true)) AddTarget(r);
//        }

//        if (debugLogs && Phase != BoardPhase.Idle && Time.time >= _nextFadeLog)
//        {
//            _nextFadeLog = Time.time + fadeLogInterval;
//            float? rem = PhaseTimer.RemainingTime(Runner);
//            Log($"{Phase}: alpha = {alpha:F2}" + (rem.HasValue ? $", {rem.Value:F1}s left in this phase" : ""));
//        }

//        if (Mathf.Approximately(alpha, _lastAlpha)) return;
//        _lastAlpha = alpha;
//        ApplyAlpha(alpha);
//    }

//    // Runs on every client when the networked phase changes, so everyone sees the effect locally.
//    void OnPhaseChanged(BoardPhase phase)
//    {
//        if (phase == BoardPhase.FadingOut) ShowEffect();
//    }

//    void SetupEffect()
//    {
//        if (celebrationEffectRoot == null) return;

//        // A prefab asset from the Project window isn't in any scene - make a local copy of it.
//        _effectObject = celebrationEffectRoot.scene.IsValid()
//            ? celebrationEffectRoot
//            : Instantiate(celebrationEffectRoot, transform.position + effectSpawnOffset, transform.rotation);

//        _effects = _effectObject.GetComponentsInChildren<ParticleSystem>(true);
//        _effectObject.SetActive(false);   // hidden until the word is solved

//        if (_effects.Length == 0)
//            Debug.LogWarning("[LetterBoard] Celebration Effect Root has no ParticleSystem on it or its children.", this);
//        else
//            Log($"Celebration effect ready and DISABLED: '{_effectObject.name}' with {_effects.Length} particle system(s).");
//    }

//    void ShowEffect()
//    {
//        if (_effectObject == null) return;

//        _effectObject.SetActive(true);
//        foreach (var ps in _effects)
//        {
//            if (ps == null) continue;
//            ps.Clear(false);
//            ps.Play(false);   // (re)start, also if it was already running from the last time
//        }

//        _effectOffTime = Time.time + effectActiveSeconds;
//        Log($"Celebration effect ENABLED for {effectActiveSeconds}s.");
//    }

//    void CheckEffectTimeout()
//    {
//        if (_effectOffTime < 0f || Time.time < _effectOffTime) return;

//        _effectOffTime = -1f;
//        if (_effectObject != null) _effectObject.SetActive(false);
//        Log("Celebration effect DISABLED again.");
//    }

//    float Progress(float duration)
//    {
//        if (duration <= 0f) return 1f;
//        float? remaining = PhaseTimer.RemainingTime(Runner);
//        if (!remaining.HasValue) return 1f;
//        return Mathf.Clamp01(1f - remaining.Value / duration);
//    }

//    void AddTarget(Renderer r)
//    {
//        if (r == null || r is ParticleSystemRenderer || !_known.Add(r)) return; // never fade/hide the particle effect

//        var mats = r.materials; // per-renderer instances so shared materials aren't touched
//        var t = new FadeTarget
//        {
//            renderer = r,
//            mats = mats,
//            colorIds = new int[mats.Length],
//            baseColors = new Color[mats.Length]
//        };

//        for (int i = 0; i < mats.Length; i++)
//        {
//            if (mats[i] == null) { t.colorIds[i] = -1; continue; }

//            if (mats[i].HasProperty(BaseColorId)) t.colorIds[i] = BaseColorId;
//            else if (mats[i].HasProperty(ColorId)) t.colorIds[i] = ColorId;
//            else t.colorIds[i] = -1;

//            if (t.colorIds[i] != -1) t.baseColors[i] = mats[i].GetColor(t.colorIds[i]);
//        }
//        _targets.Add(t);
//        Log($"Registered renderer '{r.name}' for fading ({mats.Length} material slot(s)). Total fade targets: {_targets.Count}");
//    }

//    void ApplyAlpha(float alpha)
//    {
//        foreach (var t in _targets)
//        {
//            if (t.renderer == null) continue;

//            t.renderer.enabled = alpha > 0.001f;

//            for (int i = 0; i < t.mats.Length; i++)
//            {
//                if (t.mats[i] == null || t.colorIds[i] == -1) continue;
//                Color c = t.baseColors[i];
//                c.a = t.baseColors[i].a * alpha;
//                t.mats[i].SetColor(t.colorIds[i], c);
//            }
//        }
//    }
//}

using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Put this on the board, together with a NetworkObject.
//
// The word is checked by where the stuck letters sit on the board (left to right),
// NOT by the order they were attached. When they spell the target word:
//   swap to fade material -> wait (preFadeSeconds) -> fade out -> stay hidden -> fade in
//   -> wait (postFadeSeconds) -> swap back to the normal material
// The state is networked, so every player sees the same fade.
public class LetterBoardFade : NetworkBehaviour
{
    public enum BoardPhase { Idle, FadingOut, Hidden, FadingIn, PreFade, PostFade }

    [Header("Word")]
    [SerializeField] private string targetWord = "LIVE";
    [SerializeField] private bool ignoreCase = true;

    [Header("Reading direction")]
    [Tooltip("Empty child of the board. Its +X must point LEFT -> RIGHT as the players see the board, and +Y must point up. Falls back to this object's own axes if empty.")]
    [SerializeField] private Transform readingFrame;
    [Tooltip("Optional. All letters must sit within this height of each other (same row). 0 = don't check.")]
    [SerializeField] private float maxRowSpread = 0f;

    [Header("Fade")]
    [SerializeField] private float fadeOutSeconds = 2f;
    [SerializeField] private float hiddenSeconds = 3f;
    [SerializeField] private float fadeInSeconds = 2f;
    [Tooltip("The word must stay correct this long before the fade starts (lets remote positions settle).")]
    [SerializeField] private float confirmSeconds = 0.3f;

    [Header("Materials")]
    [Tooltip("Material used while the word is solved and the board fades. Must use a Transparent/Fade surface type so alpha works. The board starts with its normal (current) material and returns to it at the end.")]
    [SerializeField] private Material fadeMaterial;
    [Tooltip("Delay after the word is confirmed: the fade material is shown, then this many seconds later the fade out starts.")]
    [SerializeField] private float preFadeSeconds = 1f;
    [Tooltip("Delay after the fade in has finished, before switching back to the normal material.")]
    [SerializeField] private float postFadeSeconds = 1f;

    [Header("What fades")]
    [Tooltip("Leave empty to use every Renderer under this object.")]
    [SerializeField] private Renderer[] boardRenderers;
    [SerializeField] private bool fadeLettersToo = true;
    [Tooltip("While the board is hidden, send every letter back to where it started so the puzzle can be played again.")]
    [SerializeField] private bool resetLettersWhenHidden = true;

    [Header("Effect")]
    [Tooltip("The effect object (with your particle systems on it or its children). It is kept DISABLED, enabled on EVERY player's screen when the word is solved, then disabled again. Can be a scene object or a prefab from the Project window. Needs no NetworkObject or NetworkTransform.")]
    [SerializeField] private GameObject celebrationEffectRoot;
    [Tooltip("How long the effect stays enabled before it is disabled again.")]
    [SerializeField] private float effectActiveSeconds = 10f;
    [Tooltip("Only used when the effect is a prefab asset: where it is spawned, relative to this board.")]
    [SerializeField] private Vector3 effectSpawnOffset = Vector3.zero;

    GameObject _effectObject;
    ParticleSystem[] _effects;
    float _effectOffTime = -1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [Tooltip("While fading, print the fade progress this often (seconds).")]
    [SerializeField] private float fadeLogInterval = 0.5f;

    [Networked] public BoardPhase Phase { get; private set; }
    [Networked] private TickTimer PhaseTimer { get; set; }

    float _stableTime;
    float _lastAlpha = 1f;

    // logging state
    string _lastCheckKey = null;
    bool _confirming;
    BoardPhase _lastLoggedPhase = BoardPhase.Idle;
    float _nextFadeLog;

    // ---- fading helpers ----
    class MatSet
    {
        public Material[] mats;
        public int[] colorIds;
        public Color[] baseColors;
    }

    class FadeTarget
    {
        public Renderer renderer;
        public MatSet normal;
        public MatSet fade;   // null = this renderer never swaps material (e.g. letters)
        public MatSet active;
    }

    bool _fadeMatActive;

    readonly List<FadeTarget> _targets = new List<FadeTarget>();
    readonly HashSet<Renderer> _known = new HashSet<Renderer>();
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    void Log(string msg)
    {
        if (debugLogs) Debug.Log($"[LetterBoard] {msg}", this);
    }

    public override void Spawned()
    {
        if (boardRenderers == null || boardRenderers.Length == 0)
            boardRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (var r in boardRenderers) AddTarget(r, true);

        _lastLoggedPhase = Phase;
        SetupEffect();

        int noFade = 0;
        foreach (var t in _targets)
            for (int i = 0; i < t.normal.colorIds.Length; i++)
                if (t.normal.colorIds[i] == -1) noFade++;

        Log($"Spawned. Target word = \"{targetWord}\" | board renderers = {boardRenderers.Length} | " +
            $"fade {fadeOutSeconds}s -> hidden {hiddenSeconds}s -> fade {fadeInSeconds}s | " +
            $"reading frame = {(readingFrame != null ? readingFrame.name : "NONE (using board's own axes)")} | " +
            $"authority = Player {Object.StateAuthority.PlayerId} (I am Player {Runner.LocalPlayer.PlayerId}, IsMine = {Object.HasStateAuthority}) | " +
            $"current phase = {Phase}");

        if (noFade > 0)
            Debug.LogWarning($"[LetterBoard] {noFade} material slot(s) have no _BaseColor/_Color property, so they cannot fade (they will just disappear at the end).", this);
    }

    // ------------------------------------------------------------------
    // Game logic - runs only on whoever holds state authority of the board
    // ------------------------------------------------------------------
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        switch (Phase)
        {
            case BoardPhase.Idle:
                if (IsWordCorrect())
                {
                    if (!_confirming)
                    {
                        _confirming = true;
                        Log($"Word looks CORRECT - confirming for {confirmSeconds}s before starting the fade...");
                    }

                    _stableTime += Runner.DeltaTime;
                    if (_stableTime >= confirmSeconds)
                    {
                        _stableTime = 0f;
                        _confirming = false;
                        Log($"WORD \"{targetWord}\" CONFIRMED! Switching to fade material.");
                        if (preFadeSeconds > 0f) SetPhase(BoardPhase.PreFade, preFadeSeconds);
                        else StartFadeOut();
                    }
                }
                else
                {
                    if (_confirming)
                        Log("Word stopped being correct before it was confirmed - cancelled.");
                    _confirming = false;
                    _stableTime = 0f;
                }
                break;

            case BoardPhase.PreFade:
                if (PhaseTimer.Expired(Runner))
                    StartFadeOut();
                break;

            case BoardPhase.FadingOut:
                if (PhaseTimer.Expired(Runner))
                    SetPhase(BoardPhase.Hidden, hiddenSeconds);
                break;

            case BoardPhase.Hidden:
                if (PhaseTimer.Expired(Runner))
                    SetPhase(BoardPhase.FadingIn, fadeInSeconds);
                break;

            case BoardPhase.FadingIn:
                if (PhaseTimer.Expired(Runner))
                {
                    if (postFadeSeconds > 0f) SetPhase(BoardPhase.PostFade, postFadeSeconds);
                    else SetPhase(BoardPhase.Idle, 0f);
                }
                break;

            case BoardPhase.PostFade:
                if (PhaseTimer.Expired(Runner))
                    SetPhase(BoardPhase.Idle, 0f);
                break;
        }
    }

    // Starts the fade out and, at the same moment, sends every letter back to its start position.
    void StartFadeOut()
    {
        if (resetLettersWhenHidden)
        {
            Log($"Fade out STARTING - resetting {NetworkedLetter.All.Count} letter(s) to their start positions.");
            foreach (var l in NetworkedLetter.All)
                if (l != null) l.RequestReset();
        }
        else
        {
            Log("Fade out STARTING - letters are NOT being reset (resetLettersWhenHidden is off).");
        }
        SetPhase(BoardPhase.FadingOut, fadeOutSeconds);
    }

    void SetPhase(BoardPhase phase, float seconds)
    {
        Log($"(authority) Changing phase {Phase} -> {phase}" + (seconds > 0f ? $" for {seconds}s" : ""));
        Phase = phase;
        PhaseTimer = seconds > 0f ? TickTimer.CreateFromSeconds(Runner, seconds) : default;
    }

    // Reads the letters that are stuck, orders them left -> right, and compares to the target word.
    // Prints a log line only when the situation changes, so the console isn't flooded.
    bool IsWordCorrect()
    {
        if (string.IsNullOrEmpty(targetWord)) return false;

        var stuck = new List<NetworkedLetter>();
        foreach (var l in NetworkedLetter.All)
            if (l != null && l.IsStuck) stuck.Add(l);

        Transform frame = readingFrame != null ? readingFrame : transform;
        Vector3 right = frame.right;
        Vector3 up = frame.up;

        stuck.Sort((a, b) => Vector3.Dot(a.Center, right).CompareTo(Vector3.Dot(b.Center, right)));

        var sb = new System.Text.StringBuilder();
        foreach (var l in stuck) sb.Append(l.Letter);
        string arrangement = sb.ToString();

        bool ok = false;
        string reason;

        if (stuck.Count != targetWord.Length)
        {
            reason = $"only {stuck.Count}/{targetWord.Length} letters are on the board ({NetworkedLetter.All.Count} letters registered in total)";
        }
        else
        {
            float spread = 0f;
            if (maxRowSpread > 0f)
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var l in stuck)
                {
                    float h = Vector3.Dot(l.Center, up);
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
                spread = max - min;
            }

            if (maxRowSpread > 0f && spread > maxRowSpread)
            {
                reason = $"letters are not in one row (height spread {spread:F3} > allowed {maxRowSpread:F3})";
            }
            else
            {
                ok = string.Equals(arrangement, targetWord,
                    ignoreCase ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal);
                reason = ok ? "MATCH" : "wrong order";
            }
        }

        string key = arrangement + "|" + reason;
        if (key != _lastCheckKey)
        {
            _lastCheckKey = key;
            string shown = arrangement.Length > 0 ? arrangement : "(nothing)";
            Log($"Board check: left-to-right = \"{shown}\" | target = \"{targetWord}\" | {reason}");
        }

        return ok;
    }

    // ------------------------------------------------------------------
    // Visuals - runs on every client, driven by the networked phase
    // ------------------------------------------------------------------
    public override void Render()
    {
        CheckEffectTimeout();

        // Every client (not just the authority) reports phase changes it sees
        if (Phase != _lastLoggedPhase)
        {
            Log($"Phase is now {Phase} (seen on this client, Player {Runner.LocalPlayer.PlayerId}).");
            _lastLoggedPhase = Phase;
            _nextFadeLog = 0f;
            OnPhaseChanged(Phase);
        }

        float alpha = 1f;
        switch (Phase)
        {
            case BoardPhase.FadingOut: alpha = 1f - Progress(fadeOutSeconds); break;
            case BoardPhase.Hidden: alpha = 0f; break;
            case BoardPhase.FadingIn: alpha = Progress(fadeInSeconds); break;
        }
        alpha = Mathf.SmoothStep(0f, 1f, alpha);

        if (fadeLettersToo && alpha < 1f)
        {
            // pick up letters that spawned after this board did
            foreach (var l in NetworkedLetter.All)
                if (l != null) foreach (var r in l.GetComponentsInChildren<Renderer>(true)) AddTarget(r, false);
        }

        // Normal material while Idle, fade material for the whole solved sequence.
        bool wantFadeMat = Phase != BoardPhase.Idle;
        if (wantFadeMat != _fadeMatActive)
        {
            _fadeMatActive = wantFadeMat;
            SetMaterialMode(wantFadeMat);
            _lastAlpha = -1f;   // force the current alpha onto the newly active material
        }

        if (debugLogs && Phase != BoardPhase.Idle && Time.time >= _nextFadeLog)
        {
            _nextFadeLog = Time.time + fadeLogInterval;
            float? rem = PhaseTimer.RemainingTime(Runner);
            Log($"{Phase}: alpha = {alpha:F2}" + (rem.HasValue ? $", {rem.Value:F1}s left in this phase" : ""));
        }

        if (Mathf.Approximately(alpha, _lastAlpha)) return;
        _lastAlpha = alpha;
        ApplyAlpha(alpha);
    }

    // Runs on every client when the networked phase changes, so everyone sees the effect locally.
    void OnPhaseChanged(BoardPhase phase)
    {
        if (phase == BoardPhase.FadingOut) ShowEffect();
    }

    void SetupEffect()
    {
        if (celebrationEffectRoot == null) return;

        // A prefab asset from the Project window isn't in any scene - make a local copy of it.
        _effectObject = celebrationEffectRoot.scene.IsValid()
            ? celebrationEffectRoot
            : Instantiate(celebrationEffectRoot, transform.position + effectSpawnOffset, transform.rotation);

        _effects = _effectObject.GetComponentsInChildren<ParticleSystem>(true);
        _effectObject.SetActive(false);   // hidden until the word is solved

        if (_effects.Length == 0)
            Debug.LogWarning("[LetterBoard] Celebration Effect Root has no ParticleSystem on it or its children.", this);
        else
            Log($"Celebration effect ready and DISABLED: '{_effectObject.name}' with {_effects.Length} particle system(s).");
    }

    void ShowEffect()
    {
        if (_effectObject == null) return;

        _effectObject.SetActive(true);
        foreach (var ps in _effects)
        {
            if (ps == null) continue;
            ps.Clear(false);
            ps.Play(false);   // (re)start, also if it was already running from the last time
        }

        _effectOffTime = Time.time + effectActiveSeconds;
        Log($"Celebration effect ENABLED for {effectActiveSeconds}s.");
    }

    void CheckEffectTimeout()
    {
        if (_effectOffTime < 0f || Time.time < _effectOffTime) return;

        _effectOffTime = -1f;
        if (_effectObject != null) _effectObject.SetActive(false);
        Log("Celebration effect DISABLED again.");
    }

    float Progress(float duration)
    {
        if (duration <= 0f) return 1f;
        float? remaining = PhaseTimer.RemainingTime(Runner);
        if (!remaining.HasValue) return 1f;
        return Mathf.Clamp01(1f - remaining.Value / duration);
    }

    MatSet BuildSet(Material[] mats)
    {
        var set = new MatSet
        {
            mats = mats,
            colorIds = new int[mats.Length],
            baseColors = new Color[mats.Length]
        };

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) { set.colorIds[i] = -1; continue; }

            if (mats[i].HasProperty(BaseColorId)) set.colorIds[i] = BaseColorId;
            else if (mats[i].HasProperty(ColorId)) set.colorIds[i] = ColorId;
            else set.colorIds[i] = -1;

            if (set.colorIds[i] != -1) set.baseColors[i] = mats[i].GetColor(set.colorIds[i]);
        }
        return set;
    }

    void AddTarget(Renderer r, bool swapMaterial)
    {
        if (r == null || r is ParticleSystemRenderer || !_known.Add(r)) return; // never fade/hide the particle effect

        var t = new FadeTarget
        {
            renderer = r,
            normal = BuildSet(r.materials) // per-renderer instances so shared materials aren't touched
        };

        if (swapMaterial && fadeMaterial != null)
        {
            // one instance of the fade material per material slot
            var fadeMats = new Material[t.normal.mats.Length];
            for (int i = 0; i < fadeMats.Length; i++) fadeMats[i] = new Material(fadeMaterial);
            t.fade = BuildSet(fadeMats);
        }

        t.active = t.normal;
        _targets.Add(t);
        Log($"Registered renderer '{r.name}' for fading ({t.normal.mats.Length} material slot(s), swaps material = {t.fade != null}). Total fade targets: {_targets.Count}");
    }

    void SetMaterialMode(bool useFade)
    {
        Log(useFade ? "Switching board to the FADE material." : "Switching board back to the NORMAL material.");
        foreach (var t in _targets)
        {
            if (t.renderer == null || t.fade == null) continue;
            t.active = useFade ? t.fade : t.normal;
            t.renderer.sharedMaterials = t.active.mats;
        }
    }

    void ApplyAlpha(float alpha)
    {
        foreach (var t in _targets)
        {
            if (t.renderer == null) continue;

            t.renderer.enabled = alpha > 0.001f;

            var set = t.active;
            for (int i = 0; i < set.mats.Length; i++)
            {
                if (set.mats[i] == null || set.colorIds[i] == -1) continue;
                Color c = set.baseColors[i];
                c.a = set.baseColors[i].a * alpha;
                set.mats[i].SetColor(set.colorIds[i], c);
            }
        }
    }
}
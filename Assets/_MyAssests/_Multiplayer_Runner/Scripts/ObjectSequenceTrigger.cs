//using System.Collections;
//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

///// <summary>
///// OBJECT SEQUENCE TRIGGER. Put it on an object with a TRIGGER collider.
/////
///// When the local player touches the trigger, the objects in the list are switched on one by one
///// (Delay Between Objects apart). Every object stays active for Active Duration seconds, then it is
///// switched off, put back at its ORIGINAL position and rotation, and (if Loop is on) switched on again.
/////
///// Example with the defaults: object 1 appears at 0 s, object 2 at 2 s,
///// object 1 resets at 5 s, object 2 resets at 7 s, and so on.
/////
///// The objects normally have a Rigidbody (Use Gravity ON, Is Kinematic OFF) and fall when they are switched on.
///// Objects without a Rigidbody also work, they just appear and disappear.
///// </summary>
//[RequireComponent(typeof(Collider))]
//public class ObjectSequenceTrigger : MonoBehaviour
//{
//    [Header("Objects (in the order they appear)")]
//    [Tooltip("Drag the objects here. Element 0 appears first.")]
//    [SerializeField] private GameObject[] objects;
//    [Tooltip("Switch every object off when the scene starts, so they only appear when triggered.")]
//    [SerializeField] private bool hideAtStart = true;

//    [Header("Timing (seconds)")]
//    [Tooltip("Gap between one object appearing and the next one appearing.")]
//    [SerializeField] private float delayBetweenObjects = 2f;
//    [Tooltip("How long each object stays active before it is switched off and reset.")]
//    [SerializeField] private float activeDuration = 5f;
//    [Tooltip("Pause between 'switched off and reset' and 'switched on again'. 0 = immediately.")]
//    [SerializeField] private float restDelay = 0f;

//    [Header("Behaviour")]
//    [Tooltip("ON: every object keeps repeating (appear, wait, reset, appear again ...). OFF: each object runs once.")]
//    [SerializeField] private bool loop = true;
//    [Tooltip("ON: the trigger works only one time per play session, even after the sequence has finished.")]
//    [SerializeField] private bool triggerOnlyOnce = false;

//    [Header("Debug")]
//    [SerializeField] private bool verboseLogs = true;

//    // ------------------------------------------------------------------ internal

//    private class Item
//    {
//        public GameObject go;
//        public Rigidbody rb;
//        public Vector3 localPos;
//        public Quaternion localRot;
//        public Coroutine routine;
//    }

//    private readonly List<Item> _items = new List<Item>();
//    private bool _running;
//    private bool _usedOnce;
//    private int _finishedCount;

//    // ------------------------------------------------------------------ setup

//    private void Awake()
//    {
//        var col = GetComponent<Collider>();
//        if (!col.isTrigger)
//            Debug.LogWarning($"[ObjectSequence:{name}] The collider is NOT a trigger. Tick 'Is Trigger' on it, otherwise the player is blocked and nothing is detected.", this);

//        if (objects == null || objects.Length == 0)
//            Debug.LogWarning($"[ObjectSequence:{name}] No objects assigned.", this);

//        if (objects != null)
//        {
//            for (int i = 0; i < objects.Length; i++)
//            {
//                var go = objects[i];
//                if (go == null)
//                {
//                    Debug.LogWarning($"[ObjectSequence:{name}] Element {i} of the list is empty, skipped.", this);
//                    continue;
//                }

//                var item = new Item
//                {
//                    go = go,
//                    rb = go.GetComponent<Rigidbody>(),
//                    localPos = go.transform.localPosition,   // original place, saved BEFORE anything moves
//                    localRot = go.transform.localRotation
//                };
//                _items.Add(item);

//                if (item.rb == null)
//                    Log($"'{go.name}' has no Rigidbody, it will only appear and disappear.");
//                else if (item.rb.isKinematic || !item.rb.useGravity)
//                    Debug.LogWarning($"[ObjectSequence:{name}] '{go.name}' has Is Kinematic ON or Use Gravity OFF, so it will NOT fall. Untick Is Kinematic and tick Use Gravity.", go);

//                if (hideAtStart) go.SetActive(false);
//            }
//        }

//        Log($"Ready. {_items.Count} object(s), delay between objects={delayBetweenObjects}s, active duration={activeDuration}s, rest={restDelay}s, loop={loop}.");
//    }

//    private void OnDisable()
//    {
//        if (_running) StopSequence();
//    }

//    // ------------------------------------------------------------------ touching

//    private void OnTriggerEnter(Collider other)
//    {
//        if (!IsLocalPlayer(other, out string why))
//        {
//            Log($"Touched by '{other.name}' but ignored ({why}).");
//            return;
//        }

//        if (_running)
//        {
//            Log($"Touched by '{other.name}' but the sequence is already running, ignored.");
//            return;
//        }

//        if (triggerOnlyOnce && _usedOnce)
//        {
//            Log($"Touched by '{other.name}' but 'Trigger Only Once' is on and it was already used, ignored.");
//            return;
//        }

//        StartSequence(other.name);
//    }

//    /// <summary>True only for the player controlled on THIS machine.</summary>
//    private static bool IsLocalPlayer(Collider c, out string reason)
//    {
//        // 1) the local XR rig
//        var xr = LocalXROriginProvider.Instance;
//        if (xr != null && xr.Root != null && c.transform.IsChildOf(xr.Root))
//        {
//            reason = "local XR rig";
//            return true;
//        }

//        // 2) the local networked avatar (the one this client has authority over)
//        var avatar = c.GetComponentInParent<NetworkAvatarSetup>();
//        if (avatar != null && avatar.Object != null && avatar.Object.IsValid)
//        {
//            if (avatar.Object.HasStateAuthority)
//            {
//                reason = "local networked avatar";
//                return true;
//            }
//            reason = "another player's avatar";
//            return false;
//        }

//        reason = "not a player";
//        return false;
//    }

//    // ------------------------------------------------------------------ sequence

//    private void StartSequence(string touchedBy)
//    {
//        if (_items.Count == 0)
//        {
//            Debug.LogWarning($"[ObjectSequence:{name}] Touched by '{touchedBy}' but there are no objects to start.", this);
//            return;
//        }

//        _running = true;
//        _usedOnce = true;
//        _finishedCount = 0;

//        Debug.Log($"[ObjectSequence:{name}] STARTED by local player (touched by '{touchedBy}'). {_items.Count} object(s).", this);

//        for (int i = 0; i < _items.Count; i++)
//            _items[i].routine = StartCoroutine(RunItem(_items[i], i));
//    }

//    private IEnumerator RunItem(Item item, int index)
//    {
//        float startDelay = index * Mathf.Max(0f, delayBetweenObjects);
//        if (startDelay > 0f)
//        {
//            Log($"'{item.go.name}' (#{index + 1}) will appear in {startDelay:F1}s.");
//            yield return new WaitForSeconds(startDelay);
//        }

//        int cycle = 0;
//        while (true)
//        {
//            cycle++;
//            ResetObject(item);   // make sure it starts from its original place with no speed
//            item.go.SetActive(true);
//            Log($"'{item.go.name}' ENABLED (cycle {cycle}). It will reset in {activeDuration:F1}s.");

//            yield return new WaitForSeconds(Mathf.Max(0.01f, activeDuration));

//            ResetObject(item);
//            item.go.SetActive(false);
//            Log($"'{item.go.name}' DISABLED and returned to its original place (cycle {cycle} done).");

//            if (!loop) break;

//            if (restDelay > 0f) yield return new WaitForSeconds(restDelay);
//        }

//        item.routine = null;
//        _finishedCount++;
//        if (_finishedCount >= _items.Count)
//        {
//            _running = false;
//            Debug.Log($"[ObjectSequence:{name}] FINISHED. All objects were reset.", this);
//        }
//    }

//    /// <summary>Puts the object back at its original place and stops all its movement.</summary>
//    private void ResetObject(Item item)
//    {
//        if (item.go == null) return;

//        if (item.rb != null && item.go.activeInHierarchy && !item.rb.isKinematic)
//        {
//            item.rb.linearVelocity = Vector3.zero;
//            item.rb.angularVelocity = Vector3.zero;
//        }

//        item.go.transform.SetLocalPositionAndRotation(item.localPos, item.localRot);
//        Physics.SyncTransforms();
//    }

//    /// <summary>Stops the sequence and sends every object back to its original place, switched off.</summary>
//    [ContextMenu("Debug: Stop and reset all objects")]
//    public void StopSequence()
//    {
//        foreach (var item in _items)
//        {
//            if (item.routine != null) StopCoroutine(item.routine);
//            item.routine = null;
//            ResetObject(item);
//            if (item.go != null) item.go.SetActive(false);
//        }

//        _running = false;
//        _finishedCount = 0;
//        Debug.Log($"[ObjectSequence:{name}] STOPPED. All objects reset and switched off.", this);
//    }

//    [ContextMenu("Debug: Start sequence now")]
//    private void DebugStart()
//    {
//        if (_running) { Debug.Log($"[ObjectSequence:{name}] Already running."); return; }
//        StartSequence("debug menu");
//    }

//    private void Log(string msg)
//    {
//        if (verboseLogs) Debug.Log($"[ObjectSequence:{name}] {msg}", this);
//    }
//}

using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// OBJECT SEQUENCE TRIGGER. Put it on an object with a TRIGGER collider.
///
/// When the local player touches the trigger, the objects in the list are switched on one by one
/// (Delay Between Objects apart). Every object stays active for Active Duration seconds, then it is
/// switched off, put back at its ORIGINAL position and rotation, and (if Loop is on) switched on again.
///
/// Example with the defaults: object 1 appears at 0 s, object 2 at 2 s,
/// object 1 resets at 5 s, object 2 resets at 7 s, and so on.
///
/// The objects normally have a Rigidbody (Use Gravity ON, Is Kinematic OFF) and fall when they are switched on.
/// Objects without a Rigidbody also work, they just appear and disappear.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ObjectSequenceTrigger : MonoBehaviour
{
    public enum WhoCanStart { LocalPlayerOnly = 0, AnyPlayer = 1 }

    [Header("Objects (in the order they appear)")]
    [Tooltip("Drag the objects here. Element 0 appears first.")]
    [SerializeField] private GameObject[] objects;
    [Tooltip("Switch every object off when the scene starts, so they only appear when triggered.")]
    [SerializeField] private bool hideAtStart = true;

    [Header("Timing (seconds)")]
    [Tooltip("Gap between one object appearing and the next one appearing.")]
    [SerializeField] private float delayBetweenObjects = 2f;
    [Tooltip("How long each object stays active before it is switched off and reset.")]
    [SerializeField] private float activeDuration = 5f;
    [Tooltip("Pause between 'switched off and reset' and 'switched on again'. 0 = immediately.")]
    [SerializeField] private float restDelay = 0f;

    [Header("Behaviour")]
    [Tooltip("ON: every object keeps repeating (appear, wait, reset, appear again ...). OFF: each object runs once.")]
    [SerializeField] private bool loop = true;
    [Tooltip("ON: the trigger works only one time per play session, even after the sequence has finished.")]
    [SerializeField] private bool triggerOnlyOnce = false;

    [Header("Who can start it")]
    [Tooltip("LocalPlayerOnly: only the player on THIS machine can start it.\n" +
             "AnyPlayer: any player who steps into the trigger starts it, and it starts on EVERY machine " +
             "(each machine sees the other players' avatars walk in, so no networking is needed).")]
    [SerializeField] private WhoCanStart whoCanStart = WhoCanStart.AnyPlayer;
    [Tooltip("How often (seconds) the script checks if a player is inside the trigger. This check works even when Unity's own " +
             "trigger event does not fire (remote avatars have no colliders).")]
    [SerializeField] private float checkInterval = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    // ------------------------------------------------------------------ internal

    private class Item
    {
        public GameObject go;
        public Rigidbody rb;
        public Vector3 localPos;
        public Quaternion localRot;
        public Coroutine routine;
    }

    private readonly List<Item> _items = new List<Item>();
    private bool _running;
    private bool _usedOnce;
    private int _finishedCount;

    // position check (works for remote avatars, which have no colliders)
    private Collider _col;
    private CharacterController _rigCC;
    private bool _insideLocal;
    private float _nextCheck;
    private HashSet<NetworkAvatarSetup> _insideAvatars = new HashSet<NetworkAvatarSetup>();
    private HashSet<NetworkAvatarSetup> _nowInside = new HashSet<NetworkAvatarSetup>();

    private class AvatarEntry
    {
        public NetworkAvatarSetup avatar;
        public Renderer[] renderers;
    }
    private readonly List<AvatarEntry> _avatarCache = new List<AvatarEntry>();
    private float _nextAvatarRefresh;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        var col = GetComponent<Collider>();
        _col = col;
        if (!col.isTrigger)
            Debug.LogWarning($"[ObjectSequence:{name}] The collider is NOT a trigger. Tick 'Is Trigger' on it, otherwise the player is blocked and nothing is detected.", this);

        if (objects == null || objects.Length == 0)
            Debug.LogWarning($"[ObjectSequence:{name}] No objects assigned.", this);

        if (objects != null)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                var go = objects[i];
                if (go == null)
                {
                    Debug.LogWarning($"[ObjectSequence:{name}] Element {i} of the list is empty, skipped.", this);
                    continue;
                }

                var item = new Item
                {
                    go = go,
                    rb = go.GetComponent<Rigidbody>(),
                    localPos = go.transform.localPosition,   // original place, saved BEFORE anything moves
                    localRot = go.transform.localRotation
                };
                _items.Add(item);

                if (item.rb == null)
                    Log($"'{go.name}' has no Rigidbody, it will only appear and disappear.");
                else if (item.rb.isKinematic || !item.rb.useGravity)
                    Debug.LogWarning($"[ObjectSequence:{name}] '{go.name}' has Is Kinematic ON or Use Gravity OFF, so it will NOT fall. Untick Is Kinematic and tick Use Gravity.", go);

                if (hideAtStart) go.SetActive(false);
            }
        }

        Log($"Ready. {_items.Count} object(s), delay between objects={delayBetweenObjects}s, active duration={activeDuration}s, rest={restDelay}s, loop={loop}.");
    }

    private void OnDisable()
    {
        if (_running) StopSequence();
    }

    // ------------------------------------------------------------------ touching

    private void OnTriggerEnter(Collider other)
    {
        if (!IsAllowedToucher(other, out string why))
        {
            Log($"Touched by '{other.name}' but ignored ({why}).");
            return;
        }

        if (_running)
        {
            Log($"Touched by '{other.name}' but the sequence is already running, ignored.");
            return;
        }

        if (triggerOnlyOnce && _usedOnce)
        {
            Log($"Touched by '{other.name}' but 'Trigger Only Once' is on and it was already used, ignored.");
            return;
        }

        StartSequence(other.name);
    }

    /// <summary>True for the player on THIS machine, and (when Who Can Start = AnyPlayer) for other players' avatars too.</summary>
    private bool IsAllowedToucher(Collider c, out string reason)
    {
        // 1) the local XR rig
        var xr = LocalXROriginProvider.Instance;
        if (xr != null && xr.Root != null && c.transform.IsChildOf(xr.Root))
        {
            reason = "local XR rig";
            return true;
        }

        // 2) a networked avatar
        var avatar = c.GetComponentInParent<NetworkAvatarSetup>();
        if (avatar != null && avatar.Object != null && avatar.Object.IsValid)
        {
            if (avatar.Object.HasStateAuthority)
            {
                reason = "local networked avatar";
                return true;
            }

            if (whoCanStart == WhoCanStart.AnyPlayer)
            {
                reason = "another player's avatar";
                return true;
            }

            reason = "another player's avatar (Who Can Start = LocalPlayerOnly)";
            return false;
        }

        reason = "not a player";
        return false;
    }

    // ------------------------------------------------------------------ position check

    private void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + Mathf.Max(0.02f, checkInterval);
        PollPlayers();
    }

    /// <summary>
    /// Starts the sequence when a player ENTERS the trigger (outside -> inside). Players who are already inside when the
    /// sequence ends do not restart it. Remote avatars have no colliders, so their body bounds are used.
    /// </summary>
    private void PollPlayers()
    {
        if (_col == null || !_col.enabled || !gameObject.activeInHierarchy) return;

        bool canStart = !_running && !(triggerOnlyOnce && _usedOnce);

        // the player on this machine
        bool localInside = IsLocalRigInside();
        if (localInside && !_insideLocal && canStart)
        {
            StartSequence("local player (position check)");
            canStart = false;
        }
        _insideLocal = localInside;

        if (whoCanStart != WhoCanStart.AnyPlayer) return;

        // every other player
        RefreshAvatarCache();
        _nowInside.Clear();

        for (int k = 0; k < _avatarCache.Count; k++)
        {
            var e = _avatarCache[k];
            if (e.avatar == null || e.avatar.Object == null || !e.avatar.Object.IsValid) continue;
            if (e.avatar.Object.HasStateAuthority) continue;   // the local player is handled above

            if (!TryGetVisibleBounds(e.renderers, out Bounds b)) continue;
            if (!Overlaps(b)) continue;

            _nowInside.Add(e.avatar);

            if (!_insideAvatars.Contains(e.avatar) && canStart)
            {
                StartSequence("player " + e.avatar.Object.StateAuthority + " (position check)");
                canStart = false;
            }
        }

        var swap = _insideAvatars;
        _insideAvatars = _nowInside;
        _nowInside = swap;
    }

    private bool IsLocalRigInside()
    {
        var xr = LocalXROriginProvider.Instance;
        if (xr == null || xr.Root == null) return false;

        if (_rigCC == null || !_rigCC.transform.IsChildOf(xr.Root))
            _rigCC = xr.Root.GetComponentInChildren<CharacterController>(true);

        if (_rigCC != null) return Overlaps(_rigCC.bounds);

        if (xr.HeadVRTarget != null)
            return Overlaps(new Bounds(xr.HeadVRTarget.position, Vector3.one * 0.2f));

        return false;
    }

    private bool Overlaps(Bounds b)
    {
        if (!_col.bounds.Intersects(b)) return false;
        if (_col is MeshCollider mc && !mc.convex) return true;

        // closest point of the trigger to the player's centre: if it lies inside the player's bounds they overlap
        Vector3 q = _col.ClosestPoint(b.center);
        return b.SqrDistance(q) < 0.0001f;
    }

    private void RefreshAvatarCache()
    {
        if (Time.time < _nextAvatarRefresh) return;
        _nextAvatarRefresh = Time.time + 1f;

        _avatarCache.Clear();
        foreach (var a in FindObjectsOfType<NetworkAvatarSetup>())
        {
            if (a.Object == null || !a.Object.IsValid) continue;

            var list = new List<Renderer>();
            foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);

            _avatarCache.Add(new AvatarEntry { avatar = a, renderers = list.ToArray() });
        }
    }

    // only renderers that are really visible count (hidden ones can have outdated bounds)
    private static bool TryGetVisibleBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    // ------------------------------------------------------------------ sequence

    private void StartSequence(string touchedBy)
    {
        if (_items.Count == 0)
        {
            Debug.LogWarning($"[ObjectSequence:{name}] Touched by '{touchedBy}' but there are no objects to start.", this);
            return;
        }

        _running = true;
        _usedOnce = true;
        _finishedCount = 0;

        Debug.Log($"[ObjectSequence:{name}] STARTED (touched by '{touchedBy}'). {_items.Count} object(s).", this);

        for (int i = 0; i < _items.Count; i++)
            _items[i].routine = StartCoroutine(RunItem(_items[i], i));
    }

    private IEnumerator RunItem(Item item, int index)
    {
        float startDelay = index * Mathf.Max(0f, delayBetweenObjects);
        if (startDelay > 0f)
        {
            Log($"'{item.go.name}' (#{index + 1}) will appear in {startDelay:F1}s.");
            yield return new WaitForSeconds(startDelay);
        }

        int cycle = 0;
        while (true)
        {
            cycle++;
            ResetObject(item);   // make sure it starts from its original place with no speed
            item.go.SetActive(true);
            Log($"'{item.go.name}' ENABLED (cycle {cycle}). It will reset in {activeDuration:F1}s.");

            yield return new WaitForSeconds(Mathf.Max(0.01f, activeDuration));

            ResetObject(item);
            item.go.SetActive(false);
            Log($"'{item.go.name}' DISABLED and returned to its original place (cycle {cycle} done).");

            if (!loop) break;

            if (restDelay > 0f) yield return new WaitForSeconds(restDelay);
        }

        item.routine = null;
        _finishedCount++;
        if (_finishedCount >= _items.Count)
        {
            _running = false;
            Debug.Log($"[ObjectSequence:{name}] FINISHED. All objects were reset.", this);
        }
    }

    /// <summary>Puts the object back at its original place and stops all its movement.</summary>
    private void ResetObject(Item item)
    {
        if (item.go == null) return;

        if (item.rb != null && item.go.activeInHierarchy && !item.rb.isKinematic)
        {
            item.rb.linearVelocity = Vector3.zero;
            item.rb.angularVelocity = Vector3.zero;
        }

        item.go.transform.SetLocalPositionAndRotation(item.localPos, item.localRot);
        Physics.SyncTransforms();
    }

    /// <summary>Stops the sequence and sends every object back to its original place, switched off.</summary>
    [ContextMenu("Debug: Stop and reset all objects")]
    public void StopSequence()
    {
        foreach (var item in _items)
        {
            if (item.routine != null) StopCoroutine(item.routine);
            item.routine = null;
            ResetObject(item);
            if (item.go != null) item.go.SetActive(false);
        }

        _running = false;
        _finishedCount = 0;
        Debug.Log($"[ObjectSequence:{name}] STOPPED. All objects reset and switched off.", this);
    }

    [ContextMenu("Debug: Start sequence now")]
    private void DebugStart()
    {
        if (_running) { Debug.Log($"[ObjectSequence:{name}] Already running."); return; }
        StartSequence("debug menu");
    }

    private void Log(string msg)
    {
        if (verboseLogs) Debug.Log($"[ObjectSequence:{name}] {msg}", this);
    }
}
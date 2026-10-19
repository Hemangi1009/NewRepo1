//using System.Collections;
//using System.Collections.Generic;
//using Fusion;
//using UnityEngine;

///// <summary>
///// Spring jumper. Put this on the spring object (the same object that has the NetworkObject).
///// When the local player lands on / climbs onto the pad, the pad is pressed down, then shoots back up
///// (launching the player with a bigger jump) and wobbles a little like a real spring.
///// Every other player sees the pad move too (RPC). Only the player who touched it gets launched.
///// </summary>
//public class SpringPad : NetworkBehaviour
//{
//    [Header("Parts")]
//    [Tooltip("The part that moves down and up (for example Props_4a). Empty = this object.")]
//    [SerializeField] private Transform springVisual;
//    [Tooltip("Optional: your own trigger collider. Empty = one is created on top of the pad automatically.")]
//    [SerializeField] private Collider customTrigger;
//    [Tooltip("If the moving part has no solid collider, a thin one is added on top so the player can stand on it.")]
//    [SerializeField] private bool addSolidColliderIfMissing = true;

//    [Header("Launch")]
//    [Tooltip("Jump height on the spring = normal jump height x this. 2 = double, 3 = triple...")]
//    [SerializeField] private float jumpHeightMultiplier = 2.5f;
//    [Tooltip("Also bounce loose physics objects (Rigidbodies) that land on the pad.")]
//    [SerializeField] private bool launchRigidbodies = true;
//    [SerializeField] private float rigidbodyLaunchSpeed = 12f;

//    [Header("Spring motion")]
//    [Tooltip("How far the pad is pressed down (meters).")]
//    [SerializeField] private float compressDepth = 0.4f;
//    [Tooltip("Seconds to press down before it shoots up.")]
//    [SerializeField] private float compressTime = 0.12f;
//    [Tooltip("Wobble speed after the launch (times per second).")]
//    [SerializeField] private float bounceFrequency = 4f;
//    [Tooltip("Small = wobbles for a long time, large = stops quickly.")]
//    [Range(0.02f, 0.9f)]
//    [SerializeField] private float bounceDamping = 0.18f;
//    [Tooltip("Minimum time between two activations.")]
//    [SerializeField] private float cooldown = 0.8f;

//    private Vector3 _restLocalPos;
//    private float _offset;
//    private float _vel;
//    private Coroutine _routine;
//    private float _lastActivate = -999f;

//    private VRJumpMotor _pendingPlayer;
//    private readonly List<Rigidbody> _pendingBodies = new List<Rigidbody>();

//    private void Awake()
//    {
//        if (springVisual == null) springVisual = transform;
//        _restLocalPos = springVisual.localPosition;
//        BuildColliders();
//    }

//    // ------------------------------------------------------------ setup

//    private void BuildColliders()
//    {
//        var renderers = springVisual.GetComponentsInChildren<Renderer>();
//        if (renderers.Length == 0)
//        {
//            Debug.LogWarning("[SpringPad] No renderer found under the spring visual, cannot build the trigger.", this);
//            return;
//        }

//        Bounds b = renderers[0].bounds;
//        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
//        Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);

//        if (addSolidColliderIfMissing && !HasSolidCollider())
//            CreateBox("SpringPadCollider", top + Vector3.down * 0.15f,
//                      new Vector3(b.size.x * 0.95f, 0.3f, b.size.z * 0.95f), false);

//        if (customTrigger == null)
//            customTrigger = CreateBox("SpringTrigger", top,
//                                      new Vector3(b.size.x * 0.9f, 0.7f, b.size.z * 0.9f), true);

//        customTrigger.isTrigger = true;
//        var relay = customTrigger.GetComponent<SpringTriggerRelay>();
//        if (relay == null) relay = customTrigger.gameObject.AddComponent<SpringTriggerRelay>();
//        relay.Init(this);
//    }

//    private bool HasSolidCollider()
//    {
//        foreach (var c in springVisual.GetComponentsInChildren<Collider>())
//            if (!c.isTrigger) return true;
//        return false;
//    }

//    private BoxCollider CreateBox(string objName, Vector3 worldCenter, Vector3 worldSize, bool trigger)
//    {
//        var go = new GameObject(objName);
//        go.transform.SetParent(springVisual, false);
//        go.transform.position = worldCenter;
//        go.transform.rotation = Quaternion.identity;

//        Vector3 ls = go.transform.lossyScale;
//        var box = go.AddComponent<BoxCollider>();
//        box.isTrigger = trigger;
//        box.size = new Vector3(
//            worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
//            worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
//            worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));
//        return box;
//    }

//    // ------------------------------------------------------------ triggering

//    /// <summary>Called by the trigger when something enters the pad's zone (on this client only).</summary>
//    public void OnBodyEntered(Collider other)
//    {
//        if (Time.time - _lastActivate < cooldown) return;

//        VRJumpMotor motor = null;
//        Rigidbody body = null;

//        if (other is CharacterController)
//        {
//            motor = other.GetComponentInParent<VRJumpMotor>();
//            if (motor == null) return;
//        }
//        else
//        {
//            if (!launchRigidbodies) return;
//            body = other.attachedRigidbody;
//            if (body == null || body.isKinematic) return;
//            if (body.GetComponentInParent<VRJumpMotor>() != null) return; // parts of the player rig

//            var netObj = body.GetComponentInParent<NetworkObject>();
//            if (netObj != null && !netObj.HasStateAuthority) return;       // the owner of that object launches it
//        }

//        if (motor != null) _pendingPlayer = motor;
//        else _pendingBodies.Add(body);

//        Activate();                                                         // play right away on this client

//        if (Object != null && Object.IsValid && Runner != null)
//            RPC_SpringTriggered(Runner.LocalPlayer);                        // tell everyone else to animate it
//    }

//    /// <summary>Runs on every client: animate the pad. The sender already played it locally.</summary>
//    [Rpc(RpcSources.All, RpcTargets.All)]
//    public void RPC_SpringTriggered(PlayerRef who)
//    {
//        if (Runner != null && who == Runner.LocalPlayer) return;
//        if (Time.time - _lastActivate < 0.3f) return;
//        Activate();
//    }

//    private void Activate()
//    {
//        _lastActivate = Time.time;
//        if (_routine != null) StopCoroutine(_routine);
//        _routine = StartCoroutine(SpringRoutine());
//    }

//    // ------------------------------------------------------------ animation

//    private IEnumerator SpringRoutine()
//    {
//        // 1) press down
//        float from = _offset;
//        float t = 0f;
//        while (t < compressTime)
//        {
//            t += Time.deltaTime;
//            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / compressTime));
//            SetOffset(Mathf.Lerp(from, -compressDepth, k));
//            yield return null;
//        }
//        SetOffset(-compressDepth);

//        // 2) release: launch whoever touched it, and let the pad shoot up and wobble like a real spring
//        Launch();
//        _vel = 0f;

//        float omega = 2f * Mathf.PI * Mathf.Max(0.1f, bounceFrequency);
//        float elapsed = 0f;
//        while (elapsed < 4f)
//        {
//            float dt = Time.deltaTime;
//            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));   // small steps keep the spring stable
//            float h = dt / steps;
//            for (int i = 0; i < steps; i++)
//            {
//                float acc = -omega * omega * _offset - 2f * bounceDamping * omega * _vel;
//                _vel += acc * h;
//                _offset += _vel * h;
//            }
//            SetOffset(_offset);

//            elapsed += dt;
//            if (Mathf.Abs(_offset) < 0.002f && Mathf.Abs(_vel) < 0.02f) break;
//            yield return null;
//        }

//        _vel = 0f;
//        SetOffset(0f);
//        _routine = null;
//    }

//    private void Launch()
//    {
//        if (_pendingPlayer != null)
//        {
//            _pendingPlayer.LaunchWithMultiplier(jumpHeightMultiplier);
//            _pendingPlayer = null;
//        }

//        foreach (var rb in _pendingBodies)
//            if (rb != null) rb.AddForce(Vector3.up * rigidbodyLaunchSpeed, ForceMode.VelocityChange);
//        _pendingBodies.Clear();
//    }

//    private void SetOffset(float value)
//    {
//        _offset = value;
//        springVisual.localPosition = _restLocalPos + Vector3.up * value;
//    }
//}

///// <summary>Added automatically to the pad's trigger collider.</summary>
//public class SpringTriggerRelay : MonoBehaviour
//{
//    private SpringPad _owner;

//    public void Init(SpringPad owner) { _owner = owner; }

//    private void OnTriggerEnter(Collider other)
//    {
//        if (_owner != null) _owner.OnBodyEntered(other);
//    }
//}

using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Spring jumper. Put this on the spring object (the same object that has the NetworkObject).
/// When the local player lands on / climbs onto the pad, the pad is pressed down, then shoots back up
/// (launching the player with a bigger jump) and wobbles a little like a real spring.
/// Every other player sees the pad move too (RPC). Only the player who touched it gets launched.
///
/// Sound: assign an AudioSource (a component, not an audio file) to 'Spring Audio Source'.
/// Its own clip, volume, pitch and 3D settings are used.
/// </summary>
public class SpringPad : NetworkBehaviour
{
    public enum SoundMoment { WhenPressedDown, WhenLaunched }

    [Header("Parts")]
    [Tooltip("The part that moves down and up (for example Props_4a). Empty = this object.")]
    [SerializeField] private Transform springVisual;
    [Tooltip("Optional: your own trigger collider. Empty = one is created on top of the pad automatically.")]
    [SerializeField] private Collider customTrigger;
    [Tooltip("If the moving part has no solid collider, a thin one is added on top so the player can stand on it.")]
    [SerializeField] private bool addSolidColliderIfMissing = true;

    [Header("Launch")]
    [Tooltip("Jump height on the spring = normal jump height x this. 2 = double, 3 = triple...")]
    [SerializeField] private float jumpHeightMultiplier = 2.5f;
    [Tooltip("Also bounce loose physics objects (Rigidbodies) that land on the pad.")]
    [SerializeField] private bool launchRigidbodies = true;
    [SerializeField] private float rigidbodyLaunchSpeed = 12f;

    [Header("Spring motion")]
    [Tooltip("How far the pad is pressed down (meters).")]
    [SerializeField] private float compressDepth = 0.4f;
    [Tooltip("Seconds to press down before it shoots up.")]
    [SerializeField] private float compressTime = 0.12f;
    [Tooltip("Wobble speed after the launch (times per second).")]
    [SerializeField] private float bounceFrequency = 4f;
    [Tooltip("Small = wobbles for a long time, large = stops quickly.")]
    [Range(0.02f, 0.9f)]
    [SerializeField] private float bounceDamping = 0.18f;
    [Tooltip("Minimum time between two activations.")]
    [SerializeField] private float cooldown = 0.8f;

    [Header("Sound")]
    [Tooltip("Drag an AudioSource here (a component from the Hierarchy, not an audio file). " +
             "Its own clip, volume, pitch and 3D settings are used. Leave empty for no sound.")]
    [SerializeField] private AudioSource springAudioSource;
    [Tooltip("WhenLaunched = the 'boing' when the pad shoots up. WhenPressedDown = the moment the pad is pressed.")]
    [SerializeField] private SoundMoment soundMoment = SoundMoment.WhenLaunched;
    [Tooltip("ON: every player near the pad hears it whenever the pad moves. OFF: only the player who is launched by it hears it.")]
    [SerializeField] private bool playForEveryone = true;

    private Vector3 _restLocalPos;
    private float _offset;
    private float _vel;
    private Coroutine _routine;
    private float _lastActivate = -999f;

    private VRJumpMotor _pendingPlayer;
    private readonly List<Rigidbody> _pendingBodies = new List<Rigidbody>();

    private void Awake()
    {
        if (springVisual == null) springVisual = transform;
        _restLocalPos = springVisual.localPosition;
        BuildColliders();
        CheckSoundSetup();
    }

    // ------------------------------------------------------------ setup

    private void BuildColliders()
    {
        var renderers = springVisual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[SpringPad] No renderer found under the spring visual, cannot build the trigger.", this);
            return;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);

        if (addSolidColliderIfMissing && !HasSolidCollider())
            CreateBox("SpringPadCollider", top + Vector3.down * 0.15f,
                      new Vector3(b.size.x * 0.95f, 0.3f, b.size.z * 0.95f), false);

        if (customTrigger == null)
            customTrigger = CreateBox("SpringTrigger", top,
                                      new Vector3(b.size.x * 0.9f, 0.7f, b.size.z * 0.9f), true);

        customTrigger.isTrigger = true;
        var relay = customTrigger.GetComponent<SpringTriggerRelay>();
        if (relay == null) relay = customTrigger.gameObject.AddComponent<SpringTriggerRelay>();
        relay.Init(this);
    }

    private bool HasSolidCollider()
    {
        foreach (var c in springVisual.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) return true;
        return false;
    }

    private BoxCollider CreateBox(string objName, Vector3 worldCenter, Vector3 worldSize, bool trigger)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(springVisual, false);
        go.transform.position = worldCenter;
        go.transform.rotation = Quaternion.identity;

        Vector3 ls = go.transform.lossyScale;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = trigger;
        box.size = new Vector3(
            worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
            worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
            worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));
        return box;
    }

    /// <summary>Logs once at start whether the sound is set up correctly.</summary>
    private void CheckSoundSetup()
    {
        if (springAudioSource == null)
        {
            Debug.Log($"[SpringPad:{name}] No Spring Audio Source assigned, the pad will be silent.", this);
            return;
        }

        if (springAudioSource.clip == null)
            Debug.LogWarning($"[SpringPad:{name}] The assigned AudioSource '{springAudioSource.name}' has NO clip. Drag an audio clip into its 'AudioClip' field, otherwise nothing will play.", this);
        else
            Debug.Log($"[SpringPad:{name}] Sound ready: AudioSource '{springAudioSource.name}', clip '{springAudioSource.clip.name}', plays {soundMoment}, for everyone={playForEveryone}.", this);

        if (springAudioSource.playOnAwake)
            Debug.LogWarning($"[SpringPad:{name}] 'Play On Awake' is ticked on AudioSource '{springAudioSource.name}', so the sound also plays when the scene starts. Untick it on the AudioSource.", this);
    }

    // ------------------------------------------------------------ triggering

    /// <summary>Called by the trigger when something enters the pad's zone (on this client only).</summary>
    public void OnBodyEntered(Collider other)
    {
        if (Time.time - _lastActivate < cooldown) return;

        VRJumpMotor motor = null;
        Rigidbody body = null;

        if (other is CharacterController)
        {
            motor = other.GetComponentInParent<VRJumpMotor>();
            if (motor == null) return;
        }
        else
        {
            if (!launchRigidbodies) return;
            body = other.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (body.GetComponentInParent<VRJumpMotor>() != null) return; // parts of the player rig

            var netObj = body.GetComponentInParent<NetworkObject>();
            if (netObj != null && !netObj.HasStateAuthority) return;       // the owner of that object launches it
        }

        if (motor != null) _pendingPlayer = motor;
        else _pendingBodies.Add(body);

        Activate();                                                         // play right away on this client

        if (Object != null && Object.IsValid && Runner != null)
            RPC_SpringTriggered(Runner.LocalPlayer);                        // tell everyone else to animate it
    }

    /// <summary>Runs on every client: animate the pad. The sender already played it locally.</summary>
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_SpringTriggered(PlayerRef who)
    {
        if (Runner != null && who == Runner.LocalPlayer) return;
        if (Time.time - _lastActivate < 0.3f) return;
        Activate();
    }

    private void Activate()
    {
        _lastActivate = Time.time;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(SpringRoutine());
    }

    // ------------------------------------------------------------ animation

    private IEnumerator SpringRoutine()
    {
        // true on the machine of the player (or the owner of the object) who is actually being launched
        bool launchesSomeoneHere = _pendingPlayer != null || _pendingBodies.Count > 0;

        if (soundMoment == SoundMoment.WhenPressedDown)
            PlaySpringSound(launchesSomeoneHere);

        // 1) press down
        float from = _offset;
        float t = 0f;
        while (t < compressTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / compressTime));
            SetOffset(Mathf.Lerp(from, -compressDepth, k));
            yield return null;
        }
        SetOffset(-compressDepth);

        // 2) release: launch whoever touched it, and let the pad shoot up and wobble like a real spring
        if (soundMoment == SoundMoment.WhenLaunched)
            PlaySpringSound(launchesSomeoneHere);

        Launch();
        _vel = 0f;

        float omega = 2f * Mathf.PI * Mathf.Max(0.1f, bounceFrequency);
        float elapsed = 0f;
        while (elapsed < 4f)
        {
            float dt = Time.deltaTime;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));   // small steps keep the spring stable
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = -omega * omega * _offset - 2f * bounceDamping * omega * _vel;
                _vel += acc * h;
                _offset += _vel * h;
            }
            SetOffset(_offset);

            elapsed += dt;
            if (Mathf.Abs(_offset) < 0.002f && Mathf.Abs(_vel) < 0.02f) break;
            yield return null;
        }

        _vel = 0f;
        SetOffset(0f);
        _routine = null;
    }

    private void Launch()
    {
        if (_pendingPlayer != null)
        {
            _pendingPlayer.LaunchWithMultiplier(jumpHeightMultiplier);
            _pendingPlayer = null;
        }

        foreach (var rb in _pendingBodies)
            if (rb != null) rb.AddForce(Vector3.up * rigidbodyLaunchSpeed, ForceMode.VelocityChange);
        _pendingBodies.Clear();
    }

    private void SetOffset(float value)
    {
        _offset = value;
        springVisual.localPosition = _restLocalPos + Vector3.up * value;
    }

    // ------------------------------------------------------------ sound

    /// <summary>Plays the assigned AudioSource (its own clip and settings).</summary>
    private void PlaySpringSound(bool launchesSomeoneHere)
    {
        if (springAudioSource == null) return;

        if (!playForEveryone && !launchesSomeoneHere)
        {
            Debug.Log($"[SpringPad:{name}] Pad moved, but 'Play For Everyone' is off and nobody is launched on this machine, so no sound here.", this);
            return;
        }

        if (springAudioSource.clip == null)
        {
            Debug.LogWarning($"[SpringPad:{name}] Cannot play: AudioSource '{springAudioSource.name}' has no clip.", this);
            return;
        }

        if (!springAudioSource.isActiveAndEnabled)
        {
            Debug.LogWarning($"[SpringPad:{name}] Cannot play: AudioSource '{springAudioSource.name}' (or its GameObject) is disabled.", this);
            return;
        }

        springAudioSource.Play();
        Debug.Log($"[SpringPad:{name}] Sound played: AudioSource '{springAudioSource.name}', clip '{springAudioSource.clip.name}' ({soundMoment}, launched here={launchesSomeoneHere}).", this);
    }
}

/// <summary>Added automatically to the pad's trigger collider.</summary>
public class SpringTriggerRelay : MonoBehaviour
{
    private SpringPad _owner;

    public void Init(SpringPad owner) { _owner = owner; }

    private void OnTriggerEnter(Collider other)
    {
        if (_owner != null) _owner.OnBodyEntered(other);
    }
}
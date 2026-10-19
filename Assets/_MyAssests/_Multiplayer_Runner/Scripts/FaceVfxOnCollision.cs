//using System.Collections.Generic;
//using UnityEngine;

///// <summary>
///// Put this on the XR Origin (the same object that has the CharacterController and VRJumpMotor).
///// Whenever the player's body touches one of the listed colliders, the VFX plays on the player's face.
///// Touching again later (after letting go) plays it again.
///// Local only: each player sees the effect on their own view.
///// </summary>
//[RequireComponent(typeof(CharacterController))]
//public class FaceVfxOnCollision : MonoBehaviour
//{
//    [Header("Colliders that trigger the effect")]
//    [Tooltip("Drag the objects here (they need a Collider). Solid colliders and trigger colliders both work.")]
//    [SerializeField] private List<Collider> targetColliders = new List<Collider>();
//    [Tooltip("Also react to colliders on the children of the listed objects.")]
//    [SerializeField] private bool includeChildColliders = true;

//    [Header("VFX")]
//    [Tooltip("Particle System or VFX Graph prefab. It is created on the player's face each time.")]
//    [SerializeField] private GameObject vfxPrefab;
//    [Tooltip("Position relative to the player's head (Z = in front of the face).")]
//    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0f, 0.3f);
//    [Tooltip("Seconds until the effect object is removed. Set it a bit longer than the effect itself.")]
//    [SerializeField] private float vfxLifetime = 3f;
//    [Tooltip("If the effect is still playing when a new hit happens, restart it instead of stacking a second one.")]
//    [SerializeField] private bool restartIfStillPlaying = true;

//    [Header("Hit detection")]
//    [Tooltip("Player must stop touching the object for this long before touching it again counts as a new hit.")]
//    [SerializeField] private float reentryGap = 0.3f;
//    [Tooltip("Minimum time between two effects.")]
//    [SerializeField] private float minInterval = 0.5f;

//    private CharacterController _cc;
//    private Camera _cam;
//    private GameObject _current;
//    private float _lastPlayTime = -999f;

//    private readonly HashSet<Collider> _targets = new HashSet<Collider>();
//    private readonly Dictionary<Collider, float> _lastTouch = new Dictionary<Collider, float>();

//    private void Awake()
//    {
//        _cc = GetComponent<CharacterController>();

//        foreach (var c in targetColliders)
//        {
//            if (c == null) continue;
//            _targets.Add(c);
//            if (includeChildColliders)
//                foreach (var child in c.GetComponentsInChildren<Collider>(true))
//                    _targets.Add(child);
//        }

//        // Trigger colliders do not report to the CharacterController, so they get a small relay.
//        foreach (var c in _targets)
//        {
//            if (c != null && c.isTrigger && c.GetComponent<FaceVfxTriggerRelay>() == null)
//                c.gameObject.AddComponent<FaceVfxTriggerRelay>().Init(this, c);
//        }
//    }

//    // Solid colliders: called by Unity whenever the CharacterController body touches something.
//    private void OnControllerColliderHit(ControllerColliderHit hit)
//    {
//        if (hit.collider != null && _targets.Contains(hit.collider))
//            RegisterTouch(hit.collider);
//    }

//    /// <summary>Called by the trigger relay.</summary>
//    public void NotifyTrigger(Collider target, Collider other)
//    {
//        if (other == _cc || other.GetComponentInParent<CharacterController>() == _cc)
//            RegisterTouch(target);
//    }

//    private void RegisterTouch(Collider target)
//    {
//        float now = Time.time;
//        _lastTouch.TryGetValue(target, out float last);
//        _lastTouch[target] = now;

//        bool isNewHit = (now - last) > reentryGap;       // was not touching a moment ago
//        bool cooledDown = (now - _lastPlayTime) >= minInterval;
//        if (!isNewHit || !cooledDown) return;

//        PlayVfx();
//    }

//    private void PlayVfx()
//    {
//        if (vfxPrefab == null) return;

//        if (_cam == null)
//        {
//            _cam = GetComponentInChildren<Camera>(true);
//            if (_cam == null) _cam = Camera.main;
//        }
//        if (_cam == null) return;

//        if (restartIfStillPlaying && _current != null) Destroy(_current);

//        _current = Instantiate(vfxPrefab, _cam.transform);
//        _current.transform.localPosition = localOffset;
//        _current.transform.localRotation = Quaternion.identity;
//        _current.SetActive(true);

//        foreach (var ps in _current.GetComponentsInChildren<ParticleSystem>(true))
//            ps.Play(true);

//        if (vfxLifetime > 0f) Destroy(_current, vfxLifetime);
//        _lastPlayTime = Time.time;
//    }
//}

///// <summary>Added automatically to trigger colliders in the list.</summary>
//public class FaceVfxTriggerRelay : MonoBehaviour
//{
//    private FaceVfxOnCollision _owner;
//    private Collider _self;

//    public void Init(FaceVfxOnCollision owner, Collider self)
//    {
//        _owner = owner;
//        _self = self;
//    }

//    private void OnTriggerEnter(Collider other)
//    {
//        if (_owner != null) _owner.NotifyTrigger(_self, other);
//    }
//}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the XR Origin (the same object that has the CharacterController and VRJumpMotor).
/// Whenever the player's body touches one of the listed colliders, the VFX and sound play on the player's face.
/// Touching again later (after letting go) plays it again.
/// Local only: each player sees and hears the effect on their own view.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FaceVfxOnCollision : MonoBehaviour
{
    [Header("Colliders that trigger the effect")]
    [Tooltip("Drag the objects here (they need a Collider). Solid colliders and trigger colliders both work.")]
    [SerializeField] private List<Collider> targetColliders = new List<Collider>();
    [Tooltip("Also react to colliders on the children of the listed objects.")]
    [SerializeField] private bool includeChildColliders = true;

    [Header("VFX")]
    [Tooltip("Particle System or VFX Graph prefab. It is created on the player's face each time.")]
    [SerializeField] private GameObject vfxPrefab;
    [Tooltip("Position relative to the player's head (Z = in front of the face).")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0f, 0.3f);
    [Tooltip("Seconds until the effect object is removed. Set it a bit longer than the effect itself.")]
    [SerializeField] private float vfxLifetime = 3f;
    [Tooltip("If the effect is still playing when a new hit happens, restart it instead of stacking a second one.")]
    [SerializeField] private bool restartIfStillPlaying = true;

    [Header("Audio")]
    [Tooltip("Sound that plays together with the VFX. Leave empty for no sound.")]
    [SerializeField] private AudioClip hitSound;
    [Tooltip("Volume of the sound (0 = silent, 1 = full).")]
    [Range(0f, 1f)]
    [SerializeField] private float soundVolume = 1f;
    [Tooltip("Optional: random pitch variation so repeated hits don't sound identical (0 = none).")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pitchVariation = 0f;
    [Tooltip("If the sound is still playing when a new hit happens, cut it off and restart it.")]
    [SerializeField] private bool restartSoundIfStillPlaying = false;

    [Header("Hit detection")]
    [Tooltip("Player must stop touching the object for this long before touching it again counts as a new hit.")]
    [SerializeField] private float reentryGap = 0.3f;
    [Tooltip("Minimum time between two effects.")]
    [SerializeField] private float minInterval = 0.5f;

    private CharacterController _cc;
    private Camera _cam;
    private GameObject _current;
    private AudioSource _audio;
    private float _lastPlayTime = -999f;

    private readonly HashSet<Collider> _targets = new HashSet<Collider>();
    private readonly Dictionary<Collider, float> _lastTouch = new Dictionary<Collider, float>();

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();

        // 2D audio source: the sound is heard directly by the player, not positioned in the world.
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.spatialBlend = 0f;

        foreach (var c in targetColliders)
        {
            if (c == null) continue;
            _targets.Add(c);
            if (includeChildColliders)
                foreach (var child in c.GetComponentsInChildren<Collider>(true))
                    _targets.Add(child);
        }

        // Trigger colliders do not report to the CharacterController, so they get a small relay.
        foreach (var c in _targets)
        {
            if (c != null && c.isTrigger && c.GetComponent<FaceVfxTriggerRelay>() == null)
                c.gameObject.AddComponent<FaceVfxTriggerRelay>().Init(this, c);
        }
    }

    // Solid colliders: called by Unity whenever the CharacterController body touches something.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider != null && _targets.Contains(hit.collider))
            RegisterTouch(hit.collider);
    }

    /// <summary>Called by the trigger relay.</summary>
    public void NotifyTrigger(Collider target, Collider other)
    {
        if (other == _cc || other.GetComponentInParent<CharacterController>() == _cc)
            RegisterTouch(target);
    }

    private void RegisterTouch(Collider target)
    {
        float now = Time.time;
        _lastTouch.TryGetValue(target, out float last);
        _lastTouch[target] = now;

        bool isNewHit = (now - last) > reentryGap;       // was not touching a moment ago
        bool cooledDown = (now - _lastPlayTime) >= minInterval;
        if (!isNewHit || !cooledDown) return;

        PlayVfx();
        PlaySound();
        _lastPlayTime = Time.time;
    }

    private void PlayVfx()
    {
        if (vfxPrefab == null) return;

        if (_cam == null)
        {
            _cam = GetComponentInChildren<Camera>(true);
            if (_cam == null) _cam = Camera.main;
        }
        if (_cam == null) return;

        if (restartIfStillPlaying && _current != null) Destroy(_current);

        _current = Instantiate(vfxPrefab, _cam.transform);
        _current.transform.localPosition = localOffset;
        _current.transform.localRotation = Quaternion.identity;
        _current.SetActive(true);

        foreach (var ps in _current.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play(true);

        if (vfxLifetime > 0f) Destroy(_current, vfxLifetime);
    }

    private void PlaySound()
    {
        if (hitSound == null || _audio == null) return;

        if (restartSoundIfStillPlaying) _audio.Stop();

        _audio.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        _audio.PlayOneShot(hitSound, soundVolume);
    }
}

/// <summary>Added automatically to trigger colliders in the list.</summary>
public class FaceVfxTriggerRelay : MonoBehaviour
{
    private FaceVfxOnCollision _owner;
    private Collider _self;

    public void Init(FaceVfxOnCollision owner, Collider self)
    {
        _owner = owner;
        _self = self;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_owner != null) _owner.NotifyTrigger(_self, other);
    }
}
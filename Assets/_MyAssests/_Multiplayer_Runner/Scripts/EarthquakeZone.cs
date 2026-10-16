using UnityEngine;

/// <summary>
/// EARTHQUAKE ZONE. Put it on ONE object that has a collider.
///
/// While the LOCAL player's feet are on / inside that collider, the player's view gets a small, smooth
/// earthquake shake. When the player walks off, the shake fades out and the view returns to normal.
///
/// How it works:
///   - "On the collider" = the player's feet (head position projected down to the rig's floor level) are
///     inside the collider, or at most 'Standing Tolerance' meters above its surface.
///     So it works with a thin trigger volume on the floor, a thick trigger volume, or a solid floor collider.
///   - The shake is added to the camera's parent ("Camera Offset" of the XR Origin) and removed again every frame,
///     so it never drifts and it never touches the CharacterController or the XR Origin itself.
///   - Everything is local: every player's own game checks its own player, so nothing needs to be networked.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EarthquakeZone : MonoBehaviour
{
    [Header("Shake strength (meters, applied to the view)")]
    [Tooltip("Sideways / forward-back shake. 0.015 = 1.5 cm. Keep it small in VR, big values cause motion sickness.")]
    [SerializeField] private float horizontalAmplitude = 0.015f;
    [Tooltip("Up / down shake. 0.02 = 2 cm.")]
    [SerializeField] private float verticalAmplitude = 0.02f;
    [Tooltip("How fast it shakes (shakes per second). 10 - 18 feels like a rumble.")]
    [SerializeField] private float frequency = 14f;
    [Tooltip("0 = steady shake, 1 = the strength swells and calms down like real tremors.")]
    [Range(0f, 1f)]
    [SerializeField] private float rumbleVariation = 0.4f;

    [Header("Smoothness")]
    [Tooltip("Seconds for the shake to build up after stepping onto the zone.")]
    [SerializeField] private float fadeInTime = 0.4f;
    [Tooltip("Seconds for the shake to calm down after leaving the zone.")]
    [SerializeField] private float fadeOutTime = 0.8f;

    [Header("Detection")]
    [Tooltip("The player counts as 'on the zone' when the feet are inside the collider or at most this many meters above its surface.")]
    [SerializeField] private float standingTolerance = 0.12f;
    [Tooltip("Optional. The object that gets shaken. Leave empty to use the camera's parent ('Camera Offset' of the XR Origin).")]
    [SerializeField] private Transform shakeTarget;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    // ------------------------------------------------------------------ internal

    private Collider _col;
    private Camera _cam;
    private Transform _target;
    private bool _targetWarned;

    private bool _inside;
    private float _intensity;      // 0 = no shake, 1 = full shake
    private Vector3 _lastOffset;   // the shake currently added to the target (so it can be removed again)
    private float _seed;
    private float _nextStatusLog;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _seed = Random.Range(0f, 1000f);

        if (_col is MeshCollider mc && !mc.convex)
        {
            Debug.LogError($"[Earthquake:{name}] This MeshCollider is not Convex, so it cannot be used. Tick 'Convex' on it, or use a Box / Sphere / Capsule collider. The zone is switched off.", this);
            enabled = false;
            return;
        }

        Log($"Ready. Horizontal={horizontalAmplitude} m, vertical={verticalAmplitude} m, frequency={frequency}, fade in {fadeInTime}s / out {fadeOutTime}s.");
    }

    private void OnDisable()
    {
        RemoveOffset();
        _inside = false;
        _intensity = 0f;
    }

    // ------------------------------------------------------------------ per frame

    private void LateUpdate()
    {
        bool inside = IsPlayerOnZone();

        if (inside != _inside)
        {
            _inside = inside;
            Debug.Log(inside
                ? $"[Earthquake:{name}] Player STEPPED ON the zone. Shake starts."
                : $"[Earthquake:{name}] Player LEFT the zone. Shake fades out.", this);
        }

        float goal = _inside ? 1f : 0f;
        float time = goal > _intensity ? fadeInTime : fadeOutTime;
        float rate = 1f / Mathf.Max(0.01f, time);
        _intensity = Mathf.MoveTowards(_intensity, goal, rate * Time.deltaTime);

        ApplyShake();
    }

    private bool IsPlayerOnZone()
    {
        var xr = LocalXROriginProvider.Instance;
        if (xr == null || xr.Root == null) return false;

        Transform root = xr.Root;
        if (_cam == null) _cam = root.GetComponentInChildren<Camera>(true);

        // feet = where the head is horizontally, at the rig's floor height
        Vector3 feet = root.position;
        if (_cam != null)
        {
            feet.x = _cam.transform.position.x;
            feet.z = _cam.transform.position.z;
        }

        Vector3 closest = _col.ClosestPoint(feet);
        return (closest - feet).sqrMagnitude <= standingTolerance * standingTolerance;
    }

    private void ApplyShake()
    {
        if (_intensity <= 0f && _lastOffset == Vector3.zero) return; // nothing to do

        Transform target = GetTarget();
        if (target == null) return;

        Vector3 offset = Vector3.zero;
        if (_intensity > 0f)
        {
            float t = Time.time * frequency;
            float x = (Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(_seed + 50f, t) - 0.5f) * 2f;
            float z = (Mathf.PerlinNoise(_seed + 100f, t) - 0.5f) * 2f;

            // slow swelling like real tremors
            float swell = Mathf.Lerp(1f - rumbleVariation, 1f, Mathf.PerlinNoise(_seed + 200f, Time.time * 0.6f));

            offset = new Vector3(x * horizontalAmplitude, y * verticalAmplitude, z * horizontalAmplitude) * (swell * _intensity);
        }

        // remove last frame's shake, add this frame's shake
        target.localPosition += offset - _lastOffset;
        _lastOffset = offset;

        if (verboseLogs && _intensity > 0f && Time.time >= _nextStatusLog)
        {
            _nextStatusLog = Time.time + 2f;
            Debug.Log($"[Earthquake:{name}] Shaking '{target.name}': intensity={_intensity:F2}, offset={offset}.", this);
        }
    }

    // ------------------------------------------------------------------ helpers

    private Transform GetTarget()
    {
        if (_target != null) return _target;

        if (shakeTarget != null)
        {
            _target = shakeTarget;
        }
        else
        {
            var xr = LocalXROriginProvider.Instance;
            Transform root = xr != null ? xr.Root : null;

            if (_cam != null && _cam.transform.parent != null && _cam.transform.parent != root)
            {
                _target = _cam.transform.parent;
            }
            else
            {
                if (!_targetWarned)
                {
                    _targetWarned = true;
                    Debug.LogWarning($"[Earthquake:{name}] Could not find a 'Camera Offset' object (the camera's parent is the XR Origin itself or missing). Drag your Camera Offset object into 'Shake Target' on this component.", this);
                }
                return null;
            }
        }

        Log($"Shake target = '{_target.name}'.");
        return _target;
    }

    /// <summary>Takes the shake off the target again so the view is exactly back to normal.</summary>
    private void RemoveOffset()
    {
        if (_target != null && _lastOffset != Vector3.zero)
            _target.localPosition -= _lastOffset;
        _lastOffset = Vector3.zero;
    }

    private void Log(string msg)
    {
        if (verboseLogs) Debug.Log($"[Earthquake:{name}] {msg}", this);
    }

    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = _inside ? new Color(1f, 0.4f, 0f, 0.9f) : new Color(1f, 0.8f, 0f, 0.5f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
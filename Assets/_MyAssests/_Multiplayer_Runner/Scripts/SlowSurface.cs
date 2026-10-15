using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Put this on the XR Origin (the object with the CharacterController).
/// While the player's feet are on one of the listed colliders (mud, sand, water...), the walking speed is reduced.
/// It returns to normal as soon as the player steps off.
///
/// Multiplayer: every player runs this on their OWN rig, so only the player standing on the surface is slowed.
/// Other players see that avatar move slower because the avatar's position is already synced. No RPC is needed.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SlowSurface : MonoBehaviour
{
    [Header("Slow surfaces")]
    [Tooltip("Drag the ground objects here (they need a Collider). Solid and trigger colliders both work.")]
    [SerializeField] private List<Collider> slowColliders = new List<Collider>();
    [Tooltip("Also count colliders on the children of the listed objects.")]
    [SerializeField] private bool includeChildColliders = true;

    [Header("Speed")]
    [Tooltip("Walking speed on the surface = normal speed x this. 0.4 = 40 % of normal.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float speedMultiplier = 0.4f;
    [Tooltip("Seconds to blend between normal and slow speed (so it does not feel like a sudden stop).")]
    [SerializeField] private float transitionTime = 0.15f;

    [Header("Ground check")]
    [SerializeField] private float checkDistance = 0.4f;

    [Header("Optional")]
    [Tooltip("Leave empty: the move providers (Dynamic/Continuous Move Provider) are found automatically.")]
    [SerializeField] private Behaviour[] moveProviders;

    private class SpeedTarget
    {
        public Behaviour component;
        public PropertyInfo property;
        public FieldInfo field;
        public float original;
    }

    private readonly List<SpeedTarget> _targets = new List<SpeedTarget>();
    private readonly HashSet<Collider> _slowSet = new HashSet<Collider>();
    private CharacterController _cc;
    private float _current = 1f;
    private float _applied = 1f;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();

        foreach (var c in slowColliders)
        {
            if (c == null) continue;
            _slowSet.Add(c);
            if (includeChildColliders)
                foreach (var child in c.GetComponentsInChildren<Collider>(true))
                    _slowSet.Add(child);
        }
    }

    private void Start()
    {
        FindMoveProviders();
    }

    private void OnDisable()
    {
        Apply(1f); // never leave the player slowed down
        _current = 1f;
    }

    private void Update()
    {
        if (_targets.Count == 0) return;

        float target = IsOnSlowSurface() ? speedMultiplier : 1f;
        float step = (1f - speedMultiplier) / Mathf.Max(0.01f, transitionTime) * Time.deltaTime;
        _current = Mathf.MoveTowards(_current, target, step);

        if (!Mathf.Approximately(_current, _applied))
            Apply(_current);
    }

    // ------------------------------------------------------------ ground check

    private bool IsOnSlowSurface()
    {
        if (_slowSet.Count == 0) return false;

        // Feet position = bottom of the CharacterController capsule (it follows the player's head)
        Vector3 feet = transform.TransformPoint(_cc.center) + Vector3.down * (_cc.height * 0.5f);
        Vector3 origin = feet + Vector3.up * 0.2f;

        var hits = Physics.RaycastAll(origin, Vector3.down, 0.2f + checkDistance, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;      // ignore the player's own colliders

            if (h.collider.isTrigger)
            {
                if (_slowSet.Contains(h.collider)) return true;  // standing in a listed trigger zone
                continue;
            }

            // first solid thing under the feet = the ground the player is standing on
            return _slowSet.Contains(h.collider);
        }
        return false;
    }

    // ------------------------------------------------------------ speed

    private void FindMoveProviders()
    {
        _targets.Clear();

        IEnumerable<Behaviour> candidates = moveProviders != null && moveProviders.Length > 0
            ? (IEnumerable<Behaviour>)moveProviders
            : transform.root.GetComponentsInChildren<Behaviour>(true);

        const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var b in candidates)
        {
            if (b == null) continue;
            Type t = b.GetType();

            // XR Interaction Toolkit: ContinuousMoveProvider / DynamicMoveProvider have a "moveSpeed" setting
            var prop = t.GetProperty("moveSpeed", pub);
            if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead && prop.CanWrite)
            {
                _targets.Add(new SpeedTarget { component = b, property = prop, original = (float)prop.GetValue(b) });
                continue;
            }

            // fallback: the serialized backing field
            FieldInfo field = null;
            for (Type tt = t; tt != null && field == null; tt = tt.BaseType)
                field = tt.GetField("m_MoveSpeed", priv | BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType == typeof(float))
                _targets.Add(new SpeedTarget { component = b, field = field, original = (float)field.GetValue(b) });
        }

        if (_targets.Count == 0)
            Debug.LogWarning("[SlowSurface] No move provider with a 'Move Speed' setting found on the XR rig. " +
                             "Drag your Dynamic/Continuous Move Provider into 'Move Providers'.", this);
        else
            Debug.Log($"[SlowSurface] Controlling the speed of {_targets.Count} move provider(s).");
    }

    private void Apply(float multiplier)
    {
        _applied = multiplier;
        foreach (var t in _targets)
        {
            if (t.component == null) continue;
            float value = t.original * multiplier;
            if (t.property != null) t.property.SetValue(t.component, value);
            else t.field.SetValue(t.component, value);
        }
    }
}

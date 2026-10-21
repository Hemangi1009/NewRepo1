using System.Collections.Generic;
using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Same idea as VRAvatarSync, but with ONE fixed head (no variants, no selection, no PlayerPrefs index).
/// The head is simply the object you assign below and it is always on.
///
///  - Local player: head + hands are read from the local XR rig (hands found by Tag, like VRAvatarSync)
///    and written to the network. Your own head mesh is hidden (first person). Your avatar hands follow the rig hands.
///  - Remote players: head + hands follow the networked poses, hand animators get the networked Grip / Trigger.
///  - A hand is hidden (locally and remotely) while that hand is holding something.
///
/// Works with a skinned avatar (e.g. Ready Player Me): assign AvatarRoot as Head Root and list the body parts
/// (shirt, hands...) under Hidden Body Renderers, so only the head parts are visible.
/// </summary>
// NOTE: it inherits from NetworkAvatarSetup on purpose. HandBall (hit detection) and LocalXROriginProvider (fall respawn)
// look for NetworkAvatarSetup to find the players. Because this class IS a NetworkAvatarSetup, both keep working
// without any change. The base Spawned() is overridden and never called, so the old IK fields stay empty and unused.
public class VRSingleHeadAvatarSync : NetworkAvatarSetup
{
    [Header("Head (only one, always on)")]
    [Tooltip("The object that gets moved/rotated to follow the player's head. For a skinned avatar use AvatarRoot.")]
    [SerializeField] private Transform headRoot;
    [Tooltip("Optional. If both eyes are assigned, the eyes are lined up with the headset camera. If empty, the centre of the head renderers is lined up with the camera (like VRAvatarSync).")]
    [SerializeField] private Transform eyeLeft;
    [SerializeField] private Transform eyeRight;
    [Tooltip("Fine tuning in the head's own axes (x right, y up, z forward), in meters.")]
    [SerializeField] private Vector3 headOffset = Vector3.zero;
    [Tooltip("Parts that are never shown, e.g. Wolf3D_Shirt, Wolf3D_Hands.")]
    [SerializeField] private Renderer[] hiddenBodyRenderers;

    [Header("Player Name (optional - leave empty to have no name tag)")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Transform playerNameTransform;
    [SerializeField] private Vector3 playerNameOffset = new Vector3(0f, 0.25f, 0f);
    [SerializeField] private Vector3 playerNameRotationOffset = Vector3.zero;

    [Networked, Capacity(32)]
    private NetworkString<_32> NetworkPlayerName { get; set; }

    [Header("Hands & Tracking")]
    [SerializeField] private GameObject leftHandVisual;
    [SerializeField] private GameObject rightHandVisual;
    [SerializeField] private Vector3 leftHandRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 rightHandRotationOffset = new Vector3(180f, 0f, 0f);

    [Header("Animator Parameter Names")]
    [SerializeField] private string gripParam = "Grip";
    [SerializeField] private string triggerParam = "Trigger";

    [Header("Local Rig Lookup")]
    [Tooltip("The local XR rig's hands are found at runtime by Tag (a prefab cannot reference scene objects). Tag your rig's hand objects with these.")]
    [SerializeField] private string localLeftHandTag = "LocalLeftHand";
    [SerializeField] private string localRightHandTag = "LocalRightHand";

    // The avatar I own (set on the state-authority instance)
    public static VRSingleHeadAvatarSync Local { get; private set; }

    // ---------------------------------------------------------------- networked data
    [Networked] private NetworkBool NetHasPose { get; set; }

    [Networked] private float NetLeftGrip { get; set; }
    [Networked] private float NetLeftTrigger { get; set; }
    [Networked] private float NetRightGrip { get; set; }
    [Networked] private float NetRightTrigger { get; set; }

    [Networked] private Vector3 NetHeadCenterTarget { get; set; }
    [Networked] private Quaternion NetHeadRotation { get; set; }

    [Networked] private Vector3 NetLeftHandPosition { get; set; }
    [Networked] private Quaternion NetLeftHandRotation { get; set; }
    [Networked] private Vector3 NetRightHandPosition { get; set; }
    [Networked] private Quaternion NetRightHandRotation { get; set; }

    [Networked] private NetworkBool NetLeftHandHidden { get; set; }
    [Networked] private NetworkBool NetRightHandHidden { get; set; }

    // ---------------------------------------------------------------- local rig references
    private Transform _localCamera;
    private Transform _localLeftHand;
    private Transform _localRightHand;
    private Animator _localLeftAnimator;
    private Animator _localRightAnimator;
    private Animator _avatarLeftAnimator;
    private Animator _avatarRightAnimator;

    private readonly List<Renderer> _headRenderers = new List<Renderer>();
    private Vector3 _headPivotToCenterLocal = Vector3.zero;
    private Vector3 _headPivotToTopLocal = Vector3.zero;
    private bool _headVisibleApplied;
    private bool _headVisibleKnown;

    private float _rigSearchRetryTimer;
    private const float RigSearchRetryInterval = 0.5f;

    // grab counters (optional hook, see SetGrabHandHidden) + hand renderers
    private int _leftGrabCount, _rightGrabCount;
    private Renderer[] _leftHandRenderers, _rightHandRenderers;

    // ---------------------------------------------------------------- spawn
    public override void Spawned()
    {
        if (leftHandVisual != null) _avatarLeftAnimator = leftHandVisual.GetComponentInChildren<Animator>();
        if (rightHandVisual != null) _avatarRightAnimator = rightHandVisual.GetComponentInChildren<Animator>();

        if (leftHandVisual != null) _leftHandRenderers = leftHandVisual.GetComponentsInChildren<Renderer>(true);
        if (rightHandVisual != null) _rightHandRenderers = rightHandVisual.GetComponentsInChildren<Renderer>(true);

        EnsureOwnNameplateReferences();
        NeutralizeExternalHandDrivers();
        SetupHead();

        if (HasStateAuthority)
        {
            Local = this;
            NetworkPlayerName = PlayerPrefs.GetString("PlayerName", "Player");

            TryResolveLocalCamera();
            TryResolveRigHands();
        }

        ApplyHeadVisibility(force: true);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
    }

    private void SetupHead()
    {
        _headRenderers.Clear();
        if (headRoot == null)
        {
            Debug.LogError("[VRSingleHeadAvatarSync] Head Root is not assigned.");
            return;
        }

        var hidden = new HashSet<Renderer>();
        if (hiddenBodyRenderers != null)
        {
            foreach (var r in hiddenBodyRenderers)
            {
                if (r == null) continue;
                hidden.Add(r);
                // switched OFF as objects (not just the renderer), so the ball's hit test does not count them in the player's size
                r.gameObject.SetActive(false);
            }
        }

        foreach (var r in headRoot.GetComponentsInChildren<Renderer>(true))
            if (r != null && !hidden.Contains(r)) _headRenderers.Add(r);

        // Measured once, before anything moves
        Vector3 worldCenter;
        Vector3 worldTop;
        bool haveBounds = _headRenderers.Count > 0;
        Bounds b = default;

        if (haveBounds)
        {
            b = _headRenderers[0].bounds;
            for (int i = 1; i < _headRenderers.Count; i++) b.Encapsulate(_headRenderers[i].bounds);
        }

        if (eyeLeft != null && eyeRight != null)
            worldCenter = (eyeLeft.position + eyeRight.position) * 0.5f;
        else
            worldCenter = haveBounds ? b.center : headRoot.position;

        worldTop = haveBounds ? new Vector3(b.center.x, b.max.y, b.center.z) : worldCenter;

        _headPivotToCenterLocal = headRoot.InverseTransformPoint(worldCenter);
        _headPivotToTopLocal = headRoot.InverseTransformPoint(worldTop);
    }

    private void EnsureOwnNameplateReferences()
    {
        if (playerNameText == null) return;

        if (!playerNameText.transform.IsChildOf(transform))
        {
            var ownText = GetComponentInChildren<TMP_Text>(true);
            if (ownText != null) playerNameText = ownText;
        }

        if (playerNameTransform == null || !playerNameTransform.IsChildOf(transform))
            playerNameTransform = playerNameText.transform;
    }

    // ---------------------------------------------------------------- hand hiding while grabbing
    /// <summary>
    /// Optional hook, same signature as VRAvatarSync.SetGrabHandHidden. The script also detects grabbing by itself
    /// (the rig's Hand script switches its Animator off while an object is held), so calling this is not required.
    /// </summary>
    public void SetGrabHandHidden(Transform interactorTransform, InteractorHandedness handedness, bool grabbing)
    {
        if (!HasStateAuthority) return;

        int side = -1;
        if (handedness == InteractorHandedness.Left) side = 0;
        else if (handedness == InteractorHandedness.Right) side = 1;
        else if (interactorTransform != null)
        {
            if (_localLeftHand != null && (interactorTransform.IsChildOf(_localLeftHand) || _localLeftHand.IsChildOf(interactorTransform))) side = 0;
            else if (_localRightHand != null && (interactorTransform.IsChildOf(_localRightHand) || _localRightHand.IsChildOf(interactorTransform))) side = 1;
        }
        if (side < 0) return;

        int delta = grabbing ? 1 : -1;
        if (side == 0) _leftGrabCount = Mathf.Max(0, _leftGrabCount + delta);
        else _rightGrabCount = Mathf.Max(0, _rightGrabCount + delta);
    }

    private static void SetRenderersVisible(Renderer[] rends, bool visible)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null && rends[i].enabled != visible) rends[i].enabled = visible;
    }

    // ---------------------------------------------------------------- head helpers
    private void ApplyHeadVisibility(bool force = false)
    {
        // local player never sees their own head; remote players see it once the first pose arrived
        bool visible = !HasStateAuthority && NetHasPose;
        if (!force && _headVisibleKnown && _headVisibleApplied == visible) return;

        for (int i = 0; i < _headRenderers.Count; i++)
            if (_headRenderers[i] != null) _headRenderers[i].enabled = visible;

        _headVisibleApplied = visible;
        _headVisibleKnown = true;
    }

    private void ApplyHeadCenteredTransform(Vector3 targetCenter, Quaternion targetRotation)
    {
        if (headRoot == null) return;

        headRoot.rotation = targetRotation;
        Vector3 worldOffset = headRoot.TransformVector(_headPivotToCenterLocal);
        headRoot.position = targetCenter - worldOffset + headRoot.TransformVector(headOffset);
    }

    // ---------------------------------------------------------------- finding the local rig
    private void TryResolveLocalCamera()
    {
        if (_localCamera != null) return;
        if (Camera.main != null) _localCamera = Camera.main.transform;
    }

    private Renderer[] _leftRigRenderers, _rightRigRenderers;

    private void TryResolveRigHands()
    {
        if (!HasStateAuthority) return;

        // Tag first (like VRAvatarSync). If the tag is missing, fall back to the hand targets of LocalXROriginProvider.
        var xr = LocalXROriginProvider.Instance;
        ResolveRigHand(ref _localLeftHand, ref _localLeftAnimator, ref _leftRigRenderers,
                       localLeftHandTag, xr != null ? xr.LeftHandVRTarget : null, "LEFT");
        ResolveRigHand(ref _localRightHand, ref _localRightAnimator, ref _rightRigRenderers,
                       localRightHandTag, xr != null ? xr.RightHandVRTarget : null, "RIGHT");
    }

    private void ResolveRigHand(ref Transform hand, ref Animator anim, ref Renderer[] rigRenderers,
                                string tag, Transform providerTarget, string label)
    {
        if (hand != null) return;

        // 1) by tag
        if (!string.IsNullOrEmpty(tag))
        {
            var go = SafeFindWithTag(tag);
            if (go != null)
            {
                hand = go.transform;
                anim = go.GetComponentInChildren<Animator>();
                rigRenderers = go.GetComponentsInChildren<Renderer>(true);
                DisableNetworkDriversOnRig(hand);
                Debug.Log($"[VRSingleHeadAvatarSync] Resolved local {label} hand by tag '{tag}'.");
                return;
            }
        }

        // 2) by LocalXROriginProvider target + the rig's Hand script
        if (providerTarget == null) return;

        Hand handScript = providerTarget.GetComponentInChildren<Hand>(true);
        if (handScript == null) handScript = providerTarget.GetComponentInParent<Hand>();
        // The hand model usually is NOT under the VR target (it lives under the controller). Search the whole rig.
        if (handScript == null) handScript = FindRigHand(providerTarget, label == "LEFT" ? 0 : 1);

        if (handScript != null)
        {
            hand = handScript.transform;                       // the visible hand model: same pose the player sees
            anim = handScript.GetComponent<Animator>();
            rigRenderers = handScript.GetComponentsInChildren<Renderer>(true);
        }
        else
        {
            hand = providerTarget;
            anim = providerTarget.GetComponentInChildren<Animator>();
            rigRenderers = providerTarget.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.LogWarning($"[VRSingleHeadAvatarSync] No Hand script found for the {label} hand, using the VR target and hiding its skinned meshes.");
        }

        DisableNetworkDriversOnRig(hand);
        Debug.Log($"[VRSingleHeadAvatarSync] Resolved local {label} hand through LocalXROriginProvider. Pose source='{hand.name}', Hand script={(handScript != null ? "FOUND" : "NOT FOUND")}, rig renderers to hide={(rigRenderers != null ? rigRenderers.Length : 0)}.");
    }

    // Looks through every Hand script in the scene (except the ones on avatars) and picks the one for this side:
    // 1) by the input binding of its grip action ({LeftHand} / {RightHand}), 2) by names in its hierarchy,
    // 3) otherwise the closest one to the VR target.
    private static Hand FindRigHand(Transform target, int side)
    {
        Hand bestSide = null, bestUnknown = null;
        float dSide = float.MaxValue, dUnknown = float.MaxValue;

        foreach (var h in FindObjectsByType<Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (h == null) continue;
            if (h.GetComponentInParent<VRSingleHeadAvatarSync>(true) != null) continue;   // avatar copy, not the rig

            int hs = HandSide(h);
            float d = (h.transform.position - target.position).sqrMagnitude;

            if (hs == side) { if (d < dSide) { dSide = d; bestSide = h; } }
            else if (hs < 0) { if (d < dUnknown) { dUnknown = d; bestUnknown = h; } }
        }

        return bestSide != null ? bestSide : bestUnknown;
    }

    // 0 = left, 1 = right, -1 = unknown
    private static int HandSide(Hand h)
    {
        var act = h.gripAction.action;
        if (act != null)
        {
            foreach (var b in act.bindings)
            {
                string path = b.path ?? string.Empty;
                if (path.IndexOf("lefthand", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (path.IndexOf("righthand", System.StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            }

            string an = ((act.actionMap != null ? act.actionMap.name : "") + " " + act.name).ToLowerInvariant();
            bool al = an.Contains("left"), ar = an.Contains("right");
            if (al && !ar) return 0;
            if (ar && !al) return 1;
        }

        for (Transform t = h.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            bool l = n.Contains("left"), r = n.Contains("right");
            if (l && !r) return 0;
            if (r && !l) return 1;
        }
        return -1;
    }

    private GameObject SafeFindWithTag(string tag)
    {
        try
        {
            var tagged = GameObject.FindGameObjectsWithTag(tag);
            for (int t = 0; t < tagged.Length; t++)
            {
                if (tagged[t].GetComponentInParent<VRSingleHeadAvatarSync>(true) != null) continue; // part of an avatar, not the rig
                return tagged[t];
            }

            var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].CompareTag(tag) && all[i].GetComponentInParent<VRSingleHeadAvatarSync>(true) == null)
                {
                    Debug.LogWarning($"[VRSingleHeadAvatarSync] Found '{all[i].name}' tagged '{tag}' but it is INACTIVE. Using it anyway.");
                    return all[i].gameObject;
                }
            }
            return null;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[VRSingleHeadAvatarSync] Tag \"{tag}\" doesn't exist yet. Create it in Project Settings > Tags and Layers.");
            return null;
        }
    }

    private static void EnsureRigHandActive(Transform hand)
    {
        if (hand != null && !hand.gameObject.activeSelf) hand.gameObject.SetActive(true);
    }

    private static void HideRigRenderers(Renderer[] rends)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null && rends[i].enabled) rends[i].enabled = false;
    }

    // ---------------------------------------------------------------- network write
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_localCamera == null) TryResolveLocalCamera();

        if (_localLeftAnimator != null)
        {
            NetLeftGrip = _localLeftAnimator.GetFloat(gripParam);
            NetLeftTrigger = _localLeftAnimator.GetFloat(triggerParam);
        }

        if (_localRightAnimator != null)
        {
            NetRightGrip = _localRightAnimator.GetFloat(gripParam);
            NetRightTrigger = _localRightAnimator.GetFloat(triggerParam);
        }

        if (_localCamera != null)
        {
            NetHeadCenterTarget = CamPos;
            NetHeadRotation = CamRot;
            NetHasPose = true;
        }

        if (_localLeftHand != null)
        {
            NetLeftHandPosition = LPos;
            NetLeftHandRotation = LRot;
        }

        if (_localRightHand != null)
        {
            NetRightHandPosition = RPos;
            NetRightHandRotation = RRot;
        }

        NetLeftHandHidden = IsLeftHandHeld;
        NetRightHandHidden = IsRightHandHeld;
    }

    // The rig's Hand script switches its Animator off while an object is held
    private bool IsLeftHandHeld => _leftGrabCount > 0 || (_localLeftAnimator != null && !_localLeftAnimator.enabled);
    private bool IsRightHandHeld => _rightGrabCount > 0 || (_localRightAnimator != null && !_localRightAnimator.enabled);

    // ---------------------------------------------------------------- render
    public override void Render()
    {
        ApplyHeadVisibility();

        if (HasStateAuthority)
        {
            if (_localCamera == null) TryResolveLocalCamera();

            if (_localLeftHand == null || _localRightHand == null)
            {
                _rigSearchRetryTimer += Time.deltaTime;
                if (_rigSearchRetryTimer >= RigSearchRetryInterval)
                {
                    _rigSearchRetryTimer = 0f;
                    TryResolveRigHands();
                }
            }

            // The player sees the avatar hands, so the rig's own hand meshes are hidden
            EnsureRigHandActive(_localLeftHand);
            EnsureRigHandActive(_localRightHand);
            HideRigRenderers(_leftRigRenderers);
            HideRigRenderers(_rightRigRenderers);

            if (_localCamera != null)
                ApplyHeadCenteredTransform(CamPos, CamRot);

            if (leftHandVisual != null && _localLeftHand != null)
            {
                leftHandVisual.transform.position = LPos;
                leftHandVisual.transform.rotation = LRot * Quaternion.Euler(leftHandRotationOffset);

                if (_avatarLeftAnimator != null && _localLeftAnimator != null)
                {
                    _avatarLeftAnimator.SetFloat(gripParam, _localLeftAnimator.GetFloat(gripParam));
                    _avatarLeftAnimator.SetFloat(triggerParam, _localLeftAnimator.GetFloat(triggerParam));
                }
            }

            if (rightHandVisual != null && _localRightHand != null)
            {
                rightHandVisual.transform.position = RPos;
                rightHandVisual.transform.rotation = RRot * Quaternion.Euler(rightHandRotationOffset);

                if (_avatarRightAnimator != null && _localRightAnimator != null)
                {
                    _avatarRightAnimator.SetFloat(gripParam, _localRightAnimator.GetFloat(gripParam));
                    _avatarRightAnimator.SetFloat(triggerParam, _localRightAnimator.GetFloat(triggerParam));
                }
            }
        }
        else
        {
            _remLPos = NetLeftHandPosition; _remLRot = NetLeftHandRotation;
            _remRPos = NetRightHandPosition; _remRRot = NetRightHandRotation;
            _haveRemote = NetHasPose;

            if (NetHasPose)
            {
                ApplyHeadCenteredTransform(NetHeadCenterTarget, NetHeadRotation);

                if (leftHandVisual != null)
                {
                    leftHandVisual.transform.position = NetLeftHandPosition;
                    leftHandVisual.transform.rotation = NetLeftHandRotation * Quaternion.Euler(leftHandRotationOffset);
                }

                if (rightHandVisual != null)
                {
                    rightHandVisual.transform.position = NetRightHandPosition;
                    rightHandVisual.transform.rotation = NetRightHandRotation * Quaternion.Euler(rightHandRotationOffset);
                }
            }

            if (_avatarLeftAnimator != null)
            {
                _avatarLeftAnimator.SetFloat(gripParam, NetLeftGrip);
                _avatarLeftAnimator.SetFloat(triggerParam, NetLeftTrigger);
            }

            if (_avatarRightAnimator != null)
            {
                _avatarRightAnimator.SetFloat(gripParam, NetRightGrip);
                _avatarRightAnimator.SetFloat(triggerParam, NetRightTrigger);
            }
        }

        // hands are hidden while holding something, and (for remote players) until the first pose arrived
        bool show = HasStateAuthority || NetHasPose;
        bool leftHidden = HasStateAuthority ? IsLeftHandHeld : NetLeftHandHidden;
        bool rightHidden = HasStateAuthority ? IsRightHandHeld : NetRightHandHidden;
        // local player: avatar hands only appear once the rig hands were found (no hands sitting at the prefab origin)
        bool leftShow = HasStateAuthority ? _localLeftHand != null : show;
        bool rightShow = HasStateAuthority ? _localRightHand != null : show;
        SetRenderersVisible(_leftHandRenderers, leftShow && !leftHidden);
        SetRenderersVisible(_rightHandRenderers, rightShow && !rightHidden);

        UpdatePlayerNameTag();
    }

    private Vector3 _remLPos, _remRPos;
    private Quaternion _remLRot = Quaternion.identity, _remRRot = Quaternion.identity;
    private bool _haveRemote;

    // ---- Pose cache -------------------------------------------------------------------------
    // A scene NetworkTransform on the XR rig can overwrite the rig transforms during Fusion's Render on
    // clients that are not the rig's owner. So the real tracked pose is sampled in Update() (before Render)
    // and this cached copy is what the avatar uses.
    private Vector3 _cLPos, _cRPos, _cCamPos;
    private Quaternion _cLRot = Quaternion.identity, _cRRot = Quaternion.identity, _cCamRot = Quaternion.identity;
    private bool _haveL, _haveR, _haveCam;

    private Vector3 LPos => _haveL ? _cLPos : _localLeftHand.position;
    private Quaternion LRot => _haveL ? _cLRot : _localLeftHand.rotation;
    private Vector3 RPos => _haveR ? _cRPos : _localRightHand.position;
    private Quaternion RRot => _haveR ? _cRRot : _localRightHand.rotation;
    private Vector3 CamPos => _haveCam ? _cCamPos : _localCamera.position;
    private Quaternion CamRot => _haveCam ? _cCamRot : _localCamera.rotation;

    // Viewer camera rotation (for the name tag), sampled once per frame in Update()
    private static Quaternion _viewCamRotCached = Quaternion.identity;
    private static bool _haveViewCamRot;
    private static int _viewCamRotFrame = -1;

    private static void SampleViewCamera()
    {
        if (_viewCamRotFrame == Time.frameCount) return;
        _viewCamRotFrame = Time.frameCount;
        Camera c = GetViewCamera();
        if (c != null) { _viewCamRotCached = c.transform.rotation; _haveViewCamRot = true; }
    }

    private void Update()
    {
        SampleViewCamera();

        if (Object == null || Runner == null || !HasStateAuthority) return;

        if (_localLeftHand != null) { _cLPos = _localLeftHand.position; _cLRot = _localLeftHand.rotation; _haveL = true; }
        if (_localRightHand != null) { _cRPos = _localRightHand.position; _cRRot = _localRightHand.rotation; _haveR = true; }
        if (_localCamera != null) { _cCamPos = _localCamera.position; _cCamRot = _localCamera.rotation; _haveCam = true; }
    }

    // The local rig must never be driven by the network, only the avatar prefab is networked.
    private void DisableNetworkDriversOnRig(Transform hand)
    {
        if (hand == null) return;
        var list = new List<UnityEngine.Behaviour>();
        list.AddRange(hand.GetComponentsInParent<UnityEngine.Behaviour>(true));
        list.AddRange(hand.GetComponentsInChildren<UnityEngine.Behaviour>(true));
        foreach (var b in list)
        {
            if (b == null || b == this || !b.enabled) continue;
            if (b.transform.IsChildOf(transform)) continue;
            string n = b.GetType().Name;
            if (n.Contains("NetworkTransform") || n.Contains("NetworkRigidbody"))
            {
                b.enabled = false;
                Debug.LogWarning($"[VRSingleHeadAvatarSync] Disabled '{n}' on LOCAL RIG object '{b.name}' - it was overwriting the rig hand pose.");
            }
        }
    }

    // Anything on this avatar's hand visuals that could move them on its own is switched off.
    private void NeutralizeExternalHandDrivers()
    {
        foreach (var root in new[] { leftHandVisual, rightHandVisual })
        {
            if (root == null) continue;
            foreach (var b in root.GetComponentsInChildren<UnityEngine.Behaviour>(true))
            {
                if (b == null || b == this || !b.enabled) continue;
                string n = b.GetType().Name;
                if (n == "Hand" || n.EndsWith("Interactor") || n.Contains("TrackedPoseDriver") || n.Contains("ActionBasedController") ||
                    n.Contains("XRController") || n.Contains("NetworkTransform") || n.Contains("NetworkRigidbody"))
                {
                    b.enabled = false;
                    Debug.LogWarning($"[VRSingleHeadAvatarSync] Disabled '{n}' on '{b.name}' (avatar hand visual).");
                }
            }
        }
    }

    // Re-applies the hand poses after every other script/animator ran this frame
    private void LateUpdate()
    {
        if (Object == null || Runner == null) return;

        if (HasStateAuthority)
        {
            HideRigRenderers(_leftRigRenderers);
            HideRigRenderers(_rightRigRenderers);

            if (leftHandVisual != null && _localLeftHand != null)
            {
                leftHandVisual.transform.position = LPos;
                leftHandVisual.transform.rotation = LRot * Quaternion.Euler(leftHandRotationOffset);
            }
            if (rightHandVisual != null && _localRightHand != null)
            {
                rightHandVisual.transform.position = RPos;
                rightHandVisual.transform.rotation = RRot * Quaternion.Euler(rightHandRotationOffset);
            }
        }
        else if (_haveRemote)
        {
            if (leftHandVisual != null)
            {
                leftHandVisual.transform.position = _remLPos;
                leftHandVisual.transform.rotation = _remLRot * Quaternion.Euler(leftHandRotationOffset);
            }
            if (rightHandVisual != null)
            {
                rightHandVisual.transform.position = _remRPos;
                rightHandVisual.transform.rotation = _remRRot * Quaternion.Euler(rightHandRotationOffset);
            }
        }
    }

    // ---------------------------------------------------------------- name tag (optional)
    private static Camera _viewCam;
    private static Camera GetViewCamera()
    {
        if (_viewCam != null && _viewCam.isActiveAndEnabled) return _viewCam;

        Camera best = null;
        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null || c.targetTexture != null) continue;
            if (c.GetComponentInParent<VRSingleHeadAvatarSync>(true) != null) continue;
            if (best == null || c.stereoTargetEye != StereoTargetEyeMask.None || c.CompareTag("MainCamera"))
                best = c;
            if (c.stereoTargetEye != StereoTargetEyeMask.None) { best = c; break; }
        }
        _viewCam = best != null ? best : Camera.main;
        return _viewCam;
    }

    private void OnEnable() { Application.onBeforeRender += OnBeforeRenderNameTag; }
    private void OnDisable() { Application.onBeforeRender -= OnBeforeRenderNameTag; }

    private void OnBeforeRenderNameTag()
    {
        if (this == null || Object == null || Runner == null) return;
        UpdatePlayerNameTag();
    }

    private void UpdatePlayerNameTag()
    {
        if (playerNameText == null || playerNameTransform == null || headRoot == null) return;

        Vector3 headTop = headRoot.TransformPoint(_headPivotToTopLocal);
        playerNameTransform.position = headTop + playerNameOffset;

        if (_haveViewCamRot)
        {
            Vector3 fwd = _viewCamRotCached * Vector3.forward;
            fwd.y = 0f;

            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = _viewCamRotCached * Vector3.up;
                fwd.y = 0f;
            }

            if (fwd.sqrMagnitude > 0.0001f)
                playerNameTransform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(playerNameRotationOffset);
        }

        playerNameText.text = NetworkPlayerName.ToString();
    }
}
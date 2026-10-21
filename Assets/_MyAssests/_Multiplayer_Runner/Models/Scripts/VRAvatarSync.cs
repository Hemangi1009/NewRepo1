using System.Collections.Generic;
using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class VRAvatarSync : NetworkBehaviour
{
    [Header("8 Head Avatar Variants")]
    [Tooltip("Drag your 8 Head GameObjects/Renderers here (Index 0 to 7)")]
    [SerializeField] private GameObject[] headVariants = new GameObject[8];

    [Header("Player Name")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private Transform playerNameTransform;
    [SerializeField] private Vector3 playerNameOffset = new Vector3(0f, 0.25f, 0f);
    [Tooltip("Extra rotation (degrees) applied on top of the billboard look-rotation. Tweak this live in Play mode to fix any remaining mirroring/orientation.")]
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
    [Tooltip("This prefab can't hold a direct reference to scene objects, so the local XR rig's hands are found at runtime by Tag instead. Create these tags in Edit > Project Settings > Tags and Layers, and assign them to your rig's hand GameObjects in the scene.")]
    [SerializeField] private string localLeftHandTag = "LocalLeftHand";
    [SerializeField] private string localRightHandTag = "LocalRightHand";

    // Synchronized index of which head is chosen (0 to 7)
    [Networked] public int ActiveHeadIndex { get; set; } = -1;

    // Networked hand animations
    [Networked] private float NetLeftGrip { get; set; }
    [Networked] private float NetLeftTrigger { get; set; }
    [Networked] private float NetRightGrip { get; set; }
    [Networked] private float NetRightTrigger { get; set; }

    // Networked head pose
    [Networked] private Vector3 NetHeadCenterTarget { get; set; }
    [Networked] private Quaternion NetHeadRotation { get; set; }

    // Networked hand poses
    [Networked] private Vector3 NetLeftHandPosition { get; set; }
    [Networked] private Quaternion NetLeftHandRotation { get; set; }
    [Networked] private Vector3 NetRightHandPosition { get; set; }
    [Networked] private Quaternion NetRightHandRotation { get; set; }

    private Transform _localCamera;
    private Transform _localLeftHand;
    private Transform _localRightHand;
    private Animator _localLeftAnimator;
    private Animator _localRightAnimator;
    private Animator _avatarLeftAnimator;
    private Animator _avatarRightAnimator;

    private int _lastAppliedIndex = -99;
    private readonly List<Renderer> _activeHeadRenderers = new List<Renderer>();

    private Vector3 _headPivotToCenterLocal = Vector3.zero;
    private Vector3 _headPivotToTopLocal = Vector3.zero;

    private float _rigSearchRetryTimer;
    private const float RigSearchRetryInterval = 0.5f;
    private int _rigSearchAttempts;

    [Header("Debug")]
    [Tooltip("Logs this avatar's local hand pose + grip/trigger values once per second (owner only). Turn off when done.")]
    [SerializeField] private bool debugLogHandState = true;
    private float _nextHandDebugTime;
    private bool _warnedLeftReactivated;
    private bool _warnedRightReactivated;
    private float _nextRemoteDebugTime;

    // ---- Hide hand mesh while grabbing ----
    // The avatar I own (set in Spawned on the state-authority instance). NetworkedGrabSync uses this.
    public static VRAvatarSync Local { get; private set; }

    [Networked] private NetworkBool NetLeftHandHidden { get; set; }
    [Networked] private NetworkBool NetRightHandHidden { get; set; }

    // Counters so holding two objects in one hand can't re-show the hand too early.
    private int _leftGrabCount, _rightGrabCount;
    private Renderer[] _leftHandRenderers, _rightHandRenderers;

    public override void Spawned()
    {
        if (leftHandVisual != null) _avatarLeftAnimator = leftHandVisual.GetComponentInChildren<Animator>();
        if (rightHandVisual != null) _avatarRightAnimator = rightHandVisual.GetComponentInChildren<Animator>();

        if (leftHandVisual != null) _leftHandRenderers = leftHandVisual.GetComponentsInChildren<Renderer>(true);
        if (rightHandVisual != null) _rightHandRenderers = rightHandVisual.GetComponentsInChildren<Renderer>(true);

        EnsureOwnNameplateReferences();
        NeutralizeExternalHandDrivers();

        // FIX: Only the LOCAL player controlling this avatar instance should resolve local preferences and hardware tracking
        if (HasStateAuthority)
        {
            Local = this;

            int chosenHead = PlayerPrefs.GetInt("SelectedAvatarIndex", 0);

            NetworkPlayerName = PlayerPrefs.GetString("PlayerName", "Player");

            ActiveHeadIndex = Mathf.Clamp(chosenHead, 0, headVariants.Length - 1);

            TryResolveLocalCamera();
            TryResolveRigHands();
        }

        ApplyHeadSelection(ActiveHeadIndex);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
    }

    // Called by NetworkedGrabSync on the local player's grab / release.
    public void SetGrabHandHidden(Transform interactorTransform, InteractorHandedness handedness, bool grabbing)
    {
        if (!HasStateAuthority) return;

        int side = -1; // 0 = left, 1 = right
        if (handedness == InteractorHandedness.Left) side = 0;
        else if (handedness == InteractorHandedness.Right) side = 1;
        else if (interactorTransform != null)
        {
            // Fallback when the interactor has no handedness set: match against the tagged rig hands.
            if (_localLeftHand != null && (interactorTransform.IsChildOf(_localLeftHand) || _localLeftHand.IsChildOf(interactorTransform))) side = 0;
            else if (_localRightHand != null && (interactorTransform.IsChildOf(_localRightHand) || _localRightHand.IsChildOf(interactorTransform))) side = 1;
        }
        if (side < 0) return; // e.g. socket interactor - ignored

        int delta = grabbing ? 1 : -1;
        if (side == 0) _leftGrabCount = Mathf.Max(0, _leftGrabCount + delta);
        else _rightGrabCount = Mathf.Max(0, _rightGrabCount + delta);
    }

    private static void SetRenderersVisible(Renderer[] rends, bool visible)
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].enabled != visible)
                rends[i].enabled = visible;
        }
    }

    private void EnsureOwnNameplateReferences()
    {
        if (playerNameText == null || !playerNameText.transform.IsChildOf(transform))
        {
            var ownText = GetComponentInChildren<TMP_Text>(true);
            if (ownText != null)
            {
                if (playerNameText != null)
                {
                    Debug.LogWarning($"[VRAvatarSync] '{name}': playerNameText was not a child of this avatar - re-resolved to this avatar's own '{ownText.name}' instead.");
                }
                playerNameText = ownText;
            }
        }

        if (playerNameTransform == null || !playerNameTransform.IsChildOf(transform))
        {
            if (playerNameText != null)
            {
                playerNameTransform = playerNameText.transform;
            }
        }
    }

    private void ApplyHeadSelection(int index)
    {
        if (headVariants == null || headVariants.Length == 0) return;

        _activeHeadRenderers.Clear();

        for (int i = 0; i < headVariants.Length; i++)
        {
            if (headVariants[i] == null) continue;

            bool isChosen = (i == index);
            headVariants[i].SetActive(isChosen);

            if (isChosen)
            {
                Renderer[] rends = headVariants[i].GetComponentsInChildren<Renderer>(true);
                _activeHeadRenderers.AddRange(rends);

                _headPivotToCenterLocal = ComputeLocalCenterOffset(headVariants[i].transform, rends);
                _headPivotToTopLocal = ComputeLocalTopOffset(headVariants[i].transform, rends);
            }
        }

        // FIX: Check HasStateAuthority so local player hides their own head mesh (first person view)
        if (HasStateAuthority)
        {
            SetHeadRenderersVisible(false);
        }
        else
        {
            SetHeadRenderersVisible(true);
        }

        _lastAppliedIndex = index;
    }

    private Vector3 ComputeLocalCenterOffset(Transform headTransform, Renderer[] rends)
    {
        if (rends == null || rends.Length == 0) return Vector3.zero;

        Bounds combined = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            combined.Encapsulate(rends[i].bounds);
        }

        Vector3 worldCenter = combined.center;
        return headTransform.InverseTransformPoint(worldCenter);
    }

    private Vector3 ComputeLocalTopOffset(Transform headTransform, Renderer[] rends)
    {
        if (rends == null || rends.Length == 0) return Vector3.zero;

        Bounds combined = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            combined.Encapsulate(rends[i].bounds);
        }

        Vector3 worldTop = new Vector3(combined.center.x, combined.max.y, combined.center.z);
        return headTransform.InverseTransformPoint(worldTop);
    }

    private void ApplyHeadCenteredTransform(Transform headTransform, Vector3 targetCenter, Quaternion targetRotation)
    {
        headTransform.rotation = targetRotation;
        Vector3 worldOffset = headTransform.TransformVector(_headPivotToCenterLocal);
        headTransform.position = targetCenter - worldOffset;
    }

    private void SetHeadRenderersVisible(bool visible)
    {
        for (int i = 0; i < _activeHeadRenderers.Count; i++)
        {
            if (_activeHeadRenderers[i] != null)
                _activeHeadRenderers[i].enabled = visible;
        }
    }

    private void HideRigRenderers(Transform handRoot)
    {
        if (handRoot == null) return;
        var rends = handRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].enabled)
                rends[i].enabled = false;
        }
    }

    private void TryResolveLocalCamera()
    {
        if (_localCamera != null) return;
        if (Camera.main != null)
        {
            _localCamera = Camera.main.transform;
        }
    }

    private void TryResolveRigHands()
    {
        // FIX: Ensure hand lookup only happens for the local player's avatar instance
        if (!HasStateAuthority) return;

        if (_localLeftHand == null && !string.IsNullOrEmpty(localLeftHandTag))
        {
            var go = SafeFindWithTag(localLeftHandTag);
            if (go != null)
            {
                _localLeftHand = go.transform;
                DisableNetworkDriversOnRig(_localLeftHand);
                Debug.Log("[VRAvatarSync] Resolved local LEFT hand.");
                _localLeftAnimator = go.GetComponentInChildren<Animator>();
            }
        }

        if (_localRightHand == null && !string.IsNullOrEmpty(localRightHandTag))
        {
            var go = SafeFindWithTag(localRightHandTag);
            if (go != null)
            {
                _localRightHand = go.transform;
                DisableNetworkDriversOnRig(_localRightHand);
                Debug.Log("[VRAvatarSync] Resolved local RIGHT hand.");
                _localRightAnimator = go.GetComponentInChildren<Animator>();
            }
        }
    }

    private GameObject SafeFindWithTag(string tag)
    {
        try
        {
            var tagged = GameObject.FindGameObjectsWithTag(tag);
            for (int t = 0; t < tagged.Length; t++)
            {
                if (tagged[t].GetComponentInParent<VRAvatarSync>(true) != null)
                {
                    Debug.LogWarning($"[VRAvatarSync] Ignoring '{tagged[t].name}' tagged '{tag}' - it is part of an avatar prefab, not the local rig.");
                    continue;
                }
                return tagged[t];
            }

            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].CompareTag(tag) && all[i].GetComponentInParent<VRAvatarSync>(true) == null)
                {
                    Debug.LogWarning($"[VRAvatarSync] Found '{all[i].name}' tagged '{tag}' but it is INACTIVE. Using it anyway.");
                    return all[i].gameObject;
                }
            }

            return null;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[VRAvatarSync] Tag \"{tag}\" doesn't exist yet.");
            return null;
        }
    }

    private void EnsureRigHandActive(Transform hand, string label, ref bool warned)
    {
        if (hand == null) return;
        GameObject go = hand.gameObject;
        if (go.activeSelf) return;

        go.SetActive(true);

        if (!warned)
        {
            warned = true;
            Debug.LogWarning($"[VRAvatarSync] Rig {label} hand '{go.name}' re-enabled.");
        }
    }

    private void DebugLogRemoteState()
    {
        if (!debugLogHandState || Time.time < _nextRemoteDebugTime) return;
        _nextRemoteDebugTime = Time.time + 1f;

        Debug.Log($"[VRAvatarSync][Diag-REMOTE] avatar owned by Player {Object.StateAuthority.PlayerId} | head={ActiveHeadIndex} | " +
                  $"LEFT pos={NetLeftHandPosition:F2} grip={NetLeftGrip:F2} trigger={NetLeftTrigger:F2} | " +
                  $"RIGHT pos={NetRightHandPosition:F2} grip={NetRightGrip:F2} trigger={NetRightTrigger:F2}");
    }

    private void DebugLogHandState()
    {
        if (!debugLogHandState || Time.time < _nextHandDebugTime) return;
        _nextHandDebugTime = Time.time + 1f;

        string L = _localLeftHand != null ? $"'{_localLeftHand.name}' pos={LPos:F2}" : "NOT FOUND";
        string R = _localRightHand != null ? $"'{_localRightHand.name}' pos={RPos:F2}" : "NOT FOUND";
        string C = _localCamera != null ? CamPos.ToString("F2") : "NOT FOUND";

        Debug.Log($"[VRAvatarSync][Diag] LEFT {L} | RIGHT {R} | camera pos={C}");
    }

    public override void FixedUpdateNetwork()
    {
        // FIX: Sample local VR hardware ONLY for your own local avatar instance
        if (HasStateAuthority)
        {
            DebugLogHandState();
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

            NetLeftHandHidden = _leftGrabCount > 0;
            NetRightHandHidden = _rightGrabCount > 0;
        }
    }

    public override void Render()
    {
        if (_lastAppliedIndex != ActiveHeadIndex && ActiveHeadIndex >= 0 && ActiveHeadIndex < headVariants.Length)
        {
            ApplyHeadSelection(ActiveHeadIndex);
        }

        // FIX: Separate local local authority rendering from remote rendering
        if (HasStateAuthority)
        {
            if (_localCamera == null) TryResolveLocalCamera();

            if (_localLeftHand == null || _localRightHand == null)
            {
                _rigSearchRetryTimer += Time.deltaTime;
                if (_rigSearchRetryTimer >= RigSearchRetryInterval)
                {
                    _rigSearchRetryTimer = 0f;
                    _rigSearchAttempts++;
                    TryResolveRigHands();
                }
            }

            SetHeadRenderersVisible(false);
            EnsureRigHandActive(_localLeftHand, "LEFT", ref _warnedLeftReactivated);
            EnsureRigHandActive(_localRightHand, "RIGHT", ref _warnedRightReactivated);
            HideRigRenderers(_localLeftHand);
            HideRigRenderers(_localRightHand);

            // Position head locally
            if (_localCamera != null && ActiveHeadIndex >= 0 && ActiveHeadIndex < headVariants.Length && headVariants[ActiveHeadIndex] != null)
            {
                ApplyHeadCenteredTransform(headVariants[ActiveHeadIndex].transform, CamPos, CamRot);
            }

            // Position local left hand visual directly to local XR rig
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

            // Position local right hand visual directly to local XR rig
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
            // Remote player logic: purely use networked variables synced over the network
            _remLPos = NetLeftHandPosition; _remLRot = NetLeftHandRotation;
            _remRPos = NetRightHandPosition; _remRRot = NetRightHandRotation;
            _haveRemote = true;

            if (ActiveHeadIndex >= 0 && ActiveHeadIndex < headVariants.Length && headVariants[ActiveHeadIndex] != null)
            {
                ApplyHeadCenteredTransform(headVariants[ActiveHeadIndex].transform, NetHeadCenterTarget, NetHeadRotation);
            }

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

            DebugLogRemoteState();
        }

        // Hide the avatar hand mesh while that hand is holding something (local = instant, remote = networked)
        bool leftHidden = HasStateAuthority ? _leftGrabCount > 0 : NetLeftHandHidden;
        bool rightHidden = HasStateAuthority ? _rightGrabCount > 0 : NetRightHandHidden;
        SetRenderersVisible(_leftHandRenderers, !leftHidden);
        SetRenderersVisible(_rightHandRenderers, !rightHidden);

        UpdatePlayerNameTag();
    }

    private Vector3 _remLPos, _remRPos;
    private Quaternion _remLRot = Quaternion.identity, _remRRot = Quaternion.identity;
    private bool _haveRemote;

    // ---- Pose cache -------------------------------------------------------------------------
    // Something in the scene (a scene-placed NetworkTransform on the XR rig, owned by Player 1)
    // can overwrite the rig hand transforms during Fusion's Render, on every client that is NOT
    // the rig's state authority. Reading the rig transform inside Render/LateUpdate then returns
    // Player 1's pose. So the real tracked pose is sampled in Update() (before Render) and
    // that cached copy is what the avatar uses.
    private Vector3 _cLPos, _cRPos, _cCamPos;
    private Quaternion _cLRot = Quaternion.identity, _cRRot = Quaternion.identity, _cCamRot = Quaternion.identity;
    private bool _haveL, _haveR, _haveCam;

    private Vector3 LPos => _haveL ? _cLPos : _localLeftHand.position;
    private Quaternion LRot => _haveL ? _cLRot : _localLeftHand.rotation;
    private Vector3 RPos => _haveR ? _cRPos : _localRightHand.position;
    private Quaternion RRot => _haveR ? _cRRot : _localRightHand.rotation;
    private Vector3 CamPos => _haveCam ? _cCamPos : _localCamera.position;
    private Quaternion CamRot => _haveCam ? _cCamRot : _localCamera.rotation;

    // Viewer camera rotation, sampled in Update() BEFORE Fusion's Render() can touch the rig.
    // Static so every avatar instance (including remote ones) shares one clean sample per frame.
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

    // The local rig must never be driven by the network - only the avatar prefab is networked.
    // Turns off any network-transform style component on the rig hand, its parents and children.
    private void DisableNetworkDriversOnRig(Transform hand)
    {
        if (hand == null) return;
        var list = new System.Collections.Generic.List<UnityEngine.Behaviour>();
        list.AddRange(hand.GetComponentsInParent<UnityEngine.Behaviour>(true));
        list.AddRange(hand.GetComponentsInChildren<UnityEngine.Behaviour>(true));
        foreach (var b in list)
        {
            if (b == null || b == this || !b.enabled) continue;
            if (b.transform.IsChildOf(transform)) continue; // never touch our own avatar
            string n = b.GetType().Name;
            if (n.Contains("NetworkTransform") || n.Contains("NetworkRigidbody"))
            {
                b.enabled = false;
                Debug.LogWarning($"[VRAvatarSync] Disabled '{n}' on LOCAL RIG object '{b.name}' - it was overwriting the rig hand pose with another player's data.");
            }
        }
    }

    // Disables anything living on THIS avatar's hand visuals that could move them on its own
    // (tracked-pose drivers, XR controllers, other network-transform scripts). This script is the
    // only thing allowed to position the avatar hands: local rig hands for the owner, networked
    // data for everyone else. Every disabled component is logged so you can see what it was.
    private void NeutralizeExternalHandDrivers()
    {
        foreach (var root in new[] { leftHandVisual, rightHandVisual })
        {
            if (root == null) continue;
            foreach (var b in root.GetComponentsInChildren<UnityEngine.Behaviour>(true))
            {
                if (b == null || b == this || !b.enabled) continue;
                string n = b.GetType().Name;
                if (n.Contains("TrackedPoseDriver") || n.Contains("ActionBasedController") ||
                    n.Contains("XRController") || n.Contains("CustomNetworkTransform") ||
                    n.Contains("NetworkTransform") || n.Contains("NetworkRigidbody"))
                {
                    b.enabled = false;
                    Debug.LogWarning($"[VRAvatarSync] Disabled '{n}' on '{b.name}' (avatar hand visual) - it was able to move the hand on its own.");
                }
            }
        }
    }

    // Re-applies the hand poses after every other script/animator has run this frame,
    // so nothing else can override them.
    private void LateUpdate()
    {
        if (Object == null || Runner == null) return;

        if (HasStateAuthority)
        {
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

    // Camera.main can point at a stale/other camera in this project, which made the name tag
    // face the wrong way on one client. Use the camera that is actually rendering to the headset/screen.
    private static Camera _viewCam;
    private static Camera GetViewCamera()
    {
        if (_viewCam != null && _viewCam.isActiveAndEnabled) return _viewCam;

        Camera best = null;
        var cams = Camera.allCameras; // enabled cameras only
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null || c.targetTexture != null) continue;
            if (c.GetComponentInParent<VRAvatarSync>(true) != null) continue;
            if (best == null || c.stereoTargetEye != StereoTargetEyeMask.None || c.CompareTag("MainCamera"))
                best = c;
            if (c.stereoTargetEye != StereoTargetEyeMask.None) { best = c; break; }
        }
        _viewCam = best != null ? best : Camera.main;
        return _viewCam;
    }

    // Re-run the name tag right before the camera renders. Fusion's Render(), NetworkTransform
    // interpolation and the XR head pose all update at different times; doing the billboard last
    // means the tag always uses the final avatar position and the final camera pose of this frame.
    private void OnEnable() { Application.onBeforeRender += OnBeforeRenderNameTag; }
    private void OnDisable() { Application.onBeforeRender -= OnBeforeRenderNameTag; }

    private void OnBeforeRenderNameTag()
    {
        if (this == null || Object == null || Runner == null) return;
        UpdatePlayerNameTag();
    }

    private void UpdatePlayerNameTag()
    {
        if (playerNameText == null || playerNameTransform == null)
            return;

        if (ActiveHeadIndex < 0 || ActiveHeadIndex >= headVariants.Length)
            return;

        GameObject activeHead = headVariants[ActiveHeadIndex];

        if (activeHead == null)
            return;

        Vector3 headTop = activeHead.transform.TransformPoint(_headPivotToTopLocal);
        playerNameTransform.position = headTop + playerNameOffset;

        if (_haveViewCamRot)
        {
            // Use the camera rotation cached in Update(); the live camera/rig can be overwritten by
            // Player 1's rig NetworkTransform during Render() on non-authority clients.
            Vector3 fwd = _viewCamRotCached * Vector3.forward;
            fwd.y = 0f;

            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = _viewCamRotCached * Vector3.up;
                fwd.y = 0f;
            }

            if (fwd.sqrMagnitude > 0.0001f)
            {
                playerNameTransform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(playerNameRotationOffset);
            }
        }

        playerNameText.text = NetworkPlayerName.ToString();
    }
}
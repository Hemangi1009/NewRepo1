using Fusion;
using UnityEngine;

public class NetworkAvatarSetup : NetworkBehaviour
{
    [Header("Local scripts to enable only for the owning client")]
    [SerializeField] private IKTargetFollowVRRig ikRig;
    [SerializeField] private AnimateOnInput animateOnInput;
    [SerializeField] private Animator animator;

    [Header("Head bone to hide from local camera (VR standard trick)")]
    [SerializeField] private Transform headBone; // e.g. mixamorig8:Head

    public override void Spawned()
    {
        bool isLocal = Object.HasStateAuthority;
        Debug.Log($"[NetworkAvatarSetup] Spawned. IsLocal={isLocal}, ObjectId={Object.Id}");

        ikRig.enabled = isLocal;

        if (isLocal)
        {
            WireLocalXROrigin();
            HideHeadForLocalPlayer();
        }
    }

    private void HideHeadForLocalPlayer()
    {
        if (headBone == null)
        {
            Debug.LogWarning("[NetworkAvatarSetup] headBone not assigned — can't hide head from local camera.");
            return;
        }

        headBone.localScale = Vector3.zero;
        Debug.Log($"[NetworkAvatarSetup] Head bone scaled to zero for local player view.");
    }

    private void WireLocalXROrigin()
    {
        var xrOrigin = LocalXROriginProvider.Instance;
        if (xrOrigin == null)
        {
            Debug.LogError("No local XR Origin found to wire avatar to.");
            return;
        }

        ikRig.head.vrTarget = xrOrigin.HeadVRTarget;
        ikRig.leftHand.vrTarget = xrOrigin.LeftHandVRTarget;
        ikRig.rightHand.vrTarget = xrOrigin.RightHandVRTarget;
    }
}
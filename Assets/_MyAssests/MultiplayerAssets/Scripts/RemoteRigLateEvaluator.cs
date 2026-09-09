using Fusion;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class RemoteRigLateEvaluator : NetworkBehaviour
{
    [SerializeField] private RigBuilder rigBuilder;

    public override void Render()
    {
        // Force rig to re-solve immediately after Fusion applies networked transforms this frame,
        // only needed for remote (non-local) avatars where jitter is visible
        if (!Object.HasStateAuthority && rigBuilder != null)
        {
            rigBuilder.enabled = true; // triggers Unity to re-evaluate in LateUpdate this frame
        }
    }
}
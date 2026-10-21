using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Uses Unity's standard XR Interaction Toolkit XRBaseInteractable (selectEntered /
// selectExited / interactorsSelecting / interactionManager). It does NOT depend on
// your NetworkedGrabbable.cs, XRPhysicsGrabbable, or NVRT.Modules.Utility - drop this
// on any grabbable that uses a normal XRGrabInteractable (or subclass of
// XRBaseInteractable) and it will work standalone.
//
// Required on the same GameObject:
//   - NetworkObject
//   - Rigidbody
//   - An XRBaseInteractable (e.g. XRGrabInteractable)
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkedGrabSync : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to auto-grab the XRBaseInteractable on this object.")]
    [SerializeField] private XRBaseInteractable interactable;

    // Networked "am I currently held" flag - handy for outlines/UI, not required by CustomNetworkTransform itself.
    [Networked] public NetworkBool IsHeld { get; private set; }

    // The kinematic state the current authority-holder actually wants (their own local rigidbody setting),
    // so remote clients can restore the correct value once authority moves to them.
    [Networked] public NetworkBool TrueKinematic { get; private set; }

    Rigidbody rb;
    bool overrodeKinematic;
    bool lastKinematicSent;
    bool waitingForAuthority;

    // Tracks the last StateAuthority we logged, so we only print when it actually changes.
    PlayerRef lastLoggedAuthority;

    public bool IsMine => Object != null && Object.HasStateAuthority;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();
    }

    public override void Spawned()
    {
        if (IsMine)
        {
            TrueKinematic = rb.isKinematic;
            lastKinematicSent = rb.isKinematic;
        }

        if (interactable != null)
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }
        else
        {
            Debug.LogWarning($"[NetworkedGrabSync] No XRBaseInteractable found on {name}. Grab events won't fire.");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Don't leave a hand hidden if this object despawns while held.
        if (interactable != null)
        {
            foreach (var i in interactable.interactorsSelecting)
                SetGrabHandHidden(i, false);
        }

        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }
    }

    void Update()
    {
        if (Runner == null || !Runner.IsRunning)
            return;

        // Authority requests in Shared mode aren't instant - pick it up once it lands.
        if (waitingForAuthority && IsMine)
        {
            waitingForAuthority = false;
            IsHeld = true;
            Debug.Log($"[NetworkedGrabSync] Authority GRANTED - Player {Runner.LocalPlayer.PlayerId} now holds '{name}'.");
        }

        // Log any time StateAuthority actually changes hands, from every client's point of view.
        if (Object.StateAuthority != lastLoggedAuthority)
        {
            lastLoggedAuthority = Object.StateAuthority;
            Debug.Log($"[NetworkedGrabSync] '{name}' StateAuthority is now Player {lastLoggedAuthority.PlayerId} (Local player = {Runner.LocalPlayer.PlayerId}, IsMine = {IsMine}).");
        }

        if (IsMine)
        {
            RestoreKinematic();

            bool kin = rb.isKinematic;
            if (kin != lastKinematicSent)
            {
                TrueKinematic = kin;
                lastKinematicSent = kin;
            }
        }
        else
        {
            // Not authority: never let local physics push this object around,
            // CustomNetworkTransform is the only thing allowed to move it.
            ForceKinematic();
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // If you use socket interactors and want grabs from sockets ignored,
        // add a check here, e.g.:
        // if (args.interactorObject is XRSocketInteractor) return;

        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            Debug.LogError($"[NetworkedGrabSync] '{name}' was grabbed but has no active NetworkObject/Runner. " +
                           "This object was never spawned through Fusion (Runner.Spawn, or a properly baked scene NetworkObject) - " +
                           "so it cannot sync or take StateAuthority. Fix the spawning setup, this script cannot work without it.");
            return;
        }

        // Hide the grabbing hand's mesh
        SetGrabHandHidden(args.interactorObject, true);

        if (!IsMine)
        {
            Debug.Log($"[NetworkedGrabSync] Player {Runner.LocalPlayer.PlayerId} grabbed '{name}' - requesting authority (currently held by Player {Object.StateAuthority.PlayerId}).");

            // Make sure whoever currently holds it visually/physically releases
            // before we take over authority (mirrors your NetworkedGrabbable RPC_ForceDrop).
            RPC_ForceRelease();

            waitingForAuthority = true;
            Object.RequestStateAuthority();
        }
        else
        {
            IsHeld = true;
        }
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        // Show the hand mesh again (always, even if the runner is gone)
        SetGrabHandHidden(args.interactorObject, false);

        if (Object == null || Runner == null || !Runner.IsRunning)
            return;

        if (IsMine)
            IsHeld = false;
    }

    void SetGrabHandHidden(IXRInteractor interactor, bool grabbing)
    {
        var avatar = VRAvatarSync.Local;
        if (avatar == null || interactor == null) return;
        avatar.SetGrabHandHidden(interactor.transform, interactor.handedness, grabbing);
    }

    void ForceKinematic()
    {
        if (rb == null || rb.isKinematic) return;
        rb.isKinematic = true;
        overrodeKinematic = true;
    }

    void RestoreKinematic()
    {
        if (!overrodeKinematic || rb == null) return;
        rb.isKinematic = TrueKinematic;
        overrodeKinematic = false;
    }

    // Tell every client to force-release this object's interactable selections
    // before authority changes hands, so the old holder's hand isn't left "stuck" selecting it.
    [Rpc(RpcSources.All, RpcTargets.All, InvokeLocal = false)]
    void RPC_ForceRelease()
    {
        if (interactable == null || !interactable.isSelected)
            return;

        var selecting = new System.Collections.Generic.List<IXRSelectInteractor>(interactable.interactorsSelecting);
        foreach (var interactor in selecting)
        {
            interactable.interactionManager.SelectExit(interactor, interactable);
        }
    }
}
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Animator))]
public class Hand : MonoBehaviour
{
    [Header("Input")]
    public InputActionProperty gripAction;
    public InputActionProperty triggerAction;

    [Header("Animation Settings")]
    public float speed = 10f;

    [Header("Optional Interactor Control")]
    [SerializeField]
    private NearFarInteractor _handInteractor;

    Animator _animator;
    private float gripTarget;
    private float triggerTarget;
    private float gripCurrent;
    private float triggerCurrent;

    private string _animatorGripParam = "Grip";
    private string _triggerGripParam = "Trigger";

    private bool _wasHandVisible = true;

    [Header("Hide Hand on Grab")]
    [SerializeField] private XRBaseInteractor interactor; // assign your XRNearFarInteractor here
    [SerializeField] private SkinnedMeshRenderer handModel; // assign your hand mesh here

    void Start()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        if (interactor != null)
        {
            interactor.selectEntered.AddListener(OnGrab);
            interactor.selectExited.AddListener(OnRelease);
        }
    }

    private void OnDisable()
    {
        if (interactor != null)
        {
            interactor.selectEntered.RemoveListener(OnGrab);
            interactor.selectExited.RemoveListener(OnRelease);
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        if (handModel != null) handModel.enabled = false; // Hide hand
        if (_animator != null) _animator.enabled = false; // Stop animator updates completely
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        // Reset animation values to current input BEFORE re-enabling
        gripCurrent = gripAction.action?.ReadValue<float>() ?? 0f;
        triggerCurrent = triggerAction.action?.ReadValue<float>() ?? 0f;

        if (_animator != null)
        {
            // Set animator parameters to current input
            _animator.SetFloat(_animatorGripParam, gripCurrent);
            _animator.SetFloat(_triggerGripParam, triggerCurrent);

            // Re-enable animator and hand mesh
            _animator.enabled = true;
        }

        if (handModel != null) handModel.enabled = true; // Show hand
    }

    void Update()
    {
        // Detect if hand just became visible (handles keyboard dismissal and any other visibility changes)
        bool isHandVisible = handModel != null && handModel.enabled;
        if (isHandVisible && !_wasHandVisible)
        {
            // Hand just became visible - reset animation to current input
            ResetHandAnimation();
        }
        _wasHandVisible = isHandVisible;

        // Only update if animator is active
        if (_animator == null || !_animator.enabled) return;

        // Read input directly
        gripTarget = gripAction.action?.ReadValue<float>() ?? 0f;
        triggerTarget = triggerAction.action?.ReadValue<float>() ?? 0f;

        AnimateHand();
    }

    private void ResetHandAnimation()
    {
        // Get current input values
        gripCurrent = gripAction.action?.ReadValue<float>() ?? 0f;
        triggerCurrent = triggerAction.action?.ReadValue<float>() ?? 0f;
        gripTarget = gripCurrent;
        triggerTarget = triggerCurrent;

        if (_animator != null)
        {
            // Set animator to match current input immediately
            _animator.SetFloat(_animatorGripParam, gripCurrent);
            _animator.SetFloat(_triggerGripParam, triggerCurrent);

            // Ensure animator is enabled
            _animator.enabled = true;
        }
    }

    void AnimateHand()
    {
        if (_handInteractor)
        {
            // Disable interaction while gripping or triggering
            //bool isSqueezing = gripTarget > 0.1f || triggerTarget > 0.1f;
            //_handInteractor.allowActivate = !isSqueezing;
            //_handInteractor.allowSelect = !isSqueezing;
        }

        if (gripCurrent != gripTarget)
        {
            gripCurrent = Mathf.MoveTowards(gripCurrent, gripTarget, Time.deltaTime * speed);
            _animator.SetFloat(_animatorGripParam, gripCurrent);
        }

        if (triggerCurrent != triggerTarget)
        {
            triggerCurrent = Mathf.MoveTowards(triggerCurrent, triggerTarget, Time.deltaTime * speed);
            _animator.SetFloat(_triggerGripParam, triggerCurrent);
        }
    }
}

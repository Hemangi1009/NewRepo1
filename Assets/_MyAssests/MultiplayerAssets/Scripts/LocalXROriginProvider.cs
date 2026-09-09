using UnityEngine;

public class LocalXROriginProvider : MonoBehaviour
{
    public static LocalXROriginProvider Instance { get; private set; }

    [SerializeField] private Transform headVRTarget;
    [SerializeField] private Transform leftHandVRTarget;
    [SerializeField] private Transform rightHandVRTarget;

    public Transform HeadVRTarget => headVRTarget;
    public Transform LeftHandVRTarget => leftHandVRTarget;
    public Transform RightHandVRTarget => rightHandVRTarget;

    private void Awake() => Instance = this;
}
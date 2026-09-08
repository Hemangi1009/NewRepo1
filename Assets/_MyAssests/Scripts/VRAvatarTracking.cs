//using UnityEngine;

//public class VRAvatarTracking : MonoBehaviour
//{
//    [Header("VR Tracking")]
//    public Transform headCamera;
//    public Transform leftController;
//    public Transform rightController;

//    [Header("Avatar Targets")]
//    public Transform headTarget;
//    public Transform leftHandTarget;
//    public Transform rightHandTarget;

//    [Header("Player Movement")]
//    public Transform xrOrigin;
//    public Animator animator;
//    public float turnSpeed = 10f;
//    public float walkThreshold = 0.1f;

//    private Vector3 lastPosition;

//    void Start()
//    {
//        if (xrOrigin != null)
//        {
//            lastPosition = xrOrigin.position;
//        }
//    }

//    void LateUpdate()
//    {
//        // HEAD
//        if (headCamera != null && headTarget != null)
//        {
//            headTarget.position = headCamera.position;
//            headTarget.rotation = headCamera.rotation;
//        }

//        // LEFT HAND
//        if (leftController != null && leftHandTarget != null)
//        {
//            leftHandTarget.position = leftController.position;
//            leftHandTarget.rotation = leftController.rotation;
//        }

//        // RIGHT HAND
//        if (rightController != null && rightHandTarget != null)
//        {
//            rightHandTarget.position = rightController.position;
//            rightHandTarget.rotation = rightController.rotation;
//        }

//        // PLAYER MOVEMENT
//        UpdateAvatarMovement();
//    }

//    void UpdateAvatarMovement()
//    {
//        if (xrOrigin == null)
//            return;

//        Vector3 currentPosition = xrOrigin.position;

//        Vector3 movement = currentPosition - lastPosition;

//        movement.y = 0f;

//        float movementAmount = movement.magnitude / Time.deltaTime;

//        // Move avatar with XR Origin
//        transform.position = new Vector3(
//            xrOrigin.position.x,
//            transform.position.y,
//            xrOrigin.position.z
//        );

//        // WALKING ANIMATION
//        if (animator != null)
//        {
//            animator.SetFloat("Speed", movementAmount);
//        }

//        // TURN AVATAR TOWARD MOVEMENT DIRECTION
//        if (movement.sqrMagnitude > walkThreshold * walkThreshold)
//        {
//            Quaternion targetRotation =
//                Quaternion.LookRotation(movement.normalized, Vector3.up);

//            transform.rotation = Quaternion.Slerp(
//                transform.rotation,
//                targetRotation,
//                turnSpeed * Time.deltaTime
//            );
//        }

//        lastPosition = currentPosition;
//    }
//}


using UnityEngine;

public class VRAvatarTracking : MonoBehaviour
{
    [Header("VR Tracking")]
    public Transform headCamera;
    public Transform leftController;
    public Transform rightController;

    [Header("Avatar Targets")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Header("Hand Rotation Offsets")]
    public Vector3 leftHandRotationOffset;
    public Vector3 rightHandRotationOffset;

    [Header("Head Offset")]
    public Vector3 headPositionOffset;
    public Vector3 headRotationOffset;

    [Header("Player Movement")]
    public Transform xrOrigin;
    public Animator animator;

    public float turnSpeed = 10f;
    public float walkThreshold = 0.1f;

    private Vector3 lastPosition;

    void Start()
    {
        if (xrOrigin != null)
        {
            lastPosition = xrOrigin.position;
        }
    }

    void LateUpdate()
    {
        UpdateHead();
        UpdateLeftHand();
        UpdateRightHand();
        UpdateAvatarMovement();
    }

    void UpdateHead()
    {
        if (headCamera == null || headTarget == null)
            return;

        headTarget.position =
            headCamera.position +
            headCamera.rotation * headPositionOffset;

        headTarget.rotation =
            headCamera.rotation *
            Quaternion.Euler(headRotationOffset);
    }

    void UpdateLeftHand()
    {
        if (leftController == null || leftHandTarget == null)
            return;

        leftHandTarget.position = leftController.position;

        leftHandTarget.rotation =
            leftController.rotation *
            Quaternion.Euler(leftHandRotationOffset);
    }

    void UpdateRightHand()
    {
        if (rightController == null || rightHandTarget == null)
            return;

        rightHandTarget.position = rightController.position;

        rightHandTarget.rotation =
            rightController.rotation *
            Quaternion.Euler(rightHandRotationOffset);
    }

    void UpdateAvatarMovement()
    {
        if (xrOrigin == null)
            return;

        Vector3 currentPosition = xrOrigin.position;

        Vector3 movement = currentPosition - lastPosition;

        // Ignore vertical headset / origin movement
        movement.y = 0f;

        float speed = 0f;

        if (Time.deltaTime > 0f)
        {
            speed = movement.magnitude / Time.deltaTime;
        }

        // Make whole avatar follow XR Origin horizontally
        transform.position = new Vector3(
            xrOrigin.position.x,
            transform.position.y,
            xrOrigin.position.z
        );

        // Walking / Idle animation
        if (animator != null)
        {
            animator.SetFloat("Speed", speed);
        }

        // Rotate entire avatar toward movement direction
        if (movement.sqrMagnitude >
            walkThreshold * walkThreshold)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    movement.normalized,
                    Vector3.up
                );

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    turnSpeed * Time.deltaTime
                );
        }

        lastPosition = currentPosition;
    }
}
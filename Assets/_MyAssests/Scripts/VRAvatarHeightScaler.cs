using UnityEngine;

public class VRAvatarHeightScaler : MonoBehaviour
{
    [Header("VR Player")]
    public Transform xrOrigin;
    public Transform headCamera;

    [Header("Avatar")]
    public Transform avatarRoot;
    public Transform avatarFeet;
    public Transform avatarHead;

    [Header("Height Settings")]
    public float minimumScale = 0.5f;
    public float maximumScale = 2.0f;

    [Header("Calibration")]
    public bool calibrateOnStart = true;

    private float originalAvatarHeight;
    private Vector3 originalAvatarScale;

    void Start()
    {
        if (avatarRoot == null)
            avatarRoot = transform;

        if (avatarFeet == null || avatarHead == null)
        {
            Debug.LogError(
                "VRAvatarHeightScaler: Please assign Avatar Feet and Avatar Head."
            );
            return;
        }

        if (xrOrigin == null || headCamera == null)
        {
            Debug.LogError(
                "VRAvatarHeightScaler: Please assign XR Origin and Head Camera."
            );
            return;
        }

        originalAvatarScale = avatarRoot.localScale;

        // Measure avatar height before scaling.
        originalAvatarHeight =
            Vector3.Distance(
                avatarFeet.position,
                avatarHead.position
            );

        if (calibrateOnStart)
        {
            CalibrateHeight();
        }
    }

    public void CalibrateHeight()
    {
        if (xrOrigin == null ||
            headCamera == null ||
            avatarRoot == null ||
            avatarFeet == null ||
            avatarHead == null)
        {
            Debug.LogWarning(
                "VRAvatarHeightScaler: Missing references."
            );
            return;
        }

        // Player's actual head height above the XR Origin/floor.
        float playerHeight =
            headCamera.position.y -
            xrOrigin.position.y;

        if (playerHeight <= 0f)
        {
            Debug.LogWarning(
                "VRAvatarHeightScaler: Player height is invalid."
            );
            return;
        }

        // Calculate required scale.
        float scaleMultiplier =
            playerHeight / originalAvatarHeight;

        scaleMultiplier =
            Mathf.Clamp(
                scaleMultiplier,
                minimumScale,
                maximumScale
            );

        // Apply uniform scale.
        avatarRoot.localScale =
            originalAvatarScale * scaleMultiplier;

        // Keep avatar's feet at the XR Origin/floor.
        Vector3 feetPosition = avatarFeet.position;

        Vector3 positionDifference =
            xrOrigin.position - feetPosition;

        positionDifference.y = 0f;

        avatarRoot.position += positionDifference;

        Debug.Log(
            "VR Avatar Height Calibration\n" +
            "Player Height: " +
            playerHeight.ToString("F2") +
            " m\n" +
            "Original Avatar Height: " +
            originalAvatarHeight.ToString("F2") +
            " m\n" +
            "Avatar Scale: " +
            scaleMultiplier.ToString("F2")
        );
    }

    public void ResetAvatarScale()
    {
        if (avatarRoot != null)
        {
            avatarRoot.localScale = originalAvatarScale;
        }
    }
}
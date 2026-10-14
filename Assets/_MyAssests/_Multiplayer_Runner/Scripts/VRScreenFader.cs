using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the XR Origin. Creates a black quad right in front of the XR camera and fades it.
/// Local only - each player fades only their own screen.
/// </summary>
public class VRScreenFader : MonoBehaviour
{
    public static VRScreenFader Instance { get; private set; }

    [Tooltip("Leave empty to use Camera.main (your XR camera must be tagged MainCamera).")]
    [SerializeField] private Camera xrCamera;
    [SerializeField] private float distanceFromCamera = 0.1f;
    [SerializeField] private Color fadeColor = Color.black;

    private Renderer _renderer;
    private Material _material;
    private float _alpha;

    private void Awake() => Instance = this;

    private bool EnsureQuad()
    {
        if (_renderer != null) return true;

        Camera cam = xrCamera != null ? xrCamera : Camera.main;
        if (cam == null) return false;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "ScreenFadeQuad";
        Destroy(quad.GetComponent<Collider>());

        quad.transform.SetParent(cam.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, distanceFromCamera);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(2f, 2f, 1f);

        _material = new Material(Shader.Find("Sprites/Default"));
        _material.renderQueue = 4000;

        _renderer = quad.GetComponent<Renderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        SetAlpha(0f);
        return true;
    }

    public void SetAlpha(float a)
    {
        if (!EnsureQuad()) return;

        _alpha = Mathf.Clamp01(a);
        Color c = fadeColor;
        c.a = _alpha;
        _material.color = c;
        _renderer.enabled = _alpha > 0.001f; // no cost while fully clear
    }

    public IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (!EnsureQuad()) yield break;

        float start = _alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetAlpha(Mathf.Lerp(start, targetAlpha, t / duration));
            yield return null;
        }
        SetAlpha(targetAlpha);
    }
}

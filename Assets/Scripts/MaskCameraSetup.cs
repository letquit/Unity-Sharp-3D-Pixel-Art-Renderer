using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class MaskCameraSetup : MonoBehaviour
{
    public LayerMask outlineLayerMask;
    public Shader maskShader;

    Camera _cam;

    void OnEnable()
    {
        Apply();
    }

    void OnValidate()
    {
        Apply();
    }

    void Apply()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) return;

        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0, 0, 0, 0);
        _cam.cullingMask = outlineLayerMask;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;

        if (maskShader == null)
            maskShader = Shader.Find("Hidden/PixelArtMaskWrite");

        if (maskShader != null)
            _cam.SetReplacementShader(maskShader, "");
    }

    void OnDisable()
    {
        if (_cam != null)
            _cam.ResetReplacementShader();
    }
}
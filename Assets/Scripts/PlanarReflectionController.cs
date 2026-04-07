using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class PlanarReflectionController : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public Camera reflectionCamera;
    public Transform waterPlane;
    public RenderTexture reflectionRT;
    public Material waterMaterial;

    [Header("Settings")]
    public float clipPlaneOffset = 0.05f;

    public bool renderInPlayOnly = true;

    public LayerMask reflectionCullingMask = ~0;

    public bool distanceCulling = false;

    public float maxRenderDistance = 200f;

    static readonly int WaterReflectionTexID = Shader.PropertyToID("_WaterReflectionTex");

    void LateUpdate()
    {
        if (renderInPlayOnly && !Application.isPlaying) return;
        if (!IsValid()) return;

        if (distanceCulling)
        {
            float dist = Mathf.Abs(mainCamera.transform.position.y - waterPlane.position.y);
            if (dist > maxRenderDistance) return;
        }

        RenderReflection();
    }

    bool IsValid()
    {
        return mainCamera != null &&
               reflectionCamera != null &&
               waterPlane != null &&
               reflectionRT != null &&
               waterMaterial != null;
    }

    void RenderReflection()
    {
        Vector3 planeNormal = waterPlane.up.normalized;
        Vector3 planePoint = waterPlane.position;

        float d = -Vector3.Dot(planeNormal, planePoint);
        Vector4 plane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, d);

        Matrix4x4 reflectionMat = CalculateReflectionMatrix(plane);

        reflectionCamera.CopyFrom(mainCamera);

        var addData = reflectionCamera.GetUniversalAdditionalCameraData();
        if (addData != null)
        {
            addData.renderPostProcessing = false;
            addData.requiresColorOption = CameraOverrideOption.Off;
            addData.requiresDepthOption = CameraOverrideOption.Off;
        }

        reflectionCamera.cullingMask = reflectionCullingMask;
        reflectionCamera.targetTexture = reflectionRT;
        reflectionCamera.useOcclusionCulling = false;
        reflectionCamera.allowHDR = false;
        reflectionCamera.allowMSAA = false;

        Vector3 camPos = mainCamera.transform.position;
        Vector3 reflPos = reflectionMat.MultiplyPoint(camPos);

        Vector3 camFwd = mainCamera.transform.forward;
        Vector3 camUp = mainCamera.transform.up;
        Vector3 reflFwd = reflectionMat.MultiplyVector(camFwd);
        Vector3 reflUp = reflectionMat.MultiplyVector(camUp);

        reflectionCamera.transform.SetPositionAndRotation(
            reflPos,
            Quaternion.LookRotation(reflFwd, reflUp)
        );

        Vector4 clipPlaneCS = CameraSpacePlane(
            reflectionCamera,
            planePoint,
            planeNormal,
            1.0f,
            clipPlaneOffset
        );

        if (mainCamera.orthographic)
        {
            reflectionCamera.orthographic = true;
            reflectionCamera.orthographicSize = mainCamera.orthographicSize;
        }
        else
        {
            reflectionCamera.orthographic = false;
            reflectionCamera.fieldOfView = mainCamera.fieldOfView;
        }

        reflectionCamera.projectionMatrix = mainCamera.CalculateObliqueMatrix(clipPlaneCS);

        GL.invertCulling = true;
        reflectionCamera.Render();
        GL.invertCulling = false;

        waterMaterial.SetTexture(WaterReflectionTexID, reflectionRT);
    }

    static Matrix4x4 CalculateReflectionMatrix(Vector4 p)
    {
        Matrix4x4 m = Matrix4x4.identity;

        m.m00 = 1f - 2f * p.x * p.x;
        m.m01 = -2f * p.x * p.y;
        m.m02 = -2f * p.x * p.z;
        m.m03 = -2f * p.x * p.w;

        m.m10 = -2f * p.y * p.x;
        m.m11 = 1f - 2f * p.y * p.y;
        m.m12 = -2f * p.y * p.z;
        m.m13 = -2f * p.y * p.w;

        m.m20 = -2f * p.z * p.x;
        m.m21 = -2f * p.z * p.y;
        m.m22 = 1f - 2f * p.z * p.z;
        m.m23 = -2f * p.z * p.w;

        return m;
    }

    static Vector4 CameraSpacePlane(
        Camera cam,
        Vector3 pointWS,
        Vector3 normalWS,
        float sideSign,
        float clipOffset
    )
    {
        Vector3 offsetPos = pointWS + normalWS * clipOffset;

        Matrix4x4 worldToCam = cam.worldToCameraMatrix;
        Vector3 pointCS = worldToCam.MultiplyPoint(offsetPos);
        Vector3 normalCS = worldToCam.MultiplyVector(normalWS).normalized * sideSign;

        return new Vector4(
            normalCS.x,
            normalCS.y,
            normalCS.z,
            -Vector3.Dot(pointCS, normalCS)
        );
    }
}
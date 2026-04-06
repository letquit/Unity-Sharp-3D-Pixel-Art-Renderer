using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class MaskCameraFollower : MonoBehaviour
{
    public Camera sourceCamera;

    private void LateUpdate()
    {
        if (sourceCamera == null) return;
        var dst = GetComponent<Camera>();
        if (dst == null) return;

        transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);

        dst.orthographic = sourceCamera.orthographic;
        dst.fieldOfView = sourceCamera.fieldOfView;
        dst.orthographicSize = sourceCamera.orthographicSize;
        dst.nearClipPlane = sourceCamera.nearClipPlane;
        dst.farClipPlane = sourceCamera.farClipPlane;
    }
}
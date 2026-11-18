using UnityEngine;
using UnityEngine.Rendering;

namespace WorldMapRendering
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BuiltinCameraDepth : MonoBehaviour
    {
        private void OnEnable() => Camera.onPreCull += RequestDepth;
        private void OnDisable() => Camera.onPreCull -= RequestDepth;

        private void RequestDepth(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                || (camera.cullingMask & (1 << gameObject.layer)) == 0)
                return;

            camera.depthTextureMode |= DepthTextureMode.Depth;
        }
    }
}

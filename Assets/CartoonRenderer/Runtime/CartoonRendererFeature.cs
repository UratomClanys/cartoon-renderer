using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    [DisallowMultipleRendererFeature("Cartoon Projection Renderer")]
    public sealed class CartoonRendererFeature : ScriptableRendererFeature
    {
        public CartoonRenderSettings settings = new();

        private Material captureMaterial;
        private Material paintHistoryMaterial;
        private Material simplifyMaterial;
        private Material lightingMaterial;
        private Material outlineMaterial;
        private Material compositeMaterial;
        private CartoonCapturePass capturePass;
        private CartoonPaintHistoryPass paintHistoryPass;
        private CartoonSimplifyPass simplifyPass;
        private CartoonLightingPass lightingPass;
        private CartoonOutlinePass outlinePass;
        private CartoonCompositePass compositePass;
        private CartoonPaintHistory paintHistory;
        private ProjectedShapePass projectedPass;

        public override void Create()
        {
            DisposeMaterials();
            projectedPass?.Dispose();
            projectedPass = new ProjectedShapePass();
            paintHistory?.Dispose();
            captureMaterial = CreateMaterial("Hidden/CartoonProjection/Capture");
            paintHistoryMaterial = CreateMaterial("Hidden/CartoonProjection/PaintHistory");
            simplifyMaterial = CreateMaterial("Hidden/CartoonProjection/Simplify");
            lightingMaterial = CreateMaterial("Hidden/CartoonProjection/Lighting");
            outlineMaterial = CreateMaterial("Hidden/CartoonProjection/Outline");
            compositeMaterial = CreateMaterial("Hidden/CartoonProjection/Composite");
            if (captureMaterial == null || paintHistoryMaterial == null || simplifyMaterial == null ||
                lightingMaterial == null || outlineMaterial == null || compositeMaterial == null)
                return;
            paintHistory = new CartoonPaintHistory();
            capturePass = new CartoonCapturePass(captureMaterial);
            capturePass.SetPaintHistory(paintHistory);
            paintHistoryPass = new CartoonPaintHistoryPass(paintHistoryMaterial);
            simplifyPass = new CartoonSimplifyPass(simplifyMaterial);
            lightingPass = new CartoonLightingPass(lightingMaterial);
            outlinePass = new CartoonOutlinePass(outlineMaterial);
            compositePass = new CartoonCompositePass(compositeMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.enabled || capturePass == null || renderingData.cameraData.cameraType == CameraType.Preview)
                return;
            if (settings.projectedShapes)
            {
                projectedPass.Setup(settings);
                renderer.EnqueuePass(projectedPass);
                return;
            }
            capturePass.Setup(settings);
            simplifyPass.Setup(settings);
            lightingPass.Setup(settings);
            outlinePass.Setup(settings);
            compositePass.Setup(settings);
            renderer.EnqueuePass(capturePass);
            renderer.EnqueuePass(paintHistoryPass);
            renderer.EnqueuePass(simplifyPass);
            renderer.EnqueuePass(lightingPass);
            renderer.EnqueuePass(outlinePass);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            DisposeMaterials();
            projectedPass?.Dispose();
            projectedPass = null;
            paintHistory?.Dispose();
            paintHistory = null;
        }

        private static Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Cartoon Projection shader not found: {shaderName}");
                return null;
            }
            return CoreUtils.CreateEngineMaterial(shader);
        }

        private void DisposeMaterials()
        {
            CoreUtils.Destroy(captureMaterial);
            CoreUtils.Destroy(paintHistoryMaterial);
            CoreUtils.Destroy(simplifyMaterial);
            CoreUtils.Destroy(lightingMaterial);
            CoreUtils.Destroy(outlineMaterial);
            CoreUtils.Destroy(compositeMaterial);
        }
    }
}

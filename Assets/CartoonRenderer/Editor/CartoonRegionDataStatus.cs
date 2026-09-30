using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection.Editor
{
    [InitializeOnLoad]
    public static class CartoonRegionDataStatus
    {
        private const string RendererAssetPath = "Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset";
        private static bool warnedThisSession;

        static CartoonRegionDataStatus()
        {
            EditorApplication.playModeStateChanged += _ =>
            {
                warnedThisSession = false;
                WarnIfMaterialColorBlocksWithoutData();
            };
        }

        [MenuItem("Tools/Cartoon Projection/Report Region Data Status")]
        public static void Report()
        {
            var report = new StringBuilder();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            report.AppendLine($"Pipeline: {(pipeline != null ? pipeline.name : "<built-in>")}");

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (rendererData == null)
            {
                report.AppendLine($"Renderer asset not found at {RendererAssetPath}");
            }
            else
            {
                report.AppendLine($"Renderer data: {rendererData.name}");
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature is CartoonRendererFeature cartoon)
                        report.AppendLine(
                            $"Feature '{feature.name}': enabled={cartoon.settings.enabled}, " +
                            $"surfaceMode={cartoon.settings.surfaceMode}, " +
                            $"paintLayerMode={cartoon.settings.paintLayerMode}, " +
                            $"renderScale={cartoon.settings.renderScale:0.00}");
                }
            }

            int baked = 0;
            int fallback = 0;
            int missing = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer))
                    continue;
                if (renderer.GetComponent<CartoonColorRegionBinding>() != null)
                    baked++;
                else if (renderer.GetComponent<CartoonShapePart>() != null)
                    fallback++;
                else
                    missing++;
            }
            report.AppendLine($"Renderers with baked region data: {baked}");
            report.AppendLine($"Renderers falling back to CartoonShapePart colors: {fallback}");
            report.AppendLine($"Renderers with NO cartoon data (neutral fallback in MaterialColorBlocks): {missing}");

            if (pipeline == null)
                report.AppendLine("NOTE: the Cartoon pipeline asset is not the active render pipeline.");
            if (missing > 0)
                report.AppendLine("WARNING: renderers without data render flat neutral colors in MaterialColorBlocks mode; bake regions first or switch surfaceMode.");

            Debug.Log(report.ToString());
        }

        // One-shot warning so a MaterialColorBlocks session with zero baked renderers
        // never silently looks like a working feature.
        public static void WarnIfMaterialColorBlocksWithoutData()
        {
            if (warnedThisSession)
                return;
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (rendererData == null)
                return;
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is not CartoonRendererFeature cartoon ||
                    !cartoon.settings.enabled ||
                    cartoon.settings.surfaceMode != CartoonSurfaceMode.MaterialColorBlocks ||
                    !cartoon.settings.enableMaterialRegions)
                    continue;

                int baked = 0;
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (renderer is (MeshRenderer or SkinnedMeshRenderer) &&
                        renderer.GetComponent<CartoonColorRegionBinding>() != null)
                        baked++;
                }
                if (baked == 0)
                {
                    warnedThisSession = true;
                    Debug.LogWarning(
                        "[CartoonProjection] SurfaceMode is MaterialColorBlocks but no renderer has baked region data. " +
                        "All geometry falls back to CartoonShapePart fill colors or neutral gray. " +
                        "Run Tools > Cartoon Projection > Material Color Region Baker, or see Tools > Report Region Data Status.");
                }
                break;
            }
        }

        public static void ResetSessionWarning() => warnedThisSession = false;
    }
}

using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    [CustomEditor(typeof(CartoonColorRegionAsset))]
    public sealed class CartoonColorRegionAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (CartoonColorRegionAsset)target;
            EditorGUILayout.LabelField("Source Root", asset.sourceRootName);
            EditorGUILayout.LabelField("Bake Version", asset.bakeVersion.ToString());
            EditorGUILayout.LabelField("Baked At", asset.bakedAt);
            EditorGUILayout.LabelField("Dependency Hash", string.IsNullOrEmpty(asset.dependencyHash) ? "-" : asset.dependencyHash[..12]);
            EditorGUILayout.LabelField("Global Merge Multiplier", asset.globalColorMergeMultiplier.ToString("0.00"));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Skinned:", asset.hasSkinnedMeshes ? "yes" : "no");
                EditorGUILayout.LabelField("BlendShapes:", asset.hasBlendShapes ? "yes" : "no");
                EditorGUILayout.LabelField("Alpha:", asset.hasAlphaMaterials ? "yes" : "no");
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Meshes ({asset.meshes.Count})", EditorStyles.boldLabel);
            foreach (var mesh in asset.meshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(mesh.rendererName, $"{mesh.regionCount} regions");
                    if (mesh.generatedMesh != null && GUILayout.Button("Ping", GUILayout.Width(44)))
                        EditorGUIUtility.PingObject(mesh.generatedMesh);
                    EditorGUILayout.LabelField($"{mesh.sourceVertexCount}->{mesh.generatedVertexCount}v", GUILayout.Width(120));
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"Regions ({asset.regions.Count})", EditorStyles.boldLabel);
            foreach (var region in asset.regions)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = EditorGUILayout.GetControlRect(false, 16f, GUILayout.Width(24f));
                    EditorGUI.DrawRect(rect, region.color);
                    EditorGUILayout.LabelField($"#{region.regionId}", GUILayout.Width(40));
                    EditorGUILayout.LabelField(region.materialName);
                    EditorGUILayout.LabelField(region.policy.ToString(), GUILayout.Width(80));
                    EditorGUILayout.LabelField($"{region.triangleCount} tris", GUILayout.Width(70));
                    if (region.protectedRegion)
                        EditorGUILayout.LabelField("protected", EditorStyles.miniLabel, GUILayout.Width(64));
                }
            }

            if (asset.warnings is { Count: > 0 })
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Bake Warnings", EditorStyles.boldLabel);
                foreach (var warning in asset.warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }
    }
}

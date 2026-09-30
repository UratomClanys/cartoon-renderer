using System.Text;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Replaces the default renderer-feature inspector so surfaceMode drives the UI while
    // the legacy preserveSourceMaterials field stays serialized but hidden.
    [CustomEditor(typeof(CartoonRendererFeature))]
    public sealed class CartoonRendererFeatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = serializedObject.FindProperty("settings");
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Settings missing.", MessageType.Error);
                return;
            }

            var it = settings.Copy();
            var end = settings.GetEndProperty();
            bool enterChildren = true;
            bool projected = settings.FindPropertyRelative("projectedShapes").boolValue;
            while (it.Next(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                if (it.name == "preserveSourceMaterials")
                    continue; // legacy v1 field, superseded by surfaceMode
                if(projected && it.name != "enabled" && it.name != "projectedShapes" && !it.name.StartsWith("projection") &&
                   it.name != "backgroundMode" && it.name != "backgroundColor") continue;
                EditorGUILayout.PropertyField(it, includeChildren: true);
            }

            serializedObject.ApplyModifiedProperties();
            if(projected)
            {
                EditorGUILayout.HelpBox("Original albedo → color regions → shared simplified contours → 2D polygon redraw. No region baking required. Legacy lighting and outline controls are bypassed.\n" + ProjectedShapePass.LastStatistics, MessageType.Info);
                return;
            }
            DrawMissingRegionDataWarning();
        }

        private void DrawMissingRegionDataWarning()
        {
            if (serializedObject.targetObject is not CartoonRendererFeature feature ||
                feature.settings.surfaceMode != CartoonSurfaceMode.MaterialColorBlocks)
                return;

            var missing = new StringBuilder();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                // Explicit shape parts without a region binding fall back to fillColor.
                if (renderer.GetComponent<CartoonShapePart>() == null ||
                    renderer.GetComponent<CartoonColorRegionBinding>() != null)
                    continue;
                missing.AppendLine(renderer.name);
                if (missing.Length > 400)
                    break;
            }

            if (missing.Length > 0)
                EditorGUILayout.HelpBox(
                    "MaterialColorBlocks is active but these renderers have no baked region data and will fall back to CartoonShapePart colors:\n" + missing,
                    MessageType.Warning);
        }
    }
}

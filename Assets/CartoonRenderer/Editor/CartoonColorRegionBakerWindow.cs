using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonColorRegionBakerWindow : EditorWindow
    {
        private GameObject root;
        private float globalMerge = 1f;
        private List<CartoonColorRegionBaker.RendererReport> reports;
        private List<CartoonMaterialRegionRule> rules;
        private CartoonColorRegionBaker.BakeResult preview;
        private Vector2 scroll;
        private readonly Dictionary<string, bool> ruleFoldouts = new();

        [MenuItem("Tools/Cartoon Projection/Material Color Region Baker")]
        public static void Open()
        {
            var window = GetWindow<CartoonColorRegionBakerWindow>("Color Region Baker");
            window.minSize = new Vector2(420f, 480f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Bakes material textures into stable per-triangle color regions, then writes color-block meshes, " +
                "a region asset and a prefab variant under Assets/CartoonRenderer/Generated/. Source FBX, materials " +
                "and prefabs are never modified.", MessageType.Info);

            root = (GameObject)EditorGUILayout.ObjectField("Character Root", root, typeof(GameObject), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan Renderers"))
                    Scan();
                using (new EditorGUI.DisabledScope(reports == null || reports.Count == 0))
                    if (GUILayout.Button("Refresh Suggestions"))
                        rules = CartoonColorRegionBaker.BuildDefaultRules(reports);
            }

            if (reports == null)
                return;
            if (reports.Count == 0)
            {
                EditorGUILayout.HelpBox("No MeshRenderer or SkinnedMeshRenderer found under the root.", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            globalMerge = EditorGUILayout.Slider("Global Merge Multiplier", globalMerge, 0.25f, 4f);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Renderers: {reports.Count}   Materials/Rules: {rules?.Count ?? 0}", EditorStyles.boldLabel);

            if (rules != null)
                DrawRules();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(rules == null))
                    if (GUILayout.Button("Bake Preview"))
                        Bake(writeAssets: false);
                using (new EditorGUI.DisabledScope(rules == null || preview == null))
                    if (GUILayout.Button("Bake Assets"))
                        Bake(writeAssets: true);
            }

            if (preview != null)
                DrawPreview();
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            if (root == null)
            {
                EditorUtility.DisplayDialog("Color Region Baker", "Assign a character root first.", "OK");
                return;
            }
            reports = CartoonColorRegionBaker.CollectRenderers(root);
            rules = CartoonColorRegionBaker.BuildDefaultRules(reports);
            preview = null;
            ruleFoldouts.Clear();
            foreach (var rule in rules)
                ruleFoldouts[rule.materialGuid] = false;
        }

        private void DrawRules()
        {
            foreach (var rule in rules)
            {
                bool open = ruleFoldouts.TryGetValue(rule.materialGuid, out bool value) && value;
                open = EditorGUILayout.Foldout(open, rule.materialName, true);
                ruleFoldouts[rule.materialGuid] = open;
                if (!open)
                    continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    var policy = (CartoonRegionPolicy)EditorGUILayout.EnumPopup("Policy", rule.policy);
                    if (policy != rule.policy)
                    {
                        rule.ApplyPolicyDefaults(policy);
                        GUIFocusClear();
                    }
                    rule.maxRegions = EditorGUILayout.IntSlider("Max Regions", rule.maxRegions, 1, 32);
                    rule.colorMergeScale = EditorGUILayout.Slider("Merge Strength", rule.colorMergeScale, 0.1f, 4f);
                    rule.minRegionTriangles = EditorGUILayout.IntSlider("Min Region Triangles", rule.minRegionTriangles, 0, 256);
                    rule.minRegionAreaRatio = EditorGUILayout.Slider("Min Area Ratio", rule.minRegionAreaRatio, 0f, 0.1f);
                    rule.preserveAlphaCutout = EditorGUILayout.Toggle("Alpha Cutout", rule.preserveAlphaCutout);
                    rule.fixedBaseLayer = EditorGUILayout.Toggle("Fixed Base Layer",
                        rule.fixedBaseLayer,
                        new GUIStyle(EditorStyles.toggle) { wordWrap = true });
                    EditorGUILayout.LabelField("  Painted-shadow materials stay in the base paint layer.",
                        EditorStyles.miniLabel);
                    rule.allowDiscreteLighting = EditorGUILayout.Toggle("Discrete Lighting", rule.allowDiscreteLighting);
                    rule.allowInternalOutline = EditorGUILayout.Toggle("Internal Outline", rule.allowInternalOutline);
                    rule.useColorOverride = EditorGUILayout.Toggle("Single Color Override", rule.useColorOverride);
                    if (rule.useColorOverride)
                        rule.colorOverride = EditorGUILayout.ColorField("Override Color", rule.colorOverride);
                    if (rule.manualPalette is { Length: > 0 })
                        for (int i = 0; i < rule.manualPalette.Length; i++)
                            rule.manualPalette[i] = EditorGUILayout.ColorField($"Palette {i}", rule.manualPalette[i]);
                    int paletteSize = EditorGUILayout.IntField("Palette Size", rule.manualPalette?.Length ?? 0);
                    if (rule.manualPalette == null || rule.manualPalette.Length != Mathf.Max(0, paletteSize))
                        rule.manualPalette = new Color[Mathf.Max(0, paletteSize)];
                }
            }
        }

        private void Bake(bool writeAssets)
        {
            if (root == null || rules == null)
                return;
            if (writeAssets && !EditorUtility.DisplayDialog("Bake Color Region Assets",
                    $"Write generated meshes, region asset and prefab variant under Assets/CartoonRenderer/Generated/{root.name}/?\n\n" +
                    "Source assets are not modified.", "Bake", "Cancel"))
                return;

            try
            {
                EditorUtility.DisplayProgressBar("Color Region Baker", "Baking color regions...", 0.5f);
                preview = CartoonColorRegionBaker.Bake(root, reports, rules, globalMerge, writeAssets);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (writeAssets && preview.output != null)
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(preview.output.prefabPath));
                Debug.Log($"[ColorRegionBaker] Wrote {preview.output.prefabPath} and {preview.output.rootFolder}");
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            foreach (var warning in preview.warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (preview.asset != null && !string.IsNullOrEmpty(preview.asset.dependencyHash))
                EditorGUILayout.LabelField("Dependency Hash", preview.asset.dependencyHash[..12]);

            foreach (var report in preview.reports)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string growth = report.build != null
                        ? $"{report.build.sourceVertexCount} -> {report.build.sourceVertexCount + report.build.vertexGrowth} verts"
                        : "no build";
                    EditorGUILayout.LabelField($"{report.name} ({report.path})", $"{report.regionCount} regions, {growth}");
                    if (report.build == null)
                        continue;
                    const int swatchSize = 22;
                    int perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - 40f) / (swatchSize + 4f)));
                    int drawn = 0;
                    Rect rowRect = EditorGUILayout.GetControlRect(false, swatchSize + 4f);
                    foreach (var region in report.build.regions)
                    {
                        if (drawn % perRow == 0)
                            rowRect = EditorGUILayout.GetControlRect(false, swatchSize + 4f);
                        var swatch = new Rect(rowRect.x + (drawn % perRow) * (swatchSize + 4f), rowRect.y, swatchSize, swatchSize);
                        EditorGUI.DrawRect(swatch, region.color);
                        EditorGUI.LabelField(swatch, new GUIContent("", $"ID {region.regionId}, {region.triangleCount} tris, {region.policy}"));
                        drawn++;
                    }
                    foreach (var warning in report.warnings)
                        EditorGUILayout.LabelField("  ! " + warning, EditorStyles.miniLabel);
                }
            }
        }

        private static void GUIFocusClear() => GUIUtility.keyboardControl = 0;
    }
}

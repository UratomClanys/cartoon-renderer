using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Offline, single-frame visual prototype of "3D scene projection -> 2D shapes ->
    // redraw". Renders a programmatic tower with a software rasterizer, extracts
    // connected shapes and contours, simplifies them with shared-boundary chains, and
    // redraws the scene from the simplified 2D polygons. NOT the runtime path.
    public sealed class CartoonShapePrototypeWindow : EditorWindow
    {
        private int resolutionWidth = 720;
        private int resolutionHeight = 540;
        private float simplifyEpsilon = 2f;
        private float minLoopArea = 24f;
        private bool lastRunOk;

        [MenuItem("Tools/Cartoon Projection/Shape Prototype (Offline)")]
        public static void Open()
        {
            var window = GetWindow<CartoonShapePrototypeWindow>("Shape Prototype");
            window.minSize = new Vector2(360f, 260f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Offline single-frame prototype (visual validation only, not the runtime path):\n" +
                "software-rasterizes a programmatic tower (3 regions, light/dark contrast, a real see-through arch hole) " +
                "into visibility buffers, extracts connected shapes and contours, simplifies with shared chains, " +
                "and redraws the scene from the 2D polygons. Four stage images are written to Generated/ShapePrototype/.",
                MessageType.Info);

            resolutionWidth = Mathf.Max(160, EditorGUILayout.IntField("Width", resolutionWidth));
            resolutionHeight = Mathf.Max(120, EditorGUILayout.IntField("Height", resolutionHeight));
            simplifyEpsilon = EditorGUILayout.Slider("Simplify Epsilon (px)", simplifyEpsilon, 0f, 12f);
            minLoopArea = EditorGUILayout.Slider("Min Loop Area (px^2)", minLoopArea, 0f, 512f);

            if (GUILayout.Button("Run Prototype"))
                Run();

            if (lastRunOk)
                EditorGUILayout.HelpBox("Last run completed. Check Generated/ShapePrototype/ for the four stages.", MessageType.None);
        }

        private void Run()
        {
            CartoonShapePrototypeSettings.SimplifyEpsilon = simplifyEpsilon;
            CartoonShapePrototypeSettings.MinLoopAreaPixels = minLoopArea;

            var outputFolder = "Assets/CartoonRenderer/Generated/ShapePrototype";
            EnsureFolder(outputFolder);

            // Stage 0: scene + camera (identical parameters for all stages).
            var scene = PrototypeScene.CreateTower();
            var cameraPosition = new Vector3(3.4f, 2.9f, -4.6f);
            var target = new Vector3(0f, 1.5f, 0f);
            var forward = (target - cameraPosition).normalized;
            var up = Vector3.up;

            EditorUtility.DisplayProgressBar("Shape Prototype", "Rasterizing visibility buffers...", 0.15f);
            var buffer = CartoonVisibilityRasterizer.Rasterize(scene, cameraPosition, forward, up, 40f,
                resolutionWidth, resolutionHeight);

            EditorUtility.DisplayProgressBar("Shape Prototype", "Extracting connected shapes...", 0.4f);
            var labelBuffer = new int[resolutionWidth * resolutionHeight];
            var shapes = CartoonShapeExtractor.Extract(buffer, labelBuffer);

            EditorUtility.DisplayProgressBar("Shape Prototype", "Simplifying contours...", 0.65f);
            // (simplification happens inside extraction via the chain registry)

            EditorUtility.DisplayProgressBar("Shape Prototype", "Redrawing from 2D polygons...", 0.85f);
            var redrawn = CartoonShapeRedrawer.Redraw(shapes, resolutionWidth, resolutionHeight,
                new Color32(238, 240, 242, 255));
            var wireframe = CartoonShapeRedrawer.DrawWireframe(shapes, resolutionWidth, resolutionHeight);

            EditorUtility.DisplayProgressBar("Shape Prototype", "Writing stage images...", 0.95f);
            int regionSourceCorners = 0;
            int regionSimplifiedCorners = 0;
            foreach (var shape in shapes)
            {
                regionSourceCorners += Sum(shape.loopSourceCornerCounts);
                regionSimplifiedCorners += Sum(shape.loops, loop => loop.Count);
            }

            SavePng($"{outputFolder}/Stage1_Original.png", buffer.smoothShaded, resolutionWidth, resolutionHeight);
            SavePng($"{outputFolder}/Stage2_RegionMask.png", buffer.regionColor, resolutionWidth, resolutionHeight);
            SavePng($"{outputFolder}/Stage3_SimplifiedContours.png", wireframe, resolutionWidth, resolutionHeight);
            SavePng($"{outputFolder}/Stage4_Redrawn.png", redrawn, resolutionWidth, resolutionHeight);
            SaveOverview(outputFolder, buffer, wireframe, redrawn, resolutionWidth, resolutionHeight);

            EditorUtility.ClearProgressBar();
            lastRunOk = true;

            Debug.Log(
                "[ShapePrototype] Stages written to " + outputFolder + "\n" +
                $"components: {shapes.Count}, source contour corners: {regionSourceCorners}, " +
                $"simplified corners: {regionSimplifiedCorners} " +
                $"({(regionSourceCorners > 0 ? 100f * regionSimplifiedCorners / regionSourceCorners : 0f):F1}% kept, epsilon {simplifyEpsilon}px)\n" +
                $"loops: {Sum(shapes, s => s.loops.Count)} (holes: {Sum(shapes, s => s.loopIsHole.FindAll(h => h).Count)})");
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>($"{outputFolder}/Overview.png"));
        }

        private static int Sum(List<int> list)
        {
            int total = 0;
            foreach (int value in list)
                total += value;
            return total;
        }

        private static int Sum<T>(List<T> list, Func<T, int> selector)
        {
            int total = 0;
            foreach (var item in list)
                total += selector(item);
            return total;
        }

        private static void SavePng(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void SaveOverview(string folder, VisibilityBuffer buffer, Color32[] wireframe,
            Color32[] redrawn, int width, int height)
        {
            var canvas = new Color32[width * 2 * height * 2];
            Blit(canvas, width * 2, buffer.smoothShaded, width, height, 0, 0);
            Blit(canvas, width * 2, buffer.regionColor, width, height, width, 0);
            Blit(canvas, width * 2, wireframe, width, height, 0, height);
            Blit(canvas, width * 2, redrawn, width, height, width, height);
            SavePng($"{folder}/Overview.png", canvas, width * 2, height * 2);
        }

        private static void Blit(Color32[] target, int targetWidth, Color32[] source, int width, int height,
            int offsetX, int offsetY)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    target[(y + offsetY) * targetWidth + x + offsetX] = source[y * width + x];
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CartoonProjection.Editor
{
    public static class CartoonRendererProjectSetup
    {
        private const string Root = "Assets/CartoonRenderer";
        private const string SettingsFolder = Root + "/Settings";
        private const string MaterialsFolder = Root + "/TestMaterials";
        private const string RendererPath = SettingsFolder + "/CartoonUniversalRenderer.asset";
        private const string PipelinePath = SettingsFolder + "/CartoonUniversalRenderPipeline.asset";
        private const string ScenePath = "Assets/Scenes/CartoonRendererTest.unity";

        [MenuItem("Tools/Cartoon Projection/Configure URP and Create Test Scene")]
        public static void Configure()
        {
            EnsureFolder(SettingsFolder);
            EnsureFolder(MaterialsFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "Cartoon Universal Renderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var feature = FindOrCreateFeature(renderer);
            ConfigureShapeDefaults(feature.settings);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Cartoon Universal Render Pipeline";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            CreateTestScene();
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Cartoon Projection Renderer configured. Open {ScenePath} and press Play.");
        }

        [MenuItem("Tools/Cartoon Projection/Validate MVP")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var failures = new List<string>();
            foreach (string shaderName in new[]
                     {
                         "Hidden/CartoonProjection/Capture", "Hidden/CartoonProjection/PaintHistory",
                         "Hidden/CartoonProjection/Simplify", "Hidden/CartoonProjection/Lighting",
                         "Hidden/CartoonProjection/Outline", "Hidden/CartoonProjection/Composite"
                     })
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null) { failures.Add($"Missing shader: {shaderName}"); continue; }
                failures.AddRange(ShaderUtil.GetShaderMessages(shader)
                    .Where(message => message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    .Select(message => $"{shaderName}: {message.message}"));
            }

            void CaptureError(string condition, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert)
                    failures.Add(condition + "\n" + stackTrace);
            }

            Application.logMessageReceived += CaptureError;
            var camera = Camera.main;
            var target = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGB32);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var feature = renderer == null ? null : renderer.rendererFeatures.OfType<CartoonRendererFeature>().FirstOrDefault();
            float originalScale = feature == null ? 0.5f : feature.settings.renderScale;
            bool originalPixelation = feature == null || feature.settings.pixelationEnabled;
            CartoonDebugView originalView = feature == null ? CartoonDebugView.Final : feature.settings.debugView;
            try
            {
                if (camera == null) failures.Add("Test scene has no Main Camera.");
                else if (feature == null) failures.Add("Cartoon Renderer Feature is not installed.");
                else
                {
                    camera.targetTexture = target;
                    foreach (float scale in new[] { 1f, 0.5f, 0.25f })
                    {
                        feature.settings.pixelationEnabled = true;
                        feature.settings.renderScale = scale;
                        feature.settings.debugView = CartoonDebugView.Final;
                        camera.Render();
                    }
                    feature.settings.pixelationEnabled = false;
                    camera.Render();
                    feature.settings.pixelationEnabled = true;
                    feature.settings.renderScale = 0.5f;
                    foreach (CartoonDebugView view in new[]
                             {
                                 CartoonDebugView.Depth, CartoonDebugView.Normal,
                                 CartoonDebugView.Lighting, CartoonDebugView.Outline
                             })
                    {
                        feature.settings.debugView = view;
                        camera.Render();
                    }
                    camera.targetTexture = null;
                }
            }
            finally
            {
                if (feature != null)
                {
                    feature.settings.renderScale = originalScale;
                    feature.settings.pixelationEnabled = originalPixelation;
                    feature.settings.debugView = originalView;
                }
                Application.logMessageReceived -= CaptureError;
                if (camera != null) camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
            }

            if (failures.Count > 0)
                throw new InvalidOperationException("Cartoon MVP validation failed:\n" + string.Join("\n", failures));
            Debug.Log("Cartoon Projection Renderer validation passed: scripts, four shaders, scene, render scales 1/0.5/0.25, full-resolution bypass, and required debug views.");
        }

        private static CartoonRendererFeature FindOrCreateFeature(UniversalRendererData renderer)
        {
            foreach (var item in renderer.rendererFeatures)
                if (item is CartoonRendererFeature existing)
                    return existing;

            var feature = ScriptableObject.CreateInstance<CartoonRendererFeature>();
            feature.name = "Cartoon Projection Renderer";
            feature.settings = new CartoonRenderSettings();
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            feature.Create();
            return feature;
        }

        private static void CreateTestScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("URP Lit shader was not found.");

            Material ground = Material("Ground", new Color(0.78f, 0.72f, 0.57f), lit);
            Material coral = Material("Coral", new Color(0.94f, 0.29f, 0.25f), lit);
            Material cyan = Material("Cyan", new Color(0.18f, 0.66f, 0.76f), lit);
            Material yellow = Material("Yellow", new Color(0.96f, 0.75f, 0.16f), lit);
            Material violet = Material("Violet", new Color(0.49f, 0.28f, 0.72f), lit);
            Material green = Material("Green", new Color(0.24f, 0.65f, 0.34f), lit);

            Primitive(PrimitiveType.Plane, "Plane", new Vector3(0, 0, 0), new Vector3(2.6f, 1, 2.2f), ground, 1);
            Primitive(PrimitiveType.Cube, "Cube - shape part", new Vector3(-2.5f, 0.75f, 0.7f), new Vector3(1.5f, 1.5f, 1.5f), coral, 2);
            Primitive(PrimitiveType.Sphere, "Sphere - shape part", new Vector3(-0.7f, 1.05f, -0.2f), Vector3.one * 2.1f, cyan, 3);
            Primitive(PrimitiveType.Capsule, "Capsule - occluded shape", new Vector3(1.1f, 1.15f, 0.65f), new Vector3(1.15f, 1.7f, 1.15f), yellow, 4);
            var cylinder = Primitive(PrimitiveType.Cylinder, "Cylinder - intersecting shape", new Vector3(2.5f, 0.85f, -0.35f), new Vector3(1.25f, 0.85f, 1.25f), violet, 5);
            cylinder.transform.rotation = Quaternion.Euler(18f, 0, 68f);

            Mesh torus = CreateTorus();
            const string torusPath = Root + "/TorusTestMesh.asset";
            Mesh savedTorus = AssetDatabase.LoadAssetAtPath<Mesh>(torusPath);
            if (savedTorus == null)
            {
                AssetDatabase.CreateAsset(torus, torusPath);
                savedTorus = torus;
            }
            else
            {
                EditorUtility.CopySerialized(torus, savedTorus);
                UnityEngine.Object.DestroyImmediate(torus);
            }
            var complex = new GameObject("Torus - complex mesh", typeof(MeshFilter), typeof(MeshRenderer));
            complex.transform.SetPositionAndRotation(new Vector3(0.8f, 1.55f, -2.25f), Quaternion.Euler(67f, 12f, 0));
            complex.GetComponent<MeshFilter>().sharedMesh = savedTorus;
            complex.GetComponent<MeshRenderer>().sharedMaterial = green;
            ConfigurePart(complex, green, 6);

            var target = new GameObject("Camera Orbit Target");
            target.transform.position = new Vector3(0, 0.8f, -0.35f);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData), typeof(CameraOrbit));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(8.7f, 6.1f, 9.2f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.76f, 0.88f, 0.94f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            camera.fieldOfView = 46f;
            camera.allowHDR = false;
            cameraObject.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            cameraObject.GetComponent<CameraOrbit>().target = target.transform;
            cameraObject.GetComponent<CameraOrbit>().orbit = false;
            cameraObject.transform.LookAt(target.transform);

            var sunObject = new GameObject("Directional Light", typeof(Light));
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.56f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.37f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.16f);
            RenderSettings.skybox = null;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, int id)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            ConfigurePart(go, material, id);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void ConfigurePart(GameObject go, Material material, int id)
        {
            var part = go.AddComponent<CartoonShapePart>();
            part.fillColor = material.GetColor("_BaseColor");
            part.shapeId = id;
            part.Apply();
        }

        private static void ConfigureShapeDefaults(CartoonRenderSettings settings)
        {
            settings.enabled = true;
            settings.pixelationEnabled = false;
            settings.renderScale = 0.5f;
            settings.lightingEnabled = false;
            settings.outlineEnabled = true;
            settings.outlineWidth = 1.25f;
            settings.silhouetteStrength = 1f;
            settings.occlusionStrength = 1f;
            settings.normalEdgeStrength = 0f;
            settings.depthEdgeStrength = 1f;
            settings.depthThreshold = 0.03f;
            settings.debugView = CartoonDebugView.Final;
        }

        private static Material Material(string name, Color color, Shader shader)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreateTorus()
        {
            const int majorSegments = 32;
            const int minorSegments = 12;
            const float majorRadius = 1.25f;
            const float minorRadius = 0.38f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int major = 0; major <= majorSegments; major++)
            {
                float a = major * Mathf.PI * 2f / majorSegments;
                Vector3 outward = new(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int minor = 0; minor <= minorSegments; minor++)
                {
                    float b = minor * Mathf.PI * 2f / minorSegments;
                    Vector3 normal = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    vertices.Add(outward * majorRadius + normal * minorRadius);
                    normals.Add(normal);
                }
            }
            int stride = minorSegments + 1;
            for (int major = 0; major < majorSegments; major++)
            for (int minor = 0; minor < minorSegments; minor++)
            {
                int i = major * stride + minor;
                triangles.Add(i); triangles.Add(i + stride); triangles.Add(i + 1);
                triangles.Add(i + 1); triangles.Add(i + stride); triangles.Add(i + stride + 1);
            }
            var mesh = new Mesh { name = "Cartoon Test Torus" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

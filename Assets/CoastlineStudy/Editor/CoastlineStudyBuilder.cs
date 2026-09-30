using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Colorido.Coastline.Editor
{
    public static class CoastlineStudyBuilder
    {
        private const string Source = "Assets/CoastlineStudy";
        private static readonly string[] Parts = { "Cliffs", "Turf", "Vegetation", "Shallows", "Foam", "Ocean", "OceanSurface", "SeaGlints" };

        [MenuItem("Tools/Colorido/Create Yellow Coast Test Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before creating a coastline scene."); return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Shader standard = SurfaceShader();
            Shader waterShader = Shader.Find("Colorido/Coastal Water");
            if (standard == null || waterShader == null)
                throw new InvalidOperationException("Yellow Coast could not find shaders for the active render pipeline.");
            foreach (string part in Parts)
                if (!File.Exists(Source + "/Models/" + part + ".obj"))
                    throw new FileNotFoundException("Missing coastline model: " + part);

            string output = AssetDatabase.GenerateUniqueAssetPath(Source + "/Generated");
            AssetDatabase.CreateFolder(Source, Path.GetFileName(output));
            AssetDatabase.CreateFolder(output, "Materials");
            Dictionary<string, Material> palette = MakeMaterials(output, standard);
            Material water = new Material(waterShader) { name = "CoastalWater" };
            AssetDatabase.CreateAsset(water, output + "/Materials/CoastalWater.mat");

            // Create an independent scene only after the user has saved any current work.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject island = new GameObject("Yellow Coast - Original Study");
            foreach (string part in Parts)
            {
                string path = Source + "/Models/" + part + ".obj";
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("OBJ importer unavailable: " + path);
                importer.globalScale = 1f;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 0f;
                importer.importTangents = ModelImporterTangents.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Cannot import " + path);
                GameObject instance = UnityEngine.Object.Instantiate(model, island.transform);
                instance.name = part;
                foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        string key = slots[i] == null ? "Chalk" : MaterialKey(slots[i].name, part);
                        if (part == "OceanSurface") slots[i] = water;
                        else if (palette.TryGetValue(key, out Material material)) slots[i] = material;
                        else { Debug.LogWarning("Unknown material " + key + "; using Chalk."); slots[i] = palette["Chalk"]; }
                    }
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = part == "Vegetation" ? ShadowCastingMode.TwoSided : ShadowCastingMode.On;
                    if (part == "Cliffs" || part == "Turf")
                        renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                }
            }
            string prefabPath = output + "/YellowCoast.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(island, prefabPath, InteractionMode.AutomatedAction);

            GameObject cameraObject = new GameObject("Coastline Camera", typeof(Camera), typeof(AudioListener), typeof(CoastlineViewer));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8.5f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.018f, .075f, .09f);
            camera.allowHDR = false;
            GameObject sunObject = new GameObject("Warm coastal sunlight", typeof(Light));
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .97f, .85f);
            sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.shadowBias = .03f; sun.shadowNormalBias = .15f;
            sunObject.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.64f, .73f, .73f);
            RenderSettings.ambientEquatorColor = new Color(.40f, .52f, .53f);
            RenderSettings.ambientGroundColor = new Color(.23f, .32f, .34f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            AssetDatabase.SaveAssets();
            if (GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal"))
                CoastlinePostInstaller.Install();
            EditorSceneManager.SaveScene(scene, output + "/YellowCoastTest.unity");
            Selection.activeGameObject = island;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            Debug.Log("Yellow Coast ready: " + output + "/YellowCoastTest.unity. Press Play; right-drag to orbit, scroll to zoom.");
        }

        private static Shader SurfaceShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return Shader.Find("Standard");
            if (pipeline.GetType().Name.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
            throw new NotSupportedException("Yellow Coast supports Built-in and URP.");
        }

        public static string MaterialKey(string name, string part)
        {
            string key = name.Replace(" (Instance)", "");
            string prefix = part + "-";
            return key.StartsWith(prefix, StringComparison.Ordinal) ? key.Substring(prefix.Length) : key;
        }

        [MenuItem("Tools/Colorido/Repair Yellow Coast Materials")]
        public static void RepairMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Shader shader = SurfaceShader();
            if (shader == null) throw new InvalidOperationException("Surface shader is missing.");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Source }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) != "YellowCoast.prefab") continue;
                string output = Path.GetDirectoryName(path).Replace('\\', '/');
                var palette = MakeMaterials(output, shader);
                var water = AssetDatabase.LoadAssetAtPath<Material>(output + "/Materials/CoastalWater.mat");
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (string part in Parts)
                    {
                        Transform target = root.transform.Find(part);
                        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Models/" + part + ".obj");
                        if (target == null || source == null) throw new InvalidOperationException("Missing part " + part);
                        var original = source.GetComponentsInChildren<MeshRenderer>(true);
                        var destinations = target.GetComponentsInChildren<MeshRenderer>(true);
                        if (original.Length != destinations.Length) throw new InvalidOperationException("Renderer count differs: " + part);
                        for (int r = 0; r < original.Length; r++)
                        {
                            Material[] slots = original[r].sharedMaterials;
                            for (int i = 0; i < slots.Length; i++)
                            {
                                string key = slots[i] == null ? "" : MaterialKey(slots[i].name, part);
                                if (part == "OceanSurface" && water != null) slots[i] = water;
                                else if (palette.TryGetValue(key, out Material material)) slots[i] = material;
                                else throw new InvalidOperationException("Unrecognized imported material: " + key);
                            }
                            destinations[r].sharedMaterials = slots;
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Yellow Coast material repair completed: " + count + " prefabs; pipeline=" + (GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.name));
        }

        private static Dictionary<string, Material> MakeMaterials(string output, Shader shader)
        {
            var colors = new Dictionary<string, Color>
            {
                {"Chalk", new Color(.91f,.91f,.77f)}, {"ChalkLight",new Color(.99f,.98f,.86f)},
                {"Stratum",new Color(.73f,.76f,.63f)}, {"Grass",new Color(.67f,.76f,.14f)},
                {"GrassLight",new Color(.78f,.84f,.20f)}, {"GrassDark",new Color(.43f,.57f,.12f)},
                {"Lemon",new Color(.96f,.81f,.16f)}, {"LemonLight",new Color(1f,.91f,.28f)},
                {"Wood",new Color(.39f,.43f,.21f)}, {"Sand",new Color(.79f,.83f,.49f)},
                {"Reef",new Color(.23f,.68f,.68f)}, {"Sea",new Color(.025f,.52f,.61f)},
                {"DeepSea",new Color(.02f,.35f,.43f)}, {"Foam",new Color(.80f,.97f,.88f)}
            };
            var result = new Dictionary<string, Material>();
            foreach (var entry in colors)
            {
                string path = output + "/Materials/" + entry.Key + ".mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew) mat = new Material(shader) { name = entry.Key };
                mat.shader = shader;
                mat.color = entry.Value;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", entry.Value);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", entry.Value);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .05f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .05f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0);
                if (isNew) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
                result.Add(entry.Key, mat);
            }
            return result;
        }
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Colorido.Coastline.Editor
{
    public static class CoastlinePostInstaller
    {
        private const string Folder = "Assets/CoastlineStudy/PostProcessing";
        private static Type UrpType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering.Universal." + name)).FirstOrDefault(t => t != null);
        }

        private static Type RenderingType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering." + name)).FirstOrDefault(t => t != null);
        }
        private static void Parameter(ScriptableObject component, string field, object value)
        {
            var parameter = component.GetType().GetField(field).GetValue(component);
            parameter.GetType().GetProperty("overrideState").SetValue(parameter, true);
            parameter.GetType().GetProperty("value").SetValue(parameter, value);
        }
        private static void ConfigureGlow(Camera camera, CoastlinePostSettings settings)
        {
            var profileType = RenderingType("VolumeProfile");
            var volumeType = RenderingType("Volume");
            string path = Folder + "/CoastlineSoftGlow.asset";
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance(profileType);
                profile.name = "Coastline Soft Glow";
                AssetDatabase.CreateAsset(profile, path);
            }
            var list = (System.Collections.IList)profileType.GetField("components").GetValue(profile);
            var bloomType = UrpType("Bloom");
            ScriptableObject bloom = null;
            foreach (ScriptableObject component in list) if (component.GetType() == bloomType) bloom = component;
            if (bloom == null)
            {
                bloom = (ScriptableObject)profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) }).Invoke(profile, new object[] { bloomType, true });
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            Parameter(bloom, "intensity", .28f);
            Parameter(bloom, "threshold", 1.05f);
            Parameter(bloom, "scatter", .55f);
            Parameter(bloom, "tint", new Color(1,.97f,.86f));
            EditorUtility.SetDirty(bloom); EditorUtility.SetDirty(profile);
            Transform child = camera.transform.Find("Coastline Soft Glow");
            if (child == null)
            {
                var go = new GameObject("Coastline Soft Glow");
                Undo.RegisterCreatedObjectUndo(go, "Create coastline glow");
                go.transform.SetParent(camera.transform, false);
                child = go.transform;
            }
            var volume = child.GetComponent(volumeType) ?? Undo.AddComponent(child.gameObject, volumeType);
            volumeType.GetProperty("isGlobal").SetValue(volume, true);
            volumeType.GetField("sharedProfile").SetValue(volume, profile);
            volumeType.GetField("priority").SetValue(volume, 20f);
            settings.glowVolume = (Behaviour)volume;
            camera.allowHDR = true;
            EditorUtility.SetDirty(camera); EditorUtility.SetDirty(volume);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/CoastlineStudy" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material.name != "Lemon" && material.name != "LemonLight" && material.name != "Foam") continue;
                if (!material.HasProperty("_EmissionColor")) continue;
                material.EnableKeyword("_EMISSION");
                float amount = material.name == "Foam" ? .65f : 1.35f;
                material.SetColor("_EmissionColor", material.color * amount);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(material);
            }
        }

        [MenuItem("Tools/Colorido/Install Stylized Coast Post Processing")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing.");
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var featureType = UrpType("FullScreenPassRendererFeature");
            var cameraDataType = UrpType("UniversalAdditionalCameraData");
            var viewer = UnityEngine.Object.FindFirstObjectByType<CoastlineViewer>();
            if (pipeline == null || !pipeline.GetType().Name.Contains("Universal") || featureType == null || cameraDataType == null)
                throw new InvalidOperationException("This installer requires URP with the Full Screen Pass feature (URP 14+).");
            if (viewer == null) throw new InvalidOperationException("Open the Yellow Coast test scene first.");
            var shader = Shader.Find("Colorido/Coastline Stylized Post");
            if (shader == null) throw new InvalidOperationException("Coastline post shader is missing.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/CoastlineStudy", "PostProcessing");
            string matPath = Folder + "/CoastlinePost.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(shader) { name = "CoastlinePost" };
                AssetDatabase.CreateAsset(material, matPath);
            }
            var pipelineSO = new SerializedObject(pipeline);
            var rendererList = pipelineSO.FindProperty("m_RendererDataList");
            int defaultIndex = pipelineSO.FindProperty("m_DefaultRendererIndex").intValue;
            var sourceRenderer = rendererList.GetArrayElementAtIndex(defaultIndex).objectReferenceValue;
            string rendererPath = Folder + "/CoastlineRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rendererPath);
            if (renderer == null)
            {
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceRenderer), rendererPath))
                    throw new InvalidOperationException("Could not copy the current renderer.");
                renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rendererPath);
            }
            // Use Unity's native Full Screen Pass, including its Render Graph implementation.
            var rendererSO = new SerializedObject(renderer);
            var features = rendererSO.FindProperty("m_RendererFeatures");
            ScriptableObject feature = null;
            for (int i = 0; i < features.arraySize; i++)
            {
                var candidate = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
                if (candidate != null && candidate.name == "Coastline Stylized Post") feature = candidate;
            }
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance(featureType);
                feature.name = "Coastline Stylized Post";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                features.InsertArrayElementAtIndex(features.arraySize);
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                rendererSO.ApplyModifiedPropertiesWithoutUndo();
            }
            var featureSO = new SerializedObject(feature);
            featureSO.FindProperty("passMaterial").objectReferenceValue = material;
            featureSO.FindProperty("injectionPoint").intValue = 600; // AfterRenderingPostProcessing
            featureSO.FindProperty("passIndex").intValue = 0;
            var fetch = featureSO.FindProperty("fetchColorBuffer");
            if (fetch != null) fetch.boolValue = true;
            // Older Full Screen Pass versions use the Color input flag instead.
            featureSO.FindProperty("requirements").intValue = fetch == null ? 4 : 0;
            featureSO.ApplyModifiedPropertiesWithoutUndo();
            featureType.GetMethod("Create").Invoke(feature, null);
            renderer.GetType().GetMethod("SetDirty")?.Invoke(renderer, null);
            int index = -1;
            for (int i = 0; i < rendererList.arraySize; i++)
                if (rendererList.GetArrayElementAtIndex(i).objectReferenceValue == renderer) index = i;
            if (index < 0)
            {
                index = rendererList.arraySize;
                rendererList.InsertArrayElementAtIndex(index);
                rendererList.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                pipelineSO.ApplyModifiedPropertiesWithoutUndo();
            }
            Camera camera = viewer.GetComponent<Camera>();
            var data = camera.GetComponent(cameraDataType) ?? Undo.AddComponent(camera.gameObject, cameraDataType);
            Undo.RecordObject(data, "Install coastline renderer");
            cameraDataType.GetMethod("SetRenderer").Invoke(data, new object[] { index });
            var dataSO = new SerializedObject(data);
            var aa = dataSO.FindProperty("m_Antialiasing");
            if (aa != null) aa.intValue = 1;
            dataSO.FindProperty("m_RenderPostProcessing").boolValue = true;
            dataSO.ApplyModifiedPropertiesWithoutUndo();
            var settings = camera.GetComponent<CoastlinePostSettings>() ?? Undo.AddComponent<CoastlinePostSettings>(camera.gameObject);
            Undo.RecordObject(settings, "Install coastline post settings");
            settings.postMaterial = material;
            ConfigureGlow(camera, settings);
            settings.Apply();
            // Give the saved edit-mode camera the same framing as the runtime viewer.
            Undo.RecordObject(camera.transform, "Frame coastline");
            camera.transform.rotation = Quaternion.Euler(32,42,0);
            camera.transform.position = viewer.focus - camera.transform.forward * 24;
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            Debug.Log("Coastline post installed: dedicated camera renderer, edge-preserving oil paint, native resolution, no pixel grid. Save the scene; Play for A/B controls.");
        }
    }
}

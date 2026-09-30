using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Writes Generated/<name>/ meshes, the region asset and a prefab variant. The source
    // prefab, FBX and materials are never modified; the variant only swaps meshes and
    // adds binding/shape-part components.
    internal static class CartoonRegionPrefabBuilder
    {
        public sealed class Output
        {
            public string rootFolder;
            public string meshFolder;
            public CartoonColorRegionAsset regionAsset;
            public string prefabPath;
        }

        public static Output Write(GameObject sourceRoot, IReadOnlyList<Mesh> generatedMeshes,
            IReadOnlyList<string> rendererPaths, CartoonColorRegionAsset regionAsset,
            IReadOnlyList<CartoonMaterialRegionRule> rules, IReadOnlyList<Color> fallbackColors,
            IReadOnlyList<(Texture2D cutout, float cutoff, Vector4 scaleOffset)> cutoutData)
        {
            string rootName = Sanitize(sourceRoot.name);
            string rootFolder = $"Assets/CartoonRenderer/Generated/{rootName}";
            string meshFolder = rootFolder + "/Meshes";
            EnsureFolder("Assets/CartoonRenderer/Generated");
            EnsureFolder(rootFolder);
            EnsureFolder(meshFolder);

            var output = new Output { rootFolder = rootFolder, meshFolder = meshFolder };

            // Mesh assets (overwrite in place so GUIDs stay stable between bakes). The
            // renderer path prefixes the file name so two renderers sharing a source mesh
            // never collide.
            for (int i = 0; i < generatedMeshes.Count; i++)
            {
                string path = $"{meshFolder}/{Sanitize(rendererPaths[i])}_{generatedMeshes[i].name}.asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(generatedMeshes[i], existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(generatedMeshes[i], path);
                }
            }

            // Region asset.
            string assetPath = $"{rootFolder}/{rootName}_ColorRegions.asset";
            var existingAsset = AssetDatabase.LoadAssetAtPath<CartoonColorRegionAsset>(assetPath);
            if (existingAsset != null)
            {
                EditorUtility.CopySerialized(regionAsset, existingAsset);
                output.regionAsset = existingAsset;
                EditorUtility.SetDirty(existingAsset);
            }
            else
            {
                AssetDatabase.CreateAsset(regionAsset, assetPath);
                output.regionAsset = regionAsset;
            }

            // Prefab variant: instantiate the source prefab (not a plain clone) so saving
            // produces a variant of it; loose hierarchies fall back to a full prefab.
            string prefabPath = $"{rootFolder}/{rootName}_Cartoon.prefab";
            var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(sourceRoot);
            var planted = sourcePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab)
                : Object.Instantiate(sourceRoot);
            planted.name = sourceRoot.name;
            try
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(planted, prefabPath, InteractionMode.AutomatedAction);
            }
            finally
            {
                Object.DestroyImmediate(planted);
            }

            var prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                for (int i = 0; i < rendererPaths.Count; i++)
                {
                    var transform = prefabContents.transform.Find(rendererPaths[i]);
                    if (transform == null)
                        continue;
                    var renderer = transform.GetComponent<Renderer>();
                    if (renderer == null)
                        continue;

                    if (i < generatedMeshes.Count && generatedMeshes[i] != null)
                    {
                        if (renderer is SkinnedMeshRenderer skinned)
                            skinned.sharedMesh = generatedMeshes[i];
                        else
                            renderer.GetComponent<MeshFilter>().sharedMesh = generatedMeshes[i];
                    }

                    var binding = renderer.GetComponent<CartoonColorRegionBinding>() ??
                                  renderer.gameObject.AddComponent<CartoonColorRegionBinding>();
                    binding.asset = output.regionAsset;
                    binding.rendererPath = rendererPaths[i];
                    binding.regionCount = i < regionAsset.meshes.Count ? regionAsset.meshes[i].regionCount : 0;

                    var part = renderer.GetComponent<CartoonShapePart>() ??
                               renderer.gameObject.AddComponent<CartoonShapePart>();
                    if (i < fallbackColors.Count)
                        part.fillColor = fallbackColors[i];
                    if (i < cutoutData.Count)
                    {
                        part.cutoutTexture = cutoutData[i].cutout;
                        part.alphaCutoff = cutoutData[i].cutoff;
                        part.baseMapScaleOffset = cutoutData[i].scaleOffset;
                    }
                    part.shapeId = (i + 1) % 256;
                    part.Apply();
                    EditorUtility.SetDirty(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            output.prefabPath = prefabPath;
            return output;
        }

        public static string ComputeDependencyHash(GameObject sourceRoot,
            IReadOnlyList<(Mesh mesh, Material[] materials)> rendererData,
            IReadOnlyList<CartoonMaterialRegionRule> rules, float globalMergeMultiplier)
        {
            var hash = new Hash128();
            foreach (var (mesh, materials) in rendererData)
            {
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                hash.Append(meshPath);
                hash.Append(AssetDatabase.GetAssetDependencyHash(meshPath).ToString());
                foreach (var material in materials)
                {
                    string materialPath = AssetDatabase.GetAssetPath(material);
                    hash.Append(materialPath);
                    if (material != null && material.mainTexture != null)
                    {
                        string texturePath = AssetDatabase.GetAssetPath(material.mainTexture);
                        hash.Append(texturePath);
                        hash.Append(AssetDatabase.GetAssetDependencyHash(texturePath).ToString());
                    }
                }
            }
            foreach (var rule in rules)
            {
                hash.Append(rule.materialGuid);
                hash.Append((int)rule.policy);
                hash.Append(rule.maxRegions);
                hash.Append(rule.colorMergeScale);
                hash.Append(rule.minRegionTriangles);
                hash.Append(rule.minRegionAreaRatio);
                hash.Append(rule.useColorOverride ? 1 : 0);
                hash.Append(rule.colorOverride.ToString());
            }
            hash.Append(globalMergeMultiplier);
            hash.Append(sourceRoot.name);
            return hash.ToString();
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrEmpty(name) ? "Character" : name;
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

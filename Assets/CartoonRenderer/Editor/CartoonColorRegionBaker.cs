using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Editor-side color region baker: samples material textures per triangle, clusters
    // adjacent triangles in OKLab, and hands the results to the mesh builder. All bake
    // work happens here in the editor; the runtime only reads vertex data.
    internal static class CartoonColorRegionBaker
    {
        public sealed class RendererReport
        {
            public string path;
            public string name;
            public bool isSkinned;
            public Mesh sourceMesh;
            public Material[] materials;
            public Renderer renderer;
            public List<SubMeshBake> subMeshBakes = new();
            public CartoonRegionMeshBuilder.BuildResult build;
            public int regionCount;
            public List<string> warnings = new();
        }

        public sealed class BakeResult
        {
            public List<RendererReport> reports = new();
            public CartoonColorRegionAsset asset;
            public CartoonRegionPrefabBuilder.Output output;
            public string dependencyHash;
            public List<string> warnings = new();
        }

        public static List<RendererReport> CollectRenderers(GameObject root)
        {
            var reports = new List<RendererReport>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not (SkinnedMeshRenderer or MeshRenderer))
                    continue;
                if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() == null)
                    continue;
                var report = new RendererReport
                {
                    path = RendererPath(root.transform, renderer.transform),
                    name = renderer.name,
                    renderer = renderer,
                    isSkinned = renderer is SkinnedMeshRenderer
                };
                report.sourceMesh = report.isSkinned
                    ? ((SkinnedMeshRenderer)renderer).sharedMesh
                    : renderer.GetComponent<MeshFilter>().sharedMesh;
                if (report.sourceMesh == null)
                {
                    report.warnings.Add("Renderer has no mesh; skipped.");
                }
                else
                {
                    var unique = new List<Material>();
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && !unique.Contains(material))
                            unique.Add(material);
                    report.materials = unique.ToArray();
                    if (report.isSkinned && report.sourceMesh.blendShapeCount > 0)
                        report.warnings.Add($"Preserves {report.sourceMesh.blendShapeCount} blendshape(s).");
                }
                reports.Add(report);
            }
            return reports;
        }

        public static List<CartoonMaterialRegionRule> BuildDefaultRules(List<RendererReport> reports)
        {
            var rules = new List<CartoonMaterialRegionRule>();
            foreach (var report in reports)
            {
                if (report.materials == null)
                    continue;
                foreach (var material in report.materials)
                {
                    if (rules.Any(r => r.materialGuid == AssetGuid(material)))
                        continue;
                    var rule = new CartoonMaterialRegionRule
                    {
                        materialGuid = AssetGuid(material),
                        materialName = material.name,
                        policy = CartoonRegionPolicyDefaults.SuggestFromNames(
                            material.name, report.name, TextureName(material))
                    };
                    rule.ApplyPolicyDefaults(rule.policy);
                    string lowerName = material.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                    if (lowerName.Contains("eyeshadow") || lowerName.Contains("paintedshadow"))
                        rule.fixedBaseLayer = true;
                    rules.Add(rule);
                }
            }
            return rules;
        }

        public static BakeResult Bake(GameObject root, List<RendererReport> reports,
            List<CartoonMaterialRegionRule> rules, float globalMergeMultiplier, bool writeAssets)
        {
            var result = new BakeResult();
            var samplerCache = new Dictionary<Texture2D, CartoonTextureSampler>();
            var asset = ScriptableObject.CreateInstance<CartoonColorRegionAsset>();
            asset.sourceRootName = root.name;
            asset.globalColorMergeMultiplier = globalMergeMultiplier;
            asset.rules = rules;
            asset.bakedAt = DateTime.UtcNow.ToString("o");
            bool hasSkinned = false;
            bool hasBlendShapes = false;
            bool hasAlpha = false;

            var rendererData = new List<(Mesh mesh, Material[] materials)>();
            var generatedMeshes = new List<Mesh>();
            var rendererPaths = new List<string>();
            var fallbackColors = new List<Color>();
            var cutoutData = new List<(Texture2D, float, Vector4)>();

            foreach (var report in reports)
            {
                if (report.sourceMesh == null || report.materials == null || report.materials.Length == 0)
                {
                    if (report.sourceMesh != null)
                        report.warnings.Add("Renderer has no materials; skipped.");
                    continue;
                }
                rendererData.Add((report.sourceMesh, report.materials));
                hasSkinned |= report.isSkinned;
                hasBlendShapes |= report.sourceMesh.blendShapeCount > 0;

                var bakes = new List<SubMeshBake>();
                for (int sub = 0; sub < report.sourceMesh.subMeshCount; sub++)
                {
                    var material = sub < report.materials.Length ? report.materials[sub] : report.materials[^1];
                    var rule = rules.FirstOrDefault(r => r.materialGuid == AssetGuid(material));
                    if (rule == null)
                    {
                        rule = new CartoonMaterialRegionRule
                        {
                            materialGuid = AssetGuid(material),
                            materialName = material.name
                        };
                        rule.ApplyPolicyDefaults(CartoonRegionPolicy.Standard);
                        rules.Add(rule);
                        result.warnings.Add($"'{material.name}' had no rule; Standard defaults applied.");
                    }

                    var bake = new SubMeshBake { subMesh = sub, rule = rule };
                    bake.colors = SampleTriangles(report.sourceMesh, sub, material, rule, samplerCache, report.warnings);
                    bake.adjacency = TriangleAdjacencyGraph.Build(report.sourceMesh, sub);
                    float distance = CartoonRegionPolicyDefaults.BaseDistance(rule.policy)
                                     * rule.colorMergeScale * globalMergeMultiplier;
                    bake.cluster = CartoonColorClustering.Cluster(bake.colors, bake.adjacency, distance,
                        rule.maxRegions, rule.minRegionTriangles, CartoonRegionPolicyDefaults.ProtectedMergeDistance);
                    report.regionCount += bake.cluster.regionCount;
                    hasAlpha |= rule.UsesAlphaCutout(material);
                    bakes.Add(bake);
                }
                report.subMeshBakes = bakes;
                report.build = CartoonRegionMeshBuilder.Build(report.sourceMesh, bakes);
                if (report.build.vertexGrowth > report.build.sourceVertexCount)
                    report.warnings.Add(
                        $"Vertex growth {report.build.sourceVertexCount} -> {report.build.sourceVertexCount + report.build.vertexGrowth} (>{1.0f + (float)report.build.vertexGrowth / report.build.sourceVertexCount:P0}).");
                result.reports.Add(report);

                generatedMeshes.Add(report.build.mesh);
                rendererPaths.Add(report.path);
                fallbackColors.Add(report.build.regions.Count > 0 ? report.build.regions[0].color : Color.white);

                var cutoutMaterial = report.materials.FirstOrDefault(m => (ruleCutoff(m, rules)?.cutoff ?? 0f) > 0f);
                if (cutoutMaterial != null)
                {
                    var data = ruleCutoff(cutoutMaterial, rules).Value;
                    cutoutData.Add((data.texture, data.cutoff, data.scaleOffset));
                }
                else
                {
                    cutoutData.Add((null, 0f, Vector4.one));
                }

                asset.meshes.Add(new CartoonRegionMeshBinding
                {
                    rendererPath = report.path,
                    rendererName = report.name,
                    isSkinned = report.isSkinned,
                    sourceMeshGuid = AssetGuid(report.sourceMesh),
                    generatedMesh = writeAssets ? null : report.build.mesh,
                    subMeshCount = report.sourceMesh.subMeshCount,
                    regionCount = report.regionCount,
                    sourceVertexCount = report.build.sourceVertexCount,
                    generatedVertexCount = report.build.sourceVertexCount + report.build.vertexGrowth,
                    sourceTriangleCount = report.sourceMesh.triangles.Length / 3
                });
                asset.regions.AddRange(report.build.regions);
            }

            foreach (var material in rules
                         .Select(r => MaterialByGuid(r.materialGuid))
                         .Where(m => m != null))
                if (!asset.sourceMaterialGuids.Contains(AssetGuid(material)))
                    asset.sourceMaterialGuids.Add(AssetGuid(material));
            asset.sourceMeshGuids = rendererData
                .Select(d => AssetGuid(d.mesh))
                .Distinct()
                .ToList();
            asset.dependencyHash = CartoonRegionPrefabBuilder.ComputeDependencyHash(root, rendererData, rules, globalMergeMultiplier);
            asset.hasSkinnedMeshes = hasSkinned;
            asset.hasBlendShapes = hasBlendShapes;
            asset.hasAlphaMaterials = hasAlpha;
            asset.warnings = result.warnings;
            result.asset = asset;
            result.dependencyHash = asset.dependencyHash;

            if (writeAssets)
            {
                // Rebind generated meshes into the asset before writing so references survive.
                for (int i = 0; i < asset.meshes.Count; i++)
                    asset.meshes[i].generatedMesh = result.reports[i].build.mesh;
                result.output = CartoonRegionPrefabBuilder.Write(root, generatedMeshes, rendererPaths,
                    asset, rules, fallbackColors, cutoutData);
                result.asset = result.output.regionAsset;
            }
            return result;
        }

        private static (Texture2D texture, float cutoff, Vector4 scaleOffset)? ruleCutoff(Material material,
            List<CartoonMaterialRegionRule> rules)
        {
            var rule = rules.FirstOrDefault(r => r.materialGuid == AssetGuid(material));
            if (rule == null || !rule.UsesAlphaCutout(material))
                return null;
            Texture2D texture = BaseMap(material) as Texture2D;
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
            Vector4 st = BaseMapST(material);
            return (texture, cutoff, st);
        }

        private static TriangleColor[] SampleTriangles(Mesh mesh, int subMesh, Material material,
            CartoonMaterialRegionRule rule, Dictionary<Texture2D, CartoonTextureSampler> samplerCache,
            List<string> warnings)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            var colors = new TriangleColor[triangles.Length / 3];
            Vector3[] vertices = mesh.vertices;

            Texture baseTexture = BaseMap(material);
            Color baseColor = BaseColor(material);
            Vector4 st = BaseMapST(material);
            CartoonTextureSampler sampler = null;
            Vector2[] uv0 = null;
            if (baseTexture is Texture2D texture2D)
            {
                if (!samplerCache.TryGetValue(texture2D, out sampler))
                {
                    sampler = CartoonTextureSampler.Create(texture2D);
                    samplerCache[texture2D] = sampler;
                }
                uv0 = mesh.uv;
                if (uv0 == null || uv0.Length != vertices.Length)
                {
                    if (!warnings.Contains($"{material.name}: no UV0; using base color only."))
                        warnings.Add($"{material.name}: no UV0; using base color only.");
                    uv0 = null;
                    sampler = null;
                }
            }
            else if (!warnings.Contains($"{material.name}: no base map texture."))
            {
                warnings.Add($"{material.name}: no base map texture.");
            }

            float cutoff = rule.UsesAlphaCutout(material) && material.HasProperty("_Cutoff")
                ? material.GetFloat("_Cutoff")
                : 0f;

            bool hero = rule.policy == CartoonRegionPolicy.HeroDetail;
            for (int t = 0; t < colors.Length; t++)
            {
                int i0 = triangles[t * 3];
                int i1 = triangles[t * 3 + 1];
                int i2 = triangles[t * 3 + 2];
                Color sample = SampleTriangle(uv0, i0, i1, i2, sampler, st, hero);
                float r = Mathf.Clamp01(sample.r * baseColor.linear.r);
                float g = Mathf.Clamp01(sample.g * baseColor.linear.g);
                float b = Mathf.Clamp01(sample.b * baseColor.linear.b);
                float alpha = sample.a * baseColor.linear.a;
                CartoonColorClustering.LinearToOklab(r, g, b, out float l, out float a, out float bOut);
                colors[t] = new TriangleColor
                {
                    L = l,
                    A = a,
                    B = bOut,
                    alpha = alpha,
                    cutout = cutoff > 0f && alpha < cutoff
                };
            }
            return colors;
        }

        private static Color SampleTriangle(Vector2[] uv0, int i0, int i1, int i2,
            CartoonTextureSampler sampler, Vector4 st, bool hero)
        {
            if (sampler == null || uv0 == null)
                return Color.white;
            Vector2 uvA = Vector2.Scale(uv0[i0], st) + new Vector2(st.z, st.w);
            Vector2 uvB = Vector2.Scale(uv0[i1], st) + new Vector2(st.z, st.w);
            Vector2 uvC = Vector2.Scale(uv0[i2], st) + new Vector2(st.z, st.w);
            // Centroid only; hero materials add edge midpoints to soften seams.
            Color sum = sampler.SampleBilinear((uvA.x + uvB.x + uvC.x) / 3f, (uvA.y + uvB.y + uvC.y) / 3f);
            int count = 1;
            if (hero)
            {
                sum += sampler.SampleBilinear((uvA.x + uvB.x) * 0.5f, (uvA.y + uvB.y) * 0.5f);
                sum += sampler.SampleBilinear((uvB.x + uvC.x) * 0.5f, (uvB.y + uvC.y) * 0.5f);
                sum += sampler.SampleBilinear((uvC.x + uvA.x) * 0.5f, (uvC.y + uvA.y) * 0.5f);
                count = 4;
            }
            return sum / count;
        }

        private static Texture BaseMap(Material material)
        {
            if (material.HasProperty("_BaseMap"))
                return material.GetTexture("_BaseMap");
            if (material.HasProperty("_MainTex"))
                return material.GetTexture("_MainTex");
            return null;
        }

        private static Color BaseColor(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            return Color.white;
        }

        private static Vector4 BaseMapST(Material material)
        {
            if (material.HasProperty("_BaseMap"))
                return new Vector4(
                    material.GetTextureScale("_BaseMap").x, material.GetTextureScale("_BaseMap").y,
                    material.GetTextureOffset("_BaseMap").x, material.GetTextureOffset("_BaseMap").y);
            if (material.HasProperty("_MainTex"))
                return new Vector4(
                    material.GetTextureScale("_MainTex").x, material.GetTextureScale("_MainTex").y,
                    material.GetTextureOffset("_MainTex").x, material.GetTextureOffset("_MainTex").y);
            return new Vector4(1f, 1f, 0f, 0f);
        }

        private static string TextureName(Material material) => BaseMap(material) != null ? BaseMap(material).name : "";

        private static string AssetGuid(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static Material MaterialByGuid(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static string RendererPath(Transform root, Transform renderer)
        {
            var steps = new List<string>();
            var current = renderer;
            while (current != null && current != root)
            {
                steps.Add(current.name);
                current = current.parent;
            }
            steps.Reverse();
            return string.Join("/", steps);
        }
    }
}

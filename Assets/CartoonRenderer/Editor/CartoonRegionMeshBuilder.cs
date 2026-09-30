using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Per-submesh bake payload handed from the baker to the mesh builder.
    internal sealed class SubMeshBake
    {
        public int subMesh;
        public CartoonMaterialRegionRule rule;
        public TriangleColor[] colors;
        public CartoonColorClustering.Result cluster;
        public TriangleAdjacencyGraph adjacency;
    }

    // Builds the color-block mesh: splits shared vertices on region boundaries, writes
    // the region color into COLOR (sRGB bytes) and regionId/255 + policy flag into
    // TEXCOORD3. Positions, normals, tangents, UV0-2/4-7, bone weights, bindposes,
    // blendshapes, submesh order, index format and bounds are preserved verbatim.
    internal static class CartoonRegionMeshBuilder
    {
        private struct BlendShapeFrame
        {
            public float weight;
            public List<Vector3> deltaVertices;
            public List<Vector3> deltaNormals;
            public List<Vector3> deltaTangents;
        }

        private sealed class BlendShape
        {
            public string name;
            public List<BlendShapeFrame> frames = new();
        }

        public sealed class BuildResult
        {
            public Mesh mesh;
            public List<CartoonColorRegion> regions = new();
            public int sourceVertexCount;
            public int vertexGrowth;
            public int regionIdOverflow;
        }

        public static BuildResult Build(Mesh source, IReadOnlyList<SubMeshBake> subMeshBakes)
        {
            var result = new BuildResult();
            // Instantiating copies every channel, submesh, bindpose and blendshape; the
            // builder then only appends split vertices and rewrites triangle indices.
            var mesh = Object.Instantiate(source);
            mesh.name = $"{source.name}_ColorBlocks";

            int originalVertexCount = source.vertexCount;
            result.sourceVertexCount = originalVertexCount;

            var positions = new List<Vector3>(source.vertices);
            var normals = source.normals.Length == originalVertexCount ? new List<Vector3>(source.normals) : null;
            var tangents = source.tangents.Length == originalVertexCount ? new List<Vector4>(source.tangents) : null;
            var uvs = new List<Vector4>[8];
            for (int channel = 0; channel < 8; channel++)
            {
                var list = new List<Vector4>();
                source.GetUVs(channel, list);
                uvs[channel] = list.Count == originalVertexCount ? list : null;
            }
            var boneWeights = source.boneWeights.Length == originalVertexCount
                ? new List<BoneWeight>(source.boneWeights)
                : null;
            var shapes = ReadBlendShapes(source, originalVertexCount);

            var colors = new List<Color32>(originalVertexCount);
            var regionInfo = new List<Vector2>(originalVertexCount);
            for (int i = 0; i < originalVertexCount; i++)
            {
                colors.Add(new Color32(255, 255, 255, 255));
                regionInfo.Add(Vector2.zero);
            }

            int subMeshCount = source.subMeshCount;
            var allTriangles = new int[subMeshCount][];
            for (int s = 0; s < subMeshCount; s++)
                allTriangles[s] = source.GetTriangles(s);

            int regionIdCursor = 0;
            var vertexOwner = new Dictionary<int, int>();
            var vertexCopies = new Dictionary<(int vertex, int region), int>();
            var newRegions = new List<CartoonColorRegion>();

            foreach (var bake in subMeshBakes)
            {
                var rule = bake.rule;
                var cluster = bake.cluster;
                if (rule == null || cluster == null || bake.colors == null || bake.adjacency == null)
                    continue;
                int[] triangles = allTriangles[bake.subMesh];

                // Regions first so vertex rewriting knows every region's id and color.
                var regions = new CartoonColorRegion[cluster.regionCount];
                for (int r = 0; r < cluster.regionCount; r++)
                {
                    if (regionIdCursor > 255)
                    {
                        result.regionIdOverflow++;
                        continue;
                    }
                    var members = new List<int>();
                    bool protectedRegion = false;
                    for (int t = 0; t < cluster.triangleRegion.Length; t++)
                    {
                        if (cluster.triangleRegion[t] != r)
                            continue;
                        members.Add(t);
                        if (cluster.protectedTriangles != null && cluster.protectedTriangles[t])
                            protectedRegion = true;
                    }

                    Color linear = CartoonColorClustering.RepresentativeColor(
                        bake.colors, members, bake.adjacency.triangleAreas);
                    Color display = rule.useColorOverride
                        ? rule.colorOverride
                        : rule.manualPalette is { Length: > 0 }
                            ? SnapToPalette(linear, rule.manualPalette)
                            : ToDisplayColor(linear);

                    var region = new CartoonColorRegion
                    {
                        regionId = regionIdCursor++,
                        materialName = rule.materialName,
                        materialGuid = rule.materialGuid,
                        policy = rule.policy,
                        protectedRegion = protectedRegion,
                        triangleCount = members.Count,
                        surfaceArea = TotalArea(members, bake.adjacency.triangleAreas),
                        color = display
                    };
                    regions[r] = region;
                    newRegions.Add(region);
                }

                for (int t = 0; t < cluster.triangleRegion.Length; t++)
                {
                    int r = cluster.triangleRegion[t];
                    if (r >= regions.Length || regions[r] == null)
                        continue; // region id overflow guard
                    var region = regions[r];
                    var display32 = (Color32)region.color;
                    float idNormalized = region.regionId / 255f;
                    // 0.9 marks fixed-base (painted shadow) regions; the capture shader
                    // keeps them out of dynamic paint-layer classification.
                    float policyFlag = rule.fixedBaseLayer ? 0.9f : PolicyFlag(region.policy);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int old = triangles[t * 3 + corner];
                        triangles[t * 3 + corner] = ResolveCorner(old, region.regionId, idNormalized,
                            policyFlag, display32, positions, normals, tangents, uvs, boneWeights,
                            colors, regionInfo, shapes, vertexOwner, vertexCopies);
                    }
                }
            }

            if (shapes.Count > 0)
            {
                mesh.ClearBlendShapes();
                int finalCount = positions.Count;
                foreach (var shape in shapes)
                    foreach (var frame in shape.frames)
                        mesh.AddBlendShapeFrame(shape.name, frame.weight,
                            Pad(frame.deltaVertices, finalCount),
                            Pad(frame.deltaNormals, finalCount),
                            Pad(frame.deltaTangents, finalCount));
            }

            mesh.SetVertices(positions);
            if (normals != null)
                mesh.SetNormals(normals);
            if (tangents != null)
                mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            // TEXCOORD3 carries (regionId/255, policy flag); any source UV3 is documented as replaced.
            mesh.SetUVs(3, regionInfo);
            for (int channel = 0; channel < 8; channel++)
                if (uvs[channel] != null && channel != 3)
                    mesh.SetUVs(channel, uvs[channel]);
            if (boneWeights != null)
                mesh.boneWeights = boneWeights.ToArray();
            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
                mesh.SetTriangles(allTriangles[s], s, false);
            mesh.RecalculateBounds();

            result.mesh = mesh;
            result.regions = newRegions;
            result.vertexGrowth = positions.Count - originalVertexCount;
            return result;
        }

        // First region to claim a vertex keeps the original index; later regions get a
        // duplicated vertex with their own COLOR/TEXCOORD3. Deterministic for a fixed bake.
        private static int ResolveCorner(int old, int regionHash, float idNormalized, float policyFlag,
            Color32 display, List<Vector3> positions, List<Vector3> normals, List<Vector4> tangents,
            List<Vector4>[] uvs, List<BoneWeight> boneWeights, List<Color32> colors,
            List<Vector2> regionInfo, List<BlendShape> shapes, Dictionary<int, int> vertexOwner,
            Dictionary<(int vertex, int region), int> vertexCopies)
        {
            if (!vertexOwner.TryGetValue(old, out int owner))
            {
                vertexOwner[old] = regionHash;
                colors[old] = display;
                regionInfo[old] = new Vector2(idNormalized, policyFlag);
                return old;
            }
            if (owner == regionHash)
                return old;
            var key = (old, regionHash);
            if (vertexCopies.TryGetValue(key, out int copy))
                return copy;

            int next = positions.Count;
            positions.Add(positions[old]);
            normals?.Add(normals[old]);
            tangents?.Add(tangents[old]);
            for (int channel = 0; channel < 8; channel++)
                uvs[channel]?.Add(uvs[channel][old]);
            boneWeights?.Add(boneWeights[old]);
            colors.Add(display);
            regionInfo.Add(new Vector2(idNormalized, policyFlag));
            foreach (var shape in shapes)
                foreach (var frame in shape.frames)
                {
                    frame.deltaVertices.Add(frame.deltaVertices[old]);
                    frame.deltaNormals.Add(frame.deltaNormals[old]);
                    frame.deltaTangents.Add(frame.deltaTangents[old]);
                }
            vertexCopies[key] = next;
            return next;
        }

        private static List<BlendShape> ReadBlendShapes(Mesh source, int vertexCount)
        {
            var shapes = new List<BlendShape>();
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                var shape = new BlendShape { name = source.GetBlendShapeName(s) };
                for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                {
                    var dv = new Vector3[vertexCount];
                    var dn = new Vector3[vertexCount];
                    var dt = new Vector3[vertexCount];
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    shape.frames.Add(new BlendShapeFrame
                    {
                        weight = source.GetBlendShapeFrameWeight(s, f),
                        deltaVertices = new List<Vector3>(dv),
                        deltaNormals = new List<Vector3>(dn),
                        deltaTangents = new List<Vector3>(dt)
                    });
                }
                shapes.Add(shape);
            }
            return shapes;
        }

        private static T[] Pad<T>(List<T> source, int count)
        {
            if (source.Count == count)
                return source.ToArray();
            var output = new T[count];
            for (int i = 0; i < source.Count && i < count; i++)
                output[i] = source[i];
            return output;
        }

        private static float PolicyFlag(CartoonRegionPolicy policy) => policy switch
        {
            CartoonRegionPolicy.HeroDetail => 0.75f,
            CartoonRegionPolicy.Aggressive => 0.5f,
            _ => 0.25f
        };

        private static float TotalArea(List<int> members, float[] areas)
        {
            float total = 0;
            foreach (int t in members)
                total += areas[t];
            return total;
        }

        private static Color ToDisplayColor(Color linear)
        {
            return new Color(
                Mathf.LinearToGammaSpace(Mathf.Clamp01(linear.r)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(linear.g)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(linear.b)),
                Mathf.Clamp01(linear.a));
        }

        private static Color SnapToPalette(Color linear, Color[] palette)
        {
            CartoonColorClustering.LinearToOklab(linear.r, linear.g, linear.b,
                out float l, out float a, out float b);
            float best = float.MaxValue;
            Color match = palette[0];
            foreach (var candidate in palette)
            {
                Color candidateLinear = candidate.linear;
                CartoonColorClustering.LinearToOklab(candidateLinear.r, candidateLinear.g, candidateLinear.b,
                    out float cl, out float ca, out float cb);
                float d = (l - cl) * (l - cl) + (a - ca) * (a - ca) + (b - cb) * (b - cb);
                if (d < best)
                {
                    best = d;
                    match = candidate;
                }
            }
            return match;
        }
    }
}

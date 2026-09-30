using System;
using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Per-triangle sample color in OKLab space plus weighting data used by clustering.
    internal struct TriangleColor
    {
        public float L;
        public float A;
        public float B;
        public float alpha;
        public bool cutout; // sampled below the alpha cutoff; excluded from clustering
    }

    // Union-find clustering over triangle adjacency edges, ordered by color distance.
    // Regions never merge across materials; small regions merge into the neighbour with
    // the longest shared border; vivid identity colors are protected from forced merges.
    internal static class CartoonColorClustering
    {
        public static float SquaredDistance(in TriangleColor a, in TriangleColor b)
        {
            float dl = a.L - b.L;
            float da = a.A - b.A;
            float db = a.B - b.B;
            return dl * dl + da * da + db * db;
        }

        public static void LinearToOklab(float r, float g, float b, out float L, out float a, out float bOut)
        {
            float l_ = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
            float m_ = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
            float s_ = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;
            float l = Mathf.Pow(Mathf.Max(l_, 1e-5f), 1f / 3f);
            float m = Mathf.Pow(Mathf.Max(m_, 1e-5f), 1f / 3f);
            float s = Mathf.Pow(Mathf.Max(s_, 1e-5f), 1f / 3f);
            L = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
            a = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
            bOut = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;
        }

        public static void OklabToLinear(float L, float a, float b, out float r, out float g, out float bOut)
        {
            float l_ = L + 0.3963377774f * a + 0.2158037573f * b;
            float m_ = L - 0.1055613458f * a - 0.0638541728f * b;
            float s_ = L - 0.0894841775f * a - 1.2914855480f * b;
            float l = l_ * l_ * l_;
            float m = m_ * m_ * m_;
            float s = s_ * s_ * s_;
            r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            bOut = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;
        }

        public static Color LinearToSrgb(Color linear)
        {
            var c = new Color(Mathf.GammaToLinearSpace(linear.r), Mathf.GammaToLinearSpace(linear.g),
                Mathf.GammaToLinearSpace(linear.b), linear.a);
            return c;
        }

        public static float Chroma(in TriangleColor c) => Mathf.Sqrt(c.A * c.A + c.B * c.B);

        public sealed class Result
        {
            public int[] triangleRegion;
            public bool[] protectedTriangles;
            public List<int> regionTriangles;
            public int regionCount;
        }

        // distance: per-policy OKLab distance threshold (already multiplied).
        // maxRegions / minTriangles / protectedDistance per plan section 8.
        public static Result Cluster(IReadOnlyList<TriangleColor> colors, TriangleAdjacencyGraph adjacency,
            float distance, int maxRegions, int minTriangles, float protectedDistance)
        {
            int triangleCount = colors.Count;
            var parent = new int[triangleCount];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra == rb)
                    return;
                if (ra > rb)
                    (ra, rb) = (rb, ra);
                parent[rb] = ra;
            }

            // Phase 1: merge adjacent triangles below the distance threshold.
            var mergeable = new List<(float distance2, int a, int b)>();
            foreach (var edge in adjacency.edges)
            {
                float d2 = SquaredDistance(colors[edge.triangleA], colors[edge.triangleB]);
                mergeable.Add((d2, edge.triangleA, edge.triangleB));
            }
            mergeable.Sort((x, y) => x.distance2.CompareTo(y.distance2));
            float threshold2 = distance * distance;
            float protected2 = protectedDistance * protectedDistance;
            foreach (var (distance2, a, b) in mergeable)
            {
                if (distance2 > threshold2)
                    break;
                // Even below threshold, a vivid-vs-muted identity jump stays separate.
                if (distance2 > protected2 && (IsVivid(colors[a]) || IsVivid(colors[b])))
                    continue;
                Union(a, b);
            }

            // Phase 2: enforce minimum triangle count per region.
            EnforceMinimum(colors, adjacency, parent, Find, Union, minTriangles);
            // Phase 3: enforce the region-count cap by cheapest merges.
            EnforceMaxRegions(colors, adjacency, parent, Find, Union, maxRegions, protected2);

            var result = new Result
            {
                triangleRegion = new int[triangleCount],
                protectedTriangles = new bool[triangleCount],
                regionTriangles = new List<int>()
            };
            for (int t = 0; t < triangleCount; t++)
                result.protectedTriangles[t] = IsVivid(colors[t]);
            var rootToRegion = new Dictionary<int, int>();
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Find(t);
                if (!rootToRegion.TryGetValue(root, out int region))
                {
                    region = result.regionTriangles.Count;
                    rootToRegion.Add(root, region);
                    result.regionTriangles.Add(t);
                }
                result.triangleRegion[t] = region;
            }
            result.regionCount = result.regionTriangles.Count;
            return result;
        }

        private static bool IsVivid(in TriangleColor c) => Chroma(c) > CartoonRegionPolicyDefaults.VividChroma;

        private static void EnforceMinimum(IReadOnlyList<TriangleColor> colors, TriangleAdjacencyGraph adjacency,
            int[] parent, Func<int, int> find, Action<int, int> union, int minTriangles)
        {
            if (minTriangles <= 1)
                return;
            // Iterate until stable because merges can push other regions below minimum.
            for (int pass = 0; pass < 8; pass++)
            {
                var regionSizes = new Dictionary<int, int>();
                for (int t = 0; t < colors.Count; t++)
                {
                    int root = find(t);
                    regionSizes.TryGetValue(root, out int size);
                    regionSizes[root] = size + 1;
                }

                bool mergedAny = false;
                // Absorb small regions into the neighbour sharing the longest border.
                foreach (var (root, size) in SortedBySize(regionSizes))
                {
                    if (size >= minTriangles)
                        continue;
                    if (TryMergeSmall(colors, adjacency, parent, find, union, root, prefer: null))
                        mergedAny = true;
                }
                if (!mergedAny)
                    break;
            }
        }

        private static IEnumerable<KeyValuePair<int, int>> SortedBySize(Dictionary<int, int> regionSizes)
        {
            var items = new List<KeyValuePair<int, int>>(regionSizes);
            items.Sort((a, b) =>
            {
                int bySize = a.Value.CompareTo(b.Value);
                return bySize != 0 ? bySize : a.Key.CompareTo(b.Key);
            });
            return items;
        }

        private static bool TryMergeSmall(IReadOnlyList<TriangleColor> colors, TriangleAdjacencyGraph adjacency,
            int[] parent, Func<int, int> find, Action<int, int> union, int smallRoot, int? prefer)
        {
            int bestTriangle = -1;
            float bestBorder = -1f;
            float bestColor = float.MaxValue;
            foreach (var edge in adjacency.edges)
            {
                int rootA = find(edge.triangleA);
                int rootB = find(edge.triangleB);
                if (rootA == rootB)
                    continue;
                int smallSide = -1;
                int otherSide = -1;
                if (find(edge.triangleA) == smallRoot)
                    (smallSide, otherSide) = (edge.triangleA, edge.triangleB);
                else if (find(edge.triangleB) == smallRoot)
                    (smallSide, otherSide) = (edge.triangleB, edge.triangleA);
                else
                    continue;

                bool otherIsVivid = IsVivid(colors[otherSide]);
                if (otherIsVivid && prefer == null && SquaredDistance(colors[smallSide], colors[otherSide]) > 0.09f)
                    continue; // keep vivid identity colors intact

                float border = edge.edgeLength;
                float colorDist = SquaredDistance(colors[smallSide], colors[otherSide]);
                bool better = prefer != null
                    ? colorDist < bestColor
                    : border > bestBorder + 1e-6f ||
                      (Mathf.Abs(border - bestBorder) <= 1e-6f && colorDist < bestColor);
                if (!better)
                    continue;
                bestBorder = border;
                bestColor = colorDist;
                bestTriangle = otherSide;
            }

            if (bestTriangle < 0)
                return false;
            union(smallRoot == find(smallRoot) ? smallRoot : find(smallRoot),
                find(bestTriangle));
            return true;
        }

        private static void EnforceMaxRegions(IReadOnlyList<TriangleColor> colors, TriangleAdjacencyGraph adjacency,
            int[] parent, Func<int, int> find, Action<int, int> union, int maxRegions, float protected2)
        {
            for (int guard = 0; guard < 4096; guard++)
            {
                var regionIds = new HashSet<int>();
                for (int t = 0; t < colors.Count; t++)
                    regionIds.Add(find(t));
                if (regionIds.Count <= maxRegions)
                    return;

                // Cheapest adjacent pair first; vivid identity colors never merge above
                // the protected distance even when the cap is tight.
                float best = float.MaxValue;
                int bestA = -1;
                int bestB = -1;
                foreach (var edge in adjacency.edges)
                {
                    int rootA = find(edge.triangleA);
                    int rootB = find(edge.triangleB);
                    if (rootA == rootB)
                        continue;
                    float d2 = SquaredDistance(colors[edge.triangleA], colors[edge.triangleB]);
                    if (d2 > protected2 && (IsVivid(colors[edge.triangleA]) || IsVivid(colors[edge.triangleB])))
                        continue;
                    if (d2 < best)
                    {
                        best = d2;
                        bestA = rootA;
                        bestB = rootB;
                    }
                }
                if (bestA < 0)
                    return; // no legal merge left; accept exceeding the cap
                union(bestA, bestB);
            }
        }

        // Area-weighted mean in OKLab, converted back to linear. Cutout triangles sample
        // transparent texels and would pollute the average, so they are skipped unless the
        // region contains nothing else.
        public static Color RepresentativeColor(IReadOnlyList<TriangleColor> colors, IReadOnlyList<int> triangleIndices,
            IReadOnlyList<float> triangleAreas)
        {
            float l = 0, a = 0, b = 0, alpha = 0, weight = 0;
            foreach (int t in triangleIndices)
            {
                if (colors[t].cutout)
                    continue;
                float area = triangleAreas != null ? triangleAreas[t] : 1f;
                l += colors[t].L * area;
                a += colors[t].A * area;
                b += colors[t].B * area;
                alpha += colors[t].alpha * area;
                weight += area;
            }
            if (weight <= 0f)
            {
                foreach (int t in triangleIndices)
                {
                    float area = triangleAreas != null ? triangleAreas[t] : 1f;
                    l += colors[t].L * area;
                    a += colors[t].A * area;
                    b += colors[t].B * area;
                    alpha += colors[t].alpha * area;
                    weight += area;
                }
            }
            if (weight <= 0f)
                return Color.white;
            OklabToLinear(l / weight, a / weight, b / weight, out float r, out float g, out float bOut);
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(bOut),
                Mathf.Clamp01(alpha / weight));
        }
    }
}

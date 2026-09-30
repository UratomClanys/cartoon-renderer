using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Per-submesh triangle adjacency. Edges are keyed by quantized positions instead of
    // vertex indices so UV-seam duplicates still connect; cross-submesh edges are ignored
    // because regions never merge across materials.
    internal sealed class TriangleAdjacencyGraph
    {
        public struct AdjacentEdge
        {
            public int triangleA;
            public int triangleB;
            public float edgeLength;
        }

        public readonly int triangleCount;
        public readonly float[] triangleAreas;
        public readonly List<AdjacentEdge> edges = new();

        private TriangleAdjacencyGraph(int triangleCount, float[] triangleAreas)
        {
            this.triangleCount = triangleCount;
            this.triangleAreas = triangleAreas;
        }

        public static float ComputeCellSize(Bounds bounds) =>
            Mathf.Clamp(
                Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 1e-4f,
                1e-7f, 1e-3f);

        public static TriangleAdjacencyGraph Build(Mesh mesh, int subMesh, float cellSizeOverride = 0f)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            Vector3[] vertices = mesh.vertices;
            int triangleCount = triangles.Length / 3;
            var areas = new float[triangleCount];
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 a = vertices[triangles[t * 3]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                areas[t] = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }

            var graph = new TriangleAdjacencyGraph(triangleCount, areas);
            if (triangleCount == 0)
                return graph;

            float cellSize = cellSizeOverride > 0f ? cellSizeOverride : ComputeCellSize(mesh.bounds);
            // Canonical index per quantized cell: the first vertex seen in a cell speaks
            // for all vertices of that cell, collapsing UV-seam duplicates into one key.
            var cellToCanonical = new Dictionary<Vector3Int, int>(vertices.Length);
            var edgeOwners = new Dictionary<(int min, int max), (int tri, float length)>();

            int Canonical(int index)
            {
                Vector3 p = vertices[index];
                var cell = new Vector3Int(
                    Mathf.RoundToInt(p.x / cellSize),
                    Mathf.RoundToInt(p.y / cellSize),
                    Mathf.RoundToInt(p.z / cellSize));
                if (!cellToCanonical.TryGetValue(cell, out int canonical))
                {
                    cellToCanonical.Add(cell, index);
                    canonical = index;
                }
                return canonical;
            }

            for (int t = 0; t < triangleCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int v0 = triangles[t * 3 + e];
                    int v1 = triangles[t * 3 + (e + 1) % 3];
                    int c0 = Canonical(v0);
                    int c1 = Canonical(v1);
                    if (c0 == c1)
                        continue; // degenerate edge after quantization
                    var key = c0 < c1 ? (c0, c1) : (c1, c0);
                    float length = Vector3.Distance(vertices[v0], vertices[v1]);
                    if (!edgeOwners.TryGetValue(key, out var owner))
                        edgeOwners[key] = (t, length);
                    else if (owner.tri != t)
                        graph.edges.Add(new AdjacentEdge
                        {
                            triangleA = Mathf.Min(owner.tri, t),
                            triangleB = Mathf.Max(owner.tri, t),
                            edgeLength = Mathf.Max(owner.length, length)
                        });
                }
            }

            // Two-manifold interior edges produce exactly one adjacency pair; edges shared
            // by 3+ triangles (non-manifold) are added pairwise above, which is acceptable
            // for clustering purposes.
            return graph;
        }
    }
}

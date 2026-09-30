using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonTriangleAdjacencyTests
    {
        [Test]
        public void QuadTrianglesSharingEdgeAreAdjacent()
        {
            var mesh = TestMesh.Quad();
            var graph = TriangleAdjacencyGraph.Build(mesh, 0);
            Assert.AreEqual(2, graph.triangleCount);
            Assert.AreEqual(1, graph.edges.Count, "Two triangles sharing one edge produce one adjacency pair.");
        }

        [Test]
        public void UvSeamDuplicateVerticesStillConnect()
        {
            // Same quad geometry, but every triangle owns duplicate vertices at identical
            // positions (as UV seams produce). Position-quantized edge keys must connect them.
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0), Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                triangles = new[] { 0, 1, 2, 6, 5, 7 } // second triangle uses duplicated verts
            };
            var graph = TriangleAdjacencyGraph.Build(mesh, 0);
            Assert.AreEqual(1, graph.edges.Count, "Seam duplicates at identical positions must still be adjacent.");
        }

        [Test]
        public void DisconnectedTrianglesAreNotAdjacent()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(10, 10, 0), new Vector3(11, 10, 0), new Vector3(10, 11, 0) },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            var graph = TriangleAdjacencyGraph.Build(mesh, 0);
            Assert.AreEqual(0, graph.edges.Count);
        }

        [Test]
        public void TriangleAreasAreComputed()
        {
            var mesh = TestMesh.Quad();
            var graph = TriangleAdjacencyGraph.Build(mesh, 0);
            Assert.AreEqual(0.5f, graph.triangleAreas[0], 1e-4f, "Right triangle with unit legs has area 0.5.");
        }
    }

    internal static class TestMesh
    {
        public static Mesh Quad()
        {
            return new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                triangles = new[] { 0, 1, 2, 2, 1, 3 }
            };
        }

        public static Mesh Strip(int segments)
        {
            // Chain of `segments` quads along +X; each quad is two triangles sharing edges
            // with their neighbours, so the whole strip is one adjacency component.
            var vertices = new Vector3[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                vertices[i * 2] = new Vector3(i, 0, 0);
                vertices[i * 2 + 1] = new Vector3(i, 1, 0);
            }
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
            }
            return new Mesh { vertices = vertices, triangles = triangles.ToArray() };
        }

        public static Mesh Cube()
        {
            var mesh = new Mesh { name = "TestCube" };
            mesh.vertices = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0),
                new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 1), new Vector3(1, 1, 1)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 2, 3, 1, // -Z
                4, 5, 6, 6, 5, 7, // +Z
                0, 1, 4, 4, 1, 5, // -Y
                2, 6, 3, 3, 6, 7, // +Y
                0, 4, 2, 2, 4, 6, // -X
                1, 3, 5, 5, 3, 7  // +X
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

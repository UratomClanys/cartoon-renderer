using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonColorClusteringTests
    {
        private static TriangleColor Lab(float r, float g, float b, float alpha = 1f)
        {
            CartoonColorClustering.LinearToOklab(r, g, b, out float l, out float a, out float bOut);
            return new TriangleColor { L = l, A = a, B = bOut, alpha = alpha, cutout = false };
        }

        private static CartoonColorClustering.Result ClusterQuad(TriangleColor t0, TriangleColor t1,
            float distance = 0.075f, int maxRegions = 8, int minTriangles = 1)
        {
            var colors = new[] { t0, t1 };
            var mesh = TestMesh.Quad();
            var adjacency = TriangleAdjacencyGraph.Build(mesh, 0);
            return CartoonColorClustering.Cluster(colors, adjacency, distance, maxRegions, minTriangles, 0.18f);
        }

        [Test]
        public void AdjacentSameColorTrianglesMerge()
        {
            var result = ClusterQuad(Lab(0.8f, 0.8f, 0.8f), Lab(0.8f, 0.79f, 0.81f));
            Assert.AreEqual(1, result.regionCount);
            Assert.AreEqual(result.triangleRegion[0], result.triangleRegion[1]);
        }

        [Test]
        public void AdjacentDifferentColorTrianglesStaySeparate()
        {
            var result = ClusterQuad(Lab(0.1f, 0.1f, 0.1f), Lab(0.9f, 0.9f, 0.9f));
            Assert.AreEqual(2, result.regionCount);
        }

        [Test]
        public void NonAdjacentSameColorTrianglesDoNotMerge()
        {
            // Two separate quads (4 triangles, no shared edges) with identical colors.
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0),
                    new Vector3(10, 0, 0), new Vector3(11, 0, 0), new Vector3(10, 1, 0), new Vector3(11, 1, 0) },
                triangles = new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 }
            };
            var adjacency = TriangleAdjacencyGraph.Build(mesh, 0);
            Assert.AreEqual(0, adjacency.edges.Count);
            var color = Lab(0.5f, 0.5f, 0.5f);
            var colors = new[] { color, color, color, color };
            var result = CartoonColorClustering.Cluster(colors, adjacency, 0.075f, 8, 1, 0.18f);
            Assert.AreEqual(4, result.regionCount, "Merging must only happen across adjacency edges.");
        }

        [Test]
        public void SmallRegionMergesIntoLargerNeighbourWhenBelowMinimum()
        {
            // Quad: one triangle red, one white; white triangle must absorb the red one
            // because minTriangles = 2 and the red region has only one triangle.
            var result = ClusterQuad(Lab(0.9f, 0.1f, 0.1f), Lab(0.9f, 0.9f, 0.9f), minTriangles: 2);
            Assert.AreEqual(1, result.regionCount);
        }

        [Test]
        public void VividIdentityColorSurvivesMaxRegionPressure()
        {
            // Strip of 5 triangles in a row: white, white, red, white, white with maxRegions 2.
            // The vivid red must not be forced to merge when a cheaper white-white merge exists.
            var mesh = TestMesh.Strip(5);
            var adjacency = TriangleAdjacencyGraph.Build(mesh, 0);
            var colors = new List<TriangleColor>
            {
                Lab(0.9f, 0.9f, 0.9f), Lab(0.88f, 0.88f, 0.88f), Lab(0.9f, 0.05f, 0.05f),
                Lab(0.91f, 0.91f, 0.91f), Lab(0.89f, 0.89f, 0.89f)
            };
            var result = CartoonColorClustering.Cluster(colors, adjacency, 0.05f, 2, 1, 0.18f);
            bool redIsOwnRegion = false;
            for (int t = 0; t < colors.Count; t++)
                if (t == 2)
                    redIsOwnRegion = result.regionCount >= 2 &&
                                     result.triangleRegion[2] != result.triangleRegion[1] &&
                                     result.triangleRegion[2] != result.triangleRegion[3];
            Assert.IsTrue(redIsOwnRegion, "Vivid red identity region must survive the region-count cap.");
        }

        [Test]
        public void ClusteringIsDeterministicAcrossRuns()
        {
            var mesh = TestMesh.Strip(6);
            var adjacency = TriangleAdjacencyGraph.Build(mesh, 0);
            var colors = new List<TriangleColor>();
            for (int i = 0; i < 6; i++)
            {
                float v = (i % 3) * 0.3f + 0.2f;
                colors.Add(Lab(v, v * 0.9f, v * 1.1f));
            }
            var first = CartoonColorClustering.Cluster(colors, adjacency, 0.075f, 8, 1, 0.18f);
            var second = CartoonColorClustering.Cluster(colors, adjacency, 0.075f, 8, 1, 0.18f);
            Assert.AreEqual(first.regionCount, second.regionCount);
            for (int t = 0; t < colors.Count; t++)
                Assert.AreEqual(first.triangleRegion[t], second.triangleRegion[t],
                    "Region assignment must be stable across identical bakes.");
        }
    }
}

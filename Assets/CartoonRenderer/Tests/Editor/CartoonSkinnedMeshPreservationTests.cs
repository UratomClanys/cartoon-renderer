using NUnit.Framework;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonSkinnedMeshPreservationTests
    {
        [Test]
        public void BoneWeightsBindposesAndBlendShapesSurvive()
        {
            var source = CreateSkinnedCube();

            CartoonColorClustering.LinearToOklab(0.8f, 0.8f, 0.8f, out float l, out float a, out float b);
            var colors = new TriangleColor[source.triangles.Length / 3];
            for (int t = 0; t < colors.Length; t++)
                colors[t] = new TriangleColor { L = l, A = a, B = b, alpha = 1f, cutout = false };

            var adjacency = TriangleAdjacencyGraph.Build(source, 0);
            var cluster = CartoonColorClustering.Cluster(colors, adjacency, 0.075f, 8, 1, 0.18f);
            var bake = new SubMeshBake
            {
                subMesh = 0,
                rule = new CartoonMaterialRegionRule { materialName = "Skin", materialGuid = "g" },
                colors = colors,
                cluster = cluster,
                adjacency = adjacency
            };

            var result = CartoonRegionMeshBuilder.Build(source, new[] { bake });
            var generated = result.mesh;

            Assert.AreEqual(source.vertexCount, result.sourceVertexCount);
            Assert.AreEqual(source.blendShapeCount, generated.blendShapeCount, "BlendShapes must survive.");
            Assert.AreEqual(source.bindposes.Length, generated.bindposes.Length, "Bindposes must survive.");
            Assert.AreEqual(source.boneWeights.Length, generated.boneWeights.Length,
                "Bone weight count must match the final vertex count.");
            for (int i = 0; i < source.boneWeights.Length; i++)
                Assert.AreEqual(source.boneWeights[i], generated.boneWeights[i], $"Bone weight {i} must be unchanged.");
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                Assert.AreEqual(source.GetBlendShapeName(s), generated.GetBlendShapeName(s));
                Assert.AreEqual(source.GetBlendShapeFrameCount(s), generated.GetBlendShapeFrameCount(s));
                for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                    Assert.AreEqual(source.GetBlendShapeFrameWeight(s, f),
                        generated.GetBlendShapeFrameWeight(s, f), $"Frame weight {s}/{f} must be unchanged.");
            }
            for (int i = 0; i < source.bindposes.Length; i++)
                Assert.AreEqual(source.bindposes[i], generated.bindposes[i], $"Bindpose {i} must be unchanged.");
        }

        private static Mesh CreateSkinnedCube()
        {
            var source = TestMesh.Cube();
            var weights = new BoneWeight[source.vertexCount];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = new BoneWeight { boneIndex0 = i % 2, weight0 = 1f };
            source.boneWeights = weights;
            source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) };
            source.AddBlendShapeFrame("Bend", 30f,
                new[] { Vector3.up * 0.1f, Vector3.up * 0.1f, Vector3.zero, Vector3.zero,
                        Vector3.up * 0.1f, Vector3.up * 0.1f, Vector3.zero, Vector3.zero },
                null, null);
            return source;
        }
    }
}

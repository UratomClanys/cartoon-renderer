using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CartoonProjection.Editor
{
    public sealed class CartoonShapePrototypeTests
    {
        private static VisibilityBuffer MakeBuffer(int width, int height)
        {
            return new VisibilityBuffer(width, height);
        }

        private static void Fill(VisibilityBuffer buffer, int x0, int y0, int x1, int y1,
            int region, Color32 color, float depth = 5f)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int index = y * buffer.width + x;
                    buffer.regionIds[index] = region;
                    buffer.objectIds[index] = 1;
                    buffer.layerIds[index] = 1;
                    buffer.depth[index] = depth;
                    buffer.regionColor[index] = color;
                }
        }

        [Test]
        public void TwoSeparatedSameColorBlocksFormTwoComponents()
        {
            var buffer = MakeBuffer(40, 40);
            Fill(buffer, 2, 2, 10, 10, 1, new Color32(255, 0, 0, 255));
            Fill(buffer, 20, 20, 30, 30, 1, new Color32(255, 0, 0, 255));
            var shapes = CartoonShapeExtractor.Extract(buffer, new int[40 * 40]);
            Assert.AreEqual(2, shapes.Count, "Disconnected same-color pixels must be separate shapes.");
        }

        [Test]
        public void SingleBlockProducesOneOuterLoopNoHoles()
        {
            var buffer = MakeBuffer(40, 40);
            Fill(buffer, 5, 5, 20, 20, 1, new Color32(0, 255, 0, 255));
            var shapes = CartoonShapeExtractor.Extract(buffer, new int[40 * 40]);
            Assert.AreEqual(1, shapes.Count);
            Assert.AreEqual(1, shapes[0].loops.Count);
            Assert.IsFalse(shapes[0].loopIsHole[0]);
            Assert.AreEqual(16 * 16, shapes[0].pixelCount);
        }

        [Test]
        public void RingBlockProducesOuterLoopAndHole()
        {
            // 24x24 ring with a 8x8 hole in the middle.
            var buffer = MakeBuffer(40, 40);
            Fill(buffer, 4, 4, 27, 27, 2, new Color32(0, 0, 255, 255));
            Fill(buffer, 12, 12, 19, 19, -1, new Color32(0, 0, 0, 0));
            // Clear the hole pixels back to background.
            for (int y = 12; y <= 19; y++)
                for (int x = 12; x <= 19; x++)
                {
                    int index = y * 40 + x;
                    buffer.regionIds[index] = VisibilityBuffer.BackgroundRegion;
                    buffer.depth[index] = float.PositiveInfinity;
                }

            var shapes = CartoonShapeExtractor.Extract(buffer, new int[40 * 40]);
            Assert.AreEqual(1, shapes.Count);
            Assert.AreEqual(2, shapes[0].loops.Count, "Ring must have an outer loop and a hole loop.");
            int holes = 0;
            foreach (bool isHole in shapes[0].loopIsHole)
                if (isHole)
                    holes++;
            Assert.AreEqual(1, holes);

            // Redraw: the hole center must stay background.
            var redrawn = CartoonShapeRedrawer.Redraw(shapes, 40, 40, new Color32(255, 255, 255, 255));
            Assert.AreEqual(new Color32(255, 255, 255, 255), redrawn[15 * 40 + 15], "Hole center must remain background.");
            Assert.AreEqual(new Color32(0, 0, 255, 255), redrawn[6 * 40 + 6], "Ring body must be filled.");
        }

        [Test]
        public void SharedBoundarySimplifiesToIdenticalPolylines()
        {
            // Left red block and right green block share the vertical edge x=16.
            var buffer = MakeBuffer(40, 20);
            Fill(buffer, 4, 2, 15, 17, 1, new Color32(255, 0, 0, 255));
            Fill(buffer, 16, 2, 34, 17, 2, new Color32(0, 255, 0, 255));
            var shapes = CartoonShapeExtractor.Extract(buffer, new int[40 * 20]);
            Assert.AreEqual(2, shapes.Count);

            // Both polygons must contain an identical shared-boundary polyline.
            var redSegments = new List<(Vector2 a, Vector2 b)>();
            var greenSegments = new List<(Vector2 a, Vector2 b)>();
            foreach (var (a, b, type) in shapes[0].segments)
                if (type == SegmentType.SharedBoundary)
                    redSegments.Add((a, b));
            foreach (var (a, b, type) in shapes[1].segments)
                if (type == SegmentType.SharedBoundary)
                    greenSegments.Add((a, b));
            Assert.Greater(redSegments.Count, 0, "Red shape must report shared boundary segments.");
            Assert.AreEqual(redSegments.Count, greenSegments.Count);
            for (int i = 0; i < redSegments.Count; i++)
            {
                // Same geometry, opposite direction.
                Assert.AreEqual(redSegments[i].a, greenSegments[i].b, "Shared chain must match exactly (A.a == B.b).");
                Assert.AreEqual(redSegments[i].b, greenSegments[i].a, "Shared chain must match exactly (A.b == B.a).");
            }

            // Redraw must leave no background crack along the shared column.
            var redrawn = CartoonShapeRedrawer.Redraw(shapes, 40, 20, new Color32(255, 255, 255, 255));
            for (int y = 3; y <= 16; y++)
            {
                var pixel = redrawn[y * 40 + 15];
                Assert.IsFalse(pixel.r == 255 && pixel.g == 255 && pixel.b == 255,
                    $"No background crack allowed at shared boundary (x=15, y={y}).");
                pixel = redrawn[y * 40 + 16];
                Assert.IsFalse(pixel.r == 255 && pixel.g == 255 && pixel.b == 255,
                    $"No background crack allowed at shared boundary (x=16, y={y}).");
            }
        }

        [Test]
        public void SimplifyEpsilonReducesCornerCount()
        {
            // A 64x64 block has 256 corners on its pixel grid; with epsilon 4 the contour
            // collapses to the four block corners.
            var buffer = MakeBuffer(80, 80);
            Fill(buffer, 8, 8, 71, 71, 1, new Color32(255, 255, 0, 255));
            var labelBuffer = new int[80 * 80];

            CartoonShapePrototypeSettings.SimplifyEpsilon = 0.01f;
            var coarse = CartoonShapeExtractor.Extract(buffer, labelBuffer);
            int sourceCorners = coarse[0].loopSourceCornerCounts[0];
            int tightCorners = coarse[0].loops[0].Count;

            CartoonShapePrototypeSettings.SimplifyEpsilon = 4f;
            var simplified = CartoonShapeExtractor.Extract(buffer, labelBuffer);
            int looseCorners = simplified[0].loops[0].Count;
            CartoonShapePrototypeSettings.SimplifyEpsilon = 2f;

            Assert.AreEqual(256, sourceCorners, "Pixel-grid contour of a 64x64 block has 256 corners.");
            Assert.AreEqual(4, tightCorners, "Near-zero epsilon keeps the pixel staircase.");
            Assert.AreEqual(4, looseCorners);
            Assert.Less(looseCorners, sourceCorners, "Simplification must reduce vertex count.");
        }

        [Test]
        public void RdpKeepsEndpointsAndCorners()
        {
            var points = new List<Vector2>
            {
                new(0, 0), new(1, 0.02f), new(2, -0.02f), new(3, 0.03f), new(10, 0),
                new(10.03f, 1f), new(10, 10)
            };
            var simplified = CartoonContourSimplifier.SimplifyPolyline(points, 0.5f);
            Assert.AreEqual(points[0], simplified[0], "Start endpoint must be kept.");
            Assert.AreEqual(points[^1], simplified[^1], "End endpoint must be kept.");
            Assert.AreEqual(3, simplified.Count, "Corner at (10,0) must survive; collinear points dropped.");
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Re-draws the simplified 2D polygons with a scanline even-odd filler and renders a
    // wireframe view (outer edges black, shared boundaries red, holes blue) so the
    // shared-edge guarantee is directly visible.
    internal static class CartoonShapeRedrawer
    {
        public static Color32[] Redraw(List<ExtractedShape> shapes, int width, int height, Color32 background)
        {
            var canvas = new Color32[width * height];
            for (int i = 0; i < canvas.Length; i++)
                canvas[i] = background;

            // Far shapes first so nearer shapes own contested boundary pixels.
            var ordered = new List<ExtractedShape>(shapes);
            ordered.Sort((a, b) => b.avgDepth.CompareTo(a.avgDepth));

            foreach (var shape in ordered)
            {
                FillShape(canvas, shape, width, height);
            }
            return canvas;
        }

        private static void FillShape(Color32[] canvas, ExtractedShape shape, int width, int height)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (var loop in shape.loops)
            {
                foreach (var vertex in loop)
                {
                    minY = Mathf.Min(minY, vertex.y);
                    maxY = Mathf.Max(maxY, vertex.y);
                }
            }
            int startRow = Mathf.Clamp(Mathf.CeilToInt(minY), 0, height - 1);
            int endRow = Mathf.Clamp(Mathf.CeilToInt(maxY) - 1, 0, height - 1);

            var crossings = new List<float>();
            for (int y = startRow; y <= endRow; y++)
            {
                float rowCenter = y + 0.5f;
                crossings.Clear();
                foreach (var loop in shape.loops)
                {
                    int count = loop.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % count];
                        // Half-open rule avoids double counting at shared vertices.
                        if ((a.y <= rowCenter && b.y > rowCenter) || (b.y <= rowCenter && a.y > rowCenter))
                        {
                            float t = (rowCenter - a.y) / (b.y - a.y);
                            crossings.Add(a.x + t * (b.x - a.x));
                        }
                    }
                }
                if (crossings.Count < 2)
                    continue;
                crossings.Sort();
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    int from = Mathf.Clamp(Mathf.CeilToInt(crossings[c]), 0, width);
                    int to = Mathf.Clamp(Mathf.CeilToInt(crossings[c + 1]), 0, width);
                    for (int x = from; x < to; x++)
                        canvas[y * width + x] = shape.color;
                }
            }
        }

        public static Color32[] DrawWireframe(List<ExtractedShape> shapes, int width, int height)
        {
            var canvas = new Color32[width * height];
            var paper = new Color32(250, 250, 248, 255);
            for (int i = 0; i < canvas.Length; i++)
                canvas[i] = paper;

            foreach (var shape in shapes)
            {
                foreach (var (a, b, type) in shape.segments)
                {
                    Color32 color = type switch
                    {
                        SegmentType.SharedBoundary => new Color32(214, 48, 49, 255), // red: shared edge
                        SegmentType.Hole => new Color32(9, 132, 227, 255),           // blue: hole
                        _ => new Color32(20, 20, 24, 255)                            // black: outer
                    };
                    DrawLine(canvas, width, height, a, b, color);
                }
            }
            return canvas;
        }

        private static void DrawLine(Color32[] canvas, int width, int height, Vector2 a, Vector2 b, Color32 color)
        {
            int x0 = Mathf.RoundToInt(a.x);
            int y0 = Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x);
            int y1 = Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0);
            int dy = -Mathf.Abs(y1 - y0);
            int stepX = x0 < x1 ? 1 : -1;
            int stepY = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                    canvas[y0 * width + x0] = color;
                if (x0 == x1 && y0 == y1)
                    break;
                int error2 = error * 2;
                if (error2 >= dy)
                {
                    error += dy;
                    x0 += stepX;
                }
                if (error2 <= dx)
                {
                    error += dx;
                    y0 += stepY;
                }
            }
        }
    }
}

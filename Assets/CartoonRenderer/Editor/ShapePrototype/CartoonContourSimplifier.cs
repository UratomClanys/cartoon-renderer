using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Ramer-Douglas-Peucker simplification with screen-pixel epsilon. Endpoints are
    // always kept so segment joints and shared chain boundaries stay exact.
    internal static class CartoonContourSimplifier
    {
        public static List<Vector2> SimplifyPolyline(List<Vector2> points, float epsilon)
        {
            if (points.Count <= 2)
                return new List<Vector2>(points);
            var keep = new bool[points.Count];
            keep[0] = true;
            keep[^1] = true;
            var stack = new Stack<(int start, int end)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var (start, end) = stack.Pop();
                if (end <= start + 1)
                    continue;
                var a = points[start];
                var b = points[end];
                float maxLengthSquared = 0f;
                int farthest = -1;
                float lengthSquared = (b - a).sqrMagnitude;
                for (int i = start + 1; i < end; i++)
                {
                    float distanceSquared;
                    if (lengthSquared < 1e-10f)
                        distanceSquared = (points[i] - a).sqrMagnitude;
                    else
                    {
                        float t = Mathf.Clamp(Vector2.Dot(points[i] - a, b - a) / lengthSquared, 0f, 1f);
                        distanceSquared = (points[i] - (a + (b - a) * t)).sqrMagnitude;
                    }
                    if (distanceSquared > maxLengthSquared)
                    {
                        maxLengthSquared = distanceSquared;
                        farthest = i;
                    }
                }
                if (maxLengthSquared > epsilon * epsilon && farthest > 0)
                {
                    keep[farthest] = true;
                    stack.Push((start, farthest));
                    stack.Push((farthest, end));
                }
            }

            var output = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
                if (keep[i])
                    output.Add(points[i]);
            return output;
        }
    }
}

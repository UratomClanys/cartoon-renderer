using UnityEngine;

namespace CartoonProjection.Editor
{
    // CPU software rasterizer producing the five visibility buffers (object id, region
    // id, paint layer, depth, region color) plus a smooth-shaded "source" image. This is
    // the offline visual prototype path only; the runtime stays GPU-based.
    internal sealed class VisibilityBuffer
    {
        public readonly int width;
        public readonly int height;
        public readonly int[] objectIds;
        public readonly int[] regionIds;
        public readonly int[] layerIds;
        public readonly float[] depth;
        public readonly Color32[] regionColor;
        public readonly Color32[] smoothShaded;

        public const int BackgroundRegion = -1;

        public VisibilityBuffer(int width, int height)
        {
            this.width = width;
            this.height = height;
            int count = width * height;
            objectIds = new int[count];
            regionIds = new int[count];
            layerIds = new int[count];
            depth = new float[count];
            regionColor = new Color32[count];
            smoothShaded = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                regionIds[i] = BackgroundRegion;
                depth[i] = float.PositiveInfinity;
                regionColor[i] = new Color32(238, 240, 242, 255);
                smoothShaded[i] = new Color32(238, 240, 242, 255);
            }
        }

        public bool IsBackground(int index) => regionIds[index] == BackgroundRegion;
    }

    internal static class CartoonVisibilityRasterizer
    {
        private static readonly Vector3 LightDirection = new(-0.45f, 0.75f, -0.5f); // toward light

        // No backface culling: the prototype uses only a few dozen triangles and the
        // depth test resolves front/back, so winding conventions cannot bite.
        public static VisibilityBuffer Rasterize(PrototypeScene scene, Vector3 cameraPosition,
            Vector3 cameraForward, Vector3 cameraUp, float fovDegrees, int width, int height)
        {
            var buffer = new VisibilityBuffer(width, height);
            cameraForward = cameraForward.normalized;
            Vector3 right = Vector3.Cross(cameraUp, cameraForward).normalized; // Unity LookRotation convention
            float aspect = (float)width / height;
            float tanHalfFov = Mathf.Tan(fovDegrees * Mathf.Deg2Rad * 0.5f);

            foreach (var triangle in scene.triangles)
            {
                var p0 = ToScreen(triangle.a, cameraPosition, right, cameraUp, cameraForward, tanHalfFov, aspect, width, height, out float z0);
                var p1 = ToScreen(triangle.b, cameraPosition, right, cameraUp, cameraForward, tanHalfFov, aspect, width, height, out float z1);
                var p2 = ToScreen(triangle.c, cameraPosition, right, cameraUp, cameraForward, tanHalfFov, aspect, width, height, out float z2);
                if (z0 <= 0.01f || z1 <= 0.01f || z2 <= 0.01f)
                    continue; // near plane clip: prototype camera keeps everything in front

                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));

                float shade0 = Shading(triangle.na);
                float shade1 = Shading(triangle.nb);
                float shade2 = Shading(triangle.nc);
                float area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                if (Mathf.Abs(area) < 1e-9f)
                    continue;
                float invArea = 1f / area;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        float w0 = ((p1.x - px) * (p2.y - py) - (p2.x - px) * (p1.y - py)) * invArea;
                        float w1 = ((p2.x - px) * (p0.y - py) - (p0.x - px) * (p2.y - py)) * invArea;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f)
                            continue;
                        float viewDepth = w0 * z0 + w1 * z1 + w2 * z2;
                        int index = y * width + x;
                        if (viewDepth >= buffer.depth[index])
                            continue;
                        buffer.depth[index] = viewDepth;
                        buffer.objectIds[index] = 1;
                        buffer.regionIds[index] = triangle.region;
                        buffer.layerIds[index] = 1; // base layer (BaseOnly prototype)
                        buffer.regionColor[index] = triangle.color;
                        float shade = Mathf.Clamp01(w0 * shade0 + w1 * shade1 + w2 * shade2);
                        buffer.smoothShaded[index] = Multiply(triangle.color, shade);
                    }
                }
            }
            return buffer;
        }

        // Continuous NdotL + ambient so the "original" stage shows the smooth PBR-like
        // gradient the shape pipeline is supposed to replace. Two-sided: prototype
        // triangle normals are not windings-checked.
        private static float Shading(Vector3 normal)
        {
            float ndotl = Mathf.Abs(Vector3.Dot(normal, LightDirection.normalized));
            return Mathf.Clamp01(0.35f + 0.65f * ndotl);
        }

        private static Color32 Multiply(Color32 color, float shade)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(color.r * shade),
                (byte)Mathf.RoundToInt(color.g * shade),
                (byte)Mathf.RoundToInt(color.b * shade), 255);
        }

        private static Vector2 ToScreen(Vector3 world, Vector3 cameraPosition, Vector3 right, Vector3 up,
            Vector3 forward, float tanHalfFov, float aspect, int width, int height, out float viewDepth)
        {
            Vector3 local = world - cameraPosition;
            viewDepth = Vector3.Dot(local, forward);
            float ndcX = Vector3.Dot(local, right) / (viewDepth * tanHalfFov * aspect);
            float ndcY = Vector3.Dot(local, up) / (viewDepth * tanHalfFov);
            return new Vector2(
                (ndcX * 0.5f + 0.5f) * width,
                (0.5f - ndcY * 0.5f) * height); // screen space, y down
        }
    }
}

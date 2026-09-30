using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // Programmatic prototype scene: a small tower with three flat regions (light tower
    // body, red-brown roof, dark base), a real see-through arch hole, and a strong
    // light/dark contrast between region colors. No Unity scene objects are created.
    internal sealed class PrototypeScene
    {
        public struct Triangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public Vector3 na;
            public Vector3 nb;
            public Vector3 nc;
            public Color32 color;
            public int region;
        }

        public readonly List<Triangle> triangles = new();

        public static Color32 TowerBodyColor = new(224, 214, 190, 255); // warm off-white
        public static Color32 RoofColor = new(146, 68, 52, 255);        // red-brown
        public static Color32 BaseColor = new(84, 86, 92, 255);         // dark grey

        public static PrototypeScene CreateTower()
        {
            var scene = new PrototypeScene();
            // Dark base plinth, split so the arch hole runs all the way to the ground.
            AddBox(scene, new Vector3(-1.025f, 0.25f, 0f), new Vector3(0.95f, 0.5f, 2f), BaseColor, 3);
            AddBox(scene, new Vector3(1.025f, 0.25f, 0f), new Vector3(0.95f, 0.5f, 2f), BaseColor, 3);
            // Tower body split into three boxes so the arch is a real see-through hole
            // (x in [-0.55, 0.55] is empty from z=-0.6 to z=+0.6).
            AddBox(scene, new Vector3(-0.775f, 1.85f, 0f), new Vector3(0.45f, 2.7f, 1.2f), TowerBodyColor, 1);
            AddBox(scene, new Vector3(0.775f, 1.85f, 0f), new Vector3(0.45f, 2.7f, 1.2f), TowerBodyColor, 1);
            AddBox(scene, new Vector3(0f, 2.9f, 0f), new Vector3(2f, 0.6f, 1.2f), TowerBodyColor, 1);
            // Red-brown pyramid roof.
            AddPyramid(scene, new Vector3(0f, 3.2f, 0f), 2.5f, 1.3f, RoofColor, 2);
            return scene;
        }

        private static void AddBox(PrototypeScene scene, Vector3 center, Vector3 size, Color32 color, int region)
        {
            Vector3 half = size * 0.5f;
            var corners = new Vector3[8];
            int index = 0;
            for (int z = 0; z < 2; z++)
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        corners[index++] = center + new Vector3(
                            (x * 2 - 1) * half.x, (y * 2 - 1) * half.y, (z * 2 - 1) * half.z);

            // Corner order: index bit0 = -x/+x, bit1 = -y/+y, bit2 = -z/+z.
            int P(int x, int y, int z) => x | (y << 1) | (z << 2);
            AddQuad(scene, corners[P(0, 0, 0)], corners[P(1, 0, 0)], corners[P(1, 1, 0)], corners[P(0, 1, 0)],
                Vector3.back, color, region);   // -Z face
            AddQuad(scene, corners[P(1, 0, 1)], corners[P(0, 0, 1)], corners[P(0, 1, 1)], corners[P(1, 1, 1)],
                Vector3.forward, color, region); // +Z face
            AddQuad(scene, corners[P(0, 0, 1)], corners[P(0, 0, 0)], corners[P(0, 1, 0)], corners[P(0, 1, 1)],
                Vector3.left, color, region);    // -X face
            AddQuad(scene, corners[P(1, 0, 0)], corners[P(1, 0, 1)], corners[P(1, 1, 1)], corners[P(1, 1, 0)],
                Vector3.right, color, region);   // +X face
            AddQuad(scene, corners[P(0, 0, 0)], corners[P(0, 0, 1)], corners[P(1, 0, 1)], corners[P(1, 0, 0)],
                Vector3.down, color, region);    // -Y face
            AddQuad(scene, corners[P(0, 1, 1)], corners[P(0, 1, 0)], corners[P(1, 1, 0)], corners[P(1, 1, 1)],
                Vector3.up, color, region);      // +Y face
        }

        private static void AddQuad(PrototypeScene scene, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 normal, Color32 color, int region)
        {
            scene.triangles.Add(new Triangle { a = a, b = b, c = c, na = normal, nb = normal, nc = normal, color = color, region = region });
            scene.triangles.Add(new Triangle { a = a, b = c, c = d, na = normal, nb = normal, nc = normal, color = color, region = region });
        }

        private static void AddPyramid(PrototypeScene scene, Vector3 baseCenter, float baseSize, float height,
            Color32 color, int region)
        {
            float half = baseSize * 0.5f;
            var apex = baseCenter + Vector3.up * height;
            var p0 = baseCenter + new Vector3(-half, 0, -half);
            var p1 = baseCenter + new Vector3(half, 0, -half);
            var p2 = baseCenter + new Vector3(half, 0, half);
            var p3 = baseCenter + new Vector3(-half, 0, half);
            AddSide(scene, p0, p1, apex, color, region);
            AddSide(scene, p1, p2, apex, color, region);
            AddSide(scene, p2, p3, apex, color, region);
            AddSide(scene, p3, p0, apex, color, region);
        }

        private static void AddSide(PrototypeScene scene, Vector3 a, Vector3 b, Vector3 apex,
            Color32 color, int region)
        {
            Vector3 normal = Vector3.Cross(apex - a, b - a).normalized;
            scene.triangles.Add(new Triangle
            {
                a = a,
                b = b,
                c = apex,
                na = normal,
                nb = normal,
                nc = (apex - Vector3.Lerp(a, b, 0.5f)).normalized,
                color = color,
                region = region
            });
        }
    }
}

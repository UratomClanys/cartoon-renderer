using System;
using UnityEngine;

namespace CartoonProjection.Editor
{
    // GPU-blits a possibly-unreadable texture into a linear-space CPU pixel cache.
    // Never touches the source TextureImporter; every temporary resource is released.
    internal sealed class CartoonTextureSampler : IDisposable
    {
        private readonly Color[] pixels;
        public readonly int width;
        public readonly int height;
        public readonly bool repeatWrap;

        private CartoonTextureSampler(Color[] pixels, int width, int height, bool repeatWrap)
        {
            this.pixels = pixels;
            this.width = width;
            this.height = height;
            this.repeatWrap = repeatWrap;
        }

        public static CartoonTextureSampler Create(Texture2D source)
        {
            if (source == null)
                return null;

            int width = source.width;
            int height = source.height;
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBFloat, 0)
            {
                sRGB = false,
                useMipMap = false
            };
            RenderTexture temporary = RenderTexture.GetTemporary(descriptor);
            Graphics.Blit(source, temporary);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temporary;
            var readable = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            try
            {
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            Color[] pixels = readable.GetPixels();
            UnityEngine.Object.DestroyImmediate(readable);
            return new CartoonTextureSampler(pixels, width, height,
                source.wrapMode == TextureWrapMode.Repeat);
        }

        // Bilinear sample in linear space; wrap or clamp follows the source texture.
        public Color SampleBilinear(float u, float v)
        {
            if (repeatWrap)
            {
                u -= Mathf.Floor(u);
                v -= Mathf.Floor(v);
            }
            else
            {
                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);
            }

            float fx = u * (width - 1);
            float fy = v * (height - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, height - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = fx - x0;
            float ty = fy - y0;

            Color c00 = pixels[y0 * width + x0];
            Color c10 = pixels[y0 * width + x1];
            Color c01 = pixels[y1 * width + x0];
            Color c11 = pixels[y1 * width + x1];
            return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
        }

        public void Dispose()
        {
            // Pixels are a managed array; nothing unmanaged to release.
        }
    }
}

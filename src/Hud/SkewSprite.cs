using System.Collections.Generic;
using UnityEngine;

namespace BoneAndEmber
{
    // A parallelogram plate drawn at runtime: a rectangle with both vertical
    // edges leaned by the same angle. uGUI has no shear, and a 9-sliced sprite
    // would keep its caps at a fixed pixel width so the angle would change with
    // the plate's height. So the texture is generated at the exact pixel size it
    // will be drawn at and used unsliced. Cached by size, they're tiny.
    internal static class SkewSprite
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        internal static Sprite Get(int width, int height, float angleDeg)
        {
            string key = width + "x" + height + "@" + angleDeg.ToString("F1");
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            float slant = Mathf.Tan(angleDeg * Mathf.Deg2Rad) * height;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // Bottom row shifted left, top row shifted right, by the slant.
                float shift = (y / (float)(height - 1) - 0.5f) * slant;
                float left = Mathf.Abs(slant) * 0.5f + shift;
                float right = width - Mathf.Abs(slant) * 0.5f + shift;
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(Mathf.Min(x + 0.5f - left, right - (x + 0.5f)) + 0.5f);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "BoneAndEmber_Skew_" + key;
            Cache[key] = sprite;
            return sprite;
        }
    }
}

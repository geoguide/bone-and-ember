using System.Collections.Generic;
using UnityEngine;

namespace BoneAndEmber
{
    // A rectangle with its right end cut at an angle, drawn at the exact pixel
    // size it will be used at, for masking a bar. Positive angle leans the cut
    // the same way a positive NotchAngle leans a divider (top toward the left),
    // so the seam and the end cap agree. Cached by size and angle.
    internal static class EndCapSprite
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        internal static Sprite Get(int width, int height, float angleDeg)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
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
                float t = height > 1 ? y / (float)(height - 1) : 0f; // 0 bottom, 1 top
                float cut = slant >= 0f ? slant * t : -slant * (1f - t);
                float right = width - cut;
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(right - (x + 0.5f) + 0.5f);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "BoneAndEmber_EndCap_" + key;
            Cache[key] = sprite;
            return sprite;
        }
    }
}

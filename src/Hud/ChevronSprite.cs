using System.Collections.Generic;
using UnityEngine;

namespace BoneAndEmber
{
    // A small solid triangle pointing right, drawn at the pixel size it will be
    // used at. Flip it with localScale.x = -1 to point left. Used by the compass
    // strip to mark a death marker that has been clamped to one end. Cached by
    // size, same as the other runtime sprites here.
    internal static class ChevronSprite
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        internal static Sprite Get(int width, int height)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
            string key = width + "x" + height;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    // Apex at the right edge, base down the left edge: inside when
                    // the distance from the midline is under the half-height at u.
                    float halfHeight = (1f - u) * 0.5f;
                    float inside = halfHeight - Mathf.Abs(v - 0.5f);
                    float a = Mathf.Clamp01(inside * height + 0.5f);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "BoneAndEmber_Chevron_" + key;
            Cache[key] = sprite;
            return sprite;
        }
    }
}

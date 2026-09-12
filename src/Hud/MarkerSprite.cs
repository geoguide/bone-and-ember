using System.Collections.Generic;
using UnityEngine;

namespace BoneAndEmber
{
    // The sense marker shape (docs/design/006-sense.md): a narrow triangle with
    // its apex at the bottom, so it sits above a thing and points down at it.
    //
    // This replaces the diamond the first two builds used. Valheim's own rain,
    // mist and ember particles are small bright diamonds and squares, so a
    // diamond marker was competing with the weather on exactly the terms the
    // weather wins. A tall sharp spike pointing down is unmistakably an
    // instruction rather than a mote, and nothing in the game emits that shape.
    //
    // Built from a signed distance to the triangle's three edges, which is what
    // lets the same geometry produce the filled icon, the hollow outline and the
    // grown dark backing with a genuinely uniform stroke width.
    internal static class MarkerSprite
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // width/height are the triangle itself. grow expands it outward by that
        // many pixels, for the dark backing. hollow > 0 keeps only a border that
        // many pixels thick. The apex always lands "grow" pixels up from the
        // bottom edge; ApexPivot reports where, so the caller can pin the point
        // of the spike to the thing it marks.
        internal static Sprite Get(int width, int height, int grow, int hollow)
        {
            width = Mathf.Max(3, width);
            height = Mathf.Max(3, height);
            grow = Mathf.Max(0, grow);
            hollow = Mathf.Max(0, hollow);

            string key = width + "x" + height + "g" + grow + "h" + hollow;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            int w = width + grow * 2;
            int h = height + grow * 2;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            // Apex at the bottom centre, base across the top, both inset by grow.
            float apexX = w * 0.5f;
            float apexY = grow;
            float topY = h - grow;
            float halfBase = width * 0.5f;

            // Inward normals for the two slanted edges. The edge runs from the
            // apex up to a top corner; halfBase across and (height) up.
            float nx = height;
            float ny = halfBase;
            float nl = Mathf.Sqrt(nx * nx + ny * ny);
            if (nl < 0.0001f) nl = 1f;

            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f;
                    float dx = px - apexX;
                    float dy = py - apexY;

                    float left = (dx * nx + dy * ny) / nl;
                    float right = (-dx * nx + dy * ny) / nl;
                    float top = topY - py;
                    float d = Mathf.Min(left, Mathf.Min(right, top));

                    float a;
                    if (hollow > 0)
                    {
                        // A band hugging the edge: inside the shape, but not deeper
                        // than the stroke thickness.
                        a = Mathf.Clamp01(d + 0.5f) - Mathf.Clamp01(d - hollow + 0.5f);
                    }
                    else
                    {
                        a = Mathf.Clamp01(d + grow + 0.5f);
                    }

                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, ApexPivot(height, grow)), 100f);
            sprite.name = "BoneAndEmber_Marker_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        // Where the apex sits as a fraction of the sprite's height, so the caller
        // can set the RectTransform pivot and have the point land exactly on the
        // projected world position.
        internal static float ApexPivot(int height, int grow)
        {
            float h = height + grow * 2;
            return h <= 0f ? 0f : grow / h;
        }

        internal static int Width(int width, int grow)
        {
            return Mathf.Max(3, width) + grow * 2;
        }

        internal static int Height(int height, int grow)
        {
            return Mathf.Max(3, height) + grow * 2;
        }
    }
}

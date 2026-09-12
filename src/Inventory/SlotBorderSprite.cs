using System.Collections.Generic;
using UnityEngine;

namespace BoneAndEmber
{
    // A thin hollow square frame, generated at runtime and 9-sliced so it stays
    // a constant thickness no matter what size a slot's plate turns out to be.
    // Same "no bundled art" approach as ChipSprite and EndCapSprite: paint a
    // Texture2D once, slice it, cache it. One sprite, tinted to Palette.Bone (or
    // whatever the caller wants) at each use site via Image.color.
    internal static class SlotBorderSprite
    {
        private const int Size = 16;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        internal static Sprite Get()
        {
            return Get(1);
        }

        internal static Sprite Get(int thickness)
        {
            thickness = Mathf.Clamp(thickness, 1, Size / 2 - 1);
            Sprite cached;
            if (Cache.TryGetValue(thickness, out cached) && cached != null) return cached;

            int Thickness = thickness;

            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32 opaque = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(255, 255, 255, 0);
            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool border = x < Thickness || x >= Size - Thickness || y < Thickness || y >= Size - Thickness;
                    pixels[y * Size + x] = border ? opaque : clear;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(Thickness, Thickness, Thickness, Thickness));
            sprite.name = "BoneAndEmber_SlotBorder_" + Thickness + "px";
            Cache[Thickness] = sprite;
            return sprite;
        }
    }
}

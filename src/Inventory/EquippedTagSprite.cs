using UnityEngine;

namespace BoneAndEmber
{
    // A right triangle filling the top-right corner of a square texture, for
    // the equipped marker. Same runtime-generation approach as ChipSprite and
    // EndCapSprite: no bundled art, paint a Texture2D once, cache the sprite.
    // Hard-edged, not antialiased like ChipSprite's corner cut: a small corner
    // flag reads as a deliberate flat shape at this size, matching the
    // "squared type, thin edges" language from 001 better than a soft blend.
    internal static class EquippedTagSprite
    {
        private const int Size = 32;

        private static Sprite _sprite;

        internal static Sprite Get()
        {
            if (_sprite != null) return _sprite;

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
                    // Texture y=0 is the bottom. The anti-diagonal from
                    // top-left to bottom-right splits the square in half;
                    // the far side (large x, large y together) is top-right.
                    bool topRight = (x + y) >= Size - 1;
                    pixels[y * Size + x] = topRight ? opaque : clear;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            _sprite.name = "BoneAndEmber_EquippedTag";
            return _sprite;
        }
    }
}

using UnityEngine;

namespace BoneAndEmber
{
    // A small 9-sliced sprite with the top-right corner clipped at 45 degrees,
    // generated at runtime. There's no AssetBundle in this pipeline yet (see the
    // font open question in the spec), so this is how we get a clipped-corner
    // plate without shipping art: draw it into a Texture2D once, slice it so the
    // corner stays crisp at any chip size.
    internal static class ChipSprite
    {
        private const int Size = 32;
        private const int Cut = 8;

        private static Sprite _sprite;

        internal static Sprite Get()
        {
            if (_sprite != null) return _sprite;

            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Texture y=0 is the bottom. Cut the top-right corner: inside the
                    // Cut x Cut corner square, everything past the diagonal is clear,
                    // with a one pixel soft edge so the slope isn't jagged.
                    float dx = x - (Size - Cut);
                    float dy = y - (Size - Cut);
                    float alpha = 1f;
                    if (dx >= 0f && dy >= 0f)
                    {
                        alpha = Mathf.Clamp01((Cut - 1) - (dx + dy) + 0.5f);
                    }
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(Cut, Cut, Cut, Cut));
            _sprite.name = "BoneAndEmber_ChipSprite";
            return _sprite;
        }
    }
}

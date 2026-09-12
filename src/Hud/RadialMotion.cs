using UnityEngine;
using Valheim.UI;

namespace BoneAndEmber
{
    // Slice 9 (docs/design/003-inventory.md): fades the wedge's ember hover
    // fill in instead of the instant _Selected snap vanilla draws for
    // mouse/keyboard. There is no wedge GameObject or outline sprite to
    // animate (003-notes.md, "Radial") - the shader's own continuous
    // _Hovering float is the only thing that can move smoothly, and it's
    // normally only driven by vanilla's private, gamepad-only hover-select
    // tween. We drive it ourselves off RadialBase.Selected instead of
    // reflecting into that private animation manager.
    //
    // Unverified until tested in game: whether the shader actually blends
    // visually off _Hovering outside gamepad hover-select mode, or hard
    // branches on _Selected regardless. If this doesn't visibly fade, the
    // fallback is to stop trusting the shader's own blend and tween the
    // resolved hover color's alpha directly instead.
    internal class RadialMotion : MonoBehaviour
    {
        private const float FadeMs = 80f;

        private RadialBase _radial;
        private RadialMenuElement _current;
        private RadialMenuElement _fadingOut;
        private Tween _inTween;
        private Tween _outTween;

        private void Awake()
        {
            _radial = GetComponent<RadialBase>();
        }

        private void Update()
        {
            if (_radial == null || !Plugin.RadialEnabled.Value) return;

            // Held every frame, not only at ConstructRadial: vanilla rescales
            // the radial on its own schedule (RadialOverlapPreventerPatch) and
            // this also makes the Configuration Manager slider live.
            RadialStyle.ApplyScale(_radial);

            RadialMenuElement selected = _radial.Selected;
            if (selected != _current)
            {
                if (_current != null)
                {
                    _fadingOut = _current;
                    _outTween = _inTween; // continue from wherever the in-fade currently sits
                    Retarget(ref _outTween, 0f);
                }

                _current = selected;
                if (selected != null)
                {
                    _inTween = default;
                    Retarget(ref _inTween, 1f);
                }
            }

            float dt = Time.deltaTime;
            if (_current != null) ApplyHover(_current, _inTween.Update(dt));
            if (_fadingOut != null)
            {
                float value = _outTween.Update(dt);
                ApplyHover(_fadingOut, value);
                if (_outTween.Done) _fadingOut = null;
            }
        }

        private static void Retarget(ref Tween tween, float target)
        {
            if (Plugin.MotionEnabled.Value) tween.Retarget(target, FadeMs);
            else tween.Snap(target);
        }

        private static void ApplyHover(RadialMenuElement element, float value)
        {
            if (element == null) return;
            Material mat = element.BackgroundMaterial;
            if (mat == null) return;
            element.Hovering = value;
        }

        private void OnDisable()
        {
            _current = null;
            _fadingOut = null;
        }
    }
}

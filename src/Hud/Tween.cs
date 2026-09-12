using UnityEngine;

namespace BoneAndEmber
{
    // Slice 9 (docs/design/003-inventory.md): a float lerp over N milliseconds
    // with quadratic ease-out, stepped from whichever Update/Refresh the owner
    // already runs every frame. No coroutines, no central ticker - every owner
    // keeps its own Tween fields and calls Update(dt) itself.
    internal struct Tween
    {
        private float _from;
        private float _to;
        private float _duration;
        private float _time;

        internal float Value { get; private set; }
        internal bool Done => _time >= _duration;

        // Jumps straight to a value with nothing in flight, for building and
        // for the Motion.Enabled=false instant path.
        internal void Snap(float value)
        {
            _from = value;
            _to = value;
            Value = value;
            _time = 0f;
            _duration = 0f;
        }

        internal void Start(float from, float to, float durationMs)
        {
            _from = from;
            _to = to;
            Value = from;
            _duration = Mathf.Max(0.0001f, durationMs / 1000f);
            _time = 0f;
        }

        // Same target: no-op, so callers can retarget every frame without
        // restarting the easing curve. New target: continues from wherever
        // the tween currently sits instead of popping back to the old start.
        internal void Retarget(float to, float durationMs)
        {
            if (Mathf.Approximately(_to, to)) return;
            Start(Value, to, durationMs);
        }

        internal float Update(float dt)
        {
            _time = Mathf.Min(_time + dt, _duration);
            float t = _duration <= 0f ? 1f : _time / _duration;
            Value = Mathf.LerpUnclamped(_from, _to, EaseOut(t));
            return Value;
        }

        private static float EaseOut(float t)
        {
            float u = 1f - t;
            return 1f - u * u;
        }
    }
}

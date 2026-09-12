using UnityEngine;

namespace BoneAndEmber
{
    internal static class HudPath
    {
        // Full hierarchy path of a transform, for log lines. Every "we hid X" and
        // "we created Y under Z" message goes through this so a wrong guess about
        // the hierarchy is visible in the log instead of on screen.
        internal static string Of(Transform t)
        {
            if (t == null) return "(null)";

            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}

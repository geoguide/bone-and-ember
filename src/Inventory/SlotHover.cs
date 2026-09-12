using UnityEngine;
using UnityEngine.EventSystems;

namespace BoneAndEmber
{
    // Which slot the mouse is over, answered by Unity's own event system
    // rather than by us.
    //
    // The first version hit-tested with RectTransformUtility.RectangleContains
    // ScreenPoint and a null camera, which is only correct on a Screen Space
    // Overlay canvas. On anything else that test misreports, and it did: moving
    // the d-pad lit up slot after slot because the mouse test was matching
    // rects it shouldn't. Pointer enter/exit is routed by the EventSystem with
    // the right camera whatever the canvas mode is, so it cannot drift.
    internal class SlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        internal static InventoryElement Current { get; private set; }

        // The last slot the pointer was actually over. Pinning reads this
        // rather than Current, because reaching for the console or the
        // screenshot key clears the live hover before the command can run.
        internal static InventoryElement Last { get; private set; }

        // Slice 9: which slot the pointer is held down on, for the press
        // border brighten in SlotStyle.RefreshFilterDim. Cleared on release
        // or on leaving the slot, same as Current.
        internal static InventoryElement Pressed { get; private set; }

        internal InventoryElement Element;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Element != null)
            {
                Current = Element;
                Last = Element;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Current == Element) Current = null;
            if (Pressed == Element) Pressed = null;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Element != null) Pressed = Element;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Pressed == Element) Pressed = null;
        }

        private void OnDisable()
        {
            // Elements are destroyed and rebuilt whenever the grid resizes, so
            // a stale reference here would outlive its slot.
            if (Current == Element) Current = null;
            if (Last == Element) Last = null;
            if (Pressed == Element) Pressed = null;
        }
    }
}

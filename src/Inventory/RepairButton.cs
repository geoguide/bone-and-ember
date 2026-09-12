using UnityEngine;
using UnityEngine.EventSystems;

namespace BoneAndEmber
{
    // Click handling for the repair line's plate. Our own handler rather than
    // a Button, for the same reason FilterChip is: a Button added at runtime
    // has no targetGraphic wired, so its ColorBlock states do nothing and the
    // press reads as dead. The plate itself is the raycast target, and
    // RepairLine turns that off whenever there is nothing to repair.
    internal class RepairButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private bool _hovered;

        public void OnPointerClick(PointerEventData eventData)
        {
            Plugin.Log.LogInfo("repair line: pressed.");
            RepairLine.Press();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RepairLine.SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_hovered) return;
            _hovered = false;
            RepairLine.SetHovered(false);
        }
    }
}

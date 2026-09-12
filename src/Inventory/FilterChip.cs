using UnityEngine;
using UnityEngine.EventSystems;

namespace BoneAndEmber
{
    // Click handling for a filter chip.
    //
    // This deliberately does not use Unity's Button. A Button added with
    // AddComponent at runtime has a null targetGraphic (Unity only wires that
    // up for you in the editor), so its ColorBlock had nothing to tint and its
    // state was invisible, while any "selected" look came from hover rather
    // than from the filter actually changing. Implementing IPointerClickHandler
    // directly removes Selectable, targetGraphic, interactable, navigation and
    // the ColorBlock from the path in one move: the chip's own Image is the
    // raycast target, and the click lands here.
    internal class FilterChip : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        internal ItemFilter Filter;

        public void OnPointerClick(PointerEventData eventData)
        {
            Plugin.Log.LogInfo("filter chip: click on " + Filter);
            FilterRow.SetActive(Filter);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            FilterRow.SetHovered(Filter, hovering: true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            FilterRow.SetHovered(Filter, hovering: false);
        }
    }
}

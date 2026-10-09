using UnityEngine;
using UnityEngine.EventSystems;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Pointer bridge for the custom in-app title bar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopWindowDragHandle :
        MonoBehaviour,
        IPointerDownHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerClickHandler
    {
        private DesktopWindowChromeController _chrome;

        public void Bind(
            DesktopWindowChromeController chrome)
        {
            _chrome = chrome;
        }

        public void OnPointerDown(
            PointerEventData eventData)
        {
            ResolveChrome();
            _chrome?.BeginDrag();
        }

        public void OnDrag(
            PointerEventData eventData)
        {
            ResolveChrome();
            _chrome?.DragToCursor();
        }

        public void OnEndDrag(
            PointerEventData eventData)
        {
            ResolveChrome();
            _chrome?.EndDrag();
        }

        public void OnPointerClick(
            PointerEventData eventData)
        {
            if (eventData == null ||
                eventData.clickCount < 2)
            {
                return;
            }

            ResolveChrome();
            _chrome?.ToggleZoom();
        }

        private void ResolveChrome()
        {
            if (_chrome == null)
            {
                _chrome =
                    GetComponentInParent<
                        DesktopWindowChromeController>();
            }
        }
    }
}

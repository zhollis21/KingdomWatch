using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace KingdomWatch.Game
{
    // Something the panel names on hover, or on a long press on a touch
    // screen (#128). The Hud draws the one tooltip; this only says when a
    // pointer is on a thing and what to call it.
    public sealed class Tip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        // Set by the Hud when it makes the tip, never saved with a scene.
        [NonSerialized] public Hud Hud;
        public string Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            // A finger arrives by pressing, and is handled there.
            if (!IsTouch(eventData)) Hud.Hover(this, true, false);
        }

        public void OnPointerExit(PointerEventData eventData) => Hud.Hover(this, false, false);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsTouch(eventData)) Hud.Hover(this, true, true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsTouch(eventData)) return;
            // A press held long enough to show the tooltip was asking what the
            // control is, not pressing it: lifting the finger must not click it.
            if (Hud.IsShowing(this)) eventData.eligibleForClick = false;
            Hud.Hover(this, false, true);
        }

        private void OnDisable()
        {
            if (Hud != null) Hud.Hover(this, false, false);
        }

        private static bool IsTouch(PointerEventData eventData) =>
            eventData is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch;
    }

    // Clicking or dragging on the minimap moves the camera there.
    public sealed class MinimapPointer : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private RectTransform area;
        private float size;
        private Action<Vector2> moveTo;
        private float mapWidth, mapHeight;

        public void Setup(RectTransform minimap, float pixels, Vector2 mapSize, Action<Vector2> onMove)
        {
            area = minimap;
            size = pixels;
            mapWidth = mapSize.x;
            mapHeight = mapSize.y;
            moveTo = onMove;
        }

        public void OnPointerDown(PointerEventData eventData) => Move(eventData);

        public void OnDrag(PointerEventData eventData) => Move(eventData);

        private void Move(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, eventData.position, null, out var local)) return;
            // The minimap's top left is its pivot, so y runs down from 0.
            var u = Mathf.Clamp01(local.x / size);
            var v = Mathf.Clamp01(1f + local.y / size);
            moveTo(new Vector2(u * mapWidth, v * mapHeight));
        }
    }
}

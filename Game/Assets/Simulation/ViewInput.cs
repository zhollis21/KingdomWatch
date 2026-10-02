using System.Collections.Generic;
using KingdomWatch.Core.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using EntityId = KingdomWatch.Core.Data.EntityId;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace KingdomWatch.Game
{
    // Touch, mouse and keyboard, turned into camera moves and selection
    // (#115). The game ships on phones and PCs, so neither is a fallback:
    //
    //   touch     drag pans, pinch zooms, tap selects
    //   mouse     left, right or middle drag pans, wheel zooms about the
    //             middle of the screen, left click selects
    //   keyboard  WASD or arrows pan, Q/E or -/+ zoom, Esc clears the
    //             selection, F follows, Home shows the whole map
    //
    // A repeat tap or click near the last one, soon after it, moves to the
    // next candidate under it (section 18: selection must cycle). None of
    // this reaches the simulation; it moves the camera and marks a selection.
    // Needs EnhancedTouchSupport enabled, which SimulationDriver does.
    public sealed class ViewInput
    {
        // All in UI-scaled pixels, so a phone and a monitor feel the same.
        private const float TapSlop = 10f;
        private const float CycleRadius = 25f;
        private const float CycleSeconds = 1.5f;
        private const float PersonPickRadius = 22f;
        private const float CommunityPickRadius = 24f;
        private const float KeyPanSpeed = 600f;
        private const float KeyZoomRate = 2f;
        // Per wheel notch. The Input System's default scroll behaviour
        // (UniformAcrossAllPlatforms) reports one notch as 1, on every platform.
        private const float WheelZoomPerNotch = 0.07f;

        private readonly CameraRig rig;
        private readonly WorldView2D view;
        private readonly List<EntityId> candidates = new List<EntityId>();

        private bool dragging, moved, blocked, tapButton;
        private Vector2 pointerStart, pointerLast;
        private float pinchDistance;
        private Vector2 pinchMiddle;
        private Vector2 lastTap;
        private float lastTapTime = float.NegativeInfinity;

        public ViewInput(CameraRig rig, WorldView2D view)
        {
            this.rig = rig;
            this.view = view;
        }

        public bool Following { get; private set; }

        // `panel` says where the panel is: pointers that start there belong to
        // its buttons, not the map.
        public void Process(IPointerBlocker panel, float uiScale, float deltaTime)
        {
            ReadPointer(panel, uiScale);
            ReadKeyboard(uiScale, deltaTime);

            if (!Following) return;
            if (!view.Selected.IsNone && view.TryGetDrawnPosition(view.Selected, out var at)) rig.CentreOn(at);
            else Following = false;
        }

        public void ToggleFollow(float uiScale)
        {
            if (Following || view.Selected.IsNone)
            {
                Following = false;
                return;
            }
            Following = true;
            // Following a person is only useful where people are drawn, and
            // following anything needs a zoom the camera can move at.
            rig.ZoomToAtLeast(view.Selected.Kind == EntityKind.Person ? (WorldView2D.NearFrom + 4f) * uiScale : rig.FreePanPixelsPerCell);
        }

        public void Deselect()
        {
            view.Selected = EntityId.None;
            Following = false;
        }

        // Centres the camera on a point, as the minimap does.
        public void MoveTo(Vector2 world)
        {
            Following = false;
            rig.CentreOn(world);
        }

        public void WholeMap()
        {
            Following = false;
            rig.WholeMap();
        }

        private void ReadPointer(IPointerBlocker panel, float uiScale)
        {
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                var a = touches[0].screenPosition;
                var b = touches[1].screenPosition;
                var distance = Vector2.Distance(a, b);
                var middle = (a + b) / 2f;
                // A finger that went down on the panel belongs to it, as a
                // single touch does, so a pinch with one there moves nothing.
                var onPanel = panel.Blocks(touches[0].startScreenPosition) || panel.Blocks(touches[1].startScreenPosition);
                if (!onPanel && pinchDistance > 0f && distance > 0f)
                {
                    rig.ZoomAt(middle, distance / pinchDistance);
                    rig.PanBy(middle - pinchMiddle);
                    Following = false;
                }
                pinchDistance = distance;
                pinchMiddle = middle;
                // Whatever finger is left when the pinch ends must not pan or tap.
                dragging = false;
                blocked = true;
                return;
            }
            pinchDistance = 0f;

            Vector2 p;
            bool down, primary;
            var cancelled = false;
            var mouse = Mouse.current;
            if (touches.Count == 1)
            {
                var phase = touches[0].phase;
                p = touches[0].screenPosition;
                // The OS cancels a touch it takes over (a call, a system
                // gesture): that ends the drag but is not a tap.
                cancelled = phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                down = phase != UnityEngine.InputSystem.TouchPhase.Ended && !cancelled;
                primary = true;
            }
            else if (mouse != null)
            {
                p = mouse.position.ReadValue();
                down = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                primary = mouse.leftButton.isPressed;
                var scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !panel.Blocks(p)) rig.ZoomBy(Mathf.Exp(scroll * WheelZoomPerNotch));
            }
            else
            {
                p = pointerLast;
                down = false;
                primary = false;
            }

            if (blocked)
            {
                if (!down) blocked = false;
                return;
            }

            if (down && !dragging)
            {
                if (panel.Blocks(p))
                {
                    blocked = true;
                    return;
                }
                dragging = true;
                moved = false;
                tapButton = primary;
                pointerStart = pointerLast = p;
            }

            if (down && dragging)
            {
                if (Vector2.Distance(pointerStart, p) > TapSlop * uiScale) moved = true;
                if (moved)
                {
                    rig.PanBy(p - pointerLast);
                    Following = false;
                }
                pointerLast = p;
            }

            if (!down && dragging)
            {
                dragging = false;
                if (!moved && tapButton && !cancelled) Tap(pointerLast, uiScale);
            }
        }

        private void ReadKeyboard(float uiScale, float deltaTime)
        {
            var keys = Keyboard.current;
            if (keys == null) return;

            var direction = Vector2.zero;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) direction.x -= 1f;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) direction.x += 1f;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) direction.y -= 1f;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) direction.y += 1f;
            if (direction != Vector2.zero)
            {
                // PanBy drags the map; the keys move the view, the other way.
                rig.PanBy(-direction.normalized * KeyPanSpeed * uiScale * deltaTime);
                Following = false;
            }

            var zoom = 0f;
            if (keys.eKey.isPressed || keys.equalsKey.isPressed || keys.numpadPlusKey.isPressed) zoom += 1f;
            if (keys.qKey.isPressed || keys.minusKey.isPressed || keys.numpadMinusKey.isPressed) zoom -= 1f;
            if (zoom != 0f) rig.ZoomBy(Mathf.Exp(zoom * KeyZoomRate * deltaTime));

            if (keys.escapeKey.wasPressedThisFrame) Deselect();
            if (keys.fKey.wasPressedThisFrame) ToggleFollow(uiScale);
            if (keys.homeKey.wasPressedThisFrame) WholeMap();
        }

        private void Tap(Vector2 p, float uiScale)
        {
            var radius = (view.Band == ZoomBand.Far ? CommunityPickRadius : PersonPickRadius) * uiScale;
            view.Pick(p, radius, candidates);
            var again = Vector2.Distance(p, lastTap) < CycleRadius * uiScale && Time.unscaledTime - lastTapTime < CycleSeconds;
            // A repeat tap takes whoever follows the current selection in this
            // tap's list, not a count into the last one: people move between
            // taps and the order shifts. Nothing selected starts at the nearest.
            var next = 0;
            if (again)
            {
                var current = candidates.IndexOf(view.Selected);
                if (current >= 0) next = (current + 1) % candidates.Count;
            }
            view.Selected = candidates.Count > 0 ? candidates[next] : EntityId.None;
            Following = false;
            lastTap = p;
            lastTapTime = Time.unscaledTime;
        }
    }
}

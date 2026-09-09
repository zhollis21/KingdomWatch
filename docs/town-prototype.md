# M0 town prototype

Orthographic 3D with sprite villagers was selected after comparing the views on desktop and a Pixel 10 Pro XL. The flat scene and view-switch code have been removed. The runtime prototype is presentation-only; it is not the deterministic simulation described in the design plan.

## Run

Open `Game/` in Unity **6000.6.0f1**, open `Assets/Prototypes/Orthographic3D.unity`, and press Play. The saved scene contains a camera and `TownPrototype`; it generates 16 houses, 80 villagers, roads, trees, and a raised bridge at startup. Twenty villagers cluster in the market for selection testing. Geometry, materials, and pixel sprites are generated locally without external art dependencies.

## Controls

- Drag to pan; mouse wheel or two-finger pinch to zoom.
- Tap a person or house to select. Repeat a nearby tap within 1.5 seconds to cycle nearby targets.
- Select a villager and press Follow villager. Dragging stops follow.
- Town returns to the overview; Previous zoom restores the previous framing.
- Camera rotation orbits through 360 degrees; tilt ranges from 25 to 80 degrees. Reset camera angle restores the original view.
- Move sun automatically runs a 45-second lighting cycle. Disable it to pause, or drag Sun direction to choose an angle.
- Inspect elevated bridge frames its stairs, supports, railings, and crossing villager.

## Android build and profiling

Android Build Support, SDK/NDK, and OpenJDK are required in Unity Hub. Connect an Android phone with USB debugging enabled and authorize the computer. Activate the Android build profile, select the phone under Run Device, and use Build And Run. The Android profile and global scene list both explicitly launch Orthographic3D. Save APKs in `Game/Builds/` (ignored by Git).

The shared Android profile enables Development Build and Autoconnect Profiler. Deep Profiling is off. Keep the app running and select the Android player in the Profiler target dropdown. Device selection is local setup; no phone serial is committed. Raw captures belong in `Game/ProfilerCaptures/` (also ignored); keep written findings in `docs/`.

A ~17-minute on-device sustained capture (Pixel 10 Pro XL, `KingdomWatch_2026-09-09_14-46-30`, 17,443 frames) establishes a baseline for the current content (16 houses, 80 villagers, roads, trees, bridge): main thread median 9.42ms (~106 FPS), p95 12.07ms, p99 16.41ms, max 73.81ms (one spike); only 0.85% of sampled frames missed the 60 FPS threshold and 0.1% missed 30 FPS. Render thread stayed under 4.5ms throughout. See #3 for the full numbers.

Not yet captured: a render-ceiling ramp test (N animated sprites until below 60 FPS) and an allocation/GC baseline. Both matter more once M2's stress-test content (~10k trees, ~500 buildings, 200 stepped agents) exists than they do against this scene, so they're deferred rather than blocking M0.

## Rendering and limitations

Houses and ground use matte URP Lit materials, directional sunlight, sky fill, and shadows. Villagers and trees remain unlit billboards facing the camera, without directional artwork. PC and Mobile URP shadow distance is 120 because the camera sits 70 units from its focus; this visual-test setting needs mobile profiling.

Villagers follow looping motion and can cross geometry. Selection uses screen-space proximity, including occluded targets. Buildings use many separate primitive parts. There is no town AI, pathfinding, simulation, zoom-level aggregation, or performance harness. The immediate-mode controls are development UI. Rotating sunlight demonstrates shading, not astronomical day/night simulation.

The user verified the prototype's appearance and operation on Android before the 2D cleanup. After cleanup, compilation and scene-reference checks were repeated; another Play-mode/device smoke test remains appropriate before merge.

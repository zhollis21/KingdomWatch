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

Initial phone captures were collected before the flat-view removal. Those captures overlap and do not establish a 2D-versus-3D performance difference. The app currently retains Unity's default mobile frame-rate target; a controlled 60 FPS baseline and sustained thermal test are still required before closing the performance portion of M0.

## Rendering and limitations

Houses and ground use matte URP Lit materials, directional sunlight, sky fill, and shadows. Villagers and trees remain unlit billboards facing the camera, without directional artwork. PC and Mobile URP shadow distance is 120 because the camera sits 70 units from its focus; this visual-test setting needs mobile profiling.

Villagers follow looping motion and can cross geometry. Selection uses screen-space proximity, including occluded targets. Buildings use many separate primitive parts. There is no town AI, pathfinding, simulation, zoom-level aggregation, or performance harness. The immediate-mode controls are development UI. Rotating sunlight demonstrates shading, not astronomical day/night simulation.

The user verified the prototype's appearance and operation on Android before the 2D cleanup. After cleanup, compilation and scene-reference checks were repeated; another Play-mode/device smoke test remains appropriate before merge.

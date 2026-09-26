# Running Core in Unity

`Game/` runs the real simulation. The scene builds the same M1 world as the harness (`World.TwoBands`, a 48×48 placeholder map, bands of 60 and 45) and draws it in ¾ oblique 2D, the game's perspective (design plan §18). It is a driver, not the game view: there is no pan, zoom or selection yet (#115), and no art.

## Getting Core into Unity

Unity loads Core as a managed plug-in. **Any `dotnet build` of Core copies `KingdomWatch.Core.dll` and its `.pdb` into `Game/Assets/KingdomWatch.Core/`**; that folder is git-ignored. So after a fresh clone, or after changing Core, build first:

```
dotnet build
```

Then let Unity reimport. The DLL Unity loads is the one the harness and tests ran: one compilation of Core, never a second one inside Unity (§5). `Game/Assets/link.xml` stops IL2CPP stripping any Core type from the player.

On Windows, if the build fails to overwrite the DLL while the Editor is open, close the Editor or its Play mode and build again.

## Run

Open `Game/` in Unity **6000.6.0f1**, open `Assets/Scenes/Oblique.unity`, and press Play. The panel shows the year, day and season, how many people are alive, how many settlements exist, and the world hash at the most recent whole year. Slower, Pause and Faster change the speed between 1 and 120 sim days per real second.

Each coloured cell is one terrain cell: plains, forest, hills, small river, deep water. The rows are squashed to give the ¾ tilt. People stand on their cells as upright markers (children are shorter and paler). A person on a task is drawn part-way along their route (`Jobs.PositionAt`), so movement hops from cell to cell until stepped movement exists (#25).

## Comparing with the harness

The driver stops exactly on every year boundary, where the harness hashes, so the two can be compared:

```
dotnet run --project Harness -c Release -- --seed 1 --years 3
```

That prints `Hash:` for the end of year 3, which should equal the panel's `Hash at year 3`. The seed is set on the `SimulationDriver` component. Checking this automatically, on device under IL2CPP, is #90.

## Android build

Android Build Support, SDK/NDK, and OpenJDK are required in Unity Hub. Connect an Android phone with USB debugging enabled and authorize the computer. Activate the Android build profile, select the phone under Run Device, and use Build And Run. The global scene list launches `Oblique`. Save APKs in `Game/Builds/` (ignored by Git).

The shared Android profile enables Development Build and Autoconnect Profiler. Deep Profiling is off. Keep the app running and select the Android player in the Profiler target dropdown. Raw captures belong in `Game/ProfilerCaptures/` (also ignored); keep written findings in `docs/`.

## History: the M0 3D prototype

M0 compared a 2D scene with an orthographic 3D one (#1), picked 3D, and later reversed that at #72 (§18). The 3D prototype (`Orthographic3D.unity`, `TownPrototype.cs`) was removed then. It is in git history before #72.

Its ~17-minute sustained capture on a Pixel 10 Pro XL (`KingdomWatch_2026-09-09_14-46-30`, 17,443 frames, 16 houses, 80 villagers) measured main thread median 9.42 ms (~106 FPS), p95 12.07 ms, p99 16.41 ms, max 73.81 ms. Only 0.85% of frames missed 60 FPS, and the render thread stayed under 4.5 ms (#3). Those numbers are for the 3D renderer and do not carry over; M2 measures the 2D one.

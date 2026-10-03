# Running Core in Unity

`Game/` runs the real simulation. The scene builds the same M1 world as the harness (`World.M1`: a 1080×1080 placeholder map, bands of 60 and 45, defined once in Core for both) and draws it in ¾ oblique 2D, the game's perspective (design plan §18). You can pan, zoom and select (#115). Terrain and villagers use Kenmi's Cute Fantasy pixel art when the private art submodule is present (#121).

## The art submodule

The art is licensed for use in the game but may not be redistributed, and this repository is public, so it lives in a private repository, [`KingdomWatch-Art`](https://github.com/zhollis21/KingdomWatch-Art), mounted at `Game/Assets/Art`. With access to it:

```
git submodule update --init
```

Some of the pack's paths are long; on Windows, if the checkout fails with "Filename too long", run `git config --global core.longpaths true` and try again.

Without the submodule (a public clone, or no access) the game still runs: the view logs that the art is missing and draws coloured cells and plain markers instead. `Game/Assets/Editor/PixelArtImport.cs` sets the import settings for everything under `Assets/Art`: point filtering, no compression, no mipmaps. If the art looks blurry or villagers are missing, reimport `Assets/Art`.

## Getting Core into Unity

Unity loads Core as a managed plug-in. **Any `dotnet build` of Core copies `KingdomWatch.Core.dll` and its `.pdb` into `Game/Assets/KingdomWatch.Core/`**; that folder is git-ignored. So after a fresh clone, or after changing Core, build first:

```
dotnet build
```

Then let Unity reimport. The DLL Unity loads is the one the harness and tests ran: one compilation of Core, never a second one inside Unity (§5). `Game/Assets/link.xml` stops IL2CPP stripping any Core type from the player.

On Windows, if the build fails to overwrite the DLL while the Editor is open, close the Editor or its Play mode and build again.

## Run

Open `Game/` in Unity **6000.6.0f1**, open `Assets/Scenes/Oblique.unity`, and press Play. The panel is described under "The panel" below. The speed buttons step through the design plan's §4 speed ladder, 1× to 10,000×, where 1× is eight real minutes per sim day (and 10,000× about 21 days per second). It starts at 1000×. Zoomed out, the map is one pixel per cell in the art's colours, one version per season, with a new season spreading across it as it does in the art (`SeasonSpread.shader`, #131); closer in, the art itself draws, down to quarter size (4 screen pixels per cell), built 32×32 cells at a time for the part of the map in view (#130). At half and quarter size the trees, rocks, bushes and flowers are tiles in two tilemaps, drawn from one runtime atlas, and people always draw in front of them. From full size up each one is its own renderer, so people walk behind trees (#131).

A cell is one 16 px tile of the art, about 1.5 m, and a villager is about one cell tall. The art draws the ¾ tilt itself, so rows are as tall as columns are wide. At Medium and Near zoom the ground follows the season: bright green in spring, deeper green in summer, yellow-green in autumn, and snow in winter, when the flowers go. A new season spreads rather than arriving everywhere at once: it starts at a dozen random places, different each season, and grows outward in ragged fronts, covering the map in about ten days, so snow creeps across the land and melts back the same way (#131). Forests have a tree on every cell (oak, spruce or fruit, in three sizes), rocks cells have one of ten rocks, berry scrub has a red or purple berry bush on every cell (#137), and about one plains cell in six has tufted grass, a flower or a sprout; some tufts sway. The variant in each cell comes from its position, so the map looks the same every run. Both kinds of water look alike, with banked shorelines (#127): still water, with a sparkle, a droplet's ring or a fish here and there. Where the river steps diagonally, one of the land cells beside the step is drawn as water so the river stays one channel; that is drawing only, and Core still has land there. Each band and settlement has a campfire and a tent for about every dozen people (settlements live in tents until Core has buildings, #23, but also have a well, a woodpile and a stone pile, which a band on the move does not). Pause stops every animation along with the simulation. People are paper dolls: body, shoes, trousers, a shirt picked by job (foragers farmer shirts, woodcutters lumberjack shirts, everyone else a plain shirt), hands and hair. Their colours and hair are picked from their id, so a person always looks the same. Everyone is drawn as an adult for now (#125). A person walks while going to or coming back from work, facing where they are going, and otherwise stands facing you; there are no work animations yet (#126). A person on a task is drawn part-way along their route (`Jobs.PositionAt`), so movement hops from cell to cell until stepped movement exists (#25).

## The panel

The panel is uGUI, built in code by `Hud.cs` (#128) and drawn in the pack's UI art at a whole number of screen pixels per art pixel (`Hud.Pixel`, about 360 of them across the shorter side), so it stays crisp on a phone and a monitor. A bar across the top shows the date, the population (a villager's head) of the selected community, named, or of the world when nothing is selected, and the **stock** of that community (food, wood, stone; hover the food for how many days it lasts) and the speed controls. A selected person shows their own community's stock, a selected settlement or band its own. It is hidden when nothing is selected, because no one owns the world's stock; polities will give "whose stock" a meaning at M7. Down the left are (in a strip under the bar instead, side by side, on a screen taller than it is wide):

- a **minimap** of the map's spring colours, with the camera's view outlined and a dot for each settlement (pale) and band (red); clicking or dragging it moves the camera;
- the **selection card**: a person's id (Core gives no names yet; #109), age, job, community and an eased health bar, or a community's size, with Follow and Clear;
- the **debug card** (the `i` button or F3): the seed, the map size, the counts and the year hash.

Hovering a control, or holding a finger on it, names it in a tooltip. `ViewInput` asks the panel (`IPointerBlocker`) whether a pointer started on it before it pans, zooms or selects. The panel reads the world and the view; the only things it does that reach the simulation are the clock's speed and pause.

Its art is five sheets that `HudArt.cs` cuts up, copied into the art submodule's `Resources` by `Update-Resources.ps1`. Without the submodule the panel draws flat frames in Unity's own font.

## Camera and selection

The game ships on phones and PCs, so both are controlled directly:

| | Touch | Mouse and keyboard |
|---|---|---|
| Pan | Drag | Drag with any button, or WASD / arrows |
| Zoom | Pinch (about the fingers) | Wheel, Q/E or -/+ (about the middle of the screen) |
| Select | Tap | Left click |
| Clear selection | Clear button | Esc or Clear |
| Follow | Follow button | F or Follow |
| Whole map | Whole map button | Home |
| Pause | Pause button | Space |
| Slower, faster | << and >> buttons | , and . |
| Debug card | i button | F3 |
| Move to a place | Tap or drag on the minimap | Click or drag on the minimap |

A repeat tap or click near the last one, within 1.5 seconds, moves to the next candidate under it, which is how you pick one person out of a crowd.

What is drawn depends on the zoom (design plan §16). **Far**, the whole-map zoom, shows one marker per settlement (pale) or band (red), each with a dark rim so it shows on snow, under a name plate whose tail points at it, giving its id and how many people it has (`Settlement 3: 42`), and taps select those. Names replace the ids once Core names settlements (#109). **Near** and **Medium** show people and the tiled terrain. Their zoom snaps to whole multiples of the art's 16 px per cell, and the camera to whole screen pixels, so pixel art stays crisp; pinching or scrolling therefore moves in steps there, while Far zooms smoothly and keeps the one-colour-per-cell map. Without the art, people are markers coloured by job: forager yellow-green, woodcutter brown, stone gatherer grey, none orange; children are pale. Medium will add buildings and roads once Core has them. The selection card shows a selected person's id, age, job and community with a health bar; the community's stock is in the top bar.

The camera only reads. Panning, zooming and selecting never change the world, so the year hash is the same whatever you do with them.

## Comparing with the harness

The driver steps through `YearStepper`, which stops exactly on every year boundary, where the harness hashes, so the two can be compared:

```
dotnet run --project Harness -c Release -- --seed 1 --years 3
```

That prints `Hash:` for the end of year 3, which should equal the debug card's `Hash at year 3`. The seed is set on the `SimulationDriver` component. The same check on an Android build (IL2CPP) matched at seed 1, year 6 (#90); it is manual, so re-run it when something that could diverge lands, such as a float in `Core` or a change to how the hash is built.

## Android build

Android Build Support, SDK/NDK, and OpenJDK are required in Unity Hub. Connect an Android phone with USB debugging enabled and authorize the computer. Activate the Android build profile, select the phone under Run Device, and use Build And Run. The global scene list launches `Oblique`. Save APKs in `Game/Builds/` (ignored by Git).

The shared Android profile enables Development Build and Autoconnect Profiler. Deep Profiling is off. Keep the app running and select the Android player in the Profiler target dropdown. Raw captures belong in `Game/ProfilerCaptures/` (also ignored); keep written findings in `docs/`.

**Kingdom Watch → Analyse newest profiler capture** reads the newest `.data` capture in that folder and writes `<capture>.markers.txt` beside it: frame-time stats (median, p95, p99, max, frames over 16.7 and 33.3 ms) and the main thread's top markers by total and self time, over every frame and over only the frames over budget, with our own `KW.*` markers listed first and in full (#132). Every table shows each marker's total and self time per frame and the worst single frame for each. It loads the capture into the Profiler window, so it asks before replacing a recording there. A `.data` file can only be decoded inside Unity; this is what makes one readable without the Profiler window. The report is a machine summary that stays local beside its capture, like the capture itself; what is worth keeping from it gets written up in `docs/`. With the Editor closed, the same analysis runs headless: `Unity -batchmode -nographics -projectPath Game -executeMethod KingdomWatch.Game.Editor.ProfilerAnalysis.Batch [-capture <path>]`. It reads the scripted runs' `.raw` captures as well as saved `.data` ones.

### Scripted runs

`pwsh tools/Profile.ps1` measures the view without anyone at the Editor (#132). It builds Core and a Windows development player (`DevBuild.cs`, into `Game/Builds/Windows/`) with the Instrumented managed code variant, since the scripted run and the `-hud-*` flags are compiled in under `UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR` (Unity 6.6 deprecated `DEVELOPMENT_BUILD`, and a development build alone no longer defines anything for C#). It launches the player at 2400×1080 (a phone held sideways), and then analyses the capture. The player flies a fixed camera route (`ScriptedRun.cs`): the whole map; then quarter, half, full and double size, each held still and then panned south; then the whole map again; then quarter size, straight out to the whole map, and back to quarter size in the same place. The last three stops measure leaving the art and coming back to it. Zooming out hides the built chunks rather than releasing them, so coming back to the same place shows them again without rebuilding. Each stop is counted in frames rather than seconds, so a faster build covers the same ground in the same frames. The simulation clock also stands still while a stop fills in, so every stop is reached on the same in-game day in every run. Everything lands in `Game/ProfilerCaptures/`:

- `Run_<stamp>.log`: for each stop, how many frames and seconds the screen took to fill in, and its frame times while moving and while held still.
- `Run_<stamp>_NN_<stop>.raw`: that stop's profiler capture. There is one per stop because the Profiler keeps only the last 2000 frames of a capture, and a whole route is longer than that. A capture ends when its stop's hold ends, so taking and saving the screenshot is not part of it.
- `Run_<stamp>_NN_<stop>.png`: a screenshot at that stop.
- `Run_<stamp>_NN_<stop>.markers.txt`: the analysis of that stop's capture.

Close the Editor first, because Unity locks an open project. `-SkipBuild` reruns the last build. The route only runs when the player is launched with `-scripted-run <folder>`, and only in development builds. The timings are desktop timings, useful for finding spikes and comparing before with after, not for phone performance. The screenshots show the licensed art, so they stay local like the captures. `/profile` (`.claude/skills/profile/`) runs all of this and compares a run with the one before it. `-HudDemo` launches the player with someone selected and the debug card open, so the screenshots show those parts of the panel; leave it off when comparing frame times. `-Flat` draws the panel as a build without the art submodule would, flat frames in Unity's own font. `-Notch` insets the panel as a phone's camera cutout and rounded corners would, to look at the layout on a screen that has neither. The panel sits inside `Screen.safeArea`; the map still draws edge to edge.

## History: the M0 3D prototype

M0 compared a 2D scene with an orthographic 3D one (#1), picked 3D, and later reversed that at #72 (§18). The 3D prototype (`Orthographic3D.unity`, `TownPrototype.cs`) was removed then. It is in git history before #72.

Its ~17-minute sustained capture on a Pixel 10 Pro XL (`KingdomWatch_2026-09-09_14-46-30`, 17,443 frames, 16 houses, 80 villagers) measured main thread median 9.42 ms (~106 FPS), p95 12.07 ms, p99 16.41 ms, max 73.81 ms. Only 0.85% of frames missed 60 FPS, and the render thread stayed under 4.5 ms (#3). Those numbers are for the 3D renderer and do not carry over; the 2D one is measured ahead of the stress test (#20, M9).

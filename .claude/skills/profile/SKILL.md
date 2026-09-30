---
name: profile
description: "Measure the running game's frame time and view cost from a profiler capture: find the newest capture or make one with a scripted run, analyse it, explain where the time goes, and compare it with an earlier run. Use when asked to profile, measure performance, check a rendering change before and after, find what a slow frame spends its time on, or compare two captures. `/profile` makes a new run; `/profile newest` analyses the newest existing capture; `/profile compare` compares the two newest runs without making one."
---

# Profile

The tooling is described in `docs/unity.md` under "Scripted runs". This skill
drives it and does the reading. Everything it touches is in
`Game/ProfilerCaptures/`, which is git-ignored, and it stays there. The
screenshots show the licensed art, so they are never committed, attached to an
issue, or pasted anywhere public.

## 1. Get a capture

- **`/profile` (no argument):** make a new run. The Editor has to be closed,
  because Unity locks an open project. Check before starting, since the build
  takes minutes and fails late otherwise:

  ```bash
  tasklist | grep -i "^Unity.exe"
  ```

  If it's open, ask the user to close it. Don't close it yourself, because they
  may have unsaved work. Then:

  ```bash
  pwsh tools/Profile.ps1            # build, fly the route, analyse
  pwsh tools/Profile.ps1 -SkipBuild # rerun the last build
  ```

  Run it in the background. It prints the run's `.log` and the path of its
  `.markers.txt` at the end. On failure, it names the Unity log to read.
- **`/profile newest`:** analyse the newest `.data` or `.raw` in
  `Game/ProfilerCaptures/`. If it has no `.markers.txt` beside it yet, produce
  one with the Editor closed:

  ```bash
  "C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe" -batchmode -nographics -projectPath Game -executeMethod KingdomWatch.Game.Editor.ProfilerAnalysis.Batch -logFile Game/ProfilerCaptures/analysis.log
  ```

  `<version>` is in `Game/ProjectSettings/ProjectVersion.txt`. With the Editor
  open instead, the user can run **Kingdom Watch → Analyse newest profiler
  capture**.
- **`/profile compare`:** use the two newest `Run_*` sets, with no new run.

## 2. Read it

A scripted run gives three things. Read all three before saying anything:

- **`Run_<stamp>.log`:** one line per stop. *Fill frames / fill s* is how
  long the screen took to finish drawing after the camera arrived. This is
  the number a user sees as the view "filling in". *move median / p95* are
  frame times during the pan, or until filled after a zoom. *hold median /
  p95* are frame times while held still. `filled: no` means the stop
  never finished drawing within the limit, which is a finding in itself.
- **`Run_<stamp>_NN_<stop>.markers.txt`:** one per stop, because the
  Profiler keeps only the last 2000 frames of a capture. A capture ends
  with its stop's hold, so the screenshot and its save are never in it. Each has the stop's
  frame-time stats, then our own `KW.*` markers in full (`KW.View.BuildChunks`, `KW.View.LayLand`,
  `KW.View.Animate`, `KW.View.People`, `KW.Simulate`...; `KW.View.Build.*`
  split `BuildChunks` into one chunk's parts and one release, and
  `KW.View.Build.Chunk`'s calls are the chunks built), then the top Unity
  markers by total and self time, over all frames and over only the frames
  over the 16.7 ms budget. Self time says where time is actually spent; total
  says under which call.
- **`Run_<stamp>_NN_<stop>.png`:** look at them with the Read tool. A number
  that improved because something stopped being drawn is a bug, not a win.

## 3. Explain

Lead with the answer: where the time goes, which stop is worst, and whether
anything never filled in. Name the markers, with their per-frame and worst
figures. Keep desktop numbers in their place: they're for spikes and for
before/after comparison, not for phone performance, and they say so if asked.

## 4. Compare

When there is an earlier run to compare against (the one before, or one the
user names), put the two side by side, stop by stop: fill frames, hold median
and p95, and each `KW.*` marker's per-frame and worst time. Say which changed
and by how much, and look at the paired screenshots for any visible
difference. The screen size is in the log's first line. Runs at different
sizes, or on different machines (`graphicsDeviceName`, same line), don't
compare. Say so rather than comparing them anyway.

Timings vary from run to run. Treat a difference under about 10% on one run
as noise, and rerun (`-SkipBuild`) before calling it a change.

# Kingdom Watch

A grounded low-fantasy god sim. Six wandering bands — three per race — arrive in an empty land. Over centuries they settle, farm, build, learn trades, form households, split into rival polities, feud, trade, march, starve, and remember. Around 1,650 individuals at equilibrium, every one of them a real person with a name, traits, skills, relationships, grudges, and ambitions.

There is no magic in the world except you. The people know it — they build shrines, ordain priests, and argue over what your interventions meant.

The player watches from any altitude and unlocks powers as the world reaches milestones.

> If something happens in the simulation, the player should be able to zoom in and see why.

With powers-only agency, this is a mechanical necessity, not an aspiration: observation is the player's only diagnostic.

## Status

M0 prototyping. Orthographic 3D with sprite villagers has been selected after desktop and Android trials, and a sustained on-device baseline looks healthy (see [docs/town-prototype.md](docs/town-prototype.md)). M1 is underway in `Core/`: entity identity, keyed RNG, person storage, and the simulation clock and event scheduler. A render-ceiling ramp test and allocation/GC baseline are deferred to M2's stress test. See [Milestones](https://github.com/zhollis21/KingdomWatch/milestones) for M0–M8.

## Run the prototype

Open `Game/` with Unity **6000.6.0f1**, open `Assets/Prototypes/Orthographic3D.unity`, and press Play. The town generates at runtime. See [prototype controls and Android setup](docs/town-prototype.md).

Repository layout: `Game/` contains Unity. The standalone C# projects are `Core/` (the simulation), `Core.Tests/`, and `Harness/` (headless console runner) — build them with `dotnet build KingdomWatch.sln`, no Unity required.

## Stack

- **Engine:** Unity 6.6 → 6.7 LTS, C#
- **Core:** `netstandard2.1`, zero Unity dependencies, deterministic
- **Platform:** Android first, desktop an acceptable fallback

## Design doc

The full design and technical plan lives at [docs/design/kingdom-watch-plan-v7.1.md](docs/design/kingdom-watch-plan-v7.1.md) — current decisions, entity model, simulation architecture, milestones, and open questions. It is a living plan that gets revised as the code teaches us things, not a frozen specification.

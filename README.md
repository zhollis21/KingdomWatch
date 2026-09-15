# Kingdom Watch

A god sim about watching. Six wandering bands arrive in an empty land, and over a few centuries they settle it, farm it, build on it, fall out over it, marry across it, starve in it, and tell stories about it. About 1,650 people at any one time, each one a real person with a name, a family, a trade, and a list of grudges.

There is no magic in the world except you. You can strike, heal, bless, and nudge. You cannot build a barn or tell anyone what to do. The people know something is out there — they build shrines, ordain priests, and argue about what your last intervention *meant*.

> If something happens in the simulation, the player should be able to zoom in and see why.

That sentence is the whole design. Everything else is in service of it.

## Where things live

| Question | Page |
|---|---|
| What are we building, and why is it shaped like this? | [The design doc](docs/design/kingdom-watch-plan-v7.1.md) — a living plan, not a spec |
| What's done, what's next, what's blocked? | [The roadmap](docs/roadmap/README.md) · [what to pick up now](docs/roadmap/next.md) — regenerated from GitHub, never hand-edited |
| How do we work in this repo? | [AGENTS.md](AGENTS.md) — conventions, the issue → `/kickoff` → PR loop, what the tests enforce |
| How do I run the Unity prototype? | [docs/town-prototype.md](docs/town-prototype.md) |
| The backlog | [Issues](https://github.com/zhollis21/KingdomWatch/issues) · [Milestones](https://github.com/zhollis21/KingdomWatch/milestones) M0–M8 |

## Build

The simulation is plain C# with no Unity in it. From the repo root:

```powershell
dotnet build KingdomWatch.sln
dotnet test KingdomWatch.sln
dotnet run --project Harness -c Release
```

`Core/` is the sim (`netstandard2.1`, zero dependencies, deterministic), `Core.Tests/` proves it, `Harness/` runs it headless for centuries at a time. `Game/` is the Unity 6 project that will eventually draw it.

## Stack

Unity 6.6 → 6.7 LTS · C# · Android first, desktop as fallback.

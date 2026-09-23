# Kingdom Watch — Design & Technical Plan (v7.1)

> **How to read this.** A living plan, not a specification. It records current best thinking and is expected to be revised as real code gets written and teaches us things. Treat its claims the way `/kickoff` treats an issue's — a well-informed hypothesis from someone who had context you may lack, worth taking seriously and not worth adopting unexamined. Where the code and this document disagree, that is a prompt to work out which one is wrong, not an automatic win for the document. §2's "Locked decisions" are the settled *game* questions, reopened deliberately rather than casually; everything else, including the code sketches below, is illustrative.

> **M0 decision, September 9, 2026:** Orthographic 3D with sprite villagers is selected following desktop and Android prototype trials. The flat 2D comparison has been retired. Current implementation and limitations are documented in [the town prototype guide](../town-prototype.md). Sustained performance budgets and the M2 mobile gate remain open. Repository directories use `Game/`, `Core/`, `Core.Tests/`, and `Harness/`; the KingdomWatch-prefixed paths below are the original design notation.

*A grounded low-fantasy god sim. Supersedes v7. Adds the simulation clock and scheduler, corrected real/sim-time cadence, threshold-crossing compression, LOD equivalence testing, keyed deterministic randomness, durable EventId, safe save snapshots, the storage accessor layer, decision provenance, family formation and death rules, witness-tracked grievances, semantic zoom, and the confirmed .NET/Unity version path.*

---

## 1. The game

Six wandering bands — three per race — arrive in an empty land. Over centuries they settle, farm, build, learn trades, form households, split into rival polities, feud, trade, march, starve, and remember. Around 1,650 individuals at equilibrium, every one of them a real person with a name, traits, skills, relationships, grudges, and ambitions.

There is no magic in the world except you. The people know it — they argue over what your interventions meant, and stories of what you did spread along trade routes and distort as they travel.

The player watches from any altitude and unlocks powers as the world reaches milestones.

**The design principle, at the top of the README:**

> If something happens in the simulation, the player should be able to zoom in and see why.

With powers-only agency, this is a mechanical necessity, not an aspiration: observation is the player's only diagnostic.

---

## 2. Locked decisions

| Question | Decision |
|---|---|
| World start | **From scratch** — 3 bands per race (60/45/30), ~270 people, no settlements |
| Starting polities | **One dispersed polity per race**; texture comes from later fragmentation |
| Equilibrium scale | ~15 settlements, ~1,650 people |
| Individual depth | **Persistent CK-like people** — everyone has traits, relationships, memories, ambitions. Behavioural depth expands over development (§7) |
| Zoom depth | Follow one person through a whole day |
| Building interiors | None — agents enter and disappear |
| Session shape | Long-lived world, kept for weeks or months |
| Time while closed | **Advances at 10×**, halting at the next crisis. **Player-toggleable**, logged in history |
| Player role | Sandbox god, **powers only** — never direct commands |
| Unlock structure | Linear track, milestone-gated with **OR-conditions** |
| Unlock persistence | Resets each world — no meta-progression |
| Economy | Seven resources with chains, data-driven and extensible |
| Technology | **Emergent from the recipe graph** — no tech tree (v2 at earliest) |
| Seasons | Full — harvest cycles, stores, winter mortality as an *outcome* |
| Skills | **Five tiers** (novice→master); tier zero needs no building; apprenticeship transmits |
| Movement | **Soft avoidance** — agents never hard-block cells |
| Polity attitudes | Polities hold **their own diplomatic state**, distinct from settlements |
| Households | **First-class entity** — person → household → home |
| Property | **Hybrid** — household owns the home, individuals own wealth; tools and arms are settlement stock, checked out (#78) |
| Kinship | Hard ban through grandparents; first cousins a **culture taboo** |
| Grievances | **Witness-tracked**, not flat decay — inheritable across generations |
| Animals | Livestock and monsters persistent; **game as regional populations** |
| Settlements | Founded and abandoned freely; **secession** is a founding trigger |
| Map | Procedural with archetypes (continents / islands / watering hole) |
| Races | Two — humans and **elves** (long-lived, insular) |
| Power constraints | **Unlimited use.** Constrained instead by the blunt-instrument rule (§16) |
| Race origins | Separate homelands; mixing gated by settlement memory |
| Interbreeding | Couples yes, children no |
| Culture | Starts shared, diverges; lives on **both settlements and people** |
| Magic | None in the world — the god is the only supernatural force |
| Attribution | **Not omniscient** — witnessed events spread and distort with distance and time |
| Warfare | Logistics, narrowly defined (§14). Sieges deferred |
| Naval | Islands at launch; traversal abstraction from day one |
| Fail state | Extinction ends the run — rare, mostly player-caused |
| Notification | Event feed only — no auto-jump, no push |
| Art perspective | **Decided at M0** by two ugly prototypes on a real phone |
| Platform | Android first; desktop an acceptable fallback |
| Engine | **Start on Unity 6.6, move to 6.7 LTS when it ships** (late 2026), C# |
| Frameworks | Core `netstandard2.1`; tests and harness `net10.0` |
| Data layout | **Dense records** behind a **storage accessor layer**; split hot fields only if M2 measures a problem |

---

## 3. Entity model

These must never collapse into each other. Getting this wrong is the failure mode where `Person` and `Settlement` end up representing four concepts each.

```
Person      — an individual. Has a birth culture and a current cultural affinity.
Household   — one or more people sharing a home, food access, and wealth.
Dynasty     — a lineage across households and generations.
Settlement  — a place. Owns bulk resources, layout, buildings, and a culture.
Polity      — one or more settlements under a ruler. The unit of war and alliance.
MobileGroup — people and supplies in transit, with no fixed place.
Culture     — a bundle of drifting values. Carried by settlements, held by people.
Race        — biology. Fixed at birth.
```

### MobileGroup

The game opens with six wandering bands carrying people, food, tools, leaders, movement state, a destination, and temporary camps — and nothing in the entity model represented them.

```
MobileGroup
    Members[]
    SharedSupplies
    Position
    Destination
    Leader
    Purpose
```

Three uses at launch: **NomadicBand**, **FoundingParty**, **Army**. Keep `Purpose` extensible — **SupplyConvoy** is the likely fourth, since warfare promises visible carts of grain on roads. If supply stays fully abstract, do not promise the player three literal carts they can watch being intercepted.

**Spatial membership is separate from social membership.** A person can simultaneously belong to household 38, serve in army 7, call Oakshire home, and be marching 40 km away. These are not contradictions — they are different relationships:

```
Social / home identity   : HouseholdId · HomeSettlementId
Current spatial container: Settlement | MobileGroup | free world position
```

Validator rule: **every living person has exactly one current spatial presence.**

`NomadicBand` needs a home in the data model **before M1** — it is the starting state of the entire game.

**Built at #54.** The two spatial containers share one interface, `ICommunity` — id, position, an optional destination, an ordered member list, a shared ledger — and the systems that feed, work, breed and bury a population (`Hunger`, `Warmth` since #53, `Jobs`, `Fertility`, `Deaths`, and the placeholder `Matchmaking`) track that and nothing more, each with a `Track` and an `Untrack`. `Settlement` is the second implementation: an id of its own kind, a fixed position, members and stores, and deliberately nothing else until M3 (#23 layout, #69 housing stock) and M7 (a ruler). A band settling is therefore a handover — `Founding.Found(band, reasons)` moves the members in order and the stock through `TransferTo`, untracks the band everywhere and tracks the settlement, and publishes `SettlementFounded` with the band as its origin — rather than any system learning a second type. The band's behaviour on top of `MobileGroup` is `NomadicBands` (§15).

Consequences that fall out:

- A settlement can change polity without changing culture
- A person can change settlement without instantly changing culture
- A polity can hold settlements of different cultures, or even races
- A dynasty can span settlements and outlive a polity

---

## 4. Simulation LOD

**LOD is two-dimensional.** Spatial detail and temporal detail are independent inputs.

|  | Normal time | Fast time | Extreme compression |
|---|---|---|---|
| **Visible** | Stepped agents, exact paths, animation, detailed combat | Scheduled behavior even though visible | Settlement summary |
| **Offscreen** | Scheduled — task completion events | Scheduled, coarser | Compressed settlement sim |

Implementation note: this is the *mental model*, not six code paths. Compute a single quality enum — `(visibility, timeScale) → SimQuality` — and have every system read that enum. One dial, two inputs.

Do not bake camera assumptions into simulation interfaces.

### Compression aggregates activity, never identity

This is the governing rule, and it resolves an apparent contradiction with the premise that every person stays real.

At 10,000×, Aldric still personally ages, marries, has children, gains skill, migrates, is injured, and dies. What disappears is his exact walking, pathfinding, individual axe swings, and hauling animations.

Without this rule, genealogy, grievances, apprenticeship, and history all stop being trustworthy at high speed — which would hollow out every system built on them.

### The simulation clock and scheduler

**Before M1.** Fixed timestep is right for determinism, but you cannot literally iterate every tick when running centuries at 10,000×.

```
World clock = integer simulation time · 1 tick = 1 simulated second

Normal / local detail:
    advance in fixed small steps

Scheduled detail:
    nextTaskCompletion  = T
    nextBirthCheck      = T
    nextSocialDecision  = T

Compression:
    advance to min(
        next scheduled event,
        requested target time
    )
```

**Built at #4.** One tick is one simulated second: fine enough for the hunger
crossing at 17:42 and the walk from 10:00 to 10:12 below, and for the finer
positions stepped detail needs at M3. Two hundred years is about 2.1e9 ticks, so
a signed 64-bit count is nowhere near a limit.

Threshold crossings share the one queue rather than forming a second input to
that `min()`. A predicted crossing and a discrete event are both *"wake me at
T"* and differ only in where they came from. What a threshold needs beyond
scheduling is **re-prediction** — Aldric eats, so the crossing he was booked for
is wrong — which is a cancel and a reschedule, not a separate structure.

### Threshold crossings make compression trustworthy

Jumping to the next *discrete* event alone silently skips disasters:

```
Oakshire has 100 food · consumption 10/day · next harvest in 30 days
Jump 30 days → 100 − 300 = −200
Famine discovered 20 days late.
```

The simulator must recognize *"food reaches zero in 10 days"* and make that a scheduled boundary. Thresholds include food depletion, starvation, age-stage transitions, pregnancy and birth, critical health, and army supply exhaustion.

This is the piece that makes 10,000× **trustworthy** rather than merely fast.

### Deterministic total ordering

```
SimulationTime
→ Phase
→ PrimaryEntityId
→ EventKindPriority
→ SecondaryEntityId
→ EventId
```

`SimulationTime → Phase → EntityId` alone is not a total order — one entity can have two same-phase events at the same instant. Without a complete discriminator, ordering depends on collection iteration and determinism silently dies.

**`EventId` is the last component because the five before it are not enough either.** Two events of the same kind, at the same instant, between the same pair of entities tie — two hauling trips finishing in the same simulated second, or a batch of birth checks scheduled "in thirty days" from a shared origin. A heap breaks such a tie on its array layout: identical on replay today, reordered the first time the queue is rebuilt from a save (§17) or compacted after heavy cancellation, and invisible to the cross-platform hash because desktop and IL2CPP execute the same operations and agree on the same wrong answer. `EventId` is already durable, monotonic, never reused and comparable, and a scheduled event needs one anyway for history and provenance — so the tiebreak is scheduling order, pinned as stored data rather than left to the container.

**Events emitted while handling another event are queued, never executed recursively.** Otherwise `PersonDied → household reacts → HouseholdEnded → settlement reacts → …` turns a clean domain-event architecture into callback spaghetti.

The scheduler enforces both rules rather than documenting them: it refuses to advance the clock from inside a handler, and refuses to schedule anything at or before the **position** it is dispatching. A same-instant reaction goes into a **later phase**, which is what the phases are for — and that keeps *"dispatch proceeds in non-decreasing key order"* a real invariant the WorldValidator can check.

**Position is the first five components; `EventId` is identity, not position.** The distinction is load-bearing. A freshly allocated id is always the larger one, so a guard comparing full identity can never reject a reaction landing exactly where its own cause did — and a handler that reproduces itself there dispatches forever with the clock frozen at one instant. Position alone still cannot bound a cascade that *climbs* — reacting for one person, then the next, then the next — so the clock also caps how far a single `AdvanceTo` call may cascade at one instant. Reactions booked ahead of time never count against that cap, so a legitimate same-tick batch is unaffected however large the population grows. The cap is per call, not per instant across calls: a paused player casting a power is an event at the frozen instant, dispatched by its own `AdvanceTo`, and a budget shared across calls would eventually throw at a player who merely acted enough times while paused. What the cap guarantees is that `AdvanceTo` terminates; a driver that keeps re-entering one instant has control between calls and can see for itself that time is not moving.

Process in phases:

```
Phase 1: physical / resource changes
Phase 2: lifecycle
Phase 3: household and social reactions
Phase 4: political reactions
Phase 5: derived and history notifications
```

Exact phases can evolve. The rule that must not is: **no reentrant event handling with arbitrary subscriber order.**

### LOD equivalence testing

Explicit tests, because the design *promises* that compression preserves identity:

```
Same seed, same initial state
  Run 10 years scheduled
  Run 10 years compressed
  Compare: population · births/deaths · resources · skills
           households · important events · political state

Run the same battle visible vs offscreen
```

Outcomes need not be bit-identical where detailed positioning legitimately matters — but they must obey **explicitly defined equivalence rules**. Otherwise you eventually ship *"zooming into battles makes your kingdom more likely to win,"* which players will swear is happening long before you believe them.

### Scheduled → stepped transitions

Because people are never destroyed there is no *person*-materialization problem — but there is a **task**-materialization problem.

Aldric is offscreen: leaves home at 10:00, arrives at the forest 10:12, chops until 10:25. The player zooms to him at 10:07. Where is he?

Scheduled tasks must retain enough state for the stepped simulation to reconstruct a plausible position and pose:

```
startTime · endTime · origin · destination · task phase · coarse route
```

This is central to the zoom-anywhere promise.

**Built at #52.** A task is a `WorkTask` on the worker: start, the three legs as durations (out, work, back), origin, destination, and the id of the one `TaskCompleted` booked for its end. The phase is arithmetic over those (`PhaseAt`), not a field — a stored phase is one more thing that can disagree with the clock. The coarse route *is* stored, beside the task in `Jobs`, copied from the band's site route when the worker sets out: the pathfinder is deterministic, so it could be recomputed from the endpoints, but a grid that changes mid-task (a bridge, one day) would then put someone on the far side of a river they never crossed. `Jobs.PositionAt(person, t)` is the reconstruction: the route cell reached by the fraction of the leg elapsed, or the destination while working. Nothing calls it before M3; it exists so the state proves sufficient now, when the check is cheap.

### Cadence: two different clocks

A single "Hz" column was wrong. At 1× one game-day is ~8 real minutes, which is 180 game-seconds per real second — so "needs at 1 simulated Hz" would mean 297,000 needs updates per real second across 1,650 people, and "diplomacy every 10–60 simulated seconds" would run diplomacy several times per *real* second.

**Real-time cadence** — presentation and local responsiveness:

| System | Rate |
|---|---|
| Rendering | 60 real FPS |
| Movement interpolation | 30–60 real Hz |
| Local avoidance | 10–30 real Hz |
| Visible AI reactions | 2–10 real Hz |

**Simulation-time cadence** — world processes:

| System | Cadence |
|---|---|
| Needs / physiology | integrate continuously; schedule threshold events |
| Economy | game hours to days |
| Social decisions | game days to months |
| Diplomacy | game days to months |
| Culture | generations and major events |

**Most of these should not poll at all once the scheduler exists.** Rather than checking Aldric's hunger every simulated second:

```
Aldric ate at 08:00 · current consumption
→ critical hunger threshold = 17:42
→ schedule HungerCritical at 17:42
```

### Time scales

**A year is 120 days** — four seasons of thirty (#11; the season split is #53's, read off the clock rather than stored, with day 0 the first day of spring). Decided ahead of seasons because ages needed a year first, and shorter than Earth's on purpose: a game year is watched at compression, every daily event is one the simulation dispatches, so a 200-year run is 24,000 days rather than 73,000, and "the 7th day of spring" is a date the chronicle can print without months. Day-scale numbers — gestation, the hunger grace period — are tuned to feel right against that year, not to match a calendar. The original figures below assumed ~365 days; the table is recomputed.

At ~8 real minutes per game-day, one game-year is ~16 real hours at 1×. So:

| Speed | 1 year | 100 years |
|---|---|---|
| 1000× | 58 sec | 1.6 hours |
| 10,000× | 5.8 sec | 9.6 min |

**1000× is not centuries speed.** The ladder needs to reach 10,000× or beyond:

```
1× · 5× · 20× · 100× · 1000× · 10,000×
```

Plus **run-until** modes: next season, next year, next major event.

This matters most for the 300-year race — without deep compression their longevity exists only on a character sheet. It also matters for the from-scratch start, where ~270 people must grow to ~1,650 over centuries.

Extreme modes need not animate normally. Speed is a primary system input.

---

## 5. Architecture

```
KingdomWatch.Core/          <- netstandard2.1, zero Unity dependencies
```

**The Core target is `netstandard2.1`, not `net8.0`.** Unity 6.x supports exactly two API compatibility levels — .NET Standard 2.1 (the default) and .NET Framework 4.8 — and its managed plug-in support table states that assemblies compiled for .NET Core, any version, are **not supported**. A `net8.0` Core assembly would simply not be consumable by Unity.

Tests and harness still target modern .NET; only the seam Unity crosses is constrained:

```
KingdomWatch.Core/          netstandard2.1   · zero Unity dependencies
KingdomWatch.Core.Tests/    net10.0          · references Core
KingdomWatch.Harness/       net10.0          · references Core
KingdomWatch.Game/          Unity 6.6 → 6.7  · consumes Core
```

**net10.0, not net8.0:** .NET 8 and .NET 9 both reach end of support on November 10, 2026. .NET 10 shipped November 11, 2025 as LTS, supported through November 2028.

**What netstandard2.1 costs is small.** It is a compile-time API surface constraint, not a performance ceiling — the harness still runs on the .NET 10 runtime and gets its JIT, and `Span<T>` / `Memory<T>` are available. The losses are newer BCL surface and a few C# features needing runtime support (`init` accessors and records want an `IsExternalInit` shim, a five-line file).

**This is temporary.** Unity is replacing Mono with CoreCLR targeting .NET 10 and C# 14 in Unity 6.8, after which Mono is gone. Timing is uncertain — the roadmap indicates end of 2026, while Unity has said CoreCLR would not be production-ready during 2026. For a multi-year project, netstandard2.1 is where Core starts, not necessarily where it stays.

**Do not multi-target Core** as `netstandard2.1;net10.0`. The debugging strategy depends on the harness and the Unity build reaching bit-identical state from the same seed (the cross-platform hash below). Two compilation targets is two chances to diverge, and any `#if` in simulation code makes divergence likely rather than merely possible. Single-target the simulation.

Core contents:

```
  World.cs             <- not built yet; the systems are still wired per caller (#17)
  Clock/                <- simulation clock, event scheduler, phase ordering
  Systems/              <- Needs, Jobs, Skills, Social, Households, Politics,
                           Economy, Seasons, Logistics, Combat, Relations,
                           Attitudes, Knowledge, Culture, Founding,
                           TownPlanner, Reservations, Milestones
  Data/                 <- SoA storage, entity handles, recipes, race tables
  Events/               <- domain events, subscribers, decision provenance
  WorldGen/             <- terrain, biomes, homelands, resources, sanity check
  Rng/DeterministicRng.cs
  History/              <- event journal + compaction
  Serialization/
```

**Rule: no game logic in MonoBehaviours.**

### WorldValidator

Go beyond "population didn't explode." Check invariants after every simulated day or year during harness runs:

```
Every EntityId is unique.
Every living person belongs somewhere.
No dead person has active tasks.
No resource count is negative.
No reservation without stock behind it: reserved is drawn out of available, never counted alongside it.
Every household member resolves.
Every settlement belongs to a valid polity.
Every child has valid parents.
Kinship graph has no cycles.
No polity has two rulers.
All durable references resolve, or intentionally point to historical stubs.
No entity is present in two spatial containers.
Every scheduler reference resolves.
No scheduled event targets a dead or recycled runtime handle,
    unless it explicitly targets a durable historical entity.
All resource reservations have an owner.
Household membership is consistent in both directions.
Every living PersonHandle maps to exactly one EntityId, and back.
Resource conservation audit balances.
```

Then fuzz thousands of seeds. When you get *"seed 39274 broke at year 347 because an orphan was adopted into a household deleted six ticks earlier,"* the simulator screaming immediately beats quietly corrupting itself for another 150 years.

**Where it lives (#13).** `Harness/WorldValidator.cs`, not `Core` — the checks are scoped to harness runs, and Core's public surface is the simulation's API rather than a home for diagnostics. `Core.Tests` references `Harness`, so the tests reach it the same way they reach `DrawCollisionDetector`. The seed sweep is `Core.Tests/Validation/SeedSweepTests.cs` for now, because the harness has no world to run until #17: `Harness/Program.cs` drives a scheduler soak with no people in it, while the composition roots that do assemble a world are test helpers. The sweep moves to the harness when #17 lands.

Most of the list above is checkable today. The exceptions wait on the systems that introduce them — polities and rulers with #39, reservation ownership with #24 — and were deliberately not written as rules that cannot fail, because a rule nobody has seen fire reads as coverage. Two more are wired but unreachable: `Genealogy.Record` refuses a non-person parent, refuses an unrecorded one, and writes each person once, so neither an invalid parent nor a kinship cycle can be built through it. They become live when #42 rebuilds a world from a save, which is exactly the path §17 warns can shift history.

One rule is broader than §5 states it. "Every scheduler reference resolves" is asked of both sides: of the queue, that each event still due names a primary entity that resolves — a person, a band or settlement, or a household, since six of the eleven scheduled kinds are owned by a community or a household rather than by a person; and of the systems, that every id a periodic stream recorded as booked is an id the queue still holds. The second half is what #80 exists because of, and it covers all nine places the pattern is used — three fields on `PersonRecord` and six records the streams keep for the communities they run for.

The rules go beyond this list where review has found something worth pinning: `PersonStore`'s `Count`/`Handle` integrity, `AgeStage` and `Sex` being defined values, `AgeStage` agreeing with `DemographicSettings.StageAt`, nobody born in the future, the job mirror agreeing with the task, and every record-named pending event still being in the queue. That last one found the bug that proved the sweep's worth on its first run: `Aging` booked each stage boundary without recording its id, so nothing cancelled one when its person died — the stream #80 missed, invisible because the handler drops a boundary for a person the store no longer holds.

**Cross-platform determinism check.** Produce a periodic world-state hash and run the same seed on desktop .NET and on Android under IL2CPP.

The hash is `Core/Validation/WorldHash.cs` (#13) — in `Core` rather than the harness precisely so the Unity build can run the same compiled code. Running it under IL2CPP and comparing is #90, which waits on #72 putting `Core` into a Unity build at all. It folds in people, households, settlements and the pending queue, each section tagged and counted so a partial comparison cannot read as agreement, and it mixes with `SplitMix64` rather than a BCL digest: a hash whose job is to prove two runtimes agree should not have a third implementation sitting between them. A system whose state it does not fold in is a system whose divergence it will not catch, silently — so adding one is part of adding a system.

**The hash must be canonical, not a hash of raw memory or layout** — sort by durable ID, serialize deterministic fields in a defined order, hash that. Otherwise desktop and IL2CPP disagree because representation differs even when the logical world is identical. Determinism is the foundation of the debugging strategy, so confirming the harness and the build reach identical state is worth doing early — before there is much state to diverge.

### Data layout

Two distinct identifier concepts. Conflating them is a latent history-corruption bug.

```csharp
// Runtime handle — indexes into storage, detects stale references.
public readonly struct PersonHandle {
    public readonly int Index;
    public readonly int Generation;
}

// Durable identity — never changes, never reused, safe in history and saves.
public readonly struct EntityId {
    public readonly EntityKind Kind;
    public readonly ulong Value;
}

// Durable event identity — never reused. Referenced by history, grievances,
// rumors, decision provenance, and player bookmarks.
public readonly struct EventId {
    public readonly ulong Value;
}
```

`PersonHandle` is for the running simulation. `EntityId` is for history, genealogy, save data, and anything that outlives the entity. Without the split, a reused storage slot silently repoints an old history entry at a different person.

The same split applies to households, settlements, polities, dynasties, and named animals or monsters.

`EntityId` carries its `Kind` as an explicit field rather than packing a tag into the `ulong`. Two plain fields are easier to read, test and print (`Person#1234`) than masks and shifts, and they let the WorldValidator check that a durable reference points at the *right kind* of entity rather than merely resolving to something. The cost is 16 bytes instead of 8, which nothing currently measures as a problem — the whole population is ~105 KB. Revisit if M2 profiling disagrees.

```csharp
public struct PersonRecord
{
    public PersonHandle  Handle;
    public EntityId      Id;      // None marks an unoccupied slot
    public WorldPosition Position;
    public short         Health;
    public AgeStage      AgeStage;    // #9: Infant … Elder, an enum since eligibility needs it
    public Sex           Sex;         // #9: fixed at birth
    public byte          BirthCulture, Assimilation;
    public SimulationTime LastFedAt;   // #51: hunger integrates from here
    public long          BornTick;    // #11: negative for founders; age is now - this, never ticked
    public EventId       PregnancyDue; // #11: the pending BirthDue, or None
    public EntityId      Household;   // #9: None for nobody's; see below
    // skills indexed separately: [personIndex * skillCount + skillId]
    public JobKind       Job;         // #52: the job they are on, set while they have a task
}
```

`Position` is the `WorldPosition` used everywhere else rather than a loose pair of ints. The sketch's `JobId` became `JobKind Job` at #52 — a role rather than a handle, because a job is a row in `JobTable` (the recipe it runs, the terrain it runs on) and not an entity with a lifetime. It was left out of #6 until that issue could make the call, and adding it then was a field plus an accessor pair, which is the entire point of storage living behind `PersonStore`. `Jobs` alone writes it, exactly while the person has a task; the death cascade clears it; and nothing in `Jobs` reads it back, since the bulk span can write it — the on-duty count comes from the tasks themselves, and the field is a mirror for readers and the validator.

`Household` is an `EntityId`, not the `HouseholdHandle` this section originally sketched (#9). Households are a few hundred plain objects in a registry rather than a recycled-slot store, so there is no generation to check, and a durable id that is never reused already makes a reference to a dissolved household fail loudly on lookup. The registry (`Households`, §6) is the only writer of the field, which is what keeps it and the household's member list agreeing. `PersonStore` also gained the reverse lookup, `TryGetHandle(EntityId)`: relationships are keyed by durable id because they outlive the people in them, so anything acting on kin gets ids back and needs handles to do anything with them.

`BornTick` is a raw `long` rather than a `SimulationTime` because that type refuses to be negative and worldgen seeds people who were forty before tick zero (#11). Age is the distance from it to now, computed when asked and never ticked; `AgeStage` is a reading of it that `Aging` (§6) refreshes at each boundary, kept as a field because every system branches on the stage far more often than anyone crosses one. `PregnancyDue` names the pending `BirthDue` event rather than keeping a due date of its own, so the record and the queue cannot disagree about whether someone is pregnant; the death cascade cancels the one and clears the other together.

**Dense records now; split measured hot fields into parallel arrays only if M2 says so.**

Struct-of-arrays exists to avoid cache misses on datasets too large to hold. At ~1,650 people and roughly 64 bytes of fields, the entire population is about **105 KB — it fits in L2 cache**. Per-frame cost will be dominated by pathfinding, job assignment, and the 100–300 stepped visible agents, not by linear scans over everyone. Hand-writing a mini-ECS before there is a game is optimizing a problem that does not exist yet.

Records also suit three things already committed to: the WorldValidator, the canonical world hash (*sort by durable ID, serialize deterministic fields in a defined order* — natural over records, fiddly over twenty index-correlated arrays), and versioned save migration.

Where SoA may eventually earn its keep is trees (~10,000) and animals, not people.

### Storage access layer

**Systems never touch storage directly.** Layout lives behind `PersonStore`, so changing it later is an afternoon confined to one class rather than a weeks-long refactor across every system — and refactors of that size are exactly where determinism bugs get introduced, which is the worst possible place given the debugging strategy.

```csharp
public sealed class PersonStore
{
    private PersonRecord[] _people;      // layout is private

    public PersonHandle Add(EntityId id, WorldPosition position, short health,
                            byte ageStage, byte birthCulture, byte assimilation,
                            SimulationTime lastFedAt);
    public void Remove(PersonHandle h);  // frees the slot for reuse
    public bool IsAlive(PersonHandle h); // the non-throwing question

    public short GetHealth(PersonHandle h) => _people[h.Index].Health;
    public void  SetHealth(PersonHandle h, short v) => _people[h.Index].Health = v;

    public Span<PersonRecord> RecordSpan();         // bulk path
    public IEnumerable<PersonHandle> Alive();       // scattered path
}
```

Runtime cost is effectively zero — small accessors on a sealed class are inlined by the JIT. **Use accessors for scattered single-entity access and spans for bulk loops**, so tight iteration keeps its vectorization. Accessors validate the handle and throw on a stale one, so a handle held across a removal fails where the bug is rather than silently reading a stranger.

`PersonStore` also owns slot allocation, which is what makes `PersonHandle.Generation` mean anything. **Removal tombstones a slot in place rather than compacting the array.** Swapping the last record into the freed slot would be denser, but it moves a live person to a different index while other code still holds handles pointing at the old one — and those handles would still carry a matching generation, so they resolve silently to the wrong person. That is the exact corruption the handle/id split exists to prevent, so density loses. A freed slot keeps the generation it reached and hands the next occupant that plus one; clearing it would send the next occupant back to generation 1 and make a handle from the *first* occupant match the second.

The consequence is that the bulk span covers every allocated slot and can include unoccupied ones — hence `RecordSpan()` rather than the `AliveSpan()` this section originally sketched, since a name promising alive-only would eventually be believed. Callers skip slots whose `Id` is `None`. Defragmenting is an M2 question if profiling raises it, not a guess to make now.

The bulk path is allocation-free. The scattered path is not quite: `Alive()` allocates one iterator per enumeration, so it is not the tick-loop path. `Core.Tests/Performance/SchedulerSoakTests` holds the scheduler to the broader zero-allocation claim (#59); systems added later get the same test.

**Correction on the Burst path.** `NativeArray`, Unity Jobs, and Burst are Unity dependencies, so they cannot be introduced into `KingdomWatch.Core` without breaking the zero-dependency rule that the harness, tests, and determinism strategy all rest on. If profiling ever demands them, the seam is a separate `KingdomWatch.UnityOptimization` backend — not an in-place change to Core.

The more likely outcome is that **1,650 people never require Burst or DOTS at all.** Do not architect an optimization backend now; simply do not assume Core can casually adopt `NativeArray` later.

Settlement, household, and polity IDs must all be **dynamic**, not fixed slots.

### Domain events

Too many systems react to the same occurrence for direct calls to stay maintainable. A single death touches households, job assignment, apprenticeship, inheritance, dynasty, marriage availability, the settlement skill pool, political succession, relationships, culture, the event feed, milestones, and history.

Publish meaningful simulation events; interested systems subscribe and react deterministically:

```
PersonBorn · PersonDied · MarriageFormed · HouseholdFormed
SettlementFounded · SettlementAbandoned · RulerSucceeded
WarDeclared · BattleEnded · DivineActWitnessed
BridgeDestroyed · FamineStarted · FamineEnded
```

This is a **domain-event layer, not event sourcing** — not every axe swing becomes an event. It feeds the history journal, milestone system, event feed, attribution, attitudes, and debugging from one mechanism.

**Built at #8.** Every event is one fixed-size `DomainEvent` — id, time, kind, two entity slots, reasons — published through a `DomainEventBus` that notifies subscribers synchronously in subscription order, sealed at the first publish so that order is fixed by wiring rather than by anything that happens at run time. The bus **refuses a publish from inside a subscriber**: a subscriber that must react by causing more events books a clock event into a later phase at the same instant and publishes from there, which is the queuing §4 asks for done through the one queue that already orders everything. The `EventJournal` is simply the subscriber that remembers; §17's compaction is still to come. A `ScheduledEventRouter` hands each scheduled wake-up to the system owning its kind, and that system publishes whatever the wake-up turned out to mean.

### Decision provenance

The README principle is philosophy until the game can actually answer *why*. When Oakshire declares war, the AI knows it weighed resource competition, cultural hostility, a recent border raid, ruler personality, and trade relations — but if those are discarded, the player only sees the outcome.

Store the top **2–4 `ReasonCode` enums** on decisions that surface in the feed:

```
War declared: Oakshire → Dunvale
  • Dunvale attacked Oakshire traders last year
  • Relations deteriorated over 23 years
  • Both claim the Red Valley

Mira left Oakshire
  • Food shortage
  • Husband died
  • Sister lives in Dunvale
  • Dunvale accepts migrants
```

Utility scores are never exposed — only the contributing reasons.

**Rule: emit reasons at the decision site; never reconstruct them afterward.** The code computing the utility emits the codes. A separate "explain" function that infers reasons after the fact will drift from the real calculation and eventually lie to the player, which is worse than showing nothing.

Cheap because it rides on domain events already being published, and it compacts with history like everything else. **The highest-value use is during development**, where the main failure mode is inexplicable emergent behaviour — a feud cascade or mass migration with no traceable cause. This is structured debug output with a schema.

### Determinism

Mandatory, binary, cheap now, near-impossible to retrofit.

- Fixed timestep; never derive sim time from `Time.deltaTime`
- Own seeded PRNG. Never `UnityEngine.Random`
- **Randomness is keyed to stable event and decision identity, not drawn from mutable subsystem streams** wherever execution paths can differ
- Stable iteration order — never iterate a `Dictionary` and act on the order
- Integer or fixed-point wherever the sim branches

A bug report becomes "seed 38471928, tick 183729" and reproduces exactly.

### Randomness must be keyed, not streamed

One stream per subsystem is better than a global RNG but is **not sufficient here**, because LOD changes execution path length. Detailed combat draws hit, damage, dodge, hit, damage…; compressed combat draws `resolveBattle()` once. Different consumption counts leave the combat stream at a different position *because the player happened to zoom in* — and every future battle changes.

Derive randomness from stable identity instead:

```csharp
Roll(worldSeed, RandomDomain.Combat, battleId, attackerId, attackSequence);
Roll(worldSeed, RandomDomain.Conception, householdId, attemptNumber);
Roll(worldSeed, RandomDomain.Social, personId, decisionEpoch, decisionType);
```

As built (#7, #57), a key names both what the draw is for and where it is taken from — `rng.Key(RandomDomain.Conception, RandomSite.ConceptionRoll).Mix(householdId).Mix(attempt)`. The domain is the coarse separation and the one that carries meaning; the **site** is mixed straight after it, at a fixed position, so that two different decisions sharing a domain still start from different key states. Worldgen sharing one domain across terrain and rivers is the ordinary case, not an abuse.

Declaring a site is not advisory: there is no overload taking a domain alone, so a draw that does not say where it comes from fails to compile. That matters because the failure it prevents is the quiet kind — a collision does not crash, fail a test, or disturb the cross-platform hash; the world stays perfectly reproducible and two things that should be independent simply move in lockstep forever.

Then adding a random draw in one event does not alter the random future of an entire subsystem. Presentation randomness is unconstrained — it never touches Core.

This is among the highest-value architectural decisions in a deterministic simulation.

---

## 6. Households and lifecycle

```
Person → belongs to → Household → occupies → Home
```

A household holds adults, children, dependents, a home, food access, and accumulated wealth.

The chain this enables:

```
Aldric + Mira marry → form household → need a home
→ housing shortage → settlement posts construction job
→ builders construct → children arrive → food draw rises
→ children reach adulthood → leave → form new households
```

**Population growth now physically changes the town.** That is the README principle applied to demographics.

### Household food

Settlement owns bulk resources. Household has food *access*, wealth/status, and possibly a small buffer. Meals consume settlement supply — no simulating every loaf in every pantry.

*UI consequence:* the "Grain: 4 days" house tooltip must read from settlement stores scaled by household size. Otherwise it advertises household economics that don't exist.

**As of #51,** `Hunger` is the draw: one `MealDue` scheduled event per food holder per day, at which each living member takes a ration from the holder's ledger. The holder is an `ICommunity` — a band or, since #54, a settlement; households have food *access* rather than food, so the draw does not go through them (#9). §4's per-person sketch — *Aldric ate at 08:00 → HungerCritical at 17:42* — is deliberately not what runs: the daily meal is itself a scheduled boundary, so a thirty-day jump dispatches thirty meals and dates the famine on the day the ledger runs short, and per-person meal *times* are a change to when the draw happens, which waits for M3's daily schedules. Hunger is integrated rather than polled: a person stores only when they last ate, and starvation is evaluated from that distance when a meal finds them unfed. Past a grace period each missed meal costs health; turning low health into a death is the mortality model's (§6 below, #11). When the ledger cannot cover everyone, the table is served in three sittings — dependents (infant, child, adolescent), then adults, then elders — and within a sitting in the group's insertion order; the tail goes without (#9, replacing #51's insertion-order placeholder). The rule protects the next generation and reads the way a chronicle would tell a famine. Status is not a factor: the only status that exists is the band's leader, and feeding priority belongs to the household, not the polity. `DaysOfFood()` is the tooltip's number as a query; a *predicted* depletion event waits until something aggregates meals over more than a day, because the ledger has no change notification to keep a prediction fresh.

### Age stages

Roughly a third of the population is children. They cannot sit at `Job = Child, Task = Idle` for sixteen years.

```
Infant       → mostly home, invisible
Child        → play, chores, socialization
Adolescent   → apprenticeship, work assistance
Adult        → full participation
Elder        → reduced work, high skill, social weight
```

Adolescent maps directly onto apprenticeship. No childhood simulator needed — but children must visibly exist in villages.

*Performance note:* ~500 children add to the stepped tier when visible. Budget for it.

As built (#11), the boundaries are birthdays in `DemographicSettings` — child at 3, adolescent at 12, adult at 16, elder at 55, placeholders — and `Aging` books one `AgeStageDue` per boundary per lifetime on the exact tick, at which the stage becomes whatever the age says. Advancing through the stages was going to be #22's (M3) and moved here because #17's population-stability run is meaningless if nobody born in it ever grows up; the adolescent-to-apprenticeship mapping stays with #22.

### Family formation

Partner eligibility checks age, race fertility compatibility, existing partnership, kinship, settlement distance, and relationship.

**Kinship: a hard ban through grandparents.** Parent, child, sibling, half-sibling, grandparent, grandchild — and aunt or uncle with niece or nephew, which the list originally omitted: they are closer than the first cousins the next paragraph makes a taboo, so leaving them out would permit what the taboo refuses (#9).

**First cousins are a culture taboo, not a rule.** Some settlements permit it, some don't, and it drifts. This reuses the taboo layer (§10) and gets a pressure valve for free: cultural reinforcement means a settlement with a strained mating pool and repeated failed pairings can loosen its taboo over time, so demographic pressure surfaces as cultural change.

**The real variable is founding lineage count, not the restriction list.** A 30-person band seeded as two extended families deadlocks within two generations; the same band seeded with 8–12 unrelated lineages is comfortable. Worldgen must guarantee a minimum count of unrelated founding lineages per band (§15).

Two mitigating facts: the pinch is early and temporary, since once bands settle and connect the mating pool becomes the polity rather than the band. And **elves are the case to test** — low fertility plus 300-year lives plus no interracial children means lineage diversification runs roughly five times slower than for humans.

Harness test: mating-pool viability over 500 years, flagging any settlement where eligible partners approach zero.

**Widows and widowers may remarry** after a mourning period. Culture can modulate its length. This matters demographically — in a 1,650-person world, blocking remarriage wastes fertile adults.

As built (#9), `FamilyFormation` is the rulebook and not the matchmaker: `Evaluate(a, b)` answers "may these two?" with a `PartnerRefusal` — same person, not adult, same sex, already partnered, mourning, kinship banned, cousin taboo, no home — and `Partner(a, b, reasons)` does "they do": publishes `MarriageFormed` with the caller's reasons, records the partnership against that event, forms a household in a newly claimed home and moves both in with any dependent children of theirs, dissolving a household left empty. Choosing *who* pairs off is the social decision system's (#38) and the only place randomness enters. A refusal is a reason rather than a bool so the decision system can tell "wrong" from "early". Three of the inputs above are not checked because they do not exist yet: race fertility (#34), settlement distance (#54) and the relationship between the two (#38 weighs it before asking). The mourning period and the cousin taboo are `FamilyFormationSettings` until culture exists to own them (#40). Both people must be in the genealogy — founders with no parents — or kinship cannot be checked and the genealogy throws rather than guess. `AgeStage` became an enum here (Infant, Child, Adolescent, Adult, Elder) because eligibility and adoption both branch on it; advancing people through it became #11's. `Sex` was added to the record for the same reason: children have a mother and a father.

**Until #38, `Matchmaking` chooses (#54).** A stand-in, and it says so in its remarks: once a year per community, each unpartnered adult woman considers the unpartnered adult men in member order, and each pair `Evaluate` allows gets one keyed draw (`RandomDomain.Courtship`, mixed with both ids and the year) against a chance that falls with the age gap — 25% a year at the same age, 2.5 points less per year apart, floored at 5% — and the first accepted man is the one. Marriages therefore spread over years rather than firing the moment two people are eligible, and a couple a generation apart is rare rather than impossible. It weighs no social tie, status or family view; that is what #38 replaces it with. The numbers are placeholders.

### Property

**Hybrid ownership.** The household owns the home and bulk goods. Individuals own personal wealth and status; Tools, Weapons and Armor are checked out from the settlement rather than owned (below).

On death, personal wealth folds into the household. This gives some wealth variation, without the machinery of full dynastic inheritance law. Tools, Weapons and Armor are the exception: the [economy ladder](kingdom-watch-economy-ladder.md) (#78) settles them as checked out from the settlement's own stock rather than owned outright, destroyed with their holder rather than inherited — so a master's *skill* is the asset that survives them, not their kit.

As built (#9), the household side is a `Household` with an id, a home and a member list, and no ledger: meals draw from the settlement's — today the band's — stock, and the household decides who eats first. The individual side has nothing to stand on yet — no personal wealth — so it is #68, and the transfer-on-death step joins the cascade with it. Homes are behind an `IHousing` seam whose only implementation is `CampSpace`, unlimited and identity-less (§15: temporary dwellings satisfy the requirement); the housing stock that actually runs short is #69.

### Death cascade

Most of this is mechanical and must happen, or it generates bugs:

```
Cancel tasks and reservations
Vacate the job → work manager reposts it
Break apprenticeship in both directions
Transfer personal wealth to the household
Emit PersonDied
```

Death **never deletes a genealogy or partnership edge**: genealogy is untouched, and a partnership is marked ended — a grudge against a dead man still shapes how his family is treated. Social ties and memories follow their own retention rules instead (see Relationships, below): a tie toward the dead decays out, and the grudge itself is a memory.

The decisions on top of that:

- **Dependent children stay with the household.** With no surviving adult, the **nearest kin household adopts them**.
- **When a household empties, its home returns to the settlement's housing stock** for reassignment. This is not just tidy: housing supply throttles household formation and therefore fertility, so a plague that empties houses makes it easier for the survivors' children to marry.
- **Political succession is handled separately** by the polity (§8), not by household inheritance.

As built (#9), the cascade is `Deaths.Die(person, reasons)`: one synchronous operation, in one order, whoever decided the death — the mortality model (#11), starvation, injury. It publishes `PersonDied` first, because the partnership record names the event that ended it; then ends the partnership, prunes the dead from every witness list, takes them out of their household, strikes them from their band (a dead leader is simply no leader; who leads next is #54's or #39's), and frees the storage slot. It is deliberately *not* a chain of phase-separated reactions: §4's phases exist so that reactions to a death land after it, and the cascade is not a reaction but what the death is. Reactions still get their turn through the event. Two consequences worth knowing: a subscriber hearing `PersonDied` sees the world from just before it, which is fine because subscribers listen and book rather than act; and `Die` cannot be called from inside a subscriber, because its own publish is the recursion the bus refuses.

Adoption walks the genealogy by degree — a surviving parent, then adult siblings, grandparents, aunts and uncles, first cousins — for a living adult with a household other than the orphaned one, and within a degree takes the lowest id, so two runs agree on who took the child. Dependents are everyone below Adult, adolescents included: §6 puts full participation at Adult, and an adolescent alone in a house is a child alone in a house. With no kin to take them, the orphans keep the household; nothing invents a guardian, and the validator (#13) can flag a household with no adult. Tasks and the job joined the cascade with #52: `Jobs.Vacate` cancels the pending completion, returns any inputs in process to the band's ledger and clears the job, before the person leaves the household and the band. The checklist's "work manager reposts it" happens implicitly rather than as a step: the next free hand sees the shortfall (§12). The steps that act on things not yet built join the cascade when they are: reservations (#24), breaking an apprenticeship (#22), destroying a master's checked-out Tools/Weapons/Armor rather than passing them on (#78), personal wealth (#68). They are added to `Deaths`, not subscribed, for the reason below.

### Relationships

**Before M1**, since marriage already depends on this. Relationships are a headline feature and were the least-defined data system in the plan. Four distinct kinds, with different retention rules:

| Kind | Contents | Retention |
|---|---|---|
| **Genealogy** | parent, child, sibling derivation, ancestry | Never decays. Permanent. |
| **Partnership** | spouse or partner | Permanent, marked ended on death |
| **Social** | liking, friendship, resentment, familiarity | Bounded and decaying |
| **Memory / grievance** | event-specific, witness-tracked | Tiered like other knowledge (§11) |

The split matters because v6 marks dead relationship edges rather than deleting them. **A 300-year-old elf cannot retain unbounded relationship objects for everyone they have ever met.** Genealogy persists forever because it is small and structural; ordinary acquaintance decays and compacts.

Retention thresholds are tuning work, not architecture — but the four-way split is architecture and must exist before relationships are written.

As built (#10), the split is four stores under `Core/Relationships/`, not one graph with a kind flag — `Genealogy`, `Partnerships`, `SocialTies`, `Memories` — because one edge shape would carry dead fields for three of its four uses. All are keyed by `EntityId`, since relationships outlive the people in them, and reads come back as spans so the marriage checks and social decisions that run under the tick loop stay allocation-free. Three things the table above leaves implicit:

- **"Marked dead, not deleted" is true of two kinds, not four.** Genealogy is untouched by death; a partnership is marked ended by the `PersonDied` event. Social ties have no dead flag — a tie toward the dead decays out like any other, because the grudge that outlives its object is a memory, not a tie. Memories are forgotten or promoted by tier.
- **Genealogy cannot form a cycle by construction.** A person is recorded once, and their parents must already be recorded, so nobody can be named as a parent of their own ancestor. The validator's cycle check (#13) has nothing to find; it exists to catch a save that was edited by hand. `Genealogy.Kinship` names the nearest relation within two generations, which is the vocabulary the ban and the cousin taboo are written in; family formation (#9) decides policy over it.
- **Decay and compaction are caller-driven.** `SocialTies.Decay` and `Memories.Compact` apply whatever time has elapsed; the system that owns a person's social life calls them on its cadence. The stores never touch the clock, and the death cascade (#9) calls `Partnerships.End` and `Memories.WitnessDied` directly rather than the stores subscribing to `PersonDied` — bus subscribers listen, they do not mutate.

### Minimal demographic model

M1 requires births and deaths, so the *mechanism* must exist before it — though the numbers are tuning, not architecture:

```
Conception eligibility · conception probability · gestation · birth
Age-specific natural mortality · maximum and soft lifespan
Nutrition and health modifiers
```

**Do not poll mortality.** `every second: roll chance of dying` is exactly the pattern the scheduler exists to replace. Evaluate mortality at sensible age and health intervals; let starvation and injury raise their own threshold events (§4).

Human and elf life tables are tuning. The mechanism is not.

As built (#11), the mechanism is three systems under `Core/Lifecycle/`, each owning its scheduled kinds the way `Hunger` owns `MealDue`, and one table, `DemographicSettings`, holding every number — one instance per race, so the elf table (#34) is a second instance and not a second system.

- **`Fertility`** — `BirthCheck` per household every ten days (the kind §4 reserved). The first woman in the household of fertile age (16–45) whose active partner is a living man in the same household conceives with a keyed chance, if she is not already pregnant, has not given birth within the postpartum period (a year, read off her youngest living child), has eaten within the hunger grace period and is not below the health floor — the nutrition and health modifiers, as gates. The postpartum gate is load-bearing: a delivery and a check share an instant whenever gestation is a multiple of the check interval, and the delivery runs first, so without it she would conceive the day she gave birth. Conception books `BirthDue` at term (90 days) and writes its id to the mother's record: **the pregnancy is the pending event**, which §17 already names as a future commitment a save keeps. At term the child is added at the mother's position, in her household and her band, recorded in the genealogy with both parents, and announced last with `PersonBorn`. Race fertility compatibility waits for #34; who the couple is remains #9's rules and #38's choice. Households arrive by subscribing to `HouseholdFormed`; a check that finds its household dissolved books no successor. Each household's pending `BirthCheck` is recorded and any other one refused (#80) — two streams would check twice as often, doubling the conception chance per interval; `Fertility` keeps that in a map of its own rather than on `Household`, which is a plain registry object with no state of its own to lend.
- **`Mortality`** — `MortalityCheck` per person once a year, on their birthday, rolling the year just lived — so the first birthday rolls infancy's first year, the maximum birthday is the one nobody survives, the table's "chance per year" means what it says, and deaths spread across the calendar instead of landing on one day. The base chance is the stage's rate until the soft lifespan (70), then a straight line to certainty at the maximum (100); being below the health floor or unfed past the grace period multiplies it. Starving to death is not a roll: the meal that takes health to zero raises `StarvationCritical` for the same instant in the lifecycle phase, and `Mortality` answers it with `Deaths.Die` — the phase model doing exactly what §4 built it for, since the meal's loop over the table must not be the thing removing people from it. Reasons are read off the age: `OldAge` past the soft lifespan, `Illness` before it, `Starved` for the crossing. Since #53, a cold night that takes health to zero raises `ExposureCritical` in the same way, and `Mortality` answers it with `Froze`. A birthday check that finds someone already at zero names `Froze` when they were fed but cold, and `Starved` otherwise. The person's record names the check they booked and any other one is refused (#80), the rule `Hunger` and `Jobs` apply to their streams — two streams would roll the life table twice a year and read as a mistuned table rather than as a bug.
- **`Aging`** — above, under Age stages.

Two things worth knowing. People reach `Aging` and `Mortality` only through `PersonBorn` — subscribe, book the first wake-up — so worldgen announces founders at tick zero the same way a birth announces a child, and there is one way in. And a wake-up that comes due for the dead is ignored rather than cancelled: durable ids are never reused, so there is nobody it could wrongly touch. The pending events that *are* cancelled are the ones a record points at — the pregnancy, and since #80 the next `MortalityCheck` — because a record that names an event can also tell a stray one from the real one. Health never recovers yet (#51 damages it and nothing heals it), so the health modifier on mortality is permanent for anyone who has ever starved; that is a gap in hunger, not in mortality, and is noted there.

### Population equilibrium

**Do not rely on winter mortality as the balancing mechanism.** If a healthy world needs deaths each winter to avoid explosion, players will eventually feel the spreadsheet.

The strongest regulator is **age at household formation**, and it is now a mechanic: forming a household requires an available home; homes require labour and materials; so housing supply throttles fertility naturally. Add food availability, land, and migration.

Winter deaths become an *outcome* of poor harvest, war disruption, bad storage, or a harsh season. A calm prosperous town has a boring winter — and that itself tells the player something.

---

## 7. Social and personal decisions

**This is the system that makes the CK layer real.** Without it, traits and grudges are storage and UI with no causal power.

Alongside the work system, adults run a low-frequency personal decision pass — per in-game day, week, or month — that translates:

```
trait + memory + relationship + ambition + status  →  action
```

Candidate actions:

```
Court someone · Marry · Form household · Seek apprenticeship
Change profession · Migrate · Reconcile · Escalate feud
Challenge leader · Support claimant · Form faction
Commit crime · Seek revenge
```

### Launch scope

**At launch: marriage, household formation, apprenticeship, migration, and profession change.** Feuds, revenge, reconciliation, factions, crime, and leadership challenges are deferred.

**Traits therefore do not unlock actions — they bias the five that exist.** An ambitious person is likelier to change profession toward high-status trades; someone who resents the chief is likelier to migrate away. A grudge is a reason to leave rather than a reason to kill.

*What this defers:* political drama. The world will have rich economic and family history and thin political history. War still functions — polity-level diplomacy produces it from cultural distance and resource competition — but wars will not have personal causes at launch.

**Risk:** this is a utility AI running over ~1,000 adults and it is the most likely source of runaway emergent weirdness — mass migration, everyone changing profession at once. Harness-test it from the first day it exists, over centuries, across many seeds.

---

## 8. Politics

Polities are the unit of war, alliance, and tribute.

**World start:** one unified polity per race. All early conflict is therefore inter-racial, which is thin — so the texture must come from **fragmentation**.

```
Band grows → settles → founds daughter settlements
→ distance, culture drift, and feuds strain the polity
→ secession → petty kingdoms → occasional reunification
```

Secession is the same mechanism as the schism-and-exile founding trigger from §11. A settlement founded by exiles *is* a secession.

**Polities hold their own diplomatic state**, distinct from the settlement attitudes in §10. Polity relations are moved by polity-level events — wars, treaties, raids, border claims, ruler personality, cultural distance, resource competition — rather than being recomputed from member settlements each time.

The productive consequence is **disagreement between a ruler and their towns.** A polity can be at war with a neighbour whose settlements individually bear no grudge, which means a ruler can drag unwilling towns into a fight. Those settlements then have a concrete reason to secede — which is exactly where the fragmentation arc comes from. Let the disagreement mean something rather than smoothing it away.

This feeds decision provenance directly: *"Oakshire declared war — border claim on Red Valley, relations deteriorated 23 years, ruler is Vengeful."*

**Succession** needs a basic model — hereditary, elected, strongest household, or culture-dependent. Culture already influences it via the hierarchical ↔ egalitarian value, which is a good hook that requires the underlying political model to exist first.

---

## 9. Economy, technology, and seasons

**Target as many resources as their chains earn, with two-step chains, defined as data.** The original estimate here was ~10; the [economy ladder](kingdom-watch-economy-ladder.md) (#78) settled on seven after two of the original ten — Clay and Pottery, plus a third, Hide — turned out to have no use that another resource didn't already cover. Padding toward a round number was never a reason to track one separately.

**The contents live in [the economy ladder](kingdom-watch-economy-ladder.md) (#78).** This section is the reasoning — why a capability graph rather than a tech tree, and why skill tiers resolve the chicken-and-egg. The ladder is the enumeration those arguments were always about: the resources and their chains, the jobs by tier, the buildings with their five gates, and the six milestones as conditions the sim can test, with target pacing. It is a map rather than a schema — nothing in it is implemented, and the append order for `ResourceKind` and `JobKind` is deliberately left to the issues that append.

```
recipe: smelt
  inputs:  [ore x2, charcoal x1]
  building: smeltery
  outputs: [metal x1]
  duration: 60
  substitutes: []
```

Start at 3–4 for M1, expand toward seven by M6 (the [economy ladder](kingdom-watch-economy-ladder.md), #78, settled the count and the set). Recipes are data (`Recipe`, `PrimitiveTier`); the resource set itself is a `ResourceKind` enum rather than a config file, as of #12. The need a file would serve — changing the set without a rebuild — does not exist yet, and per-resource data (weight for hauling) can live in a table keyed by the enum when a system first needs one. Every mutation funnels through the ledger, so swapping the enum for a table id later is mechanical. M1 ships Food, Wood and Stone, all gathered; the rest of the seven, and the Tools/Weapons/Armor the smithy forges from Metal, wait for #37 and #22.

**As of #52, people gather them.** A `JobKind` — Forager, Woodcutter, StoneGatherer — is a row in `JobTable`: the recipe it runs and the terrain it runs on (plains or forest, forest, hills). A band's living adults and elders work, tierless and at full output, inside a dawn-to-dusk window (06:00–18:00, placeholders): a worker picks a job when free — at dawn and at each completion — and starts one task of it, at the band's cheapest-to-reach site for that job, if the task would end by dusk. Need is three thresholds read live: forage while food, counting what is on its way home, covers fewer than ten days; then wood and stone to a stock cap; then idle. The window is #21's daily schedule in miniature and the thresholds are #23's town planner in miniature; both replace their part without touching the other. Elders' reduced work and adolescents' assistance are #22's, with the skill tiers they belong to.

### Technology is a capability graph, not a tree

**There is no research system.** Capability is gated by infrastructure, which the recipe graph already expresses. Wanderers work every job untooled because they have no smithy. Build a smithy — with a settlement, ore access, and a skilled person — and Tools become possible: a rate bonus on top of what already works, never a precondition for it (#78).

The only addition needed is a **primitive tier**, buildable with no buildings — foraging, hunting, gathering, dry-stone stacking, campfire smelting, temporary camps. The economy ladder (#78) settles the actual set, one crude form per capability.

**This is not free, and pacing is the open problem.** If every settlement implicitly knows every recipe, then camp → farm → quarry → smithy → hall → stone wall can cascade almost instantly and near-identically in every world. Gating needs to come from conditions the sim already tracks:

```
Building requires:
    minimum relevant skill in the settlement
    necessary inputs available
    prerequisite infrastructure
    sufficient labour surplus
    actual settlement demand
```

### Skill tiers resolve the chicken-and-egg

*How does anyone become skilled enough at smithing if nobody can smith until a smithy exists?*

**Every capability has graded skill tiers, and tier zero requires no dedicated building.**

```
Novice → Apprentice → Journeyman → Expert → Master
```

A novice works metal badly at a campfire pit. Doing so builds skill. At a threshold, a proper smithy becomes viable, and the smithy then enables faster learning and better output.

Tiers gate what a settlement can attempt — a smithy needs a journeyman metalworker, stone construction needs an expert mason.

*Naming caution:* `SkillTier.Apprentice` is not the same as being in an apprenticeship. Keep the tier and the relationship distinct in code.

```
crude, buildingless work
    ↓ learning by doing
sufficient skill
    ↓
crude forge becomes viable
    ↓
true smithing job
    ↓ apprenticeship accelerates mastery
mastery
```

If the lowest tier still requires infrastructure, the deadlock simply moves one rung up. **Every capability must have a reachable path from primitive life.**

This is also **the capability-graph pacing lever.** If a smithy needs a journeyman metalworker and reaching journeyman takes years of crude work, the technology climb is paced by human learning time rather than a timer — and it varies per world, because it depends on who happens to be good at what.

So a smithy is not `Year > 50`, it is *permanent population + charcoal production + accessible ore + a sufficiently skilled craftsperson + spare labour*. Most of that is implied by systems already in the plan — but recognize that this constitutes a **capability graph**, which is a soft technology system even though nobody researches anything. Name it, and tune its pacing deliberately.

The payoff is still the game's progression arc — stone age band to castle town — and better milestones than population thresholds: first camp, first permanent structure, first farm, first smithy, first stone building, first wall.

An explicit tech ladder is a v2 consideration at earliest.

### Graceful substitution is mandatory

At seven resources the deadlock surface is real. Every recipe needs a substitution list — the economy ladder (#78) settled that a degradation path is not the mechanism: nothing in the ladder degrades, resource or equipment, and substitution alone is what the harness test below actually needs. Harness test: 200 years with no settlement ever deadlocking.

### Seasons

Full seasons turn the economy from a rate into a cycle:

- **Storage is core** — granaries, stores, and the capacity limits that gate them. The [economy ladder](kingdom-watch-economy-ladder.md) (#78) settles this without spoilage: nothing decays in storage, capacity is the only supply-side constraint
- **Famine is a timing problem.** Adequate annual output can still starve a town in March — and you can watch the granary empty
- **Campaigning season** — armies marching in winter starve
- **Seasonal work reassignment** — farmers do something else in January
- **Livestock need winter fodder**; game grows scarce in hard years

Balancing requires multi-year harness runs.

**Built at #53.** The calendar is read off the clock: `SimulationTime.Season` and `DayOfSeason`, with nothing stored, so there is nothing to save or hash and no season-turn event. Day 0 is the first day of spring. `ToCalendarString` prints "day 7 of Summer, year 12" for the chronicle, and `ToString` stays the tick-carrying form a bug report quotes.

- **Foraging follows the seasons; nothing else does.** Yields per task are 3 in spring, 4 in summer and autumn, and 1 in winter. Winter's 1 is under the daily ration, so winter is lived on stores.
- **Bands provision for winter.** `Jobs`' food target is ten days' draw. From the first day of summer it adds a whole winter's draw, and in winter it adds what is left of the winter. The wood target is the cap plus a winter of fires for every hearth in the band, raised the same way. Without that look-ahead a ten-day buffer starves every band every winter, which is the winter-as-regulator §6 rules out.
- **Winter heating is `Warmth`**, `Hunger`'s shape as the economy ladder's §2 asked. Each winter evening a community burns one Wood per household hearth, plus one communal fire for anyone in no household. When wood is short, households with a dependent are lit first, then the rest in formation order, then the communal fire. Two dark nights are hardship, and from the third each costs health. Zero health raises `ExposureCritical`, and `Mortality` answers it with `Froze`.

A famine or a cold house is now something that went wrong: too few hands, a band grown since summer, a woodpile the foragers crowded out. Every number is a placeholder until #17 runs a world. Livestock, fodder and campaigning season wait on §13 and M4. Charcoal as the efficient fuel waits on the resource being appended.

---

## 10. Races and culture

### Races

Two at launch. **A race difference should be a number in a table, not a new system.**

Cheap: skill learning modifiers, diet ratios, terrain preference, combat values, lifespan, fertility.
Expensive: unique buildings, unique jobs, unique resources.

**The cost of 300-year life:**

- **Fertility must pay for longevity** — non-negotiable, or they dominate within centuries
- **Succession stasis** — a chief ruling 250 years means almost no political churn
- **A mastery ceiling is required**, or the long-lived become strictly superior at everything
- **Feuds outlive generations** — the payoff. A 300-year-old remembers an atrocity the perpetrators' descendants have forgotten
- **Relationship lists need decay**; longevity forces what you need anyway

**Interbreeding: couples yes, children no.** A childless pairing is a political problem in a dynastic game — a human chief who falls for an elf has no heir. It is also a population sink; treat that as a feature, so integration carries a real cost.

### Culture lives on both settlements and people

Settlements carry a culture. **People carry a birth culture plus a current cultural affinity**, so migration assimilates gradually rather than instantly:

```
First generation:  mostly Oakish
Second:            mixed
Third:             mostly Dunvalian
```

Children primarily inherit their surrounding settlement culture.

### The five layers

**Shipping at launch: 1, 3, and 4.** Architecture styles and craft tradition are deferred.

1. **Naming conventions** *(launch)* — highest value-per-cost item in the plan. Names drift per settlement; after 300 years you can tell where someone was born. Essentially free.
2. **Architecture style** *(deferred)* — sprite variants. Deferring this means **culture is invisible on the map**: you learn a settlement's character by tapping, not by looking. A cheaper middle option exists if that legibility is missed — banner colours, roof tints, or a single distinguishing detail rather than full building sets.
3. **Values** *(launch)* — martial ↔ mercantile, insular ↔ open, hierarchical ↔ egalitarian, reverence.
4. **Taboos** *(launch)* — won't eat fish, won't trade with the coast, burial rites. Cheap friction.
5. **Craft tradition** *(deferred)* — 200 years of smiths produces smiths faster. Culture as an *output* of the apprenticeship system.

### Divergence

- **Drift** — small deterministic mutation each generation
- **Reinforcement** — lived experience pushes values. Repeatedly raided → martial. **Culture becomes a record of history**
- **Convergence** — trade, marriage, migration pull neighbours together; isolation preserves difference

Consequence: **islands produce divergence, continents produce homogeneity.** Map archetypes now generate different *kinds* of world.

Cultural distance feeds diplomacy — distant values breed distrust, giving non-arbitrary war causes.

### Settlement attitudes

One system, three uses. Each settlement carries attitude values toward **each race, each other settlement, and the god** — moved by events, decaying over time, reinforced by repetition.

Migration checks the target's attitude toward the mover's race plus the mover's personal reputation. A settlement raided by elves thirty years ago will not accept an elf migrant.

**Grievances track their witnesses.** Rather than a flat decaying number, each grievance stores who was present, and its strength derives from living witnesses plus descendants who were taught it.

This buys two things a flat decay rate cannot:

- **Elves remember because they were personally there.** A settlement whose population turned over through plague or migration genuinely forgets, because nobody living witnessed it. The lifespan effect is caused rather than asserted.
- **Grievance can be inherited.** Descendants taught the event carry it forward, so a feud can outlive everyone who saw it and become a family inheritance.

Cost: a structure that grows over 500 years. It must live inside the bounded-knowledge tiering (§11) and the history compaction plan (§17). The clean maintenance hook is decrementing witnesses off the `PersonDied` domain event; cap stored witness lists and let old grievances decay or promote like other knowledge.

---

## 11. Knowledge and attribution

There is no magic in the world. Every supernatural event has exactly one cause: the player. The people know this, and react to it — with awe, fear, gratitude, or suspicion — through the same attitude and knowledge systems everything else runs through. Almost nothing new to build.

### Attribution is not omniscient

Separate **"knows the god exists"** from **"knows the god did this."**

```
Divine event → witnesses → settlement knowledge
→ rumor propagation along trade routes → cultural interpretation
```

Lightning-strike a wolf in an empty forest and nobody knows. Save Oakshire dramatically and the villagers see it; two years later traders tell Dunvale ("*they claim* their god protected them"); twenty years later it is the Storm at Oakshire, told as legend.

This reuses trade networks, culture, and the event journal you already have, and it is the most on-theme system in the design. Don't build sophisticated rumor AI first — but leave room for knowledge propagation instead of global omniscience.

**Knowledge must be bounded.** Fifteen settlements × thousands of events × per-settlement versions grows without limit. Tier it, matching the history compaction model:

```
Recent important claims   → stored individually
Old claims                → decay and disappear
Historically important    → promoted to cultural memory, persists indefinitely
Unimportant              → forgotten
```

A wolf struck by lightning is forgotten in ten years. The Storm at Oakshire is promoted and outlives everyone who saw it.

**Possible unification, to investigate at M5 — not committed now.** Witness-tracked grievances (§10) and rumor propagation are starting to look like one concept with two meanings:

```
HistoricalClaim / Memory
    OriginEventId · Meaning / valence · Witnesses
    Confidence · KnownBy · CulturalImportance

grievance = memory with a negative attitude effect
atrocity  = memory with political or racial meaning
```

A divine act is just a memory like any other, its valence set by who it helped and who it hurt — §8's "Oakshire declared war" reasoning applies equally to "the god struck the raiders' captain."

Deferred deliberately. **Cheap insurance in the meantime: key both grievances and knowledge claims on `OriginEventId`** so the two can converge later without a data migration. Don't let their internals diverge structurally before M5.

### Attribution splits opinion

Smite a raider captain: the saved village reveres you, the raiders' home settlement curses you. One act, two responses, from the attitude system already required for race relations.

---

## 12. Settlement systems

### Hierarchical job assignment

The work manager decides need (`8 lumberjacks, 4 miners, 12 farmers, 3 builders`); citizens choose among posted jobs, weighted by skill. An order of magnitude cheaper than per-citizen utility AI and far easier to debug.

**As built (#52), for bands — and, since #54, settlements, through the same `ICommunity`; a band with a destination at dawn starts nobody, everyone walks with it.** "Decide need" is three thresholds on the band's ledger, and "choose" is the first job in a fixed priority — food, wood, stone — that is needed, has a reachable site, and fits before dusk; skill weighting waits for skills (#22). The choice is made when the person is free rather than once a day, so a band whose food is covered by noon sends its afternoon hands to wood. Nothing is stored between picks: how many are on a job is a scan of the members, and what they will bring home counts toward the threshold so a round of pickers cannot all fill the same gap. The town planner (#23) replaces the thresholds with a plan; the choosing stays.

### Buildings emerge from systems, not thresholds

```
Population rises → tool demand rises → shortfall detected
→ smithy requested → construction job posted
→ materials hauled → built → skilled worker assigned
```

### The town planner may be the hardest AI problem here

Because the player never places buildings, the AI must produce towns that are functional, walkable, expandable, defensible, and attractive. The entire appeal of zooming in evaporates if villages look like spilled LEGO.

```
Settlement center
Road network
Plots / candidate sites
Building placement preferences:
    house       → near road, near kin
    farm        → fertile, outskirts
    lumber camp → forest edge
    smithy      → work district
    town hall   → central, prominent
    defenses    → perimeter
```

**This belongs in M3**, not later. It is a top-five prototype risk.

### Resource reservation

The classic colony-sim failure: five logs exist, four haulers all target them, one arrives first, three stand around. Villagers look like drunk Roombas.

Tasks must **reserve** resource quantity, target, workstation, construction slot, and possibly path destination *before* starting. Reservations cancel when the agent dies, the task changes, the resource disappears, the building burns, or the path becomes impossible.

Boring system. Determines whether your villagers look intelligent.

### Movement and collision

**Agents do not hard-block grid cells.** Buildings, walls, water, and terrain block movement; people use soft local separation and avoidance, which is a visual concern rather than a simulation one.

Hard agent collision in a village with markets, roads, gates, and 200 people is an excellent way to spend three weeks debugging traffic jams instead of building a god game. Genuine congestion can become a simulation feature later if it proves interesting.

### One authoritative resource ledger

Nearby, `Bob picks up 4 logs → carries → deposits`. Offscreen, `lumber production +40`. If those operate against different state, changing LOD duplicates or loses resources.

**All quality levels manipulate the same accounting model**, and conservation is an explicit invariant:

```
CURRENT STOCK
    available + reserved + carried + in-process  ==  current material stock

AUDIT (counters kept by the ledger; the equation is checked by WorldValidator, not per mutation)
    opening + produced + gathered + imported
      ==
    current + consumed + exported + destroyed + embodied in construction
```

The visible pile of logs is a *rendering* of the ledger, not the authoritative state.

As of #12, `ResourceLedger` is that model: one per holder (`MobileGroup.SharedSupplies` now, settlements later), stock derived from the four buckets so the first equation holds by construction, every mutation a named operation that refuses to go negative, and the flow counters bumped by the operations because there is nowhere else they could be counted. Both equations are structural while the ledger is the only write path; they become real checks the moment save/load or a bulk path exists. Reserved and carried are defined from the start so that reservation (#24) and hauling extend the ledger rather than growing a second representation.

### Founding and abandonment

- **Triggers** — population pressure, resource exhaustion, or **secession after a feud** (best: ties founding to the relationship and political layers)
- **Site selection** — fertility, water, defensibility, distance, traversal cost, race terrain preference
- **A founding party physically travels.** Free once armies exist, and a great scene
- **Abandonment** — below a population floor, survivors migrate to the nearest settlement that will accept them (§10 attitudes)
- **Ruins persist** as world features

### Daily schedules

Homes, workplaces, meal times, sleep, and a layout where walking home is plausible.

### No interiors

Buildings are opaque boxes — kills interior art, pathfinding, furniture, room sim, and occlusion. Best trade in the design.

**Buildings report without simulating** (§6 on the tooltip caveat).

**Push social life outdoors.** Wells, markets, squares, festivals, funerals. This is where relationships and grudges must *visibly* form. If they form invisibly indoors, the deepest systems become stat changes nobody sees.

### Resource flow and traversal

Within a settlement, resources physically move. Between settlements, trade is an **abstracted edge** with abstracted interdiction.

**Traversal abstraction, from day one.** Every movement — trade, march, founding party, hauling — resolves against a traversal cost with an optional transport requirement. Never hardcode "land only." Water becomes another edge type, making naval additive rather than a retrofit across four systems at once.

Pathfinding is hierarchical: grid A* locally, road/region graph between settlements, capped requests per tick.

**Built at #16.** The world is a `TerrainGrid` of one `TerrainKind` per cell — plains, forest, hills, small river, deep water — and every kind is a row in a `TerrainRules` table: an integer cost to enter, and the `Transport` flags it admits (`Foot`, `Boat`; append-only). A mover carries the flags it has, and a cell is passable when the two overlap — so a small river admits nobody until a bridge rewrites the cell, deep water admits boats, and no code anywhere asks "is this water?". `Pathfinder` is the grid A*: eight-way at 10 straight and 14 diagonal times the entered cell's cost, no cutting corners past an impassable cell, ties broken by a total order so the same query gives the same route on every platform, and allocation-free after construction. It returns the cost — which the mover turns into travel time at its own speed — and the coarse route §4 keeps in task state. A cost is directional, since each step pays for the cell it enters, so `CostOfRoute` prices a given route (the way home, reversed) rather than the return being assumed equal (#52). The grid is mutable and nothing caches routes, so a bridge is a `Set` and the next query sees it. The road/region graph and the per-tick request cap wait for the issues with a caller for them (#23, #25) and sit on top of this rather than replacing it. `WorldGen/PlaceholderMap` lays down a seeded map of plains, forest and hills split by one river for the headless run; #33 replaces it.

### Bounded map knowledge

**Nobody sees the whole map.** The god does; the people do not. Each polity carries one **known-cells bitmap** over the terrain grid, and every decision that picks a place — a settling site, a founding site, a migration target, a work site for a day's gathering, "unclaimed viable land" for a bridge, a target for an army — chooses among the cells that polity knows, never the world. The *destination* is what must be known: a route may cross unseen ground, or nothing could ever be reached that was only glimpsed from the far side. This is the map-shaped half of §11's rule that knowledge is not omniscient.

The point is the exploration motive. A polity that has used up its known viable land has to push its frontier before it can expand, and it may not know a better valley exists one ridge over. Without fog, expansion is a distance-weighted optimisation over a fully visible map and the "exploration" motive in §12 Bridges is a label with nothing behind it.

**Reveal is passive first, and live.** Anything that moves reveals a radius around its path — a band on the march, a founding party, an army, foragers and hunters working out from a settlement — and writes it straight into its polity's map as it goes. Movers do not carry maps of their own: there is no copy at departure, no merge on return, and no messenger system implied for everything that is not terrain. The army knowing something its home does not is a warfare-flavour story (M8) that is not worth a second write path; if a delay is ever wanted, the mover's *home* reference is the one place it goes. Reveal is computed from **scheduled** movement, since almost all of it is offscreen; a stepped agent walking the same route must reveal the same cells, or the LOD equivalence tests (§4) fail. A deliberate **Scouting** `MobileGroup` purpose, dispatched when a polity runs out of known viable land, comes with founding and expansion (M6).

**The bitmap is terrain only.** Terrain is static except for bridges, so a seen cell stays known. What a polity knows about *actors* on that land — a settlement, a bridge, an army it saw there — is a last-seen claim in the §11 knowledge tiers, stale until re-witnessed. Two structures, each doing one thing, and the second already exists on paper.

**The holder is the polity.** A nomadic band with no polity is its own holder; at founding the settlement takes the map over, the same handover as members and stock. Until polities exist (#39, M7) the holder is the race, which is the same thing by construction in M6. Secession copies the map; joining unions it once. Nothing pools, because nothing was ever separate.

**Legibility.** A god sees everything, so the fog is invisible unless the UI shows *what Oakshire knows* on tap. Otherwise a polity ignoring good land the player can see just looks stupid. The overlay is the cheap part; the constraint is writing it down so it is not forgotten.

**M1 slice, built at #81 as `KnownMaps`:** the band reveals as it wanders and settles on the best known cell. One map per holder, keyed by entity id (a band is its own holder until polities exist at #39), revealed in a square of `NomadicBands.RevealRadius` around wherever a band stands and around every cell of a hop it walks, and read by `Pathfinder.TryFindNearest`'s known-mask overload so a site counts only if it has been seen. The hop *candidates* are deliberately unrestricted: an unknown cell scores zero and ties, so the keyed draw still walks the band into new country. Whether work-site selection is bound by the same rule is #84; something that goes looking on purpose, so the radius can be tighter, is #85.

### Bridges

**Small rivers are impassable without a bridge.** Large rivers require boats (§19, M8).

Bridges are the **first real test of the traversal abstraction**, and they exercise it at M6 rather than waiting for naval at M8. A bridge is exactly a traversal-cost modification: an impassable water edge becomes a cheap land edge.

- **The polity commissions them**, not the settlement — the first polity-level construction decision in the design, and something concrete for polities to do besides war and alliance. Requires the polity to levy materials and labour from member settlements
- **Construction reuses the existing pipeline** — demand detected → bridge requested → job posted → materials hauled → built
- **Bridgeable narrow points score highly in site selection**, which makes towns sit at river crossings the way real ones do
- Pathfinding must handle **edges appearing and disappearing at runtime**, invalidating cached paths when a bridge is destroyed

**Nomadic bands cannot build bridges** — no settlement, no materials pipeline. Pre-settlement rivers are absolute walls, which gives the separated-homelands design a physical cause rather than just an initial placement.

#### Build motives

A bridge is commissioned when any of several weighted demands crosses the cost threshold — the same OR-condition pattern used for milestone unlocks:

```
Stranded settlement needs reconnection   (highest priority)
Trade value of the blocked route
Military necessity — projecting force or reaching a target
Exploration and expansion toward unclaimed viable land — known land, or the frontier when none is (Bounded map knowledge, above)
Migration pressure across the barrier
```

Because expansion and exploration are self-interested, a polity will bridge toward empty land regardless of who else benefits. **This is what keeps the races from staying permanently separated**, and therefore what keeps migration, assimilation, and cultural convergence live rather than dead systems.

#### Materials as a technology tier

**Wooden bridges** are cheap and quick to build, and quick to destroy. **Stone bridges** are expensive, require quarries and masons, and take a long time to bring down.

This is another expression of technology emerging from infrastructure rather than a tech tree. Early centuries are defined by wooden crossings that keep getting burned; later ones by stone spans that outlast the polities that raised them. It also dissolves the stranding risk without a special-case rule — the crossings that matter most are the hardest to destroy.

#### Destruction

**Enemies burn bridges in war.** Destruction is a **task with a duration**, not an instant action, scaled by material. Defenders can arrive and interrupt it, which gives armies a reason to fight over a specific place — something the warfare layer otherwise lacks. Progress toward destruction persists; damage is binary until the bridge falls.

**The god destroys a bridge instantly.** Mortals need days. Divine power being *fast* rather than merely strong is good characterization, and it stays inside the blunt-instrument rule since it destroys without fixing.

Bridges do not decay. Absent destruction they are permanent, and a polity commissions repairs on damaged ones.

#### Ownership

When a polity fragments, **the nearest settlement by traversal distance inherits the bridge**, and it belongs to whichever polity that settlement is in. A seceding settlement can therefore inherit a crossing and deny it to its former polity.

---

## 13. Animals

Aggregate where continuity doesn't matter. Nobody cares whether Deer #8732 is the same deer as Tuesday.

| Type | Model |
|---|---|
| Livestock | Persistent individuals or herd entities |
| Monsters | Persistent individuals |
| Predators | Persistent packs |
| Game | **Regional populations** plus visible spawned representations |

So `North Forest Deer = 173` while five actual deer appear when you look there. Hunting decrements the regional number. Large reduction in wasted simulation.

All animal types are a **separate, lighter entity type than Person** — no relationships, skills, history, or journal entries.

Monsters are wandering threats, not lairs — no quest system needed. They give settlements a reason to muster in peacetime, and they make the droppable dinosaur consistent with the world rather than an intrusion.

---

## 14. Warfare

**Logistics, defined narrowly for launch:**

```
Army has a food supply and consumes it daily.
Food comes from carried provisions or a connected friendly supply route.
Supply route has: travel cost, capacity, interdiction risk.
No food → morale ↓, movement ↓, desertion ↑, eventually death.
```

That is enough to produce *cut the bridge → supply stops → siege fails* without building a quartermaster simulator.

Armies of ~25 (roughly an 8% levy from a 300-person town) make this watchable: three carts of grain on a specific road, and when raiders take them, these named people starve.

**Sieges are not designed.** Walls, gates, duration, food stores, assault, surrender — deferred, in Open Questions. Not an MVP feature, but the castle-town fantasy eventually needs it.

---

## 15. World generation and start

### Start

**Three wandering bands per race, sized 60 / 45 / 30. Two races, ~270 people total, no settlements.**

**Each race is one dispersed polity from the start** — three roving groups under a single ruler, who lives with the largest band. That gives you a capital band and two peripheral ones from minute one, and the peripheral bands are natural secession candidates once distance and cultural drift set in.

Unequal band sizes create immediate asymmetry: the 60-strong band will likely settle first and dominate; the 30-strong band may struggle, merge, or break away.

Bands need a **nomadic mode**: movement, foraging and hunting for food, temporary camps, and a settling trigger (band size, land quality, seasonal pressure). Site selection reuses the founding system.

**Built at #54, as `NomadicBands`.** Each tracked band holds a *council* at first light — 05:00, an hour before `Jobs`' dawn pass, when nobody is out on a task — and does one of three things. It **settles** where it stands when its pressure has crossed a threshold and the camp passes the land check; pressure is member-days, each council adding the living headcount, so the 60-band reaches it about twice as fast as the 30-band (the asymmetry above, made mechanical), and the land check is whether a forager and a woodcutter would each find a site from here, using `Jobs`' own search so a camp that passes has sites the next dawn. Settling is `Founding.Found` (§3) with the reasons `PopulationPressure` and `LandSuitable`. Otherwise, after twenty days at one camp, it **moves**: every cell within six of the camp is a candidate if it is standable, reachable, and the route fits between dawn and dusk; each is scored by how many of the three jobs would find a site from it; the best wins, ties broken by one keyed draw (`RandomDomain.Wandering`). The band sets its destination there and then, `Jobs` starts nobody that dawn, and one `BandArrival` event lands the band at `dawn + route cost × ticks-per-cost-unit`, where it embodies up to five wood as the new camp and publishes `CampPitched` — the "first camp" milestone is the first of those. Nothing reachable, it stays. Otherwise it **stays**. The hop machinery — a route costed into a travel time, one arrival — is what #35's founding parties and M4's armies reuse.

Three things this is *not*. It is not movement with a cause: sites are infinite and identical until #26 makes foraging exhaust them, so a band moves because nomads move, toward the best land in reach, and the twenty days is a placeholder cadence. It is not seasonal pressure: seasons exist since #53, but feeding them to the council is #96. And it is not bounded map knowledge (§12): the council reads the whole hop box off the grid, as `Jobs` finds the nearest forest without anyone looking, until #81 gives a band a known-cells map and makes the settling pick choose among what it has seen. Seasonal pressure joining the trigger is #96; site selection with scoring, and a party that walks to the site, are #35's.

**Runaway growth is real and unbounded until #69.** With camp space for housing and plentiful food, a generated band of thirty is 923 people by year 200 and past 5,000 by 250 (`DemographicsRunTests`); the sanity check below will need the housing regulator before "no runaway pathological growth" is a check the sim can pass.

### Starting population composition

The world does not begin with 270 unattached strangers who discover romance after the loading screen. Each band is generated with:

```
Believable age distribution across all five age stages
Pre-existing households — couples with children
Kinship graph, with the required unrelated founding lineages
A leader
Basic tools and shared supplies
```

**Built at #54, as `BandGenerator`.** Lineages first: a band of *n* gets about *n* / 5 founding couples, at least 8 and at most 12, each partnered through `FamilyFormation.Partner` so the rules check them and each has a household; children are dealt to the couples in turn until the band is full, up to four each, as old as the younger parent allows at sixteen or more at the birth; anyone left over is a single adult and a lineage of their own. Ages come from the race's table - founders a few years into its adulthood, children under it, born at or past its fertile age - as keyed draws under `RandomDomain.BandGeneration`, mixed with the band's id and the person's index, so a seed is a band. The oldest adult leads — a rule needing no draw; who leads and why is M7's. Supplies are ten days of food and wood for a few camps. Tools are not generated because no tool resource exists (#78). Four of the five stages are present at start — infants, children and adolescents among the children, adults among the founders — and elders arrive with time; the age table is one race's until #34. Every person is announced with `PersonBorn` so ageing and mortality book their wake-ups. The lineage count's viability run — thirty people, two centuries, partners found in every generation — sits with the generator in `DemographicsRunTests`; the five-century form waits on #69 for the reason given above.

**Skills start flat.** No masters exist at world start, so apprenticeship accelerates nothing initially and the first journeyman must emerge purely from learning-by-doing. That is thematically right for a stone-age opening — and it makes **time to first journeyman a critical pacing number**, since it gates the first smithy and therefore the entire capability climb. Tune it in the harness early.

It also front-loads the race asymmetry: an elf novice has three centuries to reach master, a human perhaps forty working years. Elf settlements should out-skill human ones steadily, which is why the mastery ceiling (§10) matters.

**Temporary dwellings satisfy the housing requirement in nomadic mode.** Household formation requires a home (§6), and nomads have no permanent buildings — so camp space counts as housing until settlement.

**The growth curve is load-bearing**, and starting with more bands rather than inflating birth rates is how it's addressed — demography stays realistic, the climb just starts higher.

From ~270 to ~1,650 is roughly a 6× expansion, about 360 game-years at realistic rates. At 10,000× that's under two hours of watching, and the follow-a-villager fantasy arrives as soon as the first bands settle rather than centuries later.

### Generation

Procedural, with archetype selection. **Each archetype is a gameplay mode:**

- **Continents** — land-connected baseline; cultures converge
- **Watering hole** — scarcity-driven clustering. Cheapest to build, most dramatic, stresses the feud system by design
- **Islands** — naval traversal required; cultures diverge

Worldgen must place viable homelands per race.

### The sanity check, not a viability gate

Check for **brokenness only**:

```
Every starting race has water, food potential, building space,
and reachable expansion area.
Every starting band has forest in reach, for winter fuel.
No unavoidable extinction. No runaway pathological growth.
```

**Rivers are now the main threat to this check.** Small rivers are impassable until bridged, and nomadic bands cannot bridge, so a band spawning in a river-bounded pocket with no room to grow is a dead world at generation time. Worldgen must classify rivers as **small (bridgeable) or large (boats only)** and verify each band has adequate contiguous land before settling.

**Each band needs a minimum count of unrelated founding lineages** — a band seeded as two extended families deadlocks its mating pool within two generations (§6). Target 8–12 lineages per band; the exact figure comes from harness testing, and elves are the constraining case.

**Not** "converges to our preferred equilibrium" — that would reject exactly the harsh mountainous worlds worth playing. A world that settles at 950 people is a legitimate world.

**Run this as a cheap static check on device** (water, arable land, forest in reach of each band, connectivity, buildable area — milliseconds). Use the 100-year simulation as a **design-time tool in the harness** to validate the *generator* across thousands of seeds on desktop. Confidence without a load screen.

Seed plus settings gives reproducible worlds.

---

## 16. Interface and progression

### Event feed only

No camera auto-jump, no push notifications. The game never grabs the wheel — consistent with powers-only agency, and a deliberate pass on the standard mobile retention lever.

The feed is therefore **the entire interface to what happened**, which requires:

- **Severity tiers and filters** — at 10,000× you generate hundreds of events a minute
- **Every entry taps to jump the camera** to that person or place

### Semantic zoom

One continuous camera and one world — **not** separate world and town scenes — but the information hierarchy changes with scale, so the screen never becomes icon soup.

| Scale | Shows |
|---|---|
| **Far** | Polity borders, settlement names, army markers, major events, trade lines |
| **Medium** | Roads, districts, farms, buildings, resource overlays |
| **Near** | Individual people, tasks, relationships, animations, names on selection |

This is an interface concern, not a rendering one — same camera, same world, different layers surfaced.

No build menus, no priority sliders, no designation tools.

**Follow lists and search are launch polish, not v2.** Following a person, household, or settlement to filter the feed, plus name search. With 1,650 named people and a game explicitly built to make you care about one of them, losing Aldric because you cannot search for him would undercut the premise. Fine to skip through M7 while developing; required before launch.

### Milestone unlocks with OR-conditions

Linear track, reset each world. But because the world is autonomous, a strict milestone can **softlock progression** — "first war" in an unusually peaceful world, or "first overseas settlement" on a continents map.

Preserve linear *order*, allow flexible conditions:

```
Power 5 unlocks when ANY of:
    Population reaches 500
    OR third settlement founded
    OR first war occurs
```

Early milestones must fire within minutes, since the ladder repeats every world. The from-scratch start gives better ones than population thresholds: first camp, first permanent structure, first farm, first smithy, first stone building, first wall.

Order powers from observational (bless, heal, nudge) to world-shaping (plague, earthquake, dinosaur). Keep unlocks diegetic — powers *awaken*, they are not purchased.

### Powers are unlimited, and blunt

There are no cooldowns and no charge economy. Availability — what you have unlocked — is the progression; frequency is not. That deletes a UI layer and fits the sandbox god fantasy.

Unlimited use is safe only under one design rule:

> **No power directly solves a core system's failure state.**

Powers are blunt instruments, not precision tools. Lightning kills a person; it does not repair an economy. Bless a field and one farm has one good season, not a region fed.

| Power | Allowed? | Why |
|---|---|---|
| Kill a person (lightning) | Yes | Removes an individual; doesn't repair anything |
| Heal one person | Yes | Saves a life, not a population |
| Bless one field, one season | Yes | One farm, one harvest — not a food supply |
| Shape terrain | Yes | Changes the world; solves no failure state |
| Spawn a monster | Yes | Creates a problem rather than removing one |
| Earthquake | Yes | Destructive, indiscriminate |
| Inspire or enrage an individual | Yes | Biases one person's decisions |
| Spawn food stores | No | Deflates seasons, storage, famine, logistics at once |
| Instantly construct a building | No | Bypasses the entire economy and job system |
| Teleport resources | No | Kills trade, roads, and supply interdiction |
| Cure a plague | No | Removes disease as a threat |
| End a war | No | Removes the political layer's main output |

The governing rule:

> **A single application of a power manipulates concrete world state. It never directly overwrites a system-level outcome.**

Terrain is itself a system — one edit can create a land bridge, bypass a naval route, isolate an army, open fertile land, block a road, or redirect water — so "individuals and terrain versus systems" was the wrong distinction. The right one is between *changing the world* and *setting the result*:

```
Lightning kills Bob.            It does not set War.Winner.
Bless affects this field.       It does not set Settlement.Food = Full.
Terrain shaping moves tiles.    It does not set Army.Supplied = true.
```

**No scope limits or stacking rules.** A determined player can heal 1,650 people one at a time and defeat a plague, or bless every field and defeat a famine. That is accepted: the effort cost is the only limit, and it is consistent with extinction being rare and mostly player-caused. A world only dies through neglect or intent.

Without this rule, a "spawn food" power would deflate seasons, storage, famine, and logistics simultaneously — months of systems work neutralized by one button.

### Fail state

**Extinction ends the run.** Rare and mostly player-caused, since powers-only means the player cannot manage their way out of a collapse.

Tuning target: 500 years unattended across 100 seeds, extinction rate near zero.

- **A visible death spiral** with runway to intervene
- **The world persists as a chronicle** — on extinction the save becomes read-only browsable history. Turns a loss into a memento

---

## 17. Data and time while closed

**The world advances at 10× while the app is closed, halting at the next major event.**

Android will kill a background process and there is no server, so "the world kept going" is always implemented as fast-forward on open: compute elapsed real time, convert at the offline rate, run the compressed tier forward.

### Why 10×, and why the halt matters more

At 8 real minutes per game-day, offline rate translates as:

| Away for | Game time at 10× |
|---|---|
| 1 day | ~5 years |
| 1 week | ~34 years |
| 1 month | ~148 years |

For contrast, a rate of 0.1× would give ~0.35 years per week — less than six seconds of active play at 10,000×, and therefore invisible.

**The halt rule makes the rate largely moot.** Offline sim rarely runs its full duration; the rate only governs how fast the first major event arrives. Rate matters only across quiet stretches.

### Halt triggers

```
War declared · Famine onset · Plague onset
```

**Hard cap: 50 game-years.** At 10× that engages after roughly 10 real days away; longer absences all land in the same place.

Crises are rarer than political events, so **the cap is the primary bound rather than a backstop**. Short absences still halt naturally on a crisis; genuinely peaceful stretches run to the cap.

Two accepted consequences:

- **Every re-open after a long absence is bad news.** Crises-only means the game interrupts exclusively with disaster.
- **This list does not double as event-feed severity.** With three trigger types, the feed needs its own severity taxonomy designed separately.
- Personal halt triggers (the death of someone the player had followed) were considered and rejected. A long peaceful stretch plus a long absence can therefore return the player to a world where most humans they knew have died — while the elves are unchanged.

### Catch-up as content, not cost

Present it as a progressive "while you were away" chronicle, events scrolling in as the sim runs, rather than a loading bar.

**Halting is permanent — there is no resume.** Returning after three months to find one day elapsed is accepted, and has an upside: the world can never run far past a dramatic moment unattended, so every re-entry drops the player at the crisis rather than fifty years downstream of it. The requirement is that the arrival screen states plainly *why* it stopped, or it reads as a bug.

*Watch in the harness:* the value of offline progression depends entirely on crisis frequency. If wars and famines are common, the sim halts within game-hours every time and the feature does nothing.

Determinism is unaffected — catch-up is just the sim running forward from a known state.

**The setting is toggleable, not locked at world creation.** A player who discovers after a month that they dislike returning to an advanced world should be able to switch it off. Historical coherence is preserved by recording the change as a domain event in the journal:

```
Year 83 — Offline progression disabled by player
Year 116 — Offline progression enabled
```

This is a user-experience preference, not a simulation law, and determinism is unaffected either way.

### Persistence

**History compaction.** Design before the accumulation. A person dead 300 years with no living descendants and no surviving event references compresses to a stub. The journal keeps recent events in full and folds older ones into era summaries.

**Save versioning and migration.** You will ship updates over the months a world lives. A four-month-old world failing to load destroys the only thing that made the game valuable. Version field, migration chain, and tests loading old-format saves — from your *first* format.

**Atomic writes and rolling backups.** Temp, fsync, rename.

**Safe snapshot semantics.** Android can suspend mid-cascade — `PersonDied` emitted, job released, household not yet updated, succession not yet processed. That half-state must never be serialized. Define a checkpoint:

```
Finish current scheduler phase / event batch
    ↓
World invariants hold
    ↓
Snapshot permitted
```

**Serialize pending scheduled events** where they represent genuine future commitments — birth due dates, task completion, threshold crossings. Rebuilding them from entity state can silently shift history across a version change.

As built (#15), the checkpoint is a property of the clock rather than a protocol the driver follows: `SimulationClock.AtCheckpoint` is true exactly when no `AdvanceTo` is running and no domain event is mid-publish. The first line of the box is what `AdvanceTo` already guarantees on return — it drains everything due on or before its target, including the same-instant reactions handlers booked into later phases, so a cascade cannot be left half-run between calls. The death cascade itself is one synchronous call (`Deaths.Die`), not a chain the clock could stop inside. A handler or subscriber that throws out of its call faults the clock for good — the flags go back, the world does not, so a driver that catches and then saves would serialize exactly the half-state above; a thrown tick means the world is discarded, and the gate is where that is enforced. The export a snapshot is built from (`CopyPendingTo`) refuses to run off a checkpoint, and a clock is rebuilt from that export with every event keeping its `EventId`, so the bookings systems hold still name the same events afterwards. The "world invariants hold" line runs in the harness and the tests (`WorldValidator` at every checkpoint of a seed sweep), not on the phone; on device the structural rule is the whole gate. Suspension is not the hazard it reads as: the simulation is single-threaded, so a platform pause lands between calls, never inside one. What the gate guards is a driver that saves from inside a handler. Writing a snapshot to disk, and rebuilding every system from one, is #42.

---

## 18. Rendering and platform

### Art perspective is decided at M0, not deferred

Perspective affects camera behavior, selection, building footprint, occlusion, walls, trees, terrain, path readability, and zoom range — all things that bake in early.

**M0 includes two intentionally hideous prototypes:** (A) 2D sprites and shapes, (B) orthographic 3D cubes and billboards. Same fake town, both on the phone, ten minutes of zooming and panning. Then choose.

Orthographic 3D is the likely winner — free depth sorting, easy zoom and tilt, one sprite set, real building height for walls. But let the prototype answer it.

Art scope: two races, five age stages, four seasons of terrain, livestock, game, predators, monsters, per-culture architecture variants, plus sleep/eat/socialize/idle on top of work verbs. Keep pixel art small (16–24px) and use paper-doll layering.

### Unity version path

**Start on 6.6; move to 6.7 LTS when it ships** (expected late 2026).

- **6.6 → 6.7 is a one-version jump**; 6.3 → 6.7 would be four, and Unity's upgrade guides are written per-hop.
- With no existing code, the usual reason to sit on LTS — don't move a production project — does not apply. Unity states update releases undergo the same QA and stability testing as LTS; the difference is support duration, not build quality.
- 6.6 loses support when 6.7 ships, which is exactly when the move happens. The exposure is roughly zero days unless 6.7 slips.
- **M0 and M1 barely touch Unity anyway** — M0 is throwaway prototypes, M1 is a console harness with zero Unity dependency. The version first matters at M2, realistically M3.
- 6.7 LTS is reported to add an Android Adaptive Performance update exposing CPU and GPU headroom signals directly to C#. That is a natural **third input to the SimQuality dial** alongside visibility and time speed, given a simulation that runs indefinitely on a thermally throttled phone.

Unity 7 (6.8) removes Mono entirely for CoreCLR with .NET 10 and C# 14 — beta December 2026, full release Q1 2027, with no breaking changes claimed from the 6.x line. That is also when Core could potentially retarget off `netstandard2.1`.

### Platform

**Target device: Pixel 10 Pro XL.** A flagship is the most dangerous device to develop on — everything feels fine until it reaches a three-year-old midrange phone. Budget as a *fraction* of what the device can do, not to what it tolerates.

Targets are **measured, not assumed.** Baseline measurement happens at M0, since builds are already going to the phone:

1. **Render ceiling** — empty scene, N animated sprites at intended zoom, nothing else. Ramp N until below 60 FPS. That is the absolute sprite budget.
2. **Sim throughput** — pure C# on device, no rendering. A tight loop doing representative agent work (position update, needs decrement, job lookup). Measure updates per second.
3. **Sustained** — re-run both for 20 minutes. Record the degraded number, not the peak. A figure captured in the first 30 seconds is fiction.
4. **Allocation baseline** — watch GC frequency under load. Collections during the tick loop get fixed before anything else.

Then set budgets as a fraction of measured capacity. **Choose the fraction before seeing the numbers**, or you will rationalize whatever the device happens to do.

Metrics to fix once the baseline exists: sim ms per frame, render ms per frame, acceptable sustained degradation, cold load time on an aged world, save write time, save file size after 500 game-years, and total memory.

Peak load is one focused settlement at 100–300 stepped agents, ~1,650 persistent people, ~500 of them children, plus animals in the scheduled tier.

The mobile risk is **save size and cold load time on a six-month-old world**, plus relationship graph memory.

- Thermal throttling roughly halves sustained CPU after ~10 minutes
- Zero allocations in the tick loop
- Pinch-zoom that doesn't fight pan across a huge range is deceptively hard
- From August 31, 2026, new apps and updates must target Android 16 (API 36) or higher
- Personal Play Console accounts created after November 13, 2023 must run a closed test with 12+ testers opted in continuously for 14 days before production access
- Test on a cheap real device from day one

**Desktop fallback:** the engine-free core and quality-enum LOD make the switch cheap, but it changes the *game*, not just the tech. Design for mobile, decide at the stress-test gate.

---

## 19. Milestones

**M0 — foundations.** Two intentionally hideous prototypes (2D sprites vs orthographic 3D cubes and billboards), same fake town, both on the phone. Plus the **baseline performance measurements** (§18).

Test selection, not just camera — tapping is the core interaction loop:

```
pan · pinch zoom · tap a building · tap an isolated person
tap a person in a dense crowd · tap a tiny settlement from world scale
follow a selected person · return to previous zoom
```

If twenty villagers overlap at a market, "which one did I tap?" needs an answer — nearest to tap, repeated taps cycling, a small selection fan, or long-press magnification. M0 is the right time to find out.

Then choose perspective.

**M1 — headless sim.** Console only. **Two prototype bands, one per race** (the shipping world start is six — three per race, §15). Nomadic mode, settling, births, deaths, jobs, food, seasons, 3–4 resources, households. Run 200 years, print a chronicle. NUnit tests for population stability and milestone firing. *Is the world interesting as text?*

**M2 — the ugly stress test.** No art. Full population, ~10,000 trees, ~500 buildings, 200 agents stepped and pathfinding, on Android. Threshold set *before* running. Core wired into the Unity build for the first time (#72). Measure save size and cold load, and design history compaction against those numbers (#74) rather than after M3–M7 have each shaped the journal. **Go/no-go for mobile.**

**M3 — one living village, well laid out.** Wake, eat, work, harvest, haul, build, home, sleep, through a full year. Skills, apprenticeship, age stages, **town planner**, **resource reservation**. The event feed (#73) — the only way to learn what just happened in the village. Zoom in and out cleanly.

**M4 — one disruption.** Raiders and predators. Villagers flee, militia musters, fighting, burning, recovery, rebuilding.

**M5 — god powers.** Three powers: lightning, heal a person, and one of inspire/enrage or bless-a-field. **Terrain shaping is deliberately not here** — raising terrain cascades into roads, buildings, trees, rivers, bridges, path caches, territory, and water connectivity, and touching rivers means accidentally building hydrology. It arrives after M6 has worldgen and traversal mature. Witnessed attribution and the rumor propagation it runs through (#41 — §20 always had it before M5; cross-settlement propagation lights up when M6 supplies trade routes) let word of what you did spread and distort. Prove the loop on a touchscreen.

**M6 — worldgen and dynamics.** Procedural maps with sanity check, two races in separate homelands, founding and abandonment, attitudes and migration, trade, economy toward the full seven resources.

**M7 — society and politics.** Social/personal decision system, polities, secession, succession, cultural drift, naming divergence. Save/load with versioning. Vertical slice.

**M8 — naval.** Boats, cross-water trade and transport, island archetype. *Largest single feature; the §12 traversal abstraction must exist from M1.*

---

## 20. Systems requiring design before implementation

Sequenced by when they block progress. Items now specified elsewhere in this document, or in its companion [economy ladder](kingdom-watch-economy-ladder.md), are marked ✓.

**Before M1:** households and lifecycle ✓ · **storage accessor layer** ✓ · **minimal demographic timing model** ✓ · **safe save snapshot semantics** ✓ · **durable EventId** ✓ · **keyed randomness** ✓ · **LOD equivalence tests** ✓ · entity model ✓ · **MobileGroup / NomadicBand** ✓ · durable EntityId vs runtime handle ✓ · **simulation clock, event scheduler, and deterministic phase ordering** ✓ · **relationship model and retention rules** ✓ · **scheduled↔stepped task state** ✓ · domain-event layer ✓ · decision provenance ✓ · family formation and kinship ✓ · death cascade ✓ · property model ✓ · **resource ledger authority** ✓ · **WorldValidator and cross-platform state hash** ✓ · primitive recipe tier ✓ · traversal abstraction ✓ · **bounded map knowledge** ✓ · **performance budgets — measured at M0, not yet set** · **capability-graph pacing and the skill chicken-and-egg** ✓

**Before M3:** settlement layout and town planning · task and resource reservation ✓ · movement and collision model · resource regeneration (forests, wildlife, soil, finite stone and ore)

**Before M5:** knowledge and rumor propagation ✓

**Before M7:** social and personal decision system ✓ (launch scope set) · political model and succession · culture on individuals and assimilation ✓

**Launch polish:** event feed follow lists and search.

**Investigate at M5:** unifying grievances and rumors into a shared `HistoricalClaim` (§11).

**Deferred:** siege model · extreme compression tuning beyond 10,000× · explicit technology progression · **natural map change over time** (shifting rivers, erosion, forest advance and retreat). The last is cheaper than it appears: the "shape terrain" power and runtime bridge edges already require mutable terrain, dynamic graph edges, and cache invalidation, so v2 supplies new causes rather than new machinery. Pairs well with bridges — a river that shifts course strands a bridge on dry land and opens an unbridged crossing elsewhere.

---

## 21. Open questions

1. ~~**Capability-graph pacing** — what stops camp → farm → kiln → quarry → smithy → stone wall cascading identically in every world~~ — answered by [the economy ladder](kingdom-watch-economy-ladder.md) (#78): each capability has a crude tier-zero form, so the climb is paced by how long real people take to reach journeyman, which varies per world. The targets there are placeholders until #17 and #22 measure them.
2. **Performance budgets** — set as a fraction of the M0 baseline, fraction chosen before measuring
3. Disease and plague as a system
4. Siege model — walls, gates, duration, stores, assault, surrender
5. Movement and collision — do agents block one another?
6. Resource regeneration rates — forests, game, soil fertility, finite ore
7. Precise definition of a "major event" for offline halting
8. ~~Whether a master's tools pass to their apprentice or into the household~~
   — answered by [the economy ladder](kingdom-watch-economy-ladder.md) (#78):
   neither. Tools are checked out from the settlement, not owned, and are
   destroyed with their holder rather than passed on.
9. Whether a partially damaged bridge is repaired automatically or needs commissioning
10. Whether long-lived succession stasis is a feature or needs compensation
11. Political succession model — hereditary, elected, strongest household, or culture-dependent

---

## Sources

- Unity 6 releases and LTS support: https://unity.com/releases/unity-6/support
- Godot C# platform support: https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/
- Google Play target API levels: https://support.google.com/googleplay/android-developer/answer/11926878
- Google Play closed testing: https://support.google.com/googleplay/android-developer/answer/14151465

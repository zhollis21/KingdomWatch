# Kingdom Watch — Design & Technical Plan (v7.1)

> **How to read this.** A living plan, not a specification. It records current best thinking and is expected to be revised as real code gets written and teaches us things. Treat its claims the way `/kickoff` treats an issue's — a well-informed hypothesis from someone who had context you may lack, worth taking seriously and not worth adopting unexamined. Where the code and this document disagree, that is a prompt to work out which one is wrong, not an automatic win for the document. §2's "Locked decisions" are the settled *game* questions, reopened deliberately rather than casually; everything else, including the code sketches below, is illustrative.

> **M0 decision, September 9, 2026:** Orthographic 3D with sprite villagers is selected following desktop and Android prototype trials. The flat 2D comparison has been retired. Current implementation and limitations are documented in [the town prototype guide](../town-prototype.md). Sustained performance budgets and the M2 mobile gate remain open. Repository directories use `Game/`, `Core/`, `Core.Tests/`, and `Harness/`; the KingdomWatch-prefixed paths below are the original design notation.

*A grounded low-fantasy god sim. Supersedes v7. Adds the simulation clock and scheduler, corrected real/sim-time cadence, threshold-crossing compression, LOD equivalence testing, keyed deterministic randomness, durable EventId, safe save snapshots, the storage accessor layer, decision provenance, family formation and death rules, witness-tracked grievances, semantic zoom, and the confirmed .NET/Unity version path.*

---

## 1. The game

Six wandering bands — three per race — arrive in an empty land. Over centuries they settle, farm, build, learn trades, form households, split into rival polities, feud, trade, march, starve, and remember. Around 1,650 individuals at equilibrium, every one of them a real person with a name, traits, skills, relationships, grudges, and ambitions.

There is no magic in the world except you. The people know it — they build shrines, ordain priests, and argue over what your interventions meant. Stories of your miracles spread along trade routes and distort as they travel.

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
| Economy | ~10 resources with chains, data-driven and extensible |
| Technology | **Emergent from the recipe graph** — no tech tree (v2 at earliest) |
| Seasons | Full — harvest cycles, stores, winter mortality as an *outcome* |
| Skills | **Five tiers** (novice→master); tier zero needs no building; apprenticeship transmits |
| Movement | **Soft avoidance** — agents never hard-block cells |
| Polity attitudes | Polities hold **their own diplomatic state**, distinct from settlements |
| Households | **First-class entity** — person → household → home |
| Property | **Hybrid** — household owns the home, individuals own wealth and tools |
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
| Religion | Open worship; **attribution is not omniscient**. Depth deferred to M5 |
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
positions stepped detail needs at M3. Two hundred years is about 6.3e9 ticks, so
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

At ~8 real minutes per game-day, one game-year is ~48.7 real hours at 1×. So:

| Speed | 1 year | 100 years |
|---|---|---|
| 1000× | 2.9 min | 4.9 hours |
| 10,000× | 17.5 sec | 29 min |

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
  World.cs
  Clock/                <- simulation clock, event scheduler, phase ordering
  Systems/              <- Needs, Jobs, Skills, Social, Households, Politics,
                           Economy, Seasons, Logistics, Combat, Relations,
                           Attitudes, Faith, Knowledge, Culture, Founding,
                           TownPlanner, Reservations, Milestones
  Data/                 <- SoA storage, entity handles, recipes, race tables
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
Reservations never exceed availability.
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

**Cross-platform determinism check.** Produce a periodic world-state hash and run the same seed on desktop .NET and on Android under IL2CPP.

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
// miracles, rumors, decision provenance, and player bookmarks.
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
    public byte          AgeStage, BirthCulture, Assimilation;
    // skills indexed separately: [personIndex * skillCount + skillId]
    // Job (JobId) and Household (HouseholdHandle) are deferred — see below
}
```

`Position` is the `WorldPosition` used everywhere else rather than a loose pair of ints, and the `Job`/`Household` fields are deliberately absent as of #6. Neither `JobId` nor `HouseholdHandle` exists yet, and neither shape is settled — #52 describes recipes as data rather than code, which may make a job reference a data-table lookup rather than a handle at all. Guessing either one now means dependent code gets written against it before #52 and #9 make their own design decision. Adding them later is a field plus an accessor pair, which is the entire point of storage living behind `PersonStore`.

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
                            byte ageStage, byte birthCulture, byte assimilation);
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

The bulk path is allocation-free. The scattered path is not quite: `Alive()` allocates one iterator per enumeration, so it is not the tick-loop path. #59 tracks the broader zero-allocation claim, which nothing measures yet.

**Correction on the Burst path.** `NativeArray`, Unity Jobs, and Burst are Unity dependencies, so they cannot be introduced into `KingdomWatch.Core` without breaking the zero-dependency rule that the harness, tests, and determinism strategy all rest on. If profiling ever demands them, the seam is a separate `KingdomWatch.UnityOptimization` backend — not an in-place change to Core.

The more likely outcome is that **1,650 people never require Burst or DOTS at all.** Do not architect an optimization backend now; simply do not assume Core can casually adopt `NativeArray` later.

Settlement, household, and polity IDs must all be **dynamic**, not fixed slots.

### Domain events

Too many systems react to the same occurrence for direct calls to stay maintainable. A single death touches households, job assignment, apprenticeship, inheritance, dynasty, marriage availability, the settlement skill pool, political succession, relationships, culture, the event feed, milestones, history, and faith.

Publish meaningful simulation events; interested systems subscribe and react deterministically:

```
PersonBorn · PersonDied · MarriageFormed · HouseholdFormed
SettlementFounded · SettlementAbandoned · RulerSucceeded
WarDeclared · BattleEnded · DivineActWitnessed
BridgeDestroyed · FamineStarted
```

This is a **domain-event layer, not event sourcing** — not every axe swing becomes an event. It feeds the history journal, milestone system, event feed, faith attribution, attitudes, and debugging from one mechanism.

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

### Family formation

Partner eligibility checks age, race fertility compatibility, existing partnership, kinship, settlement distance, and relationship.

**Kinship: a hard ban through grandparents.** Parent, child, sibling, half-sibling, grandparent, grandchild.

**First cousins are a culture taboo, not a rule.** Some settlements permit it, some don't, and it drifts. This reuses the taboo layer (§10) and gets a pressure valve for free: cultural reinforcement means a settlement with a strained mating pool and repeated failed pairings can loosen its taboo over time, so demographic pressure surfaces as cultural change.

**The real variable is founding lineage count, not the restriction list.** A 30-person band seeded as two extended families deadlocks within two generations; the same band seeded with 8–12 unrelated lineages is comfortable. Worldgen must guarantee a minimum count of unrelated founding lineages per band (§15).

Two mitigating facts: the pinch is early and temporary, since once bands settle and connect the mating pool becomes the polity rather than the band. And **elves are the case to test** — low fertility plus 300-year lives plus no interracial children means lineage diversification runs roughly five times slower than for humans.

Harness test: mating-pool viability over 500 years, flagging any settlement where eligible partners approach zero.

**Widows and widowers may remarry** after a mourning period. Culture can modulate its length. This matters demographically — in a 1,650-person world, blocking remarriage wastes fertile adults.

### Property

**Hybrid ownership.** The household owns the home and bulk goods. Individuals own personal wealth, tools, and status.

On death, personal wealth folds into the household. This gives some wealth variation and makes a master's tools a real asset, without the machinery of full dynastic inheritance law.

### Death cascade

Most of this is mechanical and must happen, or it generates bugs:

```
Cancel tasks and reservations
Vacate the job → work manager reposts it
Break apprenticeship in both directions
Transfer personal wealth to the household
Emit PersonDied
```

Relationship edges are **marked dead, not deleted** — a grudge against a dead man still shapes how his family is treated.

The decisions on top of that:

- **Dependent children stay with the household.** With no surviving adult, the **nearest kin household adopts them**.
- **When a household empties, its home returns to the settlement's housing stock** for reassignment. This is not just tidy: housing supply throttles household formation and therefore fertility, so a plague that empties houses makes it easier for the survivors' children to marry.
- **Political succession is handled separately** by the polity (§8), not by household inheritance.

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

### Minimal demographic model

M1 requires births and deaths, so the *mechanism* must exist before it — though the numbers are tuning, not architecture:

```
Conception eligibility · conception probability · gestation · birth
Age-specific natural mortality · maximum and soft lifespan
Nutrition and health modifiers
```

**Do not poll mortality.** `every second: roll chance of dying` is exactly the pattern the scheduler exists to replace. Evaluate mortality at sensible age and health intervals; let starvation and injury raise their own threshold events (§4).

Human and elf life tables are tuning. The mechanism is not.

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

**Target ~10 resources with two-step chains, defined as data.**

```
recipe: iron_tools
  inputs:  [iron x2, charcoal x1]
  building: smithy
  outputs: [iron_tools x1]
  duration: 60
  substitutes: []
  degrades_to: bone_tools
```

Start at 3–4 for M1, expand toward 10 by M6. Resource count is a config file.

### Technology is a capability graph, not a tree

**There is no research system.** Capability is gated by infrastructure, which the recipe graph already expresses. Wanderers use stone tools because they have no smithy. Build a smithy — with a settlement, ore access, and a skilled person — and iron tools become possible.

The only addition needed is a **primitive tier**: foraging, stone tools, hide clothing, temporary camps. Buildable with no buildings.

**This is not free, and pacing is the open problem.** If every settlement implicitly knows every recipe, then camp → farm → kiln → quarry → smithy → stone wall can cascade almost instantly and near-identically in every world. Gating needs to come from conditions the sim already tracks:

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

### Graceful degradation is mandatory

At 10 resources the deadlock surface is real. Every recipe needs a substitution list and a degradation path. Harness test: 200 years with no settlement ever deadlocking.

### Seasons

Full seasons turn the economy from a rate into a cycle:

- **Storage is core** — granaries, stores, spoilage
- **Famine is a timing problem.** Adequate annual output can still starve a town in March — and you can watch the granary empty
- **Campaigning season** — armies marching in winter starve
- **Seasonal work reassignment** — farmers do something else in January
- **Livestock need winter fodder**; game grows scarce in hard years

Balancing requires multi-year harness runs.

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

## 11. Religion and knowledge

There is no magic in the world. Every supernatural event has exactly one cause: the player. The people know this and worship openly. Shrines are buildings, priests are a job, faith is a culture value — almost nothing new to build.

### Attribution is not omniscient

Separate **"knows the god exists"** from **"knows the god did this."**

```
Divine event → witnesses → settlement knowledge
→ rumor propagation along trade routes → cultural interpretation
```

Lightning-strike a wolf in an empty forest and nobody knows. Save Oakshire dramatically and the villagers see it; two years later traders tell Dunvale ("*they claim* their god protected them"); twenty years later it is the Miracle of Oakshire.

This reuses trade networks, culture, and the event journal you already have, and it is the most on-theme system in the design. Don't build sophisticated rumor AI first — but leave room for knowledge propagation instead of global omniscience.

**Knowledge must be bounded.** Fifteen settlements × thousands of events × per-settlement versions grows without limit. Tier it, matching the history compaction model:

```
Recent important claims   → stored individually
Old claims                → decay and disappear
Historically important    → promoted to cultural memory, persists indefinitely
Unimportant              → forgotten
```

A wolf struck by lightning is forgotten in ten years. The Miracle of Oakshire is promoted and outlives everyone who saw it.

**Possible unification, to investigate at M5 — not committed now.** Witness-tracked grievances (§10), rumor propagation, and religious miracles are starting to look like one concept with three meanings:

```
HistoricalClaim / Memory
    OriginEventId · Meaning / valence · Witnesses
    Confidence · KnownBy · CulturalImportance

grievance = memory with a negative attitude effect
miracle   = memory with religious meaning
atrocity  = memory with political or racial meaning
```

Deferred deliberately. **Cheap insurance in the meantime: key both grievances and knowledge claims on `OriginEventId`** so the two can converge later without a data migration. Don't let their internals diverge structurally before M5.

### Attribution splits opinion

Smite a raider captain: the saved village builds a shrine, the raiders' home settlement curses you. One act, two responses, from the attitude system already required for race relations.

### Depth deferred to M5

**How heavy a role faith plays is deliberately undecided.** The structural facts above are cheap regardless. The range:

- **Light** — flavour. Shrines and arguments, nothing mechanical
- **Consequence** — faith modulates how the world *reacts* to you, never how powerful you are. Unlocks stay milestone-gated, avoiding the Black & White worshipper-farming trap
- **Coercive** — faith is enforced. Smiting unbelievers works; fear substitutes for devotion

One constraint holds in every case: **shrines cost real resources.** A settlement diverting stone and labour to shrines instead of granaries starves in a hard winter.

---

## 12. Settlement systems

### Hierarchical job assignment

The work manager decides need (`8 lumberjacks, 4 miners, 12 farmers, 3 builders`); citizens choose among posted jobs, weighted by skill. An order of magnitude cheaper than per-citizen utility AI and far easier to debug.

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
    shrine      → central, prominent
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

AUDIT (WorldValidator, not maintained at runtime)
    opening + produced + gathered + imported
      ==
    current + consumed + exported + destroyed + embodied in construction
```

The visible pile of logs is a *rendering* of the ledger, not the authoritative state.

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

**Push social life outdoors.** Wells, markets, squares, shrines, festivals, funerals. This is where relationships, grudges, and faith must *visibly* form. If they form invisibly indoors, the deepest systems become stat changes nobody sees.

### Resource flow and traversal

Within a settlement, resources physically move. Between settlements, trade is an **abstracted edge** with abstracted interdiction.

**Traversal abstraction, from day one.** Every movement — trade, march, founding party, hauling — resolves against a traversal cost with an optional transport requirement. Never hardcode "land only." Water becomes another edge type, making naval additive rather than a retrofit across four systems at once.

Pathfinding is hierarchical: grid A* locally, road/region graph between settlements, capped requests per tick.

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
Exploration and expansion toward unclaimed viable land
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

### Starting population composition

The world does not begin with 270 unattached strangers who discover romance after the loading screen. Each band is generated with:

```
Believable age distribution across all five age stages
Pre-existing households — couples with children
Kinship graph, with the required unrelated founding lineages
A leader
Basic tools and shared supplies
```

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
No unavoidable extinction. No runaway pathological growth.
```

**Rivers are now the main threat to this check.** Small rivers are impassable until bridged, and nomadic bands cannot bridge, so a band spawning in a river-bounded pocket with no room to grow is a dead world at generation time. Worldgen must classify rivers as **small (bridgeable) or large (boats only)** and verify each band has adequate contiguous land before settling.

**Each band needs a minimum count of unrelated founding lineages** — a band seeded as two extended families deadlocks its mating pool within two generations (§6). Target 8–12 lineages per band; the exact figure comes from harness testing, and elves are the constraining case.

**Not** "converges to our preferred equilibrium" — that would reject exactly the harsh mountainous worlds worth playing. A world that settles at 950 people is a legitimate world.

**Run this as a cheap static check on device** (water, arable land, connectivity, buildable area — milliseconds). Use the 100-year simulation as a **design-time tool in the harness** to validate the *generator* across thousands of seeds on desktop. Confidence without a load screen.

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

---

## 18. Rendering and platform

### Art perspective is decided at M0, not deferred

Perspective affects camera behavior, selection, building footprint, occlusion, walls, trees, terrain, path readability, and zoom range — all things that bake in early.

**M0 includes two intentionally hideous prototypes:** (A) 2D sprites and shapes, (B) orthographic 3D cubes and billboards. Same fake town, both on the phone, ten minutes of zooming and panning. Then choose.

Orthographic 3D is the likely winner — free depth sorting, easy zoom and tilt, one sprite set, real building height for walls. But let the prototype answer it.

Art scope: two races, five age stages, four seasons of terrain, livestock, game, predators, monsters, shrines, per-culture architecture variants, plus sleep/eat/socialize/idle on top of work verbs. Keep pixel art small (16–24px) and use paper-doll layering.

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

**M2 — the ugly stress test.** No art. Full population, ~10,000 trees, ~500 buildings, 200 agents stepped and pathfinding, on Android. Threshold set *before* running. Measure save size and cold load. **Go/no-go for mobile.**

**M3 — one living village, well laid out.** Wake, eat, work, harvest, haul, build, home, sleep, through a full year. Skills, apprenticeship, age stages, **town planner**, **resource reservation**. Zoom in and out cleanly.

**M4 — one disruption.** Raiders and predators. Villagers flee, militia musters, fighting, burning, recovery, rebuilding.

**M5 — god powers and faith.** Three powers: lightning, heal a person, and one of inspire/enrage or bless-a-field. **Terrain shaping is deliberately not here** — raising terrain cascades into roads, buildings, trees, rivers, bridges, path caches, territory, and water connectivity, and touching rivers means accidentally building hydrology. It arrives after M6 has worldgen and traversal mature. Shrines, priests, witnessed attribution. Decide faith depth. Prove the loop on a touchscreen.

**M6 — worldgen and dynamics.** Procedural maps with sanity check, two races in separate homelands, founding and abandonment, attitudes and migration, trade, economy toward 10 resources.

**M7 — society and politics.** Social/personal decision system, polities, secession, succession, cultural drift, naming divergence, rumor propagation. Save/load with versioning. Vertical slice.

**M8 — naval.** Boats, cross-water trade and transport, island archetype. *Largest single feature; the §12 traversal abstraction must exist from M1.*

---

## 20. Systems requiring design before implementation

Sequenced by when they block progress. Items now specified elsewhere in this document are marked ✓.

**Before M1:** households and lifecycle ✓ · **storage accessor layer** ✓ · **minimal demographic timing model** ✓ · **safe save snapshot semantics** ✓ · **durable EventId** ✓ · **keyed randomness** ✓ · **LOD equivalence tests** ✓ · entity model ✓ · **MobileGroup / NomadicBand** ✓ · durable EntityId vs runtime handle ✓ · **simulation clock, event scheduler, and deterministic phase ordering** ✓ · **relationship model and retention rules** ✓ · **scheduled↔stepped task state** ✓ · domain-event layer ✓ · decision provenance ✓ · family formation and kinship ✓ · death cascade ✓ · property model ✓ · **resource ledger authority** ✓ · **WorldValidator and cross-platform state hash** ✓ · primitive recipe tier ✓ · traversal abstraction ✓ · **performance budgets — measured at M0, not yet set** · **capability-graph pacing and the skill chicken-and-egg**

**Before M3:** settlement layout and town planning · task and resource reservation ✓ · movement and collision model · resource regeneration (forests, wildlife, soil, finite stone and ore)

**Before M5:** faith depth (light / consequence / coercive) · knowledge and rumor propagation ✓

**Before M7:** social and personal decision system ✓ (launch scope set) · political model and succession · culture on individuals and assimilation ✓

**Launch polish:** event feed follow lists and search.

**Investigate at M5:** unifying grievances, rumors, and miracles into a shared `HistoricalClaim` (§11).

**Deferred:** siege model · extreme compression tuning beyond 10,000× · explicit technology progression · **natural map change over time** (shifting rivers, erosion, forest advance and retreat). The last is cheaper than it appears: the "shape terrain" power and runtime bridge edges already require mutable terrain, dynamic graph edges, and cache invalidation, so v2 supplies new causes rather than new machinery. Pairs well with bridges — a river that shifts course strands a bridge on dry land and opens an unbridged crossing elsewhere.

---

## 21. Open questions

1. **Capability-graph pacing** — what stops camp → farm → kiln → quarry → smithy → stone wall cascading identically in every world
2. **Performance budgets** — set as a fraction of the M0 baseline, fraction chosen before measuring
3. Disease and plague as a system
4. Siege model — walls, gates, duration, stores, assault, surrender
5. Movement and collision — do agents block one another?
6. Resource regeneration rates — forests, game, soil fertility, finite ore
7. Precise definition of a "major event" for offline halting
8. Whether a master's tools pass to their apprentice or into the household
9. Whether a partially damaged bridge is repaired automatically or needs commissioning
10. Whether long-lived succession stasis is a feature or needs compensation
11. Political succession model — hereditary, elected, strongest household, or culture-dependent

---

## Sources

- Unity 6 releases and LTS support: https://unity.com/releases/unity-6/support
- Godot C# platform support: https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/
- Google Play target API levels: https://support.google.com/googleplay/android-developer/answer/11926878
- Google Play closed testing: https://support.google.com/googleplay/android-developer/answer/14151465

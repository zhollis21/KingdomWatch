using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    [TestFixture]
    public sealed class SimulationClockTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);
        private static readonly SimulationTime Dusk = SimulationTime.FromHours(18L);
        private static readonly SimulationTime Midnight = SimulationTime.FromDays(1L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        private static SimulationClock NewClock() => new SimulationClock(new IdAllocator());

        private static EventId ScheduleTask(
            SimulationClock clock, SimulationTime time, ulong person) =>
            clock.Schedule(
                time,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(person),
                EntityId.None);

        // Records what it is handed, and optionally does something while
        // handling it - which is how the rules about scheduling mid-dispatch
        // get exercised.
        private sealed class Recorder : IScheduledEventHandler
        {
            private readonly Action<ScheduledEvent, SimulationClock>? _whileHandling;

            internal Recorder(Action<ScheduledEvent, SimulationClock>? whileHandling = null)
            {
                _whileHandling = whileHandling;
            }

            internal List<ScheduledEvent> Handled { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock)
            {
                Handled.Add(scheduled);
                _whileHandling?.Invoke(scheduled, clock);
            }
        }

        [Test]
        public void A_new_world_starts_at_zero_with_nothing_pending()
        {
            var clock = NewClock();

            Assert.Multiple(() =>
            {
                Assert.That(clock.Now, Is.EqualTo(SimulationTime.Zero));
                Assert.That(clock.ScheduledCount, Is.Zero);
                Assert.That(clock.TryPeekNext(out _), Is.False);
            });
        }

        [Test]
        public void A_clock_needs_an_allocator()
        {
            Assert.That(() => new SimulationClock(null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Scheduling_returns_a_distinct_durable_id_each_time()
        {
            var clock = NewClock();

            var first = ScheduleTask(clock, Noon, 1UL);
            var second = ScheduleTask(clock, Noon, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(first.IsNone, Is.False);
                Assert.That(clock.ScheduledCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void Nothing_can_be_scheduled_in_the_past()
        {
            var clock = NewClock();
            clock.AdvanceTo(Noon, new Recorder());

            Assert.That(
                () => ScheduleTask(clock, SimulationTime.FromHours(11L), 1UL),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void An_undefined_phase_or_kind_is_refused_at_the_clock()
        {
            var clock = NewClock();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => clock.Schedule(
                        Noon,
                        (SimulationPhase)999,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => clock.Schedule(
                        Noon,
                        SimulationPhase.Physical,
                        (ScheduledEventKind)999,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => clock.Schedule(
                        Noon,
                        SimulationPhase.None,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => clock.Schedule(
                        Noon,
                        SimulationPhase.Physical,
                        ScheduledEventKind.None,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_refused_schedule_leaves_the_queue_exactly_as_it_was()
        {
            // A rejection must not half-book anything. The id it burns is
            // deliberate and harmless - ids need to be unique and increasing,
            // not contiguous - but the queue itself has to be untouched.
            var clock = NewClock();
            ScheduleTask(clock, Dusk, 1UL);
            clock.AdvanceTo(Noon, new Recorder());

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ScheduleTask(clock, SimulationTime.FromHours(1L), 1UL),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(
                    () => clock.Schedule(
                        Dusk,
                        (SimulationPhase)999,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(clock.ScheduledCount, Is.EqualTo(1));
            });

            var recorder = new Recorder();
            clock.AdvanceTo(Midnight, recorder);

            Assert.That(recorder.Handled, Has.Count.EqualTo(1));
        }

        [Test]
        public void Scheduling_at_the_current_instant_is_allowed_while_idle()
        {
            // Not in the past, so it stands - it simply fires on the next
            // advance rather than this one.
            var clock = NewClock();
            clock.AdvanceTo(Noon, new Recorder());

            ScheduleTask(clock, Noon, 1UL);
            var recorder = new Recorder();
            clock.AdvanceTo(Dusk, recorder);

            Assert.That(recorder.Handled, Has.Count.EqualTo(1));
        }

        [Test]
        public void An_empty_queue_lets_compression_run_straight_to_the_target()
        {
            // The reason centuries at 10,000x are possible at all: with nothing
            // due, advancing a year costs the same as advancing a second.
            var clock = NewClock();
            var recorder = new Recorder();

            var dispatched = clock.AdvanceTo(SimulationTime.FromDays(73_000L), recorder);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.Zero);
                Assert.That(recorder.Handled, Is.Empty);
                Assert.That(clock.Now, Is.EqualTo(SimulationTime.FromDays(73_000L)));
            });
        }

        [Test]
        public void Advancing_runs_what_is_due_and_lands_on_the_target()
        {
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            var recorder = new Recorder();

            var dispatched = clock.AdvanceTo(Dusk, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(1));
                Assert.That(recorder.Handled, Has.Count.EqualTo(1));
                Assert.That(clock.Now, Is.EqualTo(Dusk));
            });
        }

        [Test]
        public void An_event_landing_exactly_on_the_target_fires()
        {
            // The boundary that decides whether a threshold crossing is honored
            // or skipped by one tick.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            var recorder = new Recorder();

            clock.AdvanceTo(Noon, recorder);

            Assert.That(recorder.Handled, Has.Count.EqualTo(1));
        }

        [Test]
        public void Events_past_the_target_stay_queued()
        {
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Midnight, 1UL);
            var recorder = new Recorder();

            clock.AdvanceTo(Dusk, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(1));
                Assert.That(clock.ScheduledCount, Is.EqualTo(1));
                Assert.That(clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Time, Is.EqualTo(Midnight));
            });
        }

        [Test]
        public void A_handler_reads_the_clock_at_its_own_instant()
        {
            // Not the target it was advancing toward. Anything computing a
            // duration from Now would otherwise be wrong by the whole jump.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            var seen = new List<SimulationTime>();
            var recorder = new Recorder((_, running) => seen.Add(running.Now));

            clock.AdvanceTo(Midnight, recorder);

            Assert.That(seen, Is.EqualTo(new[] { Noon }));
        }

        [Test]
        public void The_clock_cannot_be_wound_back()
        {
            var clock = NewClock();
            clock.AdvanceTo(Dusk, new Recorder());

            Assert.That(
                () => clock.AdvanceTo(Noon, new Recorder()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Advancing_to_where_the_clock_already_stands_is_allowed()
        {
            // Standing still is not running backwards. The boundary matters
            // because a caller stepping in fixed increments lands here whenever
            // the step is consumed exactly.
            var clock = NewClock();
            clock.AdvanceTo(Noon, new Recorder());
            ScheduleTask(clock, Noon, 1UL);
            var recorder = new Recorder();

            var dispatched = clock.AdvanceTo(Noon, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(1));
                Assert.That(clock.Now, Is.EqualTo(Noon));
            });
        }

        [Test]
        public void A_handler_that_throws_leaves_the_clock_usable()
        {
            // The phase rules throw from inside a handler by design, so a clock
            // that stayed wedged mid-dispatch afterwards would turn every one
            // of those into a dead world.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Dusk, 1UL);
            var exploding = new Recorder((_, _) => throw new InvalidOperationException("boom"));

            Assert.That(
                () => clock.AdvanceTo(Midnight, exploding),
                Throws.TypeOf<InvalidOperationException>());

            var recorder = new Recorder();
            var dispatched = clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(clock.Now, Is.EqualTo(Midnight));
                Assert.That(dispatched, Is.EqualTo(1));
                Assert.That(recorder.Handled[0].Time, Is.EqualTo(Dusk));
                Assert.That(clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void Advancing_needs_a_handler()
        {
            var clock = NewClock();

            Assert.That(() => clock.AdvanceTo(Noon, null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void A_cancelled_event_never_fires()
        {
            var clock = NewClock();
            var doomed = ScheduleTask(clock, Noon, 1UL);
            var kept = ScheduleTask(clock, Dusk, 1UL);

            var cancelled = clock.Cancel(doomed);
            var recorder = new Recorder();
            clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(cancelled, Is.True);
                Assert.That(recorder.Handled, Has.Count.EqualTo(1));
                Assert.That(recorder.Handled[0].Id, Is.EqualTo(kept));
            });
        }

        [Test]
        public void Cancelling_is_reflected_in_the_pending_count_immediately()
        {
            var clock = NewClock();
            var doomed = ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Dusk, 1UL);

            clock.Cancel(doomed);

            Assert.That(clock.ScheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Cancelling_reports_whether_it_beat_the_event()
        {
            // What a system re-predicting a threshold needs to know: did the
            // crossing already happen?
            var clock = NewClock();
            var scheduled = ScheduleTask(clock, Noon, 1UL);

            var first = clock.Cancel(scheduled);
            var again = clock.Cancel(scheduled);
            var neverScheduled = clock.Cancel(new EventId(9_999UL));
            var noEventAtAll = clock.Cancel(EventId.None);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.True);
                Assert.That(again, Is.False);
                Assert.That(neverScheduled, Is.False);
                Assert.That(noEventAtAll, Is.False);
            });
        }

        [Test]
        public void Cancelling_an_already_dispatched_event_reports_false()
        {
            var clock = NewClock();
            var scheduled = ScheduleTask(clock, Noon, 1UL);
            clock.AdvanceTo(Dusk, new Recorder());

            Assert.That(clock.Cancel(scheduled), Is.False);
        }

        [Test]
        public void A_threshold_can_be_re_predicted_by_cancel_then_reschedule()
        {
            // Aldric was booked to go critically hungry at noon, then ate. The
            // crossing moves; there is no second queue for it.
            var clock = NewClock();
            var stale = ScheduleTask(clock, Noon, 1UL);

            clock.Cancel(stale);
            var fresh = ScheduleTask(clock, Dusk, 1UL);

            var recorder = new Recorder();
            clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(1));
                Assert.That(recorder.Handled[0].Id, Is.EqualTo(fresh));
                Assert.That(recorder.Handled[0].Time, Is.EqualTo(Dusk));
            });
        }

        [Test]
        public void Peeking_past_a_cancelled_head_finds_the_real_next_event()
        {
            var clock = NewClock();
            var doomed = ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Dusk, 1UL);

            clock.Cancel(doomed);

            Assert.Multiple(() =>
            {
                Assert.That(clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Time, Is.EqualTo(Dusk));
            });
        }

        [Test]
        public void Peeking_an_entirely_cancelled_queue_finds_nothing()
        {
            var clock = NewClock();
            clock.Cancel(ScheduleTask(clock, Noon, 1UL));

            Assert.Multiple(() =>
            {
                Assert.That(clock.TryPeekNext(out _), Is.False);
                Assert.That(clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void Advancing_the_clock_from_inside_a_handler_is_refused()
        {
            // Section 4's rule made enforceable: events emitted while handling
            // another event are queued, never executed recursively.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            var recorder = new Recorder((_, running) => running.AdvanceTo(Midnight, new Recorder()));

            Assert.That(
                () => clock.AdvanceTo(Dusk, recorder),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void A_reaction_scheduled_into_a_later_phase_runs_in_the_same_instant()
        {
            // How a cascade is meant to work: the death settles in Lifecycle,
            // the household reacts in the phase after it, same tick.
            var clock = NewClock();
            clock.Schedule(
                Noon,
                SimulationPhase.Lifecycle,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);

            var reacted = false;
            var recorder = new Recorder((scheduled, running) =>
            {
                if (reacted)
                {
                    return;
                }

                reacted = true;
                running.Schedule(
                    scheduled.Time,
                    SimulationPhase.HouseholdAndSocial,
                    ScheduledEventKind.SocialDecision,
                    Person(1UL),
                    EntityId.None);
            });

            clock.AdvanceTo(Dusk, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(2));
                Assert.That(recorder.Handled[1].Phase, Is.EqualTo(SimulationPhase.HouseholdAndSocial));
                Assert.That(recorder.Handled[1].Time, Is.EqualTo(Noon));
            });
        }

        [Test]
        public void A_reaction_scheduled_into_an_earlier_phase_is_refused()
        {
            var clock = NewClock();
            clock.Schedule(
                Noon,
                SimulationPhase.HouseholdAndSocial,
                ScheduledEventKind.SocialDecision,
                Person(1UL),
                EntityId.None);

            var recorder = new Recorder((scheduled, running) => running.Schedule(
                scheduled.Time,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None));

            Assert.That(
                () => clock.AdvanceTo(Dusk, recorder),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void A_reaction_scheduled_behind_the_current_position_within_a_phase_is_refused()
        {
            // Same instant, same phase, but a lower primary entity - which
            // would dispatch backwards through the phase. The strictness is the
            // point: it keeps "dispatch order is non-decreasing" true, so the
            // validator (#13) can rely on it.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 5UL);

            var recorder = new Recorder((scheduled, running) => ScheduleTask(
                running, scheduled.Time, 2UL));

            Assert.That(
                () => clock.AdvanceTo(Dusk, recorder),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void A_reaction_landing_exactly_where_its_cause_did_is_refused()
        {
            // The guard compares position, not identity. Were it built on
            // CompareTo, the newly allocated id would make this reaction
            // compare greater and the guard would wave it through - and a
            // handler doing it unconditionally would dispatch forever with the
            // clock frozen at noon.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);

            var recorder = new Recorder((scheduled, running) => ScheduleTask(
                running, scheduled.Time, 1UL));

            Assert.That(
                () => clock.AdvanceTo(Dusk, recorder),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("never at or before it"));
        }

        [Test]
        public void A_cascade_that_climbs_forever_at_one_instant_is_stopped()
        {
            // Position alone cannot catch this one: each reaction is for the
            // next person, so every step really is strictly ahead of the last
            // and the clock still never advances. The budget is what bounds it.
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);

            var recorder = new Recorder((scheduled, running) => ScheduleTask(
                running, scheduled.Time, scheduled.PrimaryEntity.Value + 1UL));

            Assert.That(
                () => clock.AdvanceTo(Dusk, recorder),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("reacting to its own reaction"));
        }

        [Test]
        public void A_cascade_within_the_budget_is_left_alone()
        {
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);
            var reactions = 0;

            var recorder = new Recorder((scheduled, running) =>
            {
                if (reactions >= SimulationClock.MaxCascadePerInstant)
                {
                    return;
                }

                reactions++;
                ScheduleTask(running, scheduled.Time, scheduled.PrimaryEntity.Value + 1UL);
            });

            clock.AdvanceTo(Dusk, recorder);

            Assert.That(
                recorder.Handled,
                Has.Count.EqualTo(SimulationClock.MaxCascadePerInstant + 1));
        }

        [Test]
        public void The_cascade_budget_resets_when_the_clock_moves_on()
        {
            // Deliberately more instants than the budget allows at any one of
            // them. A budget that accumulated instead of resetting would trip
            // partway through a perfectly healthy run - so this fails if the
            // reset is removed, which a shorter run would not.
            const int Instants = SimulationClock.MaxCascadePerInstant + 500;

            var clock = NewClock();
            var recorder = new Recorder((scheduled, running) =>
            {
                if (scheduled.Phase == SimulationPhase.Physical)
                {
                    running.Schedule(
                        scheduled.Time,
                        SimulationPhase.Lifecycle,
                        ScheduledEventKind.BirthCheck,
                        scheduled.PrimaryEntity,
                        EntityId.None);
                }
            });

            for (var tick = 1; tick <= Instants; tick++)
            {
                ScheduleTask(clock, new SimulationTime(tick), 1UL);
            }

            clock.AdvanceTo(new SimulationTime(Instants), recorder);

            // One task plus its one same-instant reaction, at every instant.
            Assert.That(recorder.Handled, Has.Count.EqualTo(Instants * 2));
        }

        [Test]
        public void A_reaction_one_tick_later_is_a_reaction_not_a_cascade()
        {
            // The budget must not catch a system that legitimately reschedules
            // itself forward, which is the normal shape of a repeating task.
            var clock = NewClock();
            ScheduleTask(clock, SimulationTime.Zero.Plus(1L), 1UL);

            var recorder = new Recorder((scheduled, running) => ScheduleTask(
                running, scheduled.Time.Plus(1L), 1UL));

            var dispatched = clock.AdvanceTo(SimulationTime.FromMinutes(5L), recorder);

            Assert.That(dispatched, Is.EqualTo(300));
        }

        [Test]
        public void A_reaction_scheduled_at_an_earlier_instant_is_refused()
        {
            var clock = NewClock();
            ScheduleTask(clock, Dusk, 1UL);

            var recorder = new Recorder((_, running) => ScheduleTask(running, Noon, 1UL));

            Assert.That(
                () => clock.AdvanceTo(Midnight, recorder),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void A_reaction_scheduled_further_ahead_runs_on_a_later_advance()
        {
            var clock = NewClock();
            ScheduleTask(clock, Noon, 1UL);

            var reacted = false;
            var recorder = new Recorder((_, running) =>
            {
                if (reacted)
                {
                    return;
                }

                reacted = true;
                ScheduleTask(running, Midnight, 1UL);
            });

            var first = clock.AdvanceTo(Dusk, recorder);
            var second = clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(1));
                Assert.That(recorder.Handled[1].Time, Is.EqualTo(Midnight));
            });
        }

        [Test]
        public void Dispatch_order_is_the_ordering_key_however_events_were_scheduled()
        {
            // Scheduled in a scrambled order across phases, kinds and entities
            // that all share instants. What comes out must depend on the key
            // alone, never on the order things went in.
            var clock = NewClock();
            var times = new[] { Noon, Dusk, Noon, Dusk, Noon };
            var phases = new[]
            {
                SimulationPhase.Derived,
                SimulationPhase.Physical,
                SimulationPhase.Lifecycle,
                SimulationPhase.Political,
                SimulationPhase.Physical,
            };
            var kinds = new[]
            {
                ScheduledEventKind.SocialDecision,
                ScheduledEventKind.TaskCompleted,
                ScheduledEventKind.BirthCheck,
                ScheduledEventKind.TaskCompleted,
                ScheduledEventKind.BirthCheck,
            };

            for (var i = 0; i < times.Length; i++)
            {
                clock.Schedule(
                    times[(i * 3) % times.Length],
                    phases[(i * 3) % phases.Length],
                    kinds[(i * 3) % kinds.Length],
                    Person((ulong)(((i * 7) % 5) + 1)),
                    EntityId.None);
            }

            var recorder = new Recorder();
            clock.AdvanceTo(Midnight, recorder);

            Assert.That(recorder.Handled, Has.Count.EqualTo(5));
            Assert.That(recorder.Handled, Is.Ordered);
        }

        [Test]
        public void Heavy_cancellation_compacts_the_queue_without_reordering_it()
        {
            // Re-predicting thresholds is the normal case, not the exception,
            // so cancelled entries pile up and the queue rebuilds itself. That
            // rebuild changes its internal layout - and must not change what
            // comes out, which is exactly what the total order buys.
            var clock = NewClock();
            var survivors = new List<EventId>();

            for (var i = 0; i < 120; i++)
            {
                var scheduled = ScheduleTask(
                    clock, SimulationTime.FromMinutes(((i * 37) % 120) + 1L), (ulong)((i % 9) + 1));

                if (i % 3 == 0)
                {
                    survivors.Add(scheduled);
                }
                else
                {
                    clock.Cancel(scheduled);
                }
            }

            Assert.That(clock.ScheduledCount, Is.EqualTo(survivors.Count));

            var recorder = new Recorder();
            clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(survivors.Count));
                Assert.That(recorder.Handled, Is.Ordered);
                Assert.That(clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void The_pending_count_survives_scheduling_on_top_of_a_compacted_queue()
        {
            // ScheduledCount is bookkeeping kept alongside the heap array, and
            // the crossing it can get wrong is scheduling into an array that a
            // previous wave already compacted and drained. Each half works
            // alone; this drives the handover.
            var clock = NewClock();
            var recorder = new Recorder();
            var firstWave = 0;

            for (var i = 0; i < 50; i++)
            {
                var scheduled = ScheduleTask(
                    clock, SimulationTime.FromMinutes(((i * 17) % 60) + 1L), (ulong)((i % 6) + 1));

                if (i % 5 == 0)
                {
                    firstWave++;
                }
                else
                {
                    clock.Cancel(scheduled);
                }
            }

            Assert.That(clock.ScheduledCount, Is.EqualTo(firstWave));

            clock.AdvanceTo(SimulationTime.FromHours(1L), recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(firstWave));
                Assert.That(clock.ScheduledCount, Is.Zero);
            });

            var secondWave = 0;

            for (var i = 0; i < 40; i++)
            {
                var scheduled = ScheduleTask(
                    clock,
                    SimulationTime.FromHours(2L).Plus(((i * 13) % 60) * 60L),
                    (ulong)((i % 7) + 1));

                if (i % 4 == 0)
                {
                    secondWave++;
                }
                else
                {
                    clock.Cancel(scheduled);
                }
            }

            Assert.That(clock.ScheduledCount, Is.EqualTo(secondWave));

            clock.AdvanceTo(Midnight, recorder);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Handled, Has.Count.EqualTo(firstWave + secondWave));
                Assert.That(recorder.Handled, Is.Ordered);
                Assert.That(clock.ScheduledCount, Is.Zero);
                Assert.That(clock.TryPeekNext(out _), Is.False);
            });
        }

        [Test]
        public void The_same_schedule_replays_identically()
        {
            // The claim the whole debugging strategy rests on: the same inputs
            // produce the same run, event for event.
            var first = Run();
            var second = Run();

            Assert.That(second, Is.EqualTo(first));

            static List<string> Run()
            {
                var clock = NewClock();

                for (var i = 0; i < 60; i++)
                {
                    var scheduled = ScheduleTask(
                        clock, SimulationTime.FromMinutes(((i * 23) % 60) + 1L), (ulong)((i % 7) + 1));

                    if (i % 4 == 0)
                    {
                        clock.Cancel(scheduled);
                    }
                }

                var recorder = new Recorder();
                clock.AdvanceTo(Midnight, recorder);

                var trace = new List<string>();

                foreach (var handled in recorder.Handled)
                {
                    trace.Add(handled.ToString());
                }

                return trace;
            }
        }
    }
}

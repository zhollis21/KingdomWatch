using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    [TestFixture]
    public sealed class YearStepperTests
    {
        private static readonly SimulationTime YearOne = SimulationTime.FromYears(1L);

        [Test]
        public void A_step_inside_the_year_moves_by_exactly_what_was_given()
        {
            var stepper = new YearStepper();

            var target = stepper.Next(SimulationTime.FromDays(3L), SimulationTime.TicksPerDay);

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(SimulationTime.FromDays(4L)));
                Assert.That(stepper.Carried, Is.Zero);
            });
        }

        [Test]
        public void A_step_that_ends_on_the_boundary_stops_there_and_carries_nothing()
        {
            var stepper = new YearStepper();
            var start = YearOne.Plus(-SimulationTime.TicksPerDay);

            var target = stepper.Next(start, SimulationTime.TicksPerDay);

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(YearOne));
                Assert.That(stepper.Carried, Is.Zero);
            });
        }

        [Test]
        public void A_step_across_the_boundary_stops_on_it_and_carries_the_rest_into_the_next_call()
        {
            var stepper = new YearStepper();
            var start = YearOne.Plus(-10L);

            var first = stepper.Next(start, 25L);
            var carried = stepper.Carried;
            var second = stepper.Next(first, 0L);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(YearOne));
                Assert.That(carried, Is.EqualTo(15L));
                Assert.That(second, Is.EqualTo(YearOne.Plus(15L)));
                Assert.That(stepper.Carried, Is.Zero);
            });
        }

        [Test]
        public void More_than_a_year_at_once_stops_on_every_boundary_it_passes()
        {
            var stepper = new YearStepper();
            var now = SimulationTime.Zero;
            var visited = new List<SimulationTime>();

            now = stepper.Next(now, 3L * SimulationTime.TicksPerYear + 7L);
            visited.Add(now);

            while (stepper.Carried > 0L)
            {
                now = stepper.Next(now, 0L);
                visited.Add(now);
            }

            Assert.That(visited, Is.EqualTo(new[]
            {
                SimulationTime.FromYears(1L),
                SimulationTime.FromYears(2L),
                SimulationTime.FromYears(3L),
                SimulationTime.FromYears(3L).Plus(7L),
            }));
        }

        [Test]
        public void Nothing_given_and_nothing_carried_stays_put()
        {
            var stepper = new YearStepper();
            var now = SimulationTime.FromDays(5L);

            Assert.That(stepper.Next(now, 0L), Is.EqualTo(now));
        }

        [Test]
        public void A_negative_step_is_refused_before_anything_is_carried()
        {
            var stepper = new YearStepper();
            stepper.Next(YearOne.Plus(-1L), 5L);

            Assert.Multiple(() =>
            {
                Assert.That(() => stepper.Next(YearOne, -1L), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(stepper.Carried, Is.EqualTo(4L));
            });
        }

        [Test]
        public void A_step_that_would_overflow_what_is_carried_is_refused_and_changes_nothing()
        {
            var stepper = new YearStepper();
            stepper.Next(YearOne.Plus(-1L), 5L);

            Assert.Multiple(() =>
            {
                Assert.That(() => stepper.Next(YearOne, long.MaxValue), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(stepper.Carried, Is.EqualTo(4L));
            });
        }

        [Test]
        public void In_the_last_partial_year_of_time_it_steps_to_the_end_of_time_rather_than_to_a_boundary_that_does_not_exist()
        {
            var stepper = new YearStepper();
            var lastBoundary = SimulationTime.FromYears(long.MaxValue / SimulationTime.TicksPerYear);
            var end = new SimulationTime(long.MaxValue);

            var target = stepper.Next(lastBoundary, long.MaxValue - lastBoundary.Ticks + 3L);

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(end));
                Assert.That(stepper.Carried, Is.EqualTo(3L));
            });
        }

        [Test]
        public void Uneven_chunks_reach_the_same_hash_as_the_harness_at_every_year()
        {
            // The whole point: however a driver slices real time into steps,
            // the world it hashes on each boundary is the harness's world.
            var run = new WorldRun(1UL);
            var world = World.M1(1UL);
            var stepper = new YearStepper();
            var chunk = 1L;

            for (var year = 1L; year <= 2L; year++)
            {
                run.RunYears(1L);

                while (world.Now.CompareTo(SimulationTime.FromYears(year)) < 0)
                {
                    // Deterministic but uneven: 1, 3, 9 ... hours, wrapping.
                    chunk = chunk * 3L % 97L;
                    world.AdvanceTo(stepper.Next(world.Now, chunk * SimulationTime.TicksPerHour));
                }

                Assert.That(world.Hash(), Is.EqualTo(run.Hash()), "year " + year);
            }
        }
    }
}

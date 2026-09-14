using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;

namespace KingdomWatch.Core.Needs
{
    /// <summary>
    /// People eat, run short, and weaken. Owns
    /// <see cref="ScheduledEventKind.MealDue"/>: once a day each tracked food
    /// holder's members draw rations from its ledger, anyone left unfed for
    /// long enough loses health, and a holder that cannot feed everyone is in
    /// famine until it can.
    /// </summary>
    /// <remarks>
    /// **One meal per holder per day, not a hunger crossing per person.**
    /// Section 4 sketches the per-person form - Aldric ate at 08:00, so book
    /// HungerCritical for 17:42 - and section 6 says how food actually moves:
    /// meals consume the holder's supply, with no loaf-by-loaf pantry. The
    /// daily meal is the second of those and does the work of the first: it is
    /// a scheduled event, so a thirty-day jump dispatches thirty meals in order
    /// and the one that falls short publishes the famine on the day it starts,
    /// never at the end of the jump. What it gives up is per-person meal
    /// TIMES, which nothing consumes before M3's daily schedules (#21) - and
    /// adding those then is a change to when the draw happens, not to what it
    /// draws from.
    ///
    /// Hunger itself is integrated, not polled: a person stores only when they
    /// last ate (<see cref="PersonRecord.LastFedAt"/>), and how hungry they are
    /// is the distance from that to now, evaluated when a meal finds them
    /// unfed. Nobody's hunger is ticked.
    ///
    /// **The holder is a <see cref="MobileGroup"/>** because that is the only
    /// thing with a ledger. Settlements (#54) do not exist yet; when they do,
    /// the draw moves to whatever holds the ledger. Households (#9) have food
    /// ACCESS rather than food - section 6 - so they do not hold a ledger of
    /// their own and the draw does not go through them.
    ///
    /// **Shortfall feeds the young first.** When the ledger cannot cover
    /// everyone, the table is served in three sittings - dependents (infants,
    /// children, adolescents), then adults, then elders - and within a sitting
    /// in the order the group lists its members. The rule protects the next
    /// generation, and it makes a famine read the way a chronicle would tell
    /// it: the old go without first. Status is not a factor, because the only status
    /// that exists is the band's leader and section 6 puts feeding priority
    /// with the household, not the polity. This replaced the insertion-order
    /// placeholder #51 shipped with.
    ///
    /// **Damage here, death elsewhere.** An unfed member past
    /// <see cref="StarvationGrace"/> loses <see cref="StarvationDamagePerMeal"/>
    /// health per missed meal. A missed meal never takes health below zero,
    /// and never touches health already at or below it - what a value there
    /// means is not this system's to say. Turning low health into a death is
    /// the mortality model's (#11), with <see cref="PersonRecord.LastFedAt"/>
    /// as its nutrition input, so the mortality curve lives in exactly one place.
    ///
    /// **No predicted "food runs out in N days" event.** Section 4 names food
    /// depletion as a threshold crossing, and the daily meal is the boundary
    /// that makes compression trustworthy today. A predicted crossing on top
    /// would need re-predicting on every gather and transfer, and the ledger
    /// has no change notification to drive that - so it would silently go
    /// stale. <see cref="DaysOfFood"/> is the same number as a query, for job
    /// assignment (#52), the settling trigger (#54) and, eventually, the house
    /// tooltip section 6 warns about. The predicted event can arrive with the
    /// first thing that aggregates meals over more than a day.
    ///
    /// The numbers are placeholders in the <see cref="PrimitiveTier"/> sense:
    /// plausible, not tuned, and nothing to read a balance decision into.
    ///
    /// Allocation-free after <see cref="Track"/>: the tracked list and its
    /// entries are built up front, the member loop is by index, and
    /// publishing is the bus's allocation-free path. Deterministic by
    /// construction - no randomness, and the order within a sitting is the
    /// group's stable insertion order.
    /// </remarks>
    public sealed class Hunger : IScheduledEventHandler
    {
        /// <summary>
        /// Food one person draws at one meal, which is one per day - so also
        /// their daily draw. Three matches <see cref="PrimitiveTier.Forage"/>,
        /// which yields three and is documented as feeding a person for about
        /// a day.
        /// </summary>
        public const int DailyRation = 3;

        /// <summary>Ticks between one holder's meals.</summary>
        public const long MealInterval = SimulationTime.TicksPerDay;

        /// <summary>
        /// How long someone can go unfed before a missed meal costs health. A
        /// person who ate this morning and misses tonight's meal is hungry,
        /// not starving.
        /// </summary>
        public const long StarvationGrace = 2L * SimulationTime.TicksPerDay;

        /// <summary>Health lost per missed meal once past the grace period.</summary>
        public const short StarvationDamagePerMeal = 10;

        /// <summary>
        /// Meals are resource changes, so they run in the physical phase and
        /// everything downstream of eating - or not - can react in a later
        /// one at the same instant.
        /// </summary>
        public const SimulationPhase MealPhase = SimulationPhase.Physical;

        private readonly SimulationClock _clock;
        private readonly DomainEventBus _bus;
        private readonly PersonStore _people;

        // A list, scanned by id. There are six holders at launch and one
        // lookup per holder per day; a dictionary would be solving nothing.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        /// <param name="bus">
        /// Where famines are announced, and where the clock comes from: meals
        /// are booked on the clock the bus stamps events with, so a wake-up
        /// and the fact it produces can never disagree about when. There is
        /// no way to hand this class a different clock.
        /// </param>
        public Hunger(DomainEventBus bus, PersonStore people)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _clock = bus.Clock;
        }

        /// <summary>How many holders have meals scheduled.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>
        /// Starts feeding a holder: its first meal is booked one
        /// <see cref="MealInterval"/> from now, and each meal books the next.
        /// Refuses a holder already tracked - two meal streams on one ledger
        /// would draw twice a day.
        /// </summary>
        public void Track(MobileGroup group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            if (IndexOf(group.Id) >= 0)
            {
                throw new InvalidOperationException(
                    group.Id + " is already tracked; a second meal stream would draw twice a day.");
            }

            // Booked before recorded, for the same reason PersonStore builds
            // the handle before it moves any bookkeeping: the booking is the
            // one thing here that can be refused, and a holder recorded
            // without a meal stream would refuse to be tracked again and go
            // unfed for good.
            ScheduleMeal(group.Id);
            _tracked.Add(new Tracked(group));
        }

        /// <summary>Whether a tracked holder's last meal left someone unfed.</summary>
        public bool IsInFamine(MobileGroup group) => TrackedFor(group).InFamine;

        /// <summary>
        /// Whole days the holder's available food covers at the current
        /// living headcount. <see cref="int.MaxValue"/> when nobody draws.
        /// </summary>
        /// <remarks>
        /// A query, not a prediction: nothing keeps it current, which is why it
        /// is not a scheduled event (see the type's remarks). Living members
        /// only, because the dead do not eat and their handles may still be in
        /// the group until the death cascade (Lifecycle.Deaths) removes them.
        /// </remarks>
        public int DaysOfFood(MobileGroup group)
        {
            var tracked = TrackedFor(group);
            var living = 0;
            var members = tracked.Group.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (_people.IsAlive(members[i]))
                {
                    living++;
                }
            }

            if (living == 0)
            {
                return int.MaxValue;
            }

            var dailyDraw = (long)DailyRation * living;
            return (int)(tracked.Group.SharedSupplies.Available(ResourceKind.Food) / dailyDraw);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (scheduled.Kind != ScheduledEventKind.MealDue)
            {
                throw new InvalidOperationException(
                    "Hunger owns " + ScheduledEventKind.MealDue + ", but was handed " + scheduled + ".");
            }

            // The clock is what the router passes through, and it must be the
            // bus's: that is the one Track booked the meal on, and the one the
            // famine this meal may announce will be stamped with. A different
            // clock here means the next meal lands on a queue nobody is
            // dispatching.
            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Hunger schedules on its bus's clock, but was dispatched by another.");
            }

            var index = IndexOf(scheduled.PrimaryEntity);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for a holder Hunger is not tracking.");
            }

            ServeMeal(_tracked[index], clock.Now);

            // The stream ends with time itself. A meal due within one interval
            // of the last representable instant has no tomorrow to book into,
            // and throwing for that after the ledger has moved would leave
            // AdvanceTo unable to reach the end of the world.
            if (clock.Now.Ticks <= long.MaxValue - MealInterval)
            {
                ScheduleMeal(scheduled.PrimaryEntity);
            }
        }

        private void ServeMeal(Tracked tracked, SimulationTime now)
        {
            var fed = 0;
            var unfed = 0;

            // Three sittings, each a pass over the members in group order:
            // dependents, then adults, then elders. Three passes rather than a
            // sort because a sort would need somewhere to put the sorted
            // handles, and this runs inside the tick loop.
            ServeSitting(tracked, now, Sitting.Dependents, ref fed, ref unfed);
            ServeSitting(tracked, now, Sitting.Adults, ref fed, ref unfed);
            ServeSitting(tracked, now, Sitting.Elders, ref fed, ref unfed);

            var group = tracked.Group;

            // A famine opens on the first meal that leaves someone unfed and
            // closes on the first that feeds everyone. A meal with nobody
            // living at the table changes nothing: there is no one to be fed
            // or to go without, and "famine ended" over an empty group would
            // be a lie in the chronicle.
            //
            // Published before the flag moves: a refused publish is a wiring
            // bug the bus throws for, and the flag should not then say a
            // famine was announced that nobody heard.
            if (unfed > 0 && !tracked.InFamine)
            {
                _bus.Publish(DomainEventKind.FamineStarted, group.Id, EntityId.None);
                tracked.InFamine = true;
            }
            else if (unfed == 0 && fed > 0 && tracked.InFamine)
            {
                _bus.Publish(DomainEventKind.FamineEnded, group.Id, EntityId.None);
                tracked.InFamine = false;
            }
        }

        private void ServeSitting(
            Tracked tracked, SimulationTime now, Sitting sitting, ref int fed, ref int unfed)
        {
            var ledger = tracked.Group.SharedSupplies;
            var members = tracked.Group.Members;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                // Membership is spatial and lags death until the cascade
                // (Lifecycle.Deaths) removes the handle; the dead neither eat
                // nor starve.
                if (!_people.IsAlive(member) || SittingOf(_people.GetAgeStage(member)) != sitting)
                {
                    continue;
                }

                if (ledger.Available(ResourceKind.Food) >= DailyRation)
                {
                    ledger.Consume(ResourceKind.Food, DailyRation);
                    _people.SetLastFedAt(member, now);
                    fed++;
                    continue;
                }

                unfed++;

                if (_people.GetLastFedAt(member).TicksUntil(now) > StarvationGrace)
                {
                    Starve(member);
                }
            }
        }

        private static Sitting SittingOf(AgeStage stage)
        {
            if (AgeStages.IsDependent(stage))
            {
                return Sitting.Dependents;
            }

            return stage == AgeStage.Elder ? Sitting.Elders : Sitting.Adults;
        }

        // Zero is "as bad as starvation gets" for the mortality model to read,
        // so damage stops there. Health already at or below zero is left
        // exactly as it is: the store allows negative values and #11 decides
        // what they mean, so this neither pushes one further down nor - as an
        // unconditional Max(0, ...) would - raises it back to zero.
        private void Starve(PersonHandle member)
        {
            var health = _people.GetHealth(member);

            if (health > 0)
            {
                _people.SetHealth(member, (short)Math.Max(0, health - StarvationDamagePerMeal));
            }
        }

        private void ScheduleMeal(EntityId holder) =>
            _clock.Schedule(
                _clock.Now.Plus(MealInterval), MealPhase, ScheduledEventKind.MealDue, holder, EntityId.None);

        private Tracked TrackedFor(MobileGroup group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var index = IndexOf(group.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(group.Id + " is not tracked by Hunger.");
            }

            return _tracked[index];
        }

        private int IndexOf(EntityId holder)
        {
            for (var i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Group.Id == holder)
                {
                    return i;
                }
            }

            return -1;
        }

        // The order the table is served in when food is short. See the type's
        // remarks for why this order and not another.
        private enum Sitting
        {
            Dependents,
            Adults,
            Elders,
        }

        // A class rather than a struct so InFamine can be flipped in place
        // through the list without a copy-back.
        private sealed class Tracked
        {
            public Tracked(MobileGroup group)
            {
                Group = group;
            }

            public MobileGroup Group { get; }

            public bool InFamine { get; set; }
        }
    }
}

using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.WorldGen
{
    /// <summary>
    /// A starting band: section 15's composition made concrete. Unrelated
    /// founding couples with children, singles for the remainder, a leader,
    /// food for the first days and wood for the first camps, all standing
    /// on one cell.
    /// </summary>
    /// <remarks>
    /// **Lineages first.** Section 6 and section 15 make the founding
    /// lineage count the real variable: a band seeded as two extended
    /// families deadlocks its mating pool within two generations, and 8-12
    /// unrelated lineages is the target, with elves the constraining case.
    /// So the generator decides how many couples before anything else -
    /// <see cref="LineagesFor"/> - and fills the band around them: each
    /// couple gets children in turn until the band is full, up to
    /// <see cref="MaxChildrenPerCouple"/>, and anyone left over is a single
    /// adult, a lineage of their own. The viability run that tests
    /// the count lives with the generator, in Core.Tests.
    ///
    /// **Ages by keyed draw.** Adults are 20 to 44; a child is as old as the
    /// younger parent allows, at sixteen or more at the birth. Every draw is
    /// keyed by the band's id, the person's index in it and what the draw is
    /// for, under <see cref="RandomDomain.BandGeneration"/>, so the same seed
    /// gives the same band on every platform and no person depends on the
    /// order the others were made.
    ///
    /// **What it calls into and what it does not.** Households come from
    /// <see cref="FamilyFormation.Partner"/> and children join them through
    /// <see cref="Households.Join"/>: the rules are theirs (#9), and a band
    /// generated past them would be one the rules never checked. Each
    /// person is announced with <see cref="DomainEventKind.PersonBorn"/>
    /// once every store can answer for them, so ageing and mortality book
    /// their wake-ups as they do for a birth.
    ///
    /// **The leader is the oldest adult.** Section 15 asks for a leader and
    /// section 8 puts the ruler with the largest band; who leads and why is
    /// M7's. Oldest is a rule that needs no draw and reads as plausible.
    ///
    /// **Skills start flat, and tools do not exist.** Section 15 lists basic
    /// tools among a band's starting supplies; no tool resource exists yet
    /// (#78 enumerates the ladder) and none is invented here. Races are
    /// #34's: one <see cref="DemographicSettings"/> table, whichever the
    /// caller passes.
    ///
    /// Tracking the band - on <see cref="Hunger"/>, <see cref="Jobs"/>,
    /// <see cref="Nomadic.NomadicBands"/> and the rest - is the world's
    /// wiring (#17), not the generator's: it makes a band, and hands it
    /// back.
    ///
    /// Generation allocates, freely; it runs once per band per world.
    /// </remarks>
    public sealed class BandGenerator
    {
        /// <summary>Fewest unrelated founding couples, whatever the size allows.</summary>
        public const int MinLineages = 8;

        /// <summary>Most founding couples, however large the band.</summary>
        public const int MaxLineages = 12;

        /// <summary>Roughly one couple per this many people, between the bounds.</summary>
        public const int PeoplePerLineage = 5;

        /// <summary>Children a founding couple starts with, at most.</summary>
        public const int MaxChildrenPerCouple = 4;

        /// <summary>Youngest a founder is.</summary>
        public const long MinAdultYears = 20L;

        /// <summary>Oldest a founder is.</summary>
        public const long MaxAdultYears = 44L;

        /// <summary>Youngest a parent was at a child's birth.</summary>
        public const long MinParentYears = 16L;

        /// <summary>Oldest a starting child is.</summary>
        public const long MaxChildYears = 15L;

        /// <summary>Wood a band starts with: a few camps' worth.</summary>
        public const int StartingWood = 4 * Nomadic.NomadicBands.CampWood;

        private const short StartingHealth = 100;

        // What each draw is for, so no two draws for one person share a key.
        private const int AgeDraw = 1;
        private const int SexDraw = 2;

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Genealogy _genealogy;
        private readonly FamilyFormation _family;
        private readonly Households _households;
        private readonly DemographicSettings _settings;
        private readonly DeterministicRng _rng;

        public BandGenerator(
            DomainEventBus bus,
            PersonStore people,
            Genealogy genealogy,
            FamilyFormation family,
            Households households,
            DemographicSettings settings,
            DeterministicRng rng)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _genealogy = genealogy ?? throw new ArgumentNullException(nameof(genealogy));
            _family = family ?? throw new ArgumentNullException(nameof(family));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _settings.Validate();
            _clock = bus.Clock;
        }

        /// <summary>
        /// How many founding couples a band of this size gets: about one per
        /// <see cref="PeoplePerLineage"/>, at least <see cref="MinLineages"/>
        /// and at most <see cref="MaxLineages"/>, and never more than the
        /// size holds.
        /// </summary>
        public static int LineagesFor(int size)
        {
            var wanted = Math.Max(MinLineages, Math.Min(MaxLineages, size / PeoplePerLineage));
            return Math.Min(wanted, size / 2);
        }

        /// <summary>
        /// Makes a band of exactly this many living people standing at a
        /// position, with its households formed, its kinship recorded, its
        /// leader chosen and its supplies stocked. Refuses a size under two.
        /// </summary>
        public MobileGroup Generate(int size, WorldPosition at)
        {
            if (size < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "A band is at least a couple.");
            }

            var band = new MobileGroup(_clock.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, at);
            var key = _rng.Key(RandomDomain.BandGeneration).Mix(band.Id);
            var couples = LineagesFor(size);
            var made = 0;

            // Founders: each couple partnered, so it has a household for its
            // children to join.
            var households = new Household[couples];
            var wives = new PersonHandle[couples];
            var husbands = new PersonHandle[couples];

            for (var i = 0; i < couples; i++)
            {
                wives[i] = Founder(band, key, made++, Sex.Female);
                husbands[i] = Founder(band, key, made++, Sex.Male);
                households[i] = _family.Partner(wives[i], husbands[i], Reasons.None);
            }

            // Children, dealt round-robin so no couple has four before
            // another has one.
            var perCouple = 0;

            while (made < size && perCouple < MaxChildrenPerCouple)
            {
                for (var i = 0; i < couples && made < size; i++)
                {
                    var child = Child(band, key, made++, wives[i], husbands[i]);
                    _households.Join(households[i], child);
                }

                perCouple++;
            }

            // Whoever is left is a single adult: another lineage.
            while (made < size)
            {
                Founder(band, key, made, made % 2 == 0 ? Sex.Female : Sex.Male);
                made++;
            }

            band.Leader = OldestAdult(band);
            band.SharedSupplies.Gather(ResourceKind.Food, size * Hunger.DailyRation * Jobs.FoodTargetDays);
            band.SharedSupplies.Gather(ResourceKind.Wood, StartingWood);
            return band;
        }

        private PersonHandle Founder(MobileGroup band, RandomKey key, int index, Sex sex)
        {
            var age = MinAdultYears + key.Mix(index).Mix(AgeDraw).Range(0, (int)(MaxAdultYears - MinAdultYears + 1L));
            return Add(band, age, sex, EntityId.None, EntityId.None);
        }

        private PersonHandle Child(MobileGroup band, RandomKey key, int index, PersonHandle mother, PersonHandle father)
        {
            var now = _clock.Now;
            var youngerParent = Math.Min(_people.GetAgeYears(mother, now), _people.GetAgeYears(father, now));
            var oldest = Math.Min(MaxChildYears, youngerParent - MinParentYears);
            var draw = key.Mix(index);
            var age = draw.Mix(AgeDraw).Range(0, (int)oldest + 1);
            var sex = draw.Mix(SexDraw).Chance(1, 2) ? Sex.Female : Sex.Male;
            return Add(band, age, sex, _people.GetId(mother), _people.GetId(father));
        }

        // Added, recorded, placed, and announced last - the order a birth
        // uses, so that PersonBorn's subscribers see someone who fully exists.
        private PersonHandle Add(MobileGroup band, long ageYears, Sex sex, EntityId mother, EntityId father)
        {
            var now = _clock.Now;
            var id = _clock.Ids.Next(EntityKind.Person);
            var person = _people.Add(
                id, band.Position, StartingHealth, _settings.StageAt(ageYears), sex, 0, 0, now,
                now.Ticks - ageYears * SimulationTime.TicksPerYear);
            _genealogy.Record(id, mother, father);
            band.AddMember(person);
            _bus.Publish(DomainEventKind.PersonBorn, id, mother);
            return person;
        }

        private PersonHandle OldestAdult(MobileGroup band)
        {
            var now = _clock.Now;
            var oldest = PersonHandle.None;
            var oldestAge = -1L;
            var members = band.Members;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!AgeStages.IsAdult(_people.GetAgeStage(member)))
                {
                    continue;
                }

                var age = _people.GetAgeYears(member, now);

                if (age > oldestAge)
                {
                    oldest = member;
                    oldestAge = age;
                }
            }

            return oldest;
        }
    }
}

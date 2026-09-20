using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Rng;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// Watches a run and reports any value that two different call sites both
    /// drew - the backstop #57 asks for, sitting beside the RNG rather than
    /// inside the mixing.
    /// </summary>
    /// <remarks>
    /// **What this can and cannot find.** Declaring a
    /// <see cref="RandomSite"/> is the real protection: two distinct sites
    /// begin from distinct key states, so agreeing later is a 2^-64 accident
    /// rather than something a bare counter can reach. What remains for a
    /// watcher is the residue - and it sees only the paths a run actually
    /// executes, so a quiet report means "nothing collided here", never
    /// "nothing can collide".
    ///
    /// The same site drawing the same value twice is not a collision. That is
    /// a key being asked the same question twice, which is the whole point of
    /// keying: it must answer the same way.
    ///
    /// It remembers every distinct value it has seen, so memory grows with the
    /// number of draws. That is affordable for a soak or a test and is why
    /// nothing attaches one by default.
    /// </remarks>
    public sealed class DrawCollisionDetector : IRandomDrawObserver
    {
        private readonly Dictionary<ulong, Origin> _firstSeen = new Dictionary<ulong, Origin>();
        private readonly List<string> _collisions = new List<string>();
        private readonly ReadOnlyCollection<string> _collisionsView;

        public DrawCollisionDetector()
        {
            _collisionsView = _collisions.AsReadOnly();
        }

        /// <summary>How many draws were reported, collisions included.</summary>
        public int Draws { get; private set; }

        /// <summary>
        /// One line per collision, naming both call sites and the value they
        /// shared. Empty is the answer we want.
        /// </summary>
        public IReadOnlyList<string> Collisions => _collisionsView;

        public void Drew(RandomDomain domain, RandomSite site, ulong value)
        {
            Draws++;

            if (!_firstSeen.TryGetValue(value, out var first))
            {
                _firstSeen.Add(value, new Origin(domain, site));
                return;
            }

            if (first.Domain == domain && first.Site == site)
            {
                return;
            }

            _collisions.Add(
                first.Domain + "/" + first.Site + " and " + domain + "/" + site
                + " both drew " + value + ".");
        }

        private readonly struct Origin
        {
            internal Origin(RandomDomain domain, RandomSite site)
            {
                Domain = domain;
                Site = site;
            }

            internal RandomDomain Domain { get; }

            internal RandomSite Site { get; }
        }
    }
}

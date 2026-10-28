using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// Where a task is: on the way, or at work. Derived from the task's
    /// timings by <see cref="WorkTask.PhaseAt"/>, never stored.
    /// </summary>
    public enum TaskPhase
    {
        /// <summary>The instant is outside the task's span.</summary>
        None = 0,

        /// <summary>
        /// Walking from <see cref="WorkTask.Origin"/> to <see cref="WorkTask.Destination"/>:
        /// out to the work, or the whole of a walk home.
        /// </summary>
        Outbound = 1,

        /// <summary>At the destination, running the recipe.</summary>
        Working = 2,
    }

    /// <summary>
    /// One run of a job's recipe by one person, or one walk home at the end
    /// of their day: section 4's scheduled task, holding what the stepped
    /// simulation needs to put the person somewhere plausible if the player
    /// zooms in partway through.
    /// </summary>
    /// <remarks>
    /// Section 4 lists <c>startTime · endTime · origin · destination · task
    /// phase · coarse route</c>. Start, origin and destination are here; the
    /// end is the start plus the two legs; the phase is arithmetic over
    /// those (<see cref="PhaseAt"/>) rather than a field that could disagree
    /// with the clock; and the route is kept beside the task by
    /// <see cref="Jobs"/>, because a list does not belong in a struct that is
    /// copied out by value.
    ///
    /// **A task walks there and works; it does not walk back** (#199). The
    /// output counts when the work ends, and the next task sets out from
    /// where this one ended. Only when nothing more fits before dusk does the
    /// worker walk home, and that walk is a task of its own: no job, no work,
    /// one leg from where the last task ended to the community's position
    /// (<see cref="IsWalkHome"/>).
    ///
    /// The legs are stored as durations rather than as more instants
    /// because they are what the reconstruction divides by, and because
    /// nothing here is a sum that could overflow: each is bounded by the
    /// route's cost and the recipe's duration, both of which the pathfinder
    /// and the recipe already bound.
    ///
    /// Nothing but the scheduled <see cref="Completion"/> refers to a task by
    /// identity; the task is state on the worker, found by their handle.
    /// </remarks>
    public readonly struct WorkTask
    {
        /// <summary>No task. What a slot holds between tasks.</summary>
        public static readonly WorkTask None = default;

        /// <param name="job">The job, or <see cref="JobKind.None"/> for a walk home, which does no work.</param>
        public WorkTask(
            PersonHandle worker,
            EntityId holder,
            JobKind job,
            SimulationTime start,
            long travelTicks,
            long workTicks,
            WorldPosition origin,
            WorldPosition destination,
            EventId completion)
        {
            if (worker.IsNone)
            {
                throw new ArgumentException("A task needs a worker.", nameof(worker));
            }

            // Kind, not just None: a holder is looked up among the tracked
            // communities, and a person's id there would fail at delivery,
            // hours later.
            if (holder.Kind != EntityKind.MobileGroup && holder.Kind != EntityKind.Settlement)
            {
                throw new ArgumentException(
                    "A task delivers to a MobileGroup or a Settlement, not " + holder + ".", nameof(holder));
            }

            if (job != JobKind.None && !JobTable.IsJob(job))
            {
                throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job a task can run.");
            }

            if (travelTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(travelTicks), travelTicks, "A leg cannot take negative time.");
            }

            // A job works for a while; a walk home works not at all, and is
            // nothing but its walk.
            if (job == JobKind.None ? workTicks != 0L || travelTicks == 0L : workTicks <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(workTicks), workTicks, "Work takes a positive number of ticks, and a walk home none, over a walk of some length.");
            }

            if (completion.IsNone)
            {
                throw new ArgumentException("A task is booked before it is recorded.", nameof(completion));
            }

            // The legs are summed by End and PhaseAt, and End adds them to
            // the start: a task whose end the clock cannot represent is
            // refused here rather than by End, later.
            if (travelTicks > long.MaxValue - workTicks
                || travelTicks + workTicks > long.MaxValue - start.Ticks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(workTicks), workTicks, "The task would end past the end of time.");
            }

            Worker = worker;
            Holder = holder;
            Job = job;
            Start = start;
            TravelTicks = travelTicks;
            WorkTicks = workTicks;
            Origin = origin;
            Destination = destination;
            Completion = completion;
        }

        /// <summary>Whether this is a task at all, as opposed to <see cref="None"/>.</summary>
        public bool IsNone => Worker.IsNone;

        /// <summary>Whether this is the walk home at the end of a day's work, rather than a job.</summary>
        public bool IsWalkHome => !IsNone && Job == JobKind.None;

        /// <summary>Who is doing it.</summary>
        public PersonHandle Worker { get; }

        /// <summary>Whose ledger the output goes to: the worker's band.</summary>
        public EntityId Holder { get; }

        /// <summary>
        /// The job this is one run of; <see cref="JobTable.Recipe"/> names the
        /// recipe. <see cref="JobKind.None"/> for a walk home.
        /// </summary>
        public JobKind Job { get; }

        /// <summary>When the worker set out.</summary>
        public SimulationTime Start { get; }

        /// <summary>Ticks from origin to destination.</summary>
        public long TravelTicks { get; }

        /// <summary>Ticks at the destination: the recipe's duration. Zero for a walk home.</summary>
        public long WorkTicks { get; }

        /// <summary>Where the worker set out from: where their last task ended, or the community's position.</summary>
        public WorldPosition Origin { get; }

        /// <summary>Where the work is done; for a walk home, the community's position.</summary>
        public WorldPosition Destination { get; }

        /// <summary>The <see cref="ScheduledEventKind.TaskCompleted"/> booked for <see cref="End"/>.</summary>
        public EventId Completion { get; }

        /// <summary>When the work is done, or the walk home over.</summary>
        public SimulationTime End => Start.Plus(TravelTicks + WorkTicks);

        /// <summary>
        /// Which leg the task is on at an instant. The end is the last instant
        /// of the work, since the completion that fires there finds the worker
        /// just finishing - or of the walk, for a walk home, which finds them
        /// arrived. Anything before the start is None.
        /// </summary>
        public TaskPhase PhaseAt(SimulationTime now)
        {
            // The sentinel's zero legs would otherwise read as a walk
            // ending at tick zero.
            if (IsNone)
            {
                return TaskPhase.None;
            }

            var elapsed = Start.TicksUntil(now);

            if (elapsed < 0L || elapsed > TravelTicks + WorkTicks)
            {
                return TaskPhase.None;
            }

            return elapsed < TravelTicks || WorkTicks == 0L ? TaskPhase.Outbound : TaskPhase.Working;
        }

        public override string ToString() =>
            IsNone ? "no task"
            : (IsWalkHome ? "Walk home" : Job.ToString()) + " by " + Worker + " from " + Start + " to " + End;
    }
}

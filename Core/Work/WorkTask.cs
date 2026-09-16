using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// Where a task is: on the way out, at work, or on the way home.
    /// Derived from the task's timings by <see cref="WorkTask.PhaseAt"/>,
    /// never stored.
    /// </summary>
    public enum TaskPhase
    {
        /// <summary>The instant is outside the task's span.</summary>
        None = 0,

        /// <summary>Walking from <see cref="WorkTask.Origin"/> to <see cref="WorkTask.Destination"/>.</summary>
        Outbound = 1,

        /// <summary>At the destination, running the recipe.</summary>
        Working = 2,

        /// <summary>Walking home with the output.</summary>
        Returning = 3,
    }

    /// <summary>
    /// One run of a job's recipe by one person: section 4's scheduled task,
    /// holding what the stepped simulation needs to put the person somewhere
    /// plausible if the player zooms in partway through.
    /// </summary>
    /// <remarks>
    /// Section 4 lists <c>startTime · endTime · origin · destination · task
    /// phase · coarse route</c>. Start, origin and destination are here; the
    /// end is the start plus the three legs; the phase is arithmetic over
    /// those (<see cref="PhaseAt"/>) rather than a field that could disagree
    /// with the clock; and the route is kept beside the task by
    /// <see cref="Jobs"/>, because a list does not belong in a struct that is
    /// copied out by value.
    ///
    /// The three legs are stored as durations rather than as two more
    /// instants because they are what the reconstruction divides by, and
    /// because nothing here is a sum that could overflow: each is bounded by
    /// the route's cost and the recipe's duration, both of which the
    /// pathfinder and the recipe already bound.
    ///
    /// Nothing but the scheduled <see cref="Completion"/> refers to a task by
    /// identity; the task is state on the worker, found by their handle.
    /// </remarks>
    public readonly struct WorkTask
    {
        /// <summary>No task. What a slot holds between tasks.</summary>
        public static readonly WorkTask None = default;

        public WorkTask(
            PersonHandle worker,
            EntityId holder,
            JobKind job,
            SimulationTime start,
            long travelTicks,
            long workTicks,
            long returnTicks,
            WorldPosition origin,
            WorldPosition destination,
            EventId completion)
        {
            if (worker.IsNone)
            {
                throw new ArgumentException("A task needs a worker.", nameof(worker));
            }

            if (holder.IsNone)
            {
                throw new ArgumentException("A task needs a holder to deliver to.", nameof(holder));
            }

            if (!JobTable.IsJob(job))
            {
                throw new ArgumentOutOfRangeException(nameof(job), job, "Not a job a task can run.");
            }

            if (travelTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(travelTicks), travelTicks, "A leg cannot take negative time.");
            }

            if (workTicks <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(workTicks), workTicks, "Work takes a positive number of ticks.");
            }

            if (returnTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(returnTicks), returnTicks, "A leg cannot take negative time.");
            }

            if (completion.IsNone)
            {
                throw new ArgumentException("A task is booked before it is recorded.", nameof(completion));
            }

            // The three legs are summed by End and PhaseAt; a sum that wraps
            // would put the end before the start.
            if (travelTicks > long.MaxValue - workTicks || travelTicks + workTicks > long.MaxValue - returnTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(returnTicks), returnTicks, "The three legs together overflow the clock.");
            }

            Worker = worker;
            Holder = holder;
            Job = job;
            Start = start;
            TravelTicks = travelTicks;
            WorkTicks = workTicks;
            ReturnTicks = returnTicks;
            Origin = origin;
            Destination = destination;
            Completion = completion;
        }

        /// <summary>Whether this is a task at all, as opposed to <see cref="None"/>.</summary>
        public bool IsNone => Worker.IsNone;

        /// <summary>Who is doing it.</summary>
        public PersonHandle Worker { get; }

        /// <summary>Whose ledger the output goes to: the worker's band.</summary>
        public EntityId Holder { get; }

        /// <summary>The job this is one run of; <see cref="JobTable.Recipe"/> names the recipe.</summary>
        public JobKind Job { get; }

        /// <summary>When the worker set out.</summary>
        public SimulationTime Start { get; }

        /// <summary>Ticks from origin to destination.</summary>
        public long TravelTicks { get; }

        /// <summary>Ticks at the destination: the recipe's duration.</summary>
        public long WorkTicks { get; }

        /// <summary>Ticks from destination back to origin.</summary>
        public long ReturnTicks { get; }

        /// <summary>Where the worker set out from and returns to.</summary>
        public WorldPosition Origin { get; }

        /// <summary>Where the work is done.</summary>
        public WorldPosition Destination { get; }

        /// <summary>The <see cref="ScheduledEventKind.TaskCompleted"/> booked for <see cref="End"/>.</summary>
        public EventId Completion { get; }

        /// <summary>When the worker is back with the output.</summary>
        public SimulationTime End => Start.Plus(TravelTicks + WorkTicks + ReturnTicks);

        /// <summary>
        /// Which leg the task is on at an instant. The end is the last
        /// instant of the return leg, since the completion that fires there
        /// finds the worker just home; anything before the start is None.
        /// </summary>
        public TaskPhase PhaseAt(SimulationTime now)
        {
            var elapsed = Start.TicksUntil(now);

            if (elapsed < 0L)
            {
                return TaskPhase.None;
            }

            if (elapsed < TravelTicks)
            {
                return TaskPhase.Outbound;
            }

            if (elapsed < TravelTicks + WorkTicks)
            {
                return TaskPhase.Working;
            }

            return elapsed <= TravelTicks + WorkTicks + ReturnTicks ? TaskPhase.Returning : TaskPhase.None;
        }

        public override string ToString() =>
            IsNone ? "no task" : Job + " by " + Worker + " from " + Start + " to " + End;
    }
}

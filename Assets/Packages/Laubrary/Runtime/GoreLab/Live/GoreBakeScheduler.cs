namespace Laubrary.GoreLab
{
    /// <summary>One view (a drawn frame, possibly mirrored) that needs its wounds baked into its picture.</summary>
    public sealed class GoreBakeJob
    {
        public GoreBody body;
        public GoreViewSlot slot;

        public void Run() { body.BakeNow(slot); }
    }

    /// <summary>
    /// Decides WHEN a view is baked. Today every request runs at once. A queue that spreads the work over spare time, or a Burst version of the
    /// bake, plugs in here without GoreBody changing.
    /// </summary>
    public interface IGoreBakeScheduler
    {
        void Request(GoreBakeJob job);
    }

    public sealed class ImmediateGoreScheduler : IGoreBakeScheduler
    {
        public void Request(GoreBakeJob job) { job.Run(); }
    }
}

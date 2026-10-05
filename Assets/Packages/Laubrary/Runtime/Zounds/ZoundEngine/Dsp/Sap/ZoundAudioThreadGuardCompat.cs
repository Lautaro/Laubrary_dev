namespace Laubrary.Zounds.Dsp {
    /// <summary>Compatibility access to the shared counters; there is only one counter pair.</summary>
    public static class ZoundAudioThreadGuard {
        public static readonly Unity.Burst.SharedStatic<long> blocks = Laubrary.Audio.AudioThreadGuard.blocks;
        public static readonly Unity.Burst.SharedStatic<long> managedBlocks = Laubrary.Audio.AudioThreadGuard.managedBlocks;
        public static long Blocks => Laubrary.Audio.AudioThreadGuard.Blocks;
        public static long ManagedBlocks => Laubrary.Audio.AudioThreadGuard.ManagedBlocks;
        public static void CountBlock() => Laubrary.Audio.AudioThreadGuard.CountBlock();
    }
}

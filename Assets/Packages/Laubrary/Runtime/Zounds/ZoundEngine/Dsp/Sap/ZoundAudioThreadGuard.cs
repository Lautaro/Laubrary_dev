using Unity.Burst;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Answers "has any of this engine's real-time audio run as ordinary managed code?" by measurement, not by
    /// assumption (T-0448, the guard T-0406 asked for).
    ///
    /// **Why it matters.** The whole point of the Burst engine is that the audio mixer thread never runs managed code,
    /// because once it has, it stays attached to the scripting runtime for the rest of the process and every garbage
    /// collection anywhere can then freeze it — an audible stutter. A block rendered as managed code is exactly that
    /// failure, and nothing about the sound tells you it happened.
    ///
    /// **How it is measured.** Every real-time block counts itself. It also calls a method marked to be discarded by
    /// the Burst compiler: when the block really is compiled that call does not exist, and when it is running as managed
    /// code it does, and counts. So <see cref="ManagedBlocks"/> above nought is proof that the audio thread has run
    /// managed code in this process; nought after many <see cref="Blocks"/> is proof that this engine has not.
    ///
    /// Deliberately holds nothing but the two counters: code compiled by Burst reads them, so this type must stay free of
    /// anything Burst cannot compile.
    /// </summary>
    public static class ZoundAudioThreadGuard {
        private struct BlocksKey { }
        private struct ManagedKey { }

        /// <summary>Real-time blocks rendered since the process started (or the scripts last reloaded).</summary>
        public static readonly SharedStatic<long> blocks = SharedStatic<long>.GetOrCreate<BlocksKey>();
        /// <summary>Of those, blocks that ran as managed code rather than compiled code.</summary>
        public static readonly SharedStatic<long> managedBlocks = SharedStatic<long>.GetOrCreate<ManagedKey>();

        public static long Blocks => blocks.Data;
        public static long ManagedBlocks => managedBlocks.Data;

        /// <summary>Called once per real-time block by the generator. Compiled: one addition. Managed: two.</summary>
        public static void CountBlock() {
            blocks.Data++;
            CountIfManaged();
        }

        [BurstDiscard]
        private static void CountIfManaged() {
            managedBlocks.Data++;
        }
    }
}

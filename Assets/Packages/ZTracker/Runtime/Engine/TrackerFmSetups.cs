namespace Laubrary.ZTracker.Engine
{
    // The ten single-carrier FM setups from Documentation~/FM_SINGLE_CARRIER_SETUPS.png.
    // Operator slots: A = 0, B = 1, D = 2, C = 3 (the carrier). With these slots the
    // older "Stack" routing (0) is setup 4 and the older "Merge" routing (2) is setup 7, so
    // the editor shows them as those setups. They keep playing through the older code
    // (bit-identical to before) until a modulator is given feedback; then the compiler
    // plays them through the setup path, which matches to float rounding.
    // Everything here is plain integer code so the Burst renderer can call it.
    public static class TrackerFmSetups
    {
        // Stored in fmAlgorithm as Base + setup number (101..110). 0..7 are the older routings.
        public const int Base = 100, Count = 10, Carrier = 3, Modulators = 3;
        // Feedback at 100 % shifts a modulator's phase by up to a quarter cycle (pi/2 radians),
        // driven by the mean of its last two raw outputs. The two-sample mean keeps it from
        // hunting at high amounts; above about a third of a cycle the result turns to noise.
        public const float MaxFeedbackCycles = .25f;
        // Seconds for a changed feedback amount to settle on a playing note (no click).
        public const float FeedbackGlideSeconds = .005f;
        // Route bits (source > target).
        public const int AtoB = 1, AtoD = 2, BtoD = 4, AtoC = 8, BtoC = 16, DtoC = 32;

        public static int Routes(int setup)
        {
            switch (setup)
            {
                case 1: return AtoC;
                case 2: return AtoB | BtoC;
                case 3: return AtoC | BtoC;
                case 4: return AtoB | BtoD | DtoC;
                case 5: return AtoC | BtoC | DtoC;
                case 6: return AtoB | BtoC | DtoC;
                case 7: return AtoD | BtoD | DtoC;
                case 8: return AtoB | AtoD | BtoC | DtoC;
                case 9: return AtoB | BtoC | AtoC;
                case 10: return AtoB | BtoD | DtoC | AtoC;
                default: return 0;
            }
        }

        // Setup number (1..10) an authored fmAlgorithm is shown as, or 0 for an older
        // multi-carrier routing that keeps its own diagram and code path.
        public static int SetupOf(int algorithm)
        {
            if (algorithm > Base && algorithm <= Base + Count) return algorithm - Base;
            return algorithm == 0 ? 4 : algorithm == 2 ? 7 : 0;
        }

        public static bool Uses(int setup, int slot)
        {
            int r = Routes(setup); if (r == 0) return false;
            switch (slot)
            {
                case 0: return (r & (AtoB | AtoD | AtoC)) != 0;
                case 1: return (r & (BtoD | BtoC)) != 0;
                case 2: return (r & DtoC) != 0;
                case 3: return true;
                default: return false;
            }
        }

        public static char Letter(int slot) => "ABDC"[slot];

        // Directed edges as slot pairs (source, target), for drawing and checks.
        public static int[] Edges(int setup)
        {
            int r = Routes(setup); var list = new System.Collections.Generic.List<int>();
            void Add(int bit, int from, int to) { if ((r & bit) != 0) { list.Add(from); list.Add(to); } }
            Add(AtoB, 0, 1); Add(AtoD, 0, 2); Add(BtoD, 1, 2); Add(AtoC, 0, 3); Add(BtoC, 1, 3); Add(DtoC, 2, 3);
            return list.ToArray();
        }

        public static string Formula(int setup)
        {
            switch (setup)
            {
                case 1: return "A → C";
                case 2: return "A → B → C";
                case 3: return "A + B → C";
                case 4: return "A → B → D → C";
                case 5: return "A + B + D → C";
                case 6: return "A → B → C, D → C";
                case 7: return "A + B → D → C";
                case 8: return "A → B, A → D, B + D → C";
                case 9: return "A → B → C, A → C";
                case 10: return "A → B → D → C, A → C";
                default: return "";
            }
        }
    }
}

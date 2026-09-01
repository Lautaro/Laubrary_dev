using System;

class Main2
{
    public static void Main(string[] args)
    {
        string w = args.Length > 0 ? args[0] : "all";
        if (w == "all" || w == "A") V2.A_InverseAtEUpperBound();
        if (w == "all" || w == "B") V2.B_BreakpointDuplicate();
        if (w == "all" || w == "C") V2.C_TruncationParity();
        if (w == "all" || w == "D") V2.D_AbsoluteEpsilons();
        if (w == "TRUNC") V2Trunc.Run();
        if (w == "DRILL") V2Drill.Run();
        if (w == "H6") V2H6.Run();
        if (w == "LAST") V2Last.Run();
        if (w == "F7") V2F7.Run();
        if (w == "GRAD") V2Grad.Run();
        if (w == "CLAMP") V2Clamp.Run();
        if (w == "LIN") V2LinAttack.Run();
        if (w.StartsWith("MARCH")) { V2March.SEL = w.Length > 5 ? w.Substring(5) : "all"; V2March.Run(); }
        if (w == "CANC") V2Cancel.Run();
        if (w == "WIRE") V2Wire.Run();
        if (w == "ALLOC") V2Alloc.Run();
        if (w == "FIX") V2Fix.Run();
    }
}

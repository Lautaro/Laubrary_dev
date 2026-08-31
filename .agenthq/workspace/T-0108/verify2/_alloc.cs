string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/alloc.txt";
var sb = new System.Text.StringBuilder();
var mi = typeof(System.GC).GetMethod("GetAllocatedBytesForCurrentThread",
          System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
sb.AppendLine("GetAllocatedBytesForCurrentThread present: " + (mi != null));
System.Func<long> probe = mi != null ? (System.Func<long>)System.Delegate.CreateDelegate(typeof(System.Func<long>), mi)
                                     : (System.Func<long>)(() => System.GC.GetTotalMemory(false));
sb.AppendLine("raw probe value sampled 3x: " + probe() + ", " + probe() + ", " + probe());

object sink = null;
// 1. single contiguous allocations, escalating well past 4 MB
long[] sizes = { 1024L, 65536L, 524288L, 4L*1024*1024, 8L*1024*1024, 16L*1024*1024, 64L*1024*1024 };
foreach (long s in sizes) {
  long b0 = probe(); sink = new byte[s]; long b1 = probe();
  sb.AppendLine("  single alloc " + (s/1024) + " KB -> thread-probe delta = " + (b1-b0));
}
// 2. the SHAPE the stated mutation actually has: 65 separate 120 000 B allocations = 7.8 MB
{
  long b0 = probe(); long h0 = System.GC.GetTotalMemory(false);
  for (int r=0;r<65;r++) sink = new byte[120000];
  long b1 = probe(); long h1 = System.GC.GetTotalMemory(false);
  sb.AppendLine("  65 x 120000 B (= 7.8 MB, the STATED MUTATION's shape) -> thread delta = " + (b1-b0) + ", heap delta = " + (h1-h0));
}
// 3. what the leg would MISS: smaller per-rep allocations, judged by the leg's own pass condition
long[] perRep = { 128L, 1024L, 8192L, 65536L, 120000L };
foreach (long pr in perRep) {
  long b0 = probe(); long h0 = System.GC.GetTotalMemory(false);
  for (int r=0;r<65;r++) sink = new byte[pr];
  long b1 = probe(); long h1 = System.GC.GetTotalMemory(false);
  long td = b1-b0, hd = h1-h0;
  bool wouldPass = (td == 0) && (hd < 524288);
  sb.AppendLine("  65 x " + pr + " B (total " + (65*pr) + ") -> thread " + td + ", heap " + hd
                + "  => LT-2's own pass condition says " + (wouldPass ? "PASS (MISSED)" : "FAIL (caught)"));
}
sb.AppendLine("sink kept alive: " + (sink != null));
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";

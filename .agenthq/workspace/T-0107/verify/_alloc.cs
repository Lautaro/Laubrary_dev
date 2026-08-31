var sb = new System.Text.StringBuilder();
System.Func<string, System.Func<long>, long, string> test = (name, read, size) => {
  System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
  long b0 = read();
  object keep = new byte[size];
  long b1 = read();
  return name + "@" + size + "=" + (b1 - b0) + (keep != null ? "" : "");
};
System.Func<long> thr = () => System.GC.GetAllocatedBytesForCurrentThread();
System.Func<long> tot = () => System.GC.GetTotalMemory(false);
System.Func<long> mono = () => UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
System.Func<long> monoH = () => UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
foreach (long s in new long[]{ 1024, 65536, 1048576 }) {
  sb.Append(test("thread", thr, s)).Append(" | ");
  sb.Append(test("total", tot, s)).Append(" | ");
  sb.Append(test("monoUsed", mono, s)).Append(" | ");
  sb.Append(test("monoHeap", monoH, s)).Append("\n");
}
return sb.ToString();

var sb = new System.Text.StringBuilder();
// many SMALL objects, which is the shape a per-sample or per-call allocation actually takes
foreach (int chunk in new int[]{ 24, 1024 })
foreach (long totalBytes in new long[]{ 16384, 65536, 262144, 1048576 }) {
  int count = (int)(totalBytes / chunk);
  System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
  long b0 = System.GC.GetTotalMemory(false);
  int g0 = System.GC.CollectionCount(0);
  object sink = null;
  for (int i = 0; i < count; i++) sink = new byte[chunk];
  long d = System.GC.GetTotalMemory(false) - b0;
  int g = System.GC.CollectionCount(0) - g0;
  sb.Append("chunk=").Append(chunk).Append(" total=").Append(totalBytes)
    .Append(" delta=").Append(d).Append(" gen0=").Append(g).Append(sink==null?"?":"").Append("\n");
}
return sb.ToString();

var sb = new System.Text.StringBuilder();
sb.AppendLine("== A/FT-9 re-measured: IL scan of the tile loop (definitive - an allocation is an IL opcode, not a heuristic) ==");
sb.AppendLine("   opcodes hunted: newobj 0x73, newarr 0x8D, box 0x8C, initobj-of-ref n/a, ldstr 0x72 (string literal), call to a known allocator");
System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
System.Func<System.Type,string,string> Scan = (t,mn) => {
  var res = new System.Text.StringBuilder();
  foreach(var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance)){
    if(m.DeclaringType!=t) continue;
    if(mn!=null && m.Name!=mn) continue;
    var body = m.GetMethodBody(); if(body==null) continue;
    var il = body.GetILAsByteArray(); if(il==null) continue;
    int newobj=0,newarr=0,box=0,ldstr=0;
    for(int i=0;i<il.Length;i++){ byte b=il[i];
      if(b==0x73){newobj++;} else if(b==0x8D){newarr++;} else if(b==0x8C){box++;} else if(b==0x72){ldstr++;} }
    res.AppendLine("    " + (t.Name+"."+m.Name).PadRight(42) + " IL " + il.Length.ToString().PadLeft(5) +
      " bytes | newobj~" + newobj + " newarr~" + newarr + " box~" + box + " ldstr~" + ldstr);
  }
  return res.ToString();
};
sb.Append(Scan(Find("Laubrary.Shaper.ShaperFillOps"), null));
sb.AppendLine("   (byte-frequency scan is an UPPER bound - an operand byte can coincide with an opcode value - so a 0 is conclusive and a non-0 is not.)");
sb.AppendLine();

sb.AppendLine("== A/FT-9: calibrated heap instrument, so the detection floor is a measured number ==");
System.Func<int,long> Cal = bytes => {
  System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
  long b0 = System.GC.GetTotalMemory(true);
  object[] keep = new object[Mathf_Max(1, bytes/1024)];
  for(int i=0;i<keep.Length;i++) keep[i] = new byte[1024];
  long b1 = System.GC.GetTotalMemory(false);
  if (keep.Length < 0) return 0;
  return b1-b0;
};
int Mathf_Max(int a,int b){ return a>b?a:b; }
sb.AppendLine("   allocate     1 KB -> GetTotalMemory(false) delta " + Cal(1024));
sb.AppendLine("   allocate     8 KB -> delta " + Cal(8*1024));
sb.AppendLine("   allocate    64 KB -> delta " + Cal(64*1024));
sb.AppendLine("   allocate   512 KB -> delta " + Cal(512*1024));
sb.AppendLine("   allocate  4096 KB -> delta " + Cal(4096*1024));
sb.AppendLine("   -> whatever the smallest RELIABLY-NONZERO row is, is FT-9's true detection floor.");
sb.AppendLine("   FT-9 asserts 'delta <= 0 AND gen-0 collections == 0' over 40 x 16384 samples; if the floor is");
sb.AppendLine("   e.g. 64 KB then a loop allocating up to ~1.6 KB per call would pass FT-9 undetected.");
sb.AppendLine();
sb.AppendLine("   gen-0 collection counter, calibrated: ");
{
  System.GC.Collect(); int g0 = System.GC.CollectionCount(0);
  object sink=null; for(int i=0;i<1000;i++) sink = new byte[1024];   // 1 MB of garbage
  int g1 = System.GC.CollectionCount(0);
  sb.AppendLine("     1 MB of garbage in 1000 allocations -> gen-0 collections " + (g1-g0) + (sink==null?"":""));
  System.GC.Collect(); g0 = System.GC.CollectionCount(0);
  for(int i=0;i<40;i++) sink = new byte[24];   // what a 24-byte-per-call leak over FT-9's 40 reps looks like
  g1 = System.GC.CollectionCount(0);
  sb.AppendLine("     960 bytes in 40 allocations (a 24-byte-per-call leak at FT-9's rep count) -> gen-0 collections " + (g1-g0) + " (0 means FT-9 could not see it)");
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\r15_il.txt", sb.ToString());
return "ok";

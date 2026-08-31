import io
p = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs"
s = io.open(p, encoding="utf-8").read()

start = s.index("                var types = new[]\n                {\n                    typeof(ShaperFillResolver)")
end_marker = '''                sb.AppendLine("    excluded by name, because they allocate BY DESIGN at compile time: " +
                              string.Join(", ", excluded.ToArray()));'''
end = s.index(end_marker) + len(end_marker)

new = '''                // The instrument is a TRANSITIVE CLOSURE over the call graph, seeded at PaintTile, and not a
                // sweep of a hand-written type list. The list the shipped leg used happened to cover the real
                // closure on this build and named no owner for keeping it that way; a per-sample helper added to
                // any other type would have been invisible to it while it went on reporting zero. Nothing is
                // excluded by name any more either - the compile-time entries (Resolve, Walk, Bind, ...) simply
                // are not reachable from PaintTile, which is the honest reason to leave them out rather than a
                // list a reader has to take on trust.
                int hits = ClosureAllocations(
                    typeof(ShaperFillResolver).GetMethod("PaintTile"),
                    out int visited, out string offenders, out string reached);

                bool ilOk = hits == 0 && visited > 15;
                ok &= ilOk;
                sb.AppendLine("  IL decode of the TRANSITIVE call graph from PaintTile: " + visited +
                              " methods reached, " + hits + " heap allocations found (expected 0)  " +
                              Verdict(ilOk));
                if (hits > 0) sb.AppendLine("    offenders: " + offenders);
                sb.AppendLine("    types reached: " + reached);'''

s = s[:start] + new + s[end:]
io.open(p, "w", encoding="utf-8").write(s)
print("bt14 rewritten")

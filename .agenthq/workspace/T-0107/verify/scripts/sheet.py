import io
p = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs"
s = io.open(p, encoding="utf-8").read()

s = s.replace("            const int Cell = 96, Cols = 7, Pad = 6;",
              "            const int Cell = 96, Cols = 8, Pad = 6;", 1)

old = '''            // 14 - the seam probe. See the method summary.
            int seamCell = cells.Count;
            Add("14 Join seam probe", Bordered(28f, ShaperShellAlignment.Outward, 8f, true));
'''.replace("14 - the seam", "14 \u2014 the seam")
assert old in s, "cell 14 block not found"

new = old + '''
            // 15-16 - BD-3.6 for a border whose node bound NO FILL OF ITS OWN, and the same picture with the
            //         node owning one. The magenta rim belongs to A, which is BELOW red B in fold order, so
            //         WHERE THE RIM CROSSES B THE RED MUST BE ON TOP - in BOTH cells. They looked DIFFERENT
            //         before this was fixed: with no fill on A the rim had no accumulator of its own, landed in
            //         the bag's, and drew over B. Two cells and not one, because the defect was an asymmetry
            //         between them and a single picture cannot show an asymmetry.
            foreach (bool aOwnsFill in new[] { false, true })
            {
                var a = Disc("A", 26f, -9f, 0f);
                a.border = Border(ShaperShellAlignment.Inward, 6f, false, Solid(Color.magenta));
                if (aOwnsFill) a.fill = Solid(new Color(0.12f, 0.42f, 0.16f));
                var bMember = Disc("B", 26f, 11f, 0f);
                bMember.fill = Solid(new Color(0.85f, 0.16f, 0.16f));
                var bag = ShaperNode.Bag("Pair", ShaperCombineMode.Add, a, bMember);
                bag.fill = Solid(body);
                Add(aOwnsFill ? "16 ...A owns a fill" : "15 Hostless rim under B", bag);
            }
'''
s = s.replace(old, new, 1)

legend_old = '''            sb.AppendLine("  Cell 14 marks in MAGENTA every interior sample of the dilated silhouette whose published");
            sb.AppendLine("  coverage fell below 0.999. Marks in this render: " + seamMarks + " (expected 0).");'''
assert legend_old in s
legend_new = legend_old + '''
            sb.AppendLine("  Cells 15 and 16 must MATCH wherever the magenta rim crosses the red disc: the rim belongs");
            sb.AppendLine("  to the LEFT member, which is below the red one in fold order, so the red must be on top in");
            sb.AppendLine("  both. They differ only in whether the left member also owns a fill of its own - a dial with");
            sb.AppendLine("  nothing to do with z-order. Magenta over the red disc in 15 is the BD-3.6 failure.");'''
s = s.replace(legend_old, legend_new, 1)

io.open(p, "w", encoding="utf-8").write(s)
print("sheet updated")

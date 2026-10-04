using System;
using System.IO;
using Laubrary.GoreLab.Tests;
static class P
{
    static int Main(string[] a)
    {
        string json = File.ReadAllText(a[0]);
        int fails = 0;
        foreach (RecipeEmbedding emb in new[] { RecipeEmbedding.Canvas, RecipeEmbedding.SpriteLocal })
        {
            Console.WriteLine("== " + emb);
            foreach (var r in GoreRecipeGoldenChecker.RunAll(json, emb)) { Console.WriteLine(r); if (r.status == "FAIL") fails++; }
        }
        int ep = 0, ef = 0;
        foreach (var r in GoreGoldenChecker.RunAll(json)) { if (r.status == "PASS") ep++; else if (r.status == "FAIL") { ef++; Console.WriteLine(r); } }
        Console.WriteLine($"engine cases pass={ep} fail={ef}");
        return fails;
    }
}

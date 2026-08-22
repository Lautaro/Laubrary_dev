// LatheVoxelMesher — turns an arbitrary signed-distance function into a real triangle mesh via Surface
// Nets: one vertex per grid cell that straddles the surface (positioned at the average of its sign-changing
// edge crossings), quads stitched between the 4 cells sharing a sign-changing grid edge.
//
// Chosen over classic Marching Cubes specifically to avoid its 256-entry triangulation table — a large
// hand-transcribed lookup table is exactly the kind of thing one wrong entry silently corrupts, with no
// obvious symptom short of a hole or a bowtie somewhere in the mesh. Surface Nets needs no such table and
// is just as robust (any SDF, always watertight, no cavities) for what Molded Shapes needs. The trade-off is
// fidelity: output is grid-resolution-limited — edges come out slightly rounded/faceted at coarse settings
// rather than Marching Cubes' cleaner per-cell triangulation — acceptable for a tool whose output gets
// pixelated into a sprite either way.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    public static class LatheVoxelMesher
    {
        public static Mesh Mesh(Func<Vector3, float> sdf, int resolution, float boundsSize)
        {
            int n = Mathf.Clamp(resolution, 4, 96);
            float half = boundsSize * 0.5f;
            float cell = boundsSize / n;
            int cn = n + 1;

            var samples = new float[cn, cn, cn];
            for (int z = 0; z < cn; z++)
                for (int y = 0; y < cn; y++)
                    for (int x = 0; x < cn; x++)
                        samples[x, y, z] = sdf(new Vector3(-half + x * cell, -half + y * cell, -half + z * cell));

            var vertIndex = new int[n, n, n];
            for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++) vertIndex[x, y, z] = -1;

            var data = new LatheMeshData();

            // One Surface Nets vertex per active cell (any of its 12 edges crosses zero), placed at the
            // average of those crossing points — a good approximation of the true surface position.
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        Vector3 sum = Vector3.zero;
                        int crossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            var (ax, ay, az, bx, by, bz) = CellEdges[e];
                            float sa = samples[x + ax, y + ay, z + az];
                            float sb = samples[x + bx, y + by, z + bz];
                            if ((sa < 0f) == (sb < 0f)) continue;
                            float t = sa / (sa - sb);
                            sum += Vector3.Lerp(new Vector3(ax, ay, az), new Vector3(bx, by, bz), t);
                            crossings++;
                        }
                        if (crossings == 0) continue;
                        Vector3 local = sum / crossings;
                        Vector3 world = new Vector3(-half + (x + local.x) * cell, -half + (y + local.y) * cell, -half + (z + local.z) * cell);
                        vertIndex[x, y, z] = data.AddVert(world, Vector3.up);   // placeholder normal — recalculated below
                    }

            bool CellValid(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < n && y < n && z < n;

            void EmitQuad(int x0, int y0, int z0, int x1, int y1, int z1, int x2, int y2, int z2, int x3, int y3, int z3, bool flip)
            {
                if (!CellValid(x0, y0, z0) || !CellValid(x1, y1, z1) || !CellValid(x2, y2, z2) || !CellValid(x3, y3, z3)) return;
                int v0 = vertIndex[x0, y0, z0], v1 = vertIndex[x1, y1, z1], v2 = vertIndex[x2, y2, z2], v3 = vertIndex[x3, y3, z3];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;
                if (flip) data.AddQuad(v0, v1, v2, v3);
                else data.AddQuad(v0, v3, v2, v1);
            }

            // For every grid EDGE with a sign change, connect the (up to) 4 cells that share it into a quad.
            for (int z = 0; z <= n; z++)
                for (int y = 0; y <= n; y++)
                    for (int x = 0; x < n; x++)   // X-edges: corner(x,y,z)-(x+1,y,z)
                    {
                        float sa = samples[x, y, z], sb = samples[x + 1, y, z];
                        if ((sa < 0f) == (sb < 0f)) continue;
                        EmitQuad(x, y - 1, z - 1, x, y, z - 1, x, y, z, x, y - 1, z, sa < 0f);
                    }
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                    for (int y = 0; y < n; y++)   // Y-edges: corner(x,y,z)-(x,y+1,z)
                    {
                        float sa = samples[x, y, z], sb = samples[x, y + 1, z];
                        if ((sa < 0f) == (sb < 0f)) continue;
                        EmitQuad(x - 1, y, z - 1, x, y, z - 1, x, y, z, x - 1, y, z, sa < 0f);
                    }
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                    for (int z = 0; z < n; z++)   // Z-edges: corner(x,y,z)-(x,y,z+1)
                    {
                        float sa = samples[x, y, z], sb = samples[x, y, z + 1];
                        if ((sa < 0f) == (sb < 0f)) continue;
                        EmitQuad(x - 1, y - 1, z, x, y - 1, z, x, y, z, x - 1, y, z, sa < 0f);
                    }

            // Smooth-blending near a SHARP corner (a Box's, in particular) can leave a handful of grid cells
            // numerically isolated — a tiny disconnected shard floating near the corner, confirmed live: a
            // box with one smooth-subtracted sphere near its corner produced 8 stray islands of 1-4 cells
            // each alongside the real 781-cell body. Not a formula error (hand-verified against the same
            // case: the field itself is correct), a resolution/aliasing artifact of a sharp corner meeting a
            // smooth blend at finite grid spacing. Standard fix used by voxel/CSG tools generally: discard
            // any connected component far smaller than the main body, on the finished mesh's own triangle
            // adjacency (not the SDF grid) so it works regardless of the artifact's exact cause.
            var cleaned = RemoveSmallIslands(data, 0.05f);
            cleaned.normals.Clear();
            return cleaned.verts.Count > 0 && cleaned.tris.Count > 0 ? cleaned.ToMesh("Molded Shape") : null;
        }

        static LatheMeshData RemoveSmallIslands(LatheMeshData data, float minFractionOfLargest)
        {
            int vc = data.verts.Count;
            if (vc == 0) return data;
            var adjacency = new List<int>[vc];
            for (int i = 0; i < vc; i++) adjacency[i] = new List<int>();
            for (int t = 0; t < data.tris.Count; t += 3)
            {
                int a = data.tris[t], b = data.tris[t + 1], c = data.tris[t + 2];
                adjacency[a].Add(b); adjacency[a].Add(c);
                adjacency[b].Add(a); adjacency[b].Add(c);
                adjacency[c].Add(a); adjacency[c].Add(b);
            }

            var compId = new int[vc];
            for (int i = 0; i < vc; i++) compId[i] = -1;
            var compCount = new List<int>();
            var stack = new Stack<int>();
            for (int start = 0; start < vc; start++)
            {
                if (compId[start] != -1) continue;
                int id = compCount.Count;
                compCount.Add(0);
                compId[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int v = stack.Pop();
                    foreach (var nb in adjacency[v])
                        if (compId[nb] == -1) { compId[nb] = id; stack.Push(nb); }
                }
            }
            for (int t = 0; t < data.tris.Count; t += 3) compCount[compId[data.tris[t]]]++;

            int largest = 0;
            foreach (var c in compCount) largest = Mathf.Max(largest, c);
            int minTris = Mathf.Max(1, Mathf.CeilToInt(largest * minFractionOfLargest));

            var kept = new LatheMeshData { uvScale = data.uvScale };
            var remap = new int[vc];
            for (int i = 0; i < vc; i++) remap[i] = -1;
            for (int t = 0; t < data.tris.Count; t += 3)
            {
                int a = data.tris[t], b = data.tris[t + 1], c = data.tris[t + 2];
                if (compCount[compId[a]] < minTris) continue;
                if (remap[a] < 0) remap[a] = kept.AddVert(data.verts[a], a < data.normals.Count ? data.normals[a] : Vector3.up);
                if (remap[b] < 0) remap[b] = kept.AddVert(data.verts[b], b < data.normals.Count ? data.normals[b] : Vector3.up);
                if (remap[c] < 0) remap[c] = kept.AddVert(data.verts[c], c < data.normals.Count ? data.normals[c] : Vector3.up);
                kept.AddTri(remap[a], remap[b], remap[c]);
            }
            return kept;
        }

        // The 12 edges of a unit cell, as (cornerA offset, cornerB offset) pairs.
        static readonly (int, int, int, int, int, int)[] CellEdges =
        {
            (0,0,0, 1,0,0), (0,0,1, 1,0,1), (0,1,0, 1,1,0), (0,1,1, 1,1,1),   // X-direction
            (0,0,0, 0,1,0), (1,0,0, 1,1,0), (0,0,1, 0,1,1), (1,0,1, 1,1,1),   // Y-direction
            (0,0,0, 0,0,1), (1,0,0, 1,0,1), (0,1,0, 0,1,1), (1,1,0, 1,1,1),   // Z-direction
        };
    }
}

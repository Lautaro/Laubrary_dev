// LatheSpec — the asset behind Lathe (see D:\Unity\Laubrary Dev CLAUDE.md's naming rule: a cool name
// earned by an authoring UI, LatheWindow). Turns a profile/primitive into a solid, several of which can
// share one 3D scene. R&D tool, deliberately separate from PyrePlus.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    public class LatheSpec : ScriptableObject
    {
        // ── forward-compat with an eventual sprite bake — not read by anything yet, kept here so the
        // Canvas dial group already has a home when that lands. ─────────────────────────────────────
        [Min(1)] public int canvasSize = 64;
        public float pixelsPerUnit = 16f;
        public int seed = 1234;

        // ── turntable "frames" — the transport/scrub concept PyrePlus's preview uses, reused here to
        // spin the whole solid assembly around world Y for inspection (and eventually a multi-angle sprite
        // export), NOT to re-bake per-frame geometry — a Lathe solid's shape is static per module dial set.
        [Min(1)] public int turntableFrames = 24;
        public float previewFps = 12f;
        [HideInInspector] public int previewFrame = 0;

        public Color previewBackground = new Color(0.16f, 0.16f, 0.18f, 1f);

        [HideInInspector] public int previewSolidSel = 0;

        public List<LatheSolid> solids = new List<LatheSolid> { new LatheSolid { name = "Solid 1" } };
    }
}

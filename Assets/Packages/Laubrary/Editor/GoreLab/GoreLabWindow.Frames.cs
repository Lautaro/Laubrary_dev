// The frames the rig can tag, grouped by walking direction, and the strip that picks one.
//
// Where frames come from: the rig's target (a Zoe's Launimator view, or a bare LauminaryVersion). Each member of a
// direction set becomes a group (Front, Front-side, ...). When the set mirrors built in (Launimator serves the left
// half by flipping the right half), the derived directions are listed too, READ-ONLY: the rig stores only drawn
// sprites, and a mirrored view shows its drawn partner's pixels and tags flipped. Animations outside every set get a
// group each; a target without a version falls back to the rig's own sprite list; tags whose sprite is no longer in
// the target stay reachable under "Other tagged" rather than becoming invisible data.
using System;
using System.Collections.Generic;
using Laubrary.Launimator;
using Laubrary.Zui;
using Laubrary.ZoetropeLaunimator;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    internal sealed class FrameGroup
    {
        public string label;
        public float angle = float.NaN;    // Launimator direction (0 = back, 180 = front), NaN when not a direction
        public bool mirrored;
        public FrameGroup source;          // the drawn direction a mirrored one is derived from
        public readonly List<Sprite> sprites = new List<Sprite>();
    }

    internal sealed class ShownFrame
    {
        public FrameGroup group;
        public int index;                  // position in the group
        public Sprite sprite;
        public bool mirrored;
        public GoreSpritePixels pixels;
        public GoreFrameTags tags;         // the drawn sprite's tags (for a mirrored view: its partner's, unmirrored)
        public int sx, sy;                 // where the sprite sits on the stage canvas
        public int W => pixels != null ? pixels.W : 0;
        public int H => pixels != null ? pixels.H : 0;
        public string Label => group.label + " " + (index + 1);
    }

    public partial class GoreLabWindow
    {
        readonly List<FrameGroup> groups = new List<FrameGroup>();
        internal Vector2Int canvas;
        internal ShownFrame shown;
        readonly Dictionary<(Sprite, bool), GoreSpritePixels> pixelCache = new Dictionary<(Sprite, bool), GoreSpritePixels>();

        // ── the frame list ──────────────────────────────────────────────────────────────────────────

        static LauminaryVersion TargetVersion(GoreRig rig)
        {
            if (rig == null) return null;
            if (rig.zoe != null)
            {
                if (rig.zoe.view is ZonedLauminaryView zoned && zoned.version != null) return zoned.version;
                if (rig.zoe.view is LauminaryView plain && plain.version != null) return plain.version;
            }
            return rig.reel;
        }

        void BuildFrameModel()
        {
            groups.Clear();
            var rig = Rig;
            if (rig == null) return;
            var seen = new HashSet<Sprite>();
            var version = TargetVersion(rig);

            if (version != null)
            {
                var inSets = new HashSet<string>();
                var mirrors = new List<FrameGroup>();
                if (version.sets != null)
                    foreach (var set in version.sets)
                    {
                        if (set?.members == null || set.members.Count == 0) continue;
                        var ordered = new List<LauminationSetMember>(set.members);
                        ordered.RemoveAll(m => m == null);
                        ordered.Sort((a, b) => Math.Abs(a.angleDegrees - 180f).CompareTo(Math.Abs(b.angleDegrees - 180f)));
                        bool anyAbove = ordered.Exists(m => m.angleDegrees > 180.01f);
                        var authored = new List<FrameGroup>();
                        foreach (var m in ordered)
                        {
                            if (m.laumination?.frames == null) continue;
                            inSets.Add(m.laumination.name);
                            var g = new FrameGroup { label = DirectionName(m.angleDegrees), angle = m.angleDegrees };
                            foreach (var s in m.laumination.frames) if (s != null && seen.Add(s)) g.sprites.Add(s);
                            if (g.sprites.Count == 0) continue;
                            groups.Add(g);
                            authored.Add(g);
                        }
                        // Launimator mirrors only when every authored member faces the right half (0..180).
                        if (set.mirrorBuiltIn && !anyAbove)
                            foreach (var g in authored)
                            {
                                if (g.angle <= 0.5f || g.angle >= 179.5f) continue;
                                var mg = new FrameGroup { label = DirectionName(360f - g.angle) + " mirror", angle = 360f - g.angle, mirrored = true, source = g };
                                mg.sprites.AddRange(g.sprites);
                                mirrors.Add(mg);
                            }
                    }
                mirrors.Sort((a, b) => a.angle.CompareTo(b.angle));
                groups.AddRange(mirrors);

                if (version.animations != null)
                    foreach (var anim in version.animations)
                    {
                        if (anim?.frames == null || inSets.Contains(anim.name)) continue;
                        var g = new FrameGroup { label = string.IsNullOrEmpty(anim.name) ? "Animation" : anim.name };
                        foreach (var s in anim.frames) if (s != null && seen.Add(s)) g.sprites.Add(s);
                        if (g.sprites.Count > 0) groups.Add(g);
                    }
            }
            else
            {
                var g = new FrameGroup { label = "Frames" };
                foreach (var s in rig.EnumerateTargetSprites()) if (s != null && seen.Add(s)) g.sprites.Add(s);
                if (g.sprites.Count > 0) groups.Add(g);
            }

            var orphans = new FrameGroup { label = "Other tagged" };
            if (rig.frames != null)
                foreach (var f in rig.frames)
                    if (f != null && f.sprite != null && seen.Add(f.sprite)) orphans.sprites.Add(f.sprite);
            if (orphans.sprites.Count > 0) groups.Add(orphans);

            canvas = Vector2Int.zero;
            foreach (var g in groups)
                foreach (var s in g.sprites)
                {
                    var r = s.rect;
                    canvas.x = Mathf.Max(canvas.x, Mathf.RoundToInt(r.width));
                    canvas.y = Mathf.Max(canvas.y, Mathf.RoundToInt(r.height));
                }
        }

        /// Spec 11.3's direction names, from a Launimator angle (0 = back, clockwise, 180 = front).
        static string DirectionName(float angle)
        {
            float a = ((angle % 360f) + 360f) % 360f;
            string Near(float target, string name) => Mathf.Abs(Mathf.DeltaAngle(a, target)) < 1f ? name : null;
            return Near(180f, "Front") ?? Near(135f, "Front-side") ?? Near(225f, "Front-side") ?? Near(90f, "Side")
                ?? Near(270f, "Side") ?? Near(45f, "Back-side") ?? Near(315f, "Back-side") ?? Near(0f, "Back") ?? $"{a:0}°";
        }

        // ── the shown frame ─────────────────────────────────────────────────────────────────────────

        internal bool HasFrames => groups.Count > 0;

        void ResolveShown()
        {
            shown = null;
            if (groups.Count == 0) return;
            FrameGroup group = null;
            int index = -1;
            foreach (var g in groups)
            {
                if (g.mirrored != selMirrored) continue;
                int i = g.sprites.IndexOf(selSprite);
                if (i >= 0) { group = g; index = i; break; }
            }
            if (group == null) { group = groups[0]; index = 0; }
            selSprite = group.sprites[index];
            selMirrored = group.mirrored;

            var px = Pixels(selSprite, selMirrored);
            if (px == null) return;
            shown = new ShownFrame
            {
                group = group, index = index, sprite = selSprite, mirrored = selMirrored, pixels = px,
                tags = FindFrame(selSprite),
                sx = Mathf.FloorToInt((canvas.x - px.W) * 0.5f),
                sy = canvas.y - px.H,
            };
        }

        GoreSpritePixels Pixels(Sprite s, bool mirrored)
        {
            if (s == null) return null;
            if (pixelCache.TryGetValue((s, mirrored), out var p) && p != null && p.texture != null) return p;
            p = GoreSpritePixels.Read(s, mirrored);
            if (p != null) pixelCache[(s, mirrored)] = p;
            return p;
        }

        void ClearPixelCache()
        {
            foreach (var p in pixelCache.Values) p?.Dispose();
            pixelCache.Clear();
        }

        internal void SelectFrame(FrameGroup g, int index)
        {
            if (g == null || g.sprites.Count == 0) return;
            index = Mathf.Clamp(index, 0, g.sprites.Count - 1);
            selSprite = g.sprites[index];
            selMirrored = g.mirrored;
            ResolveShown();
            pendingWound.Clear();
            if (tab == Tab.Test) RecutShown();
            AfterEdit();
        }

        internal void StepFrame(int delta)
        {
            if (shown == null) return;
            int n = shown.group.sprites.Count;
            SelectFrame(shown.group, ((shown.index + delta) % n + n) % n);
        }

        internal void StepDirection(int delta)
        {
            if (shown == null || groups.Count == 0) return;
            int gi = groups.IndexOf(shown.group);
            var g = groups[((gi + delta) % groups.Count + groups.Count) % groups.Count];
            SelectFrame(g, Mathf.Min(shown.index, g.sprites.Count - 1));
        }

        // ── the strip ───────────────────────────────────────────────────────────────────────────────

        const float ThumbW = 30f, ThumbH = 36f;

        /// A frame is "done" for the active member when it has a tag or is marked as not showing the member.
        bool FrameDone(Sprite s)
        {
            var mf = MemberAt(FindFrame(s), memberIndex, false);
            return mf != null && (mf.present || mf.skip);
        }

        void BuildStrip(VisualElement host)
        {
            if (Rig == null) return;
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.style.flexShrink = 0f;
            scroll.style.height = ThumbH + 26f;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            host.Add(scroll);

            if (groups.Count == 0)
            {
                scroll.Add(Z.Text("No frames: set the rig's target (Frame tab)", ZuiText.Subtle,
                    "The frames come from the rig's target: a Zoe with a Launimator view, or a bare animation version."));
                return;
            }

            string member = MemberName(memberIndex);
            foreach (var g in groups)
            {
                var col = new VisualElement();
                col.style.marginRight = 8f;
                col.style.flexShrink = 0f;

                int done = 0;
                foreach (var s in g.sprites) if (FrameDone(s)) done++;
                string badge = done == g.sprites.Count ? " ✔" : done > 0 ? " …" : "";
                var head = Z.Text(g.label + badge, ZuiText.Small,
                    (g.mirrored ? $"Mirror of {g.source?.label}: shown flipped from its drawn frames and read only (tag the drawn direction). "
                                : float.IsNaN(g.angle) ? "" : $"Direction at {g.angle:0}°. ")
                    + $"✔ = every frame has a {member} tag, … = some do.");
                col.Add(head);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                for (int i = 0; i < g.sprites.Count; i++)
                {
                    var s = g.sprites[i];
                    int idx = i;
                    var mf = MemberAt(FindFrame(s), memberIndex, false);
                    string state = mf == null ? $"no {member} tag" : mf.skip ? $"{member} marked not visible" : mf.present ? $"{member} tagged" : $"no {member} tag";
                    state += ". Dots at the bottom: one per body member (green tagged, grey not visible, red to do)";
                    var b = Z.Button("", $"{g.label} frame {i + 1}: {state}. Click to show it.", () => SelectFrame(g, idx));
                    b.style.width = ThumbW;
                    b.style.height = ThumbH;
                    b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 1f;
                    if (shown != null && shown.group == g && shown.index == i) b.AddToClassList("zui-radio__on");

                    var img = new Image { sprite = s, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    img.style.flexGrow = 1f;
                    if (g.mirrored) img.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                    b.Add(img);

                    // one dot per body member along the bottom: green = tagged, grey = marked not visible, red = still to do
                    var ft = FindFrame(s);
                    var dots = new VisualElement { pickingMode = PickingMode.Ignore };
                    dots.style.position = Position.Absolute;
                    dots.style.left = 2f; dots.style.right = 2f; dots.style.bottom = 1f;
                    dots.style.flexDirection = FlexDirection.Row;
                    dots.style.justifyContent = Justify.Center;
                    for (int m = 0; m < MemberCount; m++)
                    {
                        var md = MemberAt(ft, m, false);
                        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                        dot.style.width = 6f; dot.style.height = 6f; dot.style.marginLeft = dot.style.marginRight = 1f;
                        dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 3f;
                        dot.style.backgroundColor = md != null && md.present ? new Color(0.45f, 0.95f, 0.5f)
                            : md != null && md.skip ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.9f, 0.35f, 0.3f);
                        dots.Add(dot);
                    }
                    b.Add(dots);
                    if (shown != null && shown.group == g && shown.index == i)
                    {
                        var orange = new Color(1f, 0.65f, 0.2f);
                        b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = orange;
                        b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 2f;
                    }
                    row.Add(b);
                }
                col.Add(row);
                scroll.Add(col);
            }
        }
    }
}

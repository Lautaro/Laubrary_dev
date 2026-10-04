using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Laubrary.GoreLab.Editor
{
    // Fast tagging: a character has dozens of frames, so a shape drawn once can be carried to the following frames of its direction, and a first guess
    // can be put on every frame that has nothing yet. Both are starting points to be corrected by hand.
    public partial class GoreLabWindow
    {
        void ShowHint(string text) { ShowNotification(new GUIContent(text), 3.0); }

        /// <summary>
        /// Copies the active member (shape, orientation, and its paint layers) from the shown frame to the next frame, or to every later frame of this
        /// direction. Paint is carried pixel for pixel to where the target sprite is solid and inside the copied shape.
        /// </summary>
        void CopyMemberForward(bool wholeDirection)
        {
            if (!CanEditShown || ActiveMember == null) return;
            var srcTags = FindFrame(shown.sprite);
            var src = MemberAt(srcTags, memberIndex, false);
            if (src == null || !src.present) { ShowHint($"Draw the {MemberName(memberIndex).ToLowerInvariant()} on this frame first."); return; }

            var sprites = shown.group.sprites;
            int from = shown.index + 1;
            int to = wholeDirection ? sprites.Count - 1 : Math.Min(from, sprites.Count - 1);
            if (from > to) { ShowHint("This is the last frame of the direction."); return; }

            int srcW = shown.W;
            Edit(wholeDirection ? "Copy shape to the rest of the direction" : "Copy shape to the next frame", () =>
            {
                for (int i = from; i <= to; i++)
                {
                    var px = Pixels(sprites[i], false);
                    if (px == null) continue;
                    var ft = GetOrCreateFrame(sprites[i], shown.group.label + " " + (i + 1));
                    var dst = MemberAt(ft, memberIndex, true);
                    dst.present = true;
                    dst.skip = src.skip;
                    dst.tag = src.tag;
                    dst.behind = CarryMask(src.behind, srcW, px, src.tag);
                    dst.exempt = CarryMask(src.exempt, srcW, px, src.tag);
                }
            });
        }

        static int[] CarryMask(int[] source, int sourceWidth, GoreSpritePixels target, in MemberTag tag)
        {
            if (source == null || source.Length == 0 || sourceWidth <= 0) return new int[0];
            var kept = new List<int>(source.Length);
            for (int k = 0; k < source.Length; k++)
            {
                int x = source[k] % sourceWidth, y = source[k] / sourceWidth;
                if (x >= target.W || y >= target.H) continue;
                if (!target.grid.Solid(x, y) || !GoreTagMath.InsideOutline(tag, x + 0.5, y + 0.5)) continue;
                kept.Add(y * target.W + x);
            }
            return kept.ToArray();
        }

        /// <summary>
        /// Puts a rough first guess on every frame of the shown direction that has no tag for the active member. The head is guessed from the top of the
        /// sprite; the torso is a box below the head, so the head must be there first.
        /// </summary>
        void AutoTagDirection()
        {
            if (!CanEditShown || ActiveMember == null) return;
            var forward = DefaultForward();
            var sprites = shown.group.sprites;
            int made = 0;
            Edit("Auto-tag the direction", () =>
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    var existing = MemberAt(FindFrame(sprites[i]), memberIndex, false);
                    if (existing != null && existing.present) continue;
                    var px = Pixels(sprites[i], false);
                    if (px == null) continue;
                    MemberTag tag;
                    if (ActiveMember.kind == MemberKind.Ball)
                    {
                        if (!GuessHead(px.grid, forward, out tag)) continue;
                    }
                    else
                    {
                        var head = MemberAt(FindFrame(sprites[i]), 0, false);
                        if (head == null || !head.present) continue;
                        tag = GuessTorso(head.tag);
                    }
                    var ft = GetOrCreateFrame(sprites[i], shown.group.label + " " + (i + 1));
                    var mf = MemberAt(ft, memberIndex, true);
                    mf.present = true;
                    mf.tag = tag;
                    made++;
                }
            });
            ShowHint(made > 0
                ? $"Guessed {made} {MemberName(memberIndex).ToLowerInvariant()} shape{(made > 1 ? "s" : "")}. Check them and drag to correct."
                : ActiveMember.kind == MemberKind.Box ? "Nothing to guess: every frame has a torso, or has no head to guess from." : "Every frame of this direction already has a head.");
        }

        // The head is the top of the sprite: centred on the first rows, as tall as 11.5% of the sprite.
        static bool GuessHead(GoreGrid g, Vector3 forward, out MemberTag tag)
        {
            tag = default;
            int top = -1;
            for (int y = 0; y < g.h && top < 0; y++)
                for (int x = 0; x < g.w; x++)
                    if (g.Solid(x, y)) { top = y; break; }
            if (top < 0) return false;
            double sx = 0; int n = 0;
            for (int y = top; y < Math.Min(g.h, top + 4); y++)
                for (int x = 0; x < g.w; x++)
                    if (g.Solid(x, y)) { sx += x + 0.5; n++; }
            double ry = Math.Max(4.0, g.h * 0.115), rx = ry * 0.95;
            tag = GoreTagEdit.NewTag(MemberKind.Ball, sx / n, top + ry * 1.05, forward);
            tag.rx = rx; tag.ry = ry; tag.rz = rx;
            return true;
        }

        // A box below the head, a little wider than it is tall-ish, facing the way the head faces.
        static MemberTag GuessTorso(in MemberTag head)
        {
            double ux = head.ux, uy = head.uy, d = head.ry * 2.5;
            var t = head;
            t.kind = MemberKind.Box;
            t.cx = head.cx - ux * d; t.cy = head.cy - uy * d;
            t.rx = head.rx * 1.9; t.ry = head.ry * 1.9; t.rz = head.rx * 1.2;
            t.n = 3.5;
            return t;
        }
    }
}

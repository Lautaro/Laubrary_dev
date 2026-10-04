// Data access and undo for the GoreLab window. Every write to the rig goes through here: record the rig, mutate, mark
// it dirty. A drag opens a gesture so its many records collapse into ONE undo step.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.GoreLab.Editor
{
    public partial class GoreLabWindow
    {
        int gestureGroup = -1;

        // ── undo ──────────────────────────────────────────────────────────────────────────────────────

        /// Start a drag. Everything recorded until EndGesture becomes one undo step.
        internal void BeginGesture()
        {
            Undo.IncrementCurrentGroup();   // so the drag never merges with whatever the user did before it
            gestureGroup = Undo.GetCurrentGroup();
        }

        /// Record the rig before mutating it. Called before EVERY mutation of a drag (a record only captures the
        /// change made in the same editor update), collapsed by EndGesture.
        internal void Record(string what)
        {
            if (Rig == null) return;
            Undo.RecordObject(Rig, what);
            EditorUtility.SetDirty(Rig);
        }

        internal void EndGesture()
        {
            if (gestureGroup >= 0) Undo.CollapseUndoOperations(gestureGroup);
            gestureGroup = -1;
        }

        /// A one-shot edit (a button): one undo step, then the panes re-read the data.
        internal void Edit(string what, Action change)
        {
            if (Rig == null) return;
            Undo.RecordObject(Rig, what);
            change();
            EditorUtility.SetDirty(Rig);
            AfterEdit();
        }

        // ── members ───────────────────────────────────────────────────────────────────────────────────

        internal int MemberCount => Rig != null && Rig.members != null ? Rig.members.Count : 0;

        internal GoreMemberDef ActiveMember
        {
            get
            {
                if (MemberCount == 0) return null;
                memberIndex = Mathf.Clamp(memberIndex, 0, MemberCount - 1);
                return Rig.members[memberIndex];
            }
        }

        internal string MemberName(int mi)
        {
            var m = mi >= 0 && mi < MemberCount ? Rig.members[mi] : null;
            return m == null ? "Member" : string.IsNullOrEmpty(m.name) ? "Member " + (mi + 1) : m.name;
        }

        internal Color MemberColour(int mi)
        {
            var m = mi >= 0 && mi < MemberCount ? Rig.members[mi] : null;
            if (m == null) return Color.white;
            var c = m.editorColour;
            if (c.a <= 0f) c = m.kind == MemberKind.Box ? new Color(0.47f, 1f, 0.55f) : new Color(0.35f, 0.9f, 1f);
            c.a = 1f;
            return c;
        }

        // ── frames ────────────────────────────────────────────────────────────────────────────────────

        /// The rig's tags for a drawn sprite, or null. A linear scan on purpose: the rig's own lookup is cached and
        /// this window adds frames to the list while editing.
        internal GoreFrameTags FindFrame(Sprite s)
        {
            if (Rig == null || Rig.frames == null || s == null) return null;
            for (int i = 0; i < Rig.frames.Count; i++)
                if (Rig.frames[i] != null && Rig.frames[i].sprite == s) return Rig.frames[i];
            return null;
        }

        /// The tags for a drawn sprite, created on first edit. Callers have already recorded the rig.
        internal GoreFrameTags GetOrCreateFrame(Sprite s, string label)
        {
            var f = FindFrame(s);
            if (f != null) return f;
            f = new GoreFrameTags { sprite = s, label = label };
            f.EnsureMembers(MemberCount);
            Rig.frames.Add(f);
            Rig.InvalidateFrameCache();
            return f;
        }

        /// One member's data on a frame, the list grown to parallel the rig's members when `create` is set.
        internal static GoreMemberFrame MemberAt(GoreFrameTags f, int mi, bool create)
        {
            if (f == null || mi < 0) return null;
            if (f.members == null || mi >= f.members.Count)
            {
                if (!create) return null;
                f.EnsureMembers(mi + 1);
            }
            if (f.members[mi] == null)
            {
                if (!create) return null;
                f.members[mi] = new GoreMemberFrame();
            }
            return f.members[mi];
        }

        /// Whether the shown frame can be edited: mirrored directions are derived from their drawn partner.
        internal bool CanEditShown => shown != null && !shown.mirrored;

        /// The active member's authored data on the shown frame. With `create`, the frame entry is made (callers have
        /// recorded the rig). Null on a mirrored frame.
        internal GoreMemberFrame ActiveMemberFrame(bool create)
        {
            if (!CanEditShown || ActiveMember == null) return null;
            var f = create ? GetOrCreateFrame(shown.sprite, shown.Label) : FindFrame(shown.sprite);
            return MemberAt(f, memberIndex, create);
        }

        /// The active member's tag on the shown frame, if it has one.
        internal bool TryActiveTag(out MemberTag tag)
        {
            var mf = ActiveMemberFrame(false);
            tag = mf != null ? mf.tag : default;
            return mf != null && mf.present;
        }

        /// A member as it appears on the shown frame: for a mirrored direction its drawn partner's tag and masks,
        /// mirrored. Returns false when the frame has no tag for that member.
        internal bool TryShownMember(int mi, out MemberTag tag, out int[] behind, out int[] exempt, out bool skip)
        {
            tag = default; behind = exempt = Array.Empty<int>(); skip = false;
            if (shown == null) return false;
            var mf = MemberAt(shown.tags, mi, false);
            if (mf == null || !mf.present) return false;
            skip = mf.skip;
            if (!shown.mirrored)
            {
                tag = mf.tag;
                behind = mf.behind ?? Array.Empty<int>();
                exempt = mf.exempt ?? Array.Empty<int>();
                return true;
            }
            tag = GoreTagMath.Mirror(mf.tag, shown.W);
            behind = GoreTagEdit.MirrorMask(mf.behind, shown.W);
            exempt = GoreTagEdit.MirrorMask(mf.exempt, shown.W);
            return true;
        }

        /// Spec 11.4: the active member faces away from where this direction looks, or two members face opposite ways.
        internal string OrientationWarning()
        {
            if (shown == null || !TryShownMember(memberIndex, out var t, out _, out _, out _)) return null;
            string me = MemberName(memberIndex);
            if (!float.IsNaN(shown.group.angle) && GoreTagEdit.DotForward(t, GoreTagEdit.ExpectedForward(shown.group.angle)) < 0)
                return $"{me} faces away from where this direction looks (Rotate tab: Default)";
            for (int i = 0; i < MemberCount; i++)
            {
                if (i == memberIndex || !TryShownMember(i, out var o, out _, out _, out bool skip) || skip) continue;
                if (GoreTagEdit.Dot(t, o) < 0) return $"{me} and {MemberName(i)} face opposite ways";
            }
            return null;
        }

        /// The forward a new or reset tag gets on the shown frame.
        internal Vector3 DefaultForward()
            => shown != null && !float.IsNaN(shown.group.angle) ? GoreTagEdit.ExpectedForward(shown.group.angle) : new Vector3(0f, 0f, 1f);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>The authored data of one member on one drawn frame: its 3D tag and the two paint masks. Masks are sprite-local pixel indices (top-left origin, y down, index = y * width + x).</summary>
    [Serializable]
    public sealed class GoreMemberFrame
    {
        [Tooltip("This frame has a tag for the member. Without one the member cannot be cut on this frame.")]
        public bool present;
        [Tooltip("The member is tagged but deliberately not cut on this frame (for example the head is not visible).")]
        public bool skip;
        [Tooltip("Where the member is on the sprite and how it is shaped and turned.")]
        public MemberTag tag;
        [Tooltip("Pixels where the body is behind the member (a cut there leaves dark gore instead of see-through).")]
        public int[] behind = Array.Empty<int>();
        [Tooltip("Pixels drawn in front of the member (an arm over the torso). They are never cut.")]
        public int[] exempt = Array.Empty<int>();
    }

    /// <summary>All member data for one drawn sprite. 'members' runs parallel to the rig's member list.</summary>
    [Serializable]
    public sealed class GoreFrameTags
    {
        [Tooltip("The drawn sprite these tags belong to.")]
        public Sprite sprite;
        [Tooltip("Free text shown in the editor's frame strip.")]
        public string label;
        [Tooltip("One entry per rig member, in the same order as the rig's member list.")]
        public List<GoreMemberFrame> members = new List<GoreMemberFrame>();

        /// <summary>Pads the member list so it has at least 'count' entries (new entries are untagged).</summary>
        public void EnsureMembers(int count)
        {
            if (members == null) members = new List<GoreMemberFrame>();
            while (members.Count < count) members.Add(new GoreMemberFrame());
        }
    }
}

using System;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>One body member of a rig (the head, the torso...). Its position in the rig's list is its identity everywhere else.</summary>
    [Serializable]
    public sealed class GoreMemberDef
    {
        [Tooltip("Name shown in the editor and in the member chooser.")]
        public string name = "Member";
        [Tooltip("Ball is an ellipsoid (a head); Box is a rounded box (a torso).")]
        public MemberKind kind = MemberKind.Ball;
        [Tooltip("Colour of this member's outline and shape in the editor and in the asset preview. Has no effect in the game.")]
        public Color editorColour = Color.white;

        [Tooltip("On: a slice or a neck cut can sever this member and a piece of it flies off. Off: it can still be wounded (cuts, bullets and pellets dig holes that may start at the edges and go in a little) but no part of it can ever be cut off.")]
        public bool sliceable = true;

        public GoreMemberDef() { }
        public GoreMemberDef(string name, MemberKind kind, Color editorColour, bool sliceable = true) { this.name = name; this.kind = kind; this.editorColour = editorColour; this.sliceable = sliceable; }
    }
}

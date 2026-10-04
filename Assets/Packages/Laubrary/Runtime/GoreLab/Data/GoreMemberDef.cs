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

        public GoreMemberDef() { }
        public GoreMemberDef(string name, MemberKind kind, Color editorColour) { this.name = name; this.kind = kind; this.editorColour = editorColour; }
    }
}

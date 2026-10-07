using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Laubrary.Zoetrope.Editor
{
    /// What a field editor may do to the Zoe window it draws into: write through the window's own undoable
    /// save path, and ask for a rebuild when its change alters what is shown.
    public sealed class ZoeFieldEditContext
    {
        public Action<string, Action<SerializedProperty>> Commit;
        public Action Rebuild;
    }

    /// <summary>
    /// A seam for modules that plug into a Zoe (a player controller, a brain) to draw one of their own fields
    /// in the Zoe window with proper ZUI controls — typically a picker built from something only the character
    /// knows, such as its declared actions. The Zoe window asks every registered editor before falling back to
    /// its generic field drawer, and needs no knowledge of the module.
    ///
    /// Register by implementing this in any editor assembly; instances are found automatically.
    /// </summary>
    public interface IZoeFieldEditor
    {
        /// Draw <paramref name="prop"/> (a field of <paramref name="owner"/>) into <paramref name="host"/> and
        /// return true, or return false to leave it to the generic drawer.
        bool TryBuild(VisualElement host, SerializedProperty prop, object owner, Zoe zoe, ZoeFieldEditContext ctx);
    }

    static class ZoeFieldEditors
    {
        static List<IZoeFieldEditor> s_all;

        static List<IZoeFieldEditor> All
        {
            get
            {
                if (s_all != null) return s_all;
                s_all = new List<IZoeFieldEditor>();
                foreach (var t in TypeCache.GetTypesDerivedFrom<IZoeFieldEditor>())
                {
                    if (t.IsAbstract || t.IsInterface || t.GetConstructor(Type.EmptyTypes) == null) continue;
                    s_all.Add((IZoeFieldEditor)Activator.CreateInstance(t));
                }
                return s_all;
            }
        }

        public static bool TryBuild(VisualElement host, SerializedProperty prop, object owner, Zoe zoe, ZoeFieldEditContext ctx)
        {
            foreach (var e in All)
                if (e.TryBuild(host, prop, owner, zoe, ctx)) return true;
            return false;
        }
    }
}

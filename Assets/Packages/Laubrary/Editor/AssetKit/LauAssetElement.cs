using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one "reference to a LauAsset" control: a ZuiChip. Left-click (OnActivate) opens the shared
    /// LauAssetBrowser to pick a different one; right-click (OnContext) opens the full card (New / Edit /
    /// Clear / Make private / Make public / Clone). Also accepts a drag-and-drop of a matching asset from the
    /// Project window.
    ///
    /// Replaces the older always-visible "thumbnail swatch + Recall… + New ▾ + ✎" button row per
    /// LAUASSET_PICKER_SWEEP.md's own rules (no inline button row stealing a row of width; a reference reads
    /// as a chip; everything besides picking lives on a right-click context card) — that doc claimed every
    /// chip site was already rewritten this way, but this control itself never actually became one.
    ///
    /// <para><b>T-0381 — two deliberate exceptions to "everything lives on the right-click card".</b> The
    /// owner could not find Edit, because a right-click card is invisible until you already suspect it is
    /// there; discoverability beats the row-width purity the sweep was chasing for the ONE action a reference
    /// is most often followed by. So a chip may now be followed by up to two 20px icon buttons, each drawn
    /// only when it has something to do for the CURRENT value:
    /// <list type="bullet">
    /// <item>a pencil — open this asset in its own authoring tool — when <see cref="LauAssetEditors.CanOpen"/>;</item>
    /// <item>a latching "private" toggle — keep a bespoke copy inside the asset this window is editing,
    /// hidden from the shared browser — when the window told the chip what it is editing (the
    /// <c>owner</c> argument, or <see cref="AmbientOwner"/>, which <c>ZuiAssetWindow</c> sets around its own
    /// body build so every reflected chip in every asset tool gets it for free).</item>
    /// </list>
    /// With neither available the chip is returned bare, exactly as before — no wrapper, no reserved width.
    /// Everything rarer (New, Clear, Clone, Clone from…) stays on the right-click card.</para>
    public static class LauAssetElement
    {
        /// <summary>The asset the CURRENT window is editing, for chips that were not handed an owner
        /// explicitly — the natural thing to embed a private copy into. Set (and restored) by
        /// <c>ZuiAssetWindow.BuildUI</c> around its subclass's body build, synchronously, so it can never
        /// leak across windows: a chip built outside that scope simply gets no owner and offers no private
        /// actions, which is a missing affordance, never a copy embedded into the wrong asset.</summary>
        public static Object AmbientOwner;

        /// <summary>Sets <see cref="AmbientOwner"/> for the duration of a body build. Always use it in a
        /// <c>using</c>, never a bare assignment, so an exception mid-build cannot leave it set.</summary>
        public static IDisposable OwnerScope(Object owner) => new Scope(owner);

        sealed class Scope : IDisposable
        {
            readonly Object _previous;
            public Scope(Object owner) { _previous = AmbientOwner; AmbientOwner = owner; }
            public void Dispose() => AmbientOwner = _previous;
        }

        /// <param name="onPick">Receives the newly assigned (or cleared, via the context menu) asset.</param>
        /// <param name="tooltip">What this field is FOR — required, like every Z control.</param>
        /// <param name="owner">The asset this field belongs to — what a private copy gets embedded into.
        /// Null falls back to <see cref="AmbientOwner"/>.</param>
        public static VisualElement Build(Object current, Action<Object> onPick, Type constraint,
            Dictionary<Object, Texture2D> thumbCache, string suggestedName, string folder, string tooltip,
            Object owner = null)
        {
            owner ??= AmbientOwner;

            var chip = new ZuiChip(current != null ? current.name : null, tooltip, empty: current == null);
            if (current != null)
            {
                var tex = LauAssetGridGUI.GetThumbnail(current, null, thumbCache);
                if (tex != null) chip.Thumbnail = tex;
            }

            chip.OnActivate = c =>
            {
                var wb = c.worldBound;
                LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), constraint,
                    picked => onPick?.Invoke(picked), current);
            };
            chip.OnContext = c => ShowContextMenu(c, current, onPick, constraint, suggestedName, folder, owner);
            chip.Accepts = o => o != null && constraint.IsInstanceOfType(o);
            chip.OnDrop = o => onPick?.Invoke(o);

            bool canOpen = LauAssetEditors.CanOpen(current);
            bool canHost = LauPrivateAsset.CanHost(owner);
            if (!canOpen && !canHost) return chip;

            var group = new VisualElement();
            group.AddToClassList("zui-chipgroup");
            group.Add(chip);
            if (canOpen) group.Add(EditButton(current));
            if (canHost) group.Add(PrivateToggle(current, onPick, constraint, suggestedName, folder, owner));
            return group;
        }

        // ── the two visible actions ─────────────────────────────────────────────────────────

        static VisualElement EditButton(Object current)
        {
            var b = Z.IconButton("pencil", $"Open “{current.name}” in its own editor.",
                () => LauAssetEditors.Open(current));
            b.AddToClassList("zui-chipgroup__act");
            return b;
        }

        /// Public ⇄ Private as a latching toggle, because that is what the choice is: either this field points
        /// at a shared library asset everyone can pick, or at a bespoke copy living inside the asset being
        /// edited. (T-0371 drew it as a two-option "Public | Private" radio on the one Zoe row that had it;
        /// a latching icon costs a third of the width, which is what makes it affordable on EVERY chip.)
        static VisualElement PrivateToggle(Object current, Action<Object> onPick, Type constraint,
            string suggestedName, string folder, Object owner)
        {
            bool isPrivate = LauPrivateAsset.IsPrivate(current, owner);
            var concrete = SoleConcreteType(constraint);
            bool actionable = isPrivate || current != null || concrete != null;

            string tip = isPrivate
                ? $"This copy lives inside “{owner.name}” and is hidden from the shared browser, so this row is " +
                  "the only place it can be edited from. Turn off to save it into the shared library instead — " +
                  "the copy in here is kept, so undo never loses it."
                : current != null
                    ? $"Keep a private copy of “{current.name}” inside “{owner.name}”: same tuning, hidden from " +
                      "the shared browser, editable only from this row. The shared asset is left untouched."
                    : concrete != null
                        ? $"Make a bespoke {ObjectNames.NicifyVariableName(concrete.Name)} that lives inside " +
                          $"“{owner.name}” and never shows up in the shared browser."
                        : "Pick an asset first — a private copy is made from whatever this points at.";

            ZuiToggleButton t = null;
            t = Z.IconToggle("package", tip, isPrivate, on =>
            {
                Object result = on
                    ? LauPrivateAsset.MakePrivate(owner, current, concrete, suggestedName,
                        picked => onPick?.Invoke(picked))
                    : LauPrivateAsset.MakePublic(owner, current, folder, picked => onPick?.Invoke(picked));
                // A successful switch re-points the field, which rebuilds the row (and this toggle) from the
                // new value. A refused one changed nothing, so the latch must not be left claiming it did.
                if (result == null) t?.SetValueWithoutNotify(!on);
            });
            t.AddToClassList("zui-chipgroup__act");
            t.SetEnabled(actionable);
            return t;
        }

        // ── right-click card ────────────────────────────────────────────────────────────────

        static void ShowContextMenu(VisualElement chip, Object current, Action<Object> onPick, Type constraint,
            string suggestedName, string folder, Object owner)
        {
            var menu = new GenericMenu();
            var creatable = LauAssetEditors.RegisteredTypesFor(constraint).Where(LauAssetEditors.CanCreate).ToList();
            if (creatable.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("New"));
            }
            else
            {
                foreach (var t in creatable)
                {
                    var concrete = t;
                    string label = creatable.Count == 1 ? "New" : "New/" + concrete.Name;
                    menu.AddItem(new GUIContent(label), false, () => CreateAndAssign(concrete, suggestedName, folder, onPick));
                }
            }

            if (current != null && LauAssetEditors.CanOpen(current))
                menu.AddItem(new GUIContent("Edit"), false, () => LauAssetEditors.Open(current));
            else
                menu.AddDisabledItem(new GUIContent("Edit"));

            if (current != null)
                menu.AddItem(new GUIContent("Clear"), false, () => onPick?.Invoke(null));
            else
                menu.AddDisabledItem(new GUIContent("Clear"));

            // Private (embedded) copies — only where the chip knows what asset it belongs to.
            menu.AddSeparator("");
            bool canHost = LauPrivateAsset.CanHost(owner);
            bool isPrivate = canHost && LauPrivateAsset.IsPrivate(current, owner);
            var soleType = SoleConcreteType(constraint);

            if (canHost && !isPrivate && (current != null || soleType != null))
                menu.AddItem(new GUIContent("Make private"), false,
                    () => LauPrivateAsset.MakePrivate(owner, current, soleType, suggestedName,
                              picked => onPick?.Invoke(picked)));
            else
                menu.AddDisabledItem(new GUIContent("Make private"));

            if (isPrivate)
                menu.AddItem(new GUIContent("Make public"), false,
                    () => LauPrivateAsset.MakePublic(owner, current, folder, picked => onPick?.Invoke(picked)));
            else
                menu.AddDisabledItem(new GUIContent("Make public"));

            // Clone always yields a BRAND-NEW private copy (never a reused spare, never a shared reference
            // back to the source) — from what is here now, or from something picked for the purpose.
            if (canHost && current != null)
                menu.AddItem(new GUIContent("Clone"), false,
                    () => LauPrivateAsset.MakePrivate(owner, current, soleType, suggestedName,
                              picked => onPick?.Invoke(picked), freshCopy: true, undoName: "Clone Private"));
            else
                menu.AddDisabledItem(new GUIContent("Clone"));

            if (canHost)
            {
                var wb = chip.worldBound;
                menu.AddItem(new GUIContent("Clone from…"), false, () =>
                    LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), constraint,
                        source => LauPrivateAsset.MakePrivate(owner, source, soleType, suggestedName,
                                      picked => onPick?.Invoke(picked), freshCopy: true, undoName: "Clone Private"),
                        current));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Clone from…"));
            }

            menu.ShowAsContext();
        }

        static void CreateAndAssign(Type concrete, string suggestedName, string folder, Action<Object> onPick)
        {
            var made = LauAssetEditors.Create(concrete, suggestedName, folder);
            if (made == null) return;
            onPick?.Invoke(made);
            if (LauAssetEditors.CanOpen(made)) LauAssetEditors.Open(made);
        }

        /// The one concrete type a constraint can only mean — what a blank private copy would have to be when
        /// the field is still empty. Null when the constraint is an interface several assets satisfy (a
        /// Chunks Blast slot takes a Pyre OR a PyreSpawnSource), because guessing there would silently author
        /// the wrong kind of asset.
        static Type SoleConcreteType(Type constraint)
        {
            Type only = null;
            foreach (var t in AssetLibraryUntyped.ConcreteTypesSatisfying(constraint))
            {
                if (only != null) return null;
                only = t;
            }
            return only;
        }
    }
}

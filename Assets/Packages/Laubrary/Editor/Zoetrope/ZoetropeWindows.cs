using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.AssetKit.Editor;
using Laubrary.Chunks;
using Laubrary.Combat2D;
using Laubrary.LaunimatorZounds.Editor;
using Laubrary.Launimator;
using Laubrary.ZoetropeLaunimator;
using Laubrary.Zui;
using Laubrary.Mirage;
using Laubrary.Mirage.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The enemy-authoring hub: browse / create / duplicate / rename / delete Zoetrope Defs, and configure the
    /// selected one — all from the shared AssetKit base (empty-state shows the library). Every edit goes through
    /// a <see cref="SerializedObject"/>, which is what makes them Undo-able and what lets the pluggable
    /// <c>[SerializeReference]</c> fields (the view type, the effect types) be re-typed at all.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration). What moved and what didn't:
    ///  • the polymorphic <c>[SerializeReference]</c> header (ZUI.PolymorphicFoldout) is now the shared
    ///    <see cref="ZuiManagedRef"/> — a header naming the current concrete type over a body THIS file fills,
    ///    which is what keeps the clip-name dropdowns and LauAsset pickers on child fields;
    ///  • every LauAsset reference (scalar and list) is the new retained <see cref="LauAssetElement"/> rather
    ///    than an IMGUI island, so the thumbnail + Recall/New ▾/Edit ✎ row survives intact;
    ///  • scalar fields go through <see cref="ZuiSerialized"/>, which caps their width — the IMGUI original had
    ///    to hand-cap every one of them for exactly the same reason;
    ///  • a concrete type carrying its own <c>[CustomPropertyDrawer]</c> (ZonedLauminaryViewDrawer's "Open in
    ///    Laumination Builder", PyreChunksFxDrawer's "Preview in Pyre") is drawn by that drawer inside an
    ///    <see cref="IMGUIContainer"/>. Those drawers belong to other modules and are Rect-based IMGUI by
    ///    definition — hosting them is the only way not to regress them, and it is the same direct-invoke call
    ///    the IMGUI version already used (deliberately bypassing PropertyField, whose [Header] decorator pass
    ///    re-draws a section title a second time inside the open foldout).
    /// </summary>
    public abstract class ZoetropeDefWindow<T> : ZuiAssetWindow<T> where T : ScriptableObject
    {
        protected override string DefaultFolder => "Assets/Zoetrope";

        // A bare scalar field with no width of its own stretches to fill the window (measured 604px in a
        // 616px-wide ZoeWindow under the IMGUI original). Same budget, now applied by ZuiSerialized.
        protected const float ScalarFieldWidth = 200f;
        protected const float PairedFieldWidth = 130f;   // beside a rich picker/another control — needs less
        protected const float NumFieldWidth = 70f;
        // An "+ Add …" button left to itself fills the whole window — a 700px-wide button for a two-word
        // label is exactly the no-infinite-width-controls case, and ZuiAudit flags it.
        protected const float AddButtonWidth = 160f;

        protected SerializedObject So { get; set; }

        // Survives the rebuilds every dial edit triggers — see BuildAsset. Protected (not private): ZoeWindow
        // overrides BuildAsset itself (to host the D-13 section toggle bar, T-0084) and reuses this same
        // scroll-position-preservation dance rather than duplicating it.
        protected Vector2 _scrollOffset;
        protected bool _restoringScroll;
        protected ScrollView _scroll;

        // Thumbnails for every LauAsset-typed field this window draws — owned here, cleared on the way out.
        protected readonly Dictionary<Object, Texture2D> FieldThumbs = new Dictionary<Object, Texture2D>();

        // Not sealed (only): ZoeWindow overrides this further to host the section toggle bar (T-0084);
        // every other subclass (WeaponDefWindow, AmmoDefWindow) keeps this default plain-scroll behaviour
        // untouched.
        protected override void BuildAsset(VisualElement root, T asset)
        {
            So = new SerializedObject(asset);

            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            BuildBody(scroll.contentContainer, asset);
            root.Add(scroll);

            // Keep the scroll position across rebuilds. Picking a trigger or a placement rebuilds the whole
            // body (those choices gate conditional rows), and a fresh ScrollView starts at the top — so every
            // click threw the user back to the top of a long asset, which reads as the window resetting
            // itself. Restored after layout, because scrollOffset cannot be set before the content has a size.
            _scroll = scroll;
            scroll.verticalScroller.valueChanged += _ => { if (!_restoringScroll) _scrollOffset = scroll.scrollOffset; };
            var wanted = _scrollOffset;
            scroll.schedule.Execute(() =>
            {
                _restoringScroll = true;
                scroll.scrollOffset = wanted;
                _restoringScroll = false;
            });
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            LauAssetGridGUI.ClearCache(FieldThumbs);
        }

        protected override void OnAssetChanged() => LauAssetGridGUI.ClearCache(FieldThumbs);

        /// The per-asset editor. The default lists every serialized property, giving the LauAsset and
        /// managed-reference treatments to the ones that deserve them; a subclass overrides it to place its
        /// own short, related fields in shared rows (see ZoeWindow/WeaponDefWindow/AmmoDefWindow below).
        protected virtual void BuildBody(VisualElement root, T asset)
        {
            var it = So.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;

                if (it.propertyType == SerializedPropertyType.ManagedReference)
                {
                    BuildManagedRef(root, it.Copy(), ObjectNames.NicifyVariableName(it.name), asset);
                    continue;
                }

                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var fieldType = GetFieldType(asset.GetType(), it.name);
                    if (fieldType != null && typeof(Object).IsAssignableFrom(fieldType))
                    {
                        BuildSingleAssetRefField(root, it.Copy(), fieldType);
                        continue;
                    }
                }

                // A List<T> of UnityEngine.Object references (e.g. Zoe.weapons): NextVisible(false) treats the
                // whole list as one opaque Generic/isArray property and never descends into its elements, so it
                // never reaches the ObjectReference branch above — reach in via GetArrayElementAtIndex instead,
                // so each row still gets the LauAsset treatment rather than a bare ObjectField.
                if (it.isArray && it.propertyType == SerializedPropertyType.Generic)
                {
                    var fieldType = GetFieldType(asset.GetType(), it.name);
                    var elemType = fieldType != null && fieldType.IsGenericType
                                   && fieldType.GetGenericTypeDefinition() == typeof(List<>)
                        ? fieldType.GetGenericArguments()[0] : null;
                    if (elemType != null && typeof(Object).IsAssignableFrom(elemType))
                    {
                        BuildObjectListProperty(root, it.Copy(), elemType, ObjectNames.NicifyVariableName(it.name));
                        continue;
                    }
                }

                root.Add(ZuiSerialized.Field(it.Copy(), width: ScalarFieldWidth));
            }
        }

        // ── mutation ────────────────────────────────────────────────────────────────────────
        /// Write one property and commit — ApplyModifiedProperties registers the Undo step itself, and
        /// coalesces a slider drag into one.
        protected void Commit(string path, Action<SerializedProperty> write)
        {
            var p = So?.FindProperty(path);
            if (p == null) return;
            So.Update();
            write(p);
            So.ApplyModifiedProperties();
        }

        /// A numeric field that clamps on commit and shows the clamped value back.
        protected VisualElement NumField(string label, string path, float value, string tooltip,
            Func<float, float> clamp = null, float width = NumFieldWidth)
        {
            UnityEngine.UIElements.FloatField f = null;
            f = Z.Float(value, tooltip, v =>
            {
                float c = clamp != null ? clamp(v) : v;
                Commit(path, p => p.floatValue = c);
                if (!Mathf.Approximately(c, v)) f.SetValueWithoutNotify(c);
            }, width);
            return Z.Field(label, tooltip, f);
        }

        protected VisualElement IntFieldClamped(string label, string path, int value, string tooltip,
            Func<int, int> clamp = null, float width = NumFieldWidth)
        {
            UnityEngine.UIElements.IntegerField f = null;
            f = Z.Int(value, tooltip, v =>
            {
                int c = clamp != null ? clamp(v) : v;
                Commit(path, p => p.intValue = c);
                if (c != v) f.SetValueWithoutNotify(c);
            }, width);
            return Z.Field(label, tooltip, f);
        }

        // ── [SerializeReference] ────────────────────────────────────────────────────────────

        /// A pluggable field: the type-switching header, then whatever fields the chosen concrete type has.
        /// <paramref name="topLevelAsset"/> (optional) is the OWNING asset — passed through to the clip
        /// dropdown as a fallback clip-name source for a boxed value with no "version" field of its own
        /// (e.g. a hit reaction, whose clips are authored on the same Lauminary the character's view plays).
        protected void BuildManagedRef(VisualElement root, SerializedProperty prop, string title,
            object topLevelAsset = null)
        {
            if (prop == null) return;
            string tip = ZuiSerialized.TooltipOf(prop, $"Which kind of {title} this is, plus its own settings.");
            var mref = Z.ManagedRef(prop, title, tip);
            mref.OnTypeChanged += Rebuild;     // a different type means a different set of fields
            root.Add(mref);
            if (mref.BoxedValue != null) BuildManagedRefChildren(mref, prop, mref.BoxedValue, topLevelAsset);
        }

        protected void BuildManagedRefChildren(VisualElement host, SerializedProperty managedRefProperty,
            object boxedValue, object topLevelAsset)
        {
            // A concrete type with its own [CustomPropertyDrawer] is drawn BY that drawer (see this class's
            // doc comment) — invoked directly rather than via PropertyField, whose [Header] decorator pass
            // would re-draw the enclosing section's title a second time inside the open foldout.
            var drawerType = GetCustomPropertyDrawerType(boxedValue.GetType());
            if (drawerType != null) { host.Add(CustomDrawerIsland(managedRefProperty, drawerType)); return; }

            var end = managedRefProperty.GetEndProperty();
            var child = managedRefProperty.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (TryBuildClipDropdown(host, child, boxedValue, topLevelAsset)) continue;
                if (TryBuildZoundPicker(host, child)) continue;
                if (TryBuildPlaybackMode(host, child, boxedValue)) continue;
                if (TryBuildBodySpriteFxTargetPart(host, child, boxedValue, topLevelAsset as Zoe)) continue;
                if (TryBuildAssetRefField(host, child, boxedValue)) continue;
                host.Add(ZuiSerialized.Field(child.Copy(), width: ScalarFieldWidth));
            }
        }

        // An enum picker — segmented for a short single-line set, a wrapping MiniRadio for a longer one. NEVER a
        // dropdown (ui-layout-rules: enum → radios/segmented, a dropdown is only for dynamic authored-name lists,
        // which is what ZoeWindow's StringDropdown stays as). Lives on the base so both the hand-built effect
        // cards (ZoeWindow) and the generic managed-ref child drawer above can use it.
        protected VisualElement EnumPicker(SerializedProperty prop, string label, string tooltip, bool rebuild = true)
        {
            var choices = prop.enumDisplayNames;
            string path = prop.propertyPath;
            void Pick(int i) { Commit(path, p => p.enumValueIndex = i); if (rebuild) Rebuild(); }   // Position/Trigger gate conditional rows
            VisualElement control = choices.Length <= 3
                ? Z.Segmented(prop.enumValueIndex, choices, tooltip, Pick)
                : Z.MiniRadio(prop.enumValueIndex, choices, tooltip, Pick, wrap: true);
            // A null label means the OPTIONS already say what the control is — "Trigger [Immediate|On Frame]"
            // and "Direction [Hit Direction|None]" both print the same word twice, and a label that repeats
            // its own values is the redundant-title case the layout rules call out. The tooltip still carries
            // the explanation, which is where an explanation belongs.
            return string.IsNullOrEmpty(label) ? control : Z.Field(label, tooltip, control);
        }

        // ── playback-binding pair (FxPlaybackMode `playback` + float `fxSeconds`, e.g. Body SpriteFx) ────────
        // The mode draws as a radio (enum → radios, never a dropdown) with a tooltip composed for the CURRENT
        // choice, and the Run-At-End window field rides beside it — ALWAYS in the row so picking a mode never
        // reflows the card, with `visibility` doing the showing (Hidden keeps its layout space, per the
        // stable-layout rule). Matched by field TYPE, not owner type, so any future effect carrying a playback
        // binding gets the same treatment for free.
        bool TryBuildPlaybackMode(VisualElement host, SerializedProperty child, object boxedValue)
        {
            if (child.propertyType != SerializedPropertyType.Enum) return false;
            var field = boxedValue.GetType().GetField(child.name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.FieldType != typeof(FxPlaybackMode)) return false;

            var mode = (FxPlaybackMode)child.enumValueIndex;

            // Composed per-mode: the shared "what the options mean" base plus the CURRENT mode's fine print
            // (each degrade note only shows while its mode is the one selected).
            const string baseTip = "How the stack plays against this event. Once = a single play-through of the " +
                "stack's own duration. Loop = repeat for the event's remaining duration. Run At End = start late " +
                "so it finishes exactly as the event ends (a fade-out). Ping Pong = once forward now, once " +
                "backward timed to the end. Once Reversed = a single play-through run backwards.";
            string modeTip = baseTip + (mode switch
            {
                FxPlaybackMode.Loop => " Currently Loop: with no known event duration (no clip, or a clip " +
                    "with no fixed end) it degrades to a single play.",
                FxPlaybackMode.RunAtEnd => " Currently Run At End: the window is Fx Seconds (0 = the stack's " +
                    "own duration); an event shorter than the window compresses the play to fit, and with no " +
                    "known event duration it degrades to a single immediate play.",
                FxPlaybackMode.PingPong => " Currently Ping Pong: with no known event duration the backward " +
                    "pass follows the forward one immediately (a there-and-back pulse).",
                FxPlaybackMode.OnceReversed => " Currently Once Reversed: the stack's clock runs 1→0, so the " +
                    "same asset that materialises this character dematerialises it. Only the clock reverses — " +
                    "the hashing grain and the animation underneath keep running forwards.",
                _ => " Currently Once: the original single play.",
            });

            // Peek the paired fxSeconds (declared immediately after `playback` on the effect).
            SerializedProperty fxSecondsProp = null;
            var peek = child.Copy();
            if (peek.NextVisible(false) && peek.name == "fxSeconds") fxSecondsProp = peek.Copy();

            var row = Z.Row(EnumPicker(child.Copy(), "Playback", modeTip));   // Commit + Rebuild (recomposes tooltips/visibility)
            if (fxSecondsProp != null)
            {
                string secTip = mode == FxPlaybackMode.RunAtEnd
                    ? "The fade-out window in seconds — the stack starts when the event has this much time " +
                      "left, so the play ends exactly with the event. 0 = use the stack's own duration."
                    : "Run At End only — pick that playback mode to use this window. (Kept in place so the " +
                      "row never reflows.)";
                var fxField = NumField("Fx Seconds", fxSecondsProp.propertyPath, fxSecondsProp.floatValue, secTip,
                    v => Mathf.Max(0f, v));
                // Reserved, not removed: Hidden keeps the layout space, so switching modes never shifts the card.
                fxField.style.visibility = mode == FxPlaybackMode.RunAtEnd ? Visibility.Visible : Visibility.Hidden;
                row.Add(Z.HSpace());
                row.Add(fxField);
                child.NextVisible(false);   // consume fxSeconds — it is drawn here, beside its mode
            }
            host.Add(row);
            return true;
        }

        // Body SpriteFx's optional restriction is a REFERENCE to a part declared on this Zoe, never a free-typed
        // string. Empty means the intentional default: affect every declared body part.
        bool TryBuildBodySpriteFxTargetPart(VisualElement host, SerializedProperty child, object boxedValue, Zoe zoe)
        {
            if (!(boxedValue is BodySpriteFxEffect) || child.name != "targetPart") return false;
            var partNames = zoe != null ? WeaponAttachmentLibrary.FindPartNames(zoe) : null;
            if (partNames == null || partNames.Count == 0) return true;

            const string tip = "Which declared composite body part this stack affects. Whole body (the default) " +
                "applies it to every declared part renderer; choosing one confines it to that part.";
            var labels = new List<string>(partNames.Count + 1) { "(whole body)" };
            labels.AddRange(partNames);
            string current = child.stringValue ?? "";
            var ids = new List<string>(partNames);
            if (!string.IsNullOrEmpty(current) && !ids.Contains(current))
            { ids.Insert(0, current); labels.Insert(1, $"{current} (unresolved)"); }
            int currentIndex = string.IsNullOrEmpty(current) ? 0 : Mathf.Max(0, ids.IndexOf(current) + 1);
            string path = child.propertyPath;
            host.Add(Z.Field("Target Part", tip, Z.Dropdown(currentIndex, labels, tip,
                value => Commit(path, p => p.stringValue = value <= 0 ? "" : ids[value - 1]), 200f)));
            return true;
        }

        /// The one deliberate IMGUI island in this window: a foreign module's Rect-based PropertyDrawer,
        /// hosted so its behaviour cannot regress.
        static VisualElement CustomDrawerIsland(SerializedProperty prop, Type drawerType)
        {
            var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType);
            var so = prop.serializedObject;
            string path = prop.propertyPath;

            var container = new IMGUIContainer
            {
                tooltip = "Settings for this kind, drawn by its own inspector.",
            };
            container.onGUIHandler = () =>
            {
                var p = so.FindProperty(path);
                if (p == null) return;
                so.Update();
                float h = drawer.GetPropertyHeight(p, GUIContent.none);
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(h),
                    GUILayout.ExpandWidth(true));
                drawer.OnGUI(rect, p, GUIContent.none);
                so.ApplyModifiedProperties();
            };
            container.style.flexShrink = 0f;
            return container;
        }

        // ── clip-name dropdowns ─────────────────────────────────────────────────────────────
        // A field named idleClip/clip/hurtClip/deathClip on a pluggable value is really picking one name out
        // of a Lauminary version's animation list, not free text. Resolved purely by REFLECTION over the boxed
        // value (no compile-time reference to Launimator/ZoetropeLaunimator types), so this stays a core-
        // Zoetrope concern, decoupled from whichever bridge module supplies the concrete view type.

        static readonly string[] ClipFieldNames = { "idleClip", "clip", "hurtClip", "deathClip" };

        // ── Zound picker ────────────────────────────────────────────────────────────────────
        // A field named `zoundName` is picking a Zound out of the project's library, never free text — a
        // misspelt name fails silently at runtime, which is the same trap the clip fields above exist to
        // close. Resolved through ZoundPickerHook so Zoetrope never references Zounds; with no bridge
        // registered the field degrades to a plain text box rather than disappearing.
        bool TryBuildZoundPicker(VisualElement host, SerializedProperty child)
        {
            if (child.propertyType != SerializedPropertyType.String || child.name != "zoundName") return false;
            if (!ZoundPickerHook.Available) return false;

            var prop = child.Copy();
            string Current() => string.IsNullOrEmpty(prop.stringValue) ? "(none)" : prop.stringValue;

            var button = Z.Button(Current(),
                "Click to pick a Zound. Right-click to hear the current one.", null).W(190f);
            button.clicked += () =>
                ZoundPickerHook.Show(GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero),
                    picked =>
                    {
                        // ApplyModifiedProperties (not ...WithoutUndo) so the pick is undoable — this is a
                        // data edit on the asset, and every other field here routes through Undo.
                        prop.stringValue = picked;
                        prop.serializedObject.ApplyModifiedProperties();
                        button.text = Current();
                        ZoundPickerHook.Preview?.Invoke(picked);   // hear what you just chose, immediately
                    });

            // Right-click auditions it. A sound field you cannot hear from is a name you have to trust, and
            // the whole reason these are picked rather than typed is that trusting a name does not work.
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || string.IsNullOrEmpty(prop.stringValue)) return;
                ZoundPickerHook.Preview?.Invoke(prop.stringValue);
                e.StopPropagation();
            });

            // No label: the effect card's own header already says "Zound", and repeating it beside the
            // picker says the same word twice — the mockup's shape, and the layout rules' redundant-title rule.
            host.Add(button);
            return true;
        }

        bool TryBuildClipDropdown(VisualElement host, SerializedProperty child, object boxedValue, object topLevelAsset)
        {
            if (child.propertyType != SerializedPropertyType.String) return false;
            if (Array.IndexOf(ClipFieldNames, child.name) < 0) return false;

            var options = GetClipNameOptions(boxedValue);
            if (options == null && topLevelAsset != null)
            {
                var view = GetFieldValue(topLevelAsset, "view");
                if (view != null) options = GetClipNameOptions(view);
            }
            if (options == null) return false;   // not that shape at all — fall back to a plain text field

            string label = ObjectNames.NicifyVariableName(child.name);
            if (options.Length == 0)
            {
                host.Add(Z.Field(label, "This view has no clips authored yet.",
                    Z.Text("(no clips authored)", ZuiText.Subtle, "This view has no clips authored yet.")));
                return true;
            }

            // hurtClip + deathClip read far better sharing one row than stacked — paired only when deathClip
            // immediately follows, so a lone hurtClip/idleClip/clip still gets its own row.
            SerializedProperty pairedDeath = null;
            if (child.name == "hurtClip")
            {
                var peek = child.Copy();
                if (peek.NextVisible(false) && peek.name == "deathClip") pairedDeath = peek.Copy();
            }

            var row = Z.Row(ClipDropdown(child.Copy(), options, optional: child.name == "hurtClip"));
            if (pairedDeath != null)
            {
                row.Add(Z.HSpace());
                row.Add(ClipDropdown(pairedDeath, options, optional: false));
                child.NextVisible(false);   // consume deathClip too
            }
            host.Add(row);
            return true;
        }

        VisualElement ClipDropdown(SerializedProperty prop, string[] options, bool optional)
        {
            string label = ObjectNames.NicifyVariableName(prop.name);
            string tip = ZuiSerialized.TooltipOf(prop,
                $"Which authored animation plays as this view's {label.ToLowerInvariant()}.");
            var shown = optional ? new[] { "(none)" }.Concat(options).ToList() : options.ToList();
            int current = shown.IndexOf(string.IsNullOrEmpty(prop.stringValue) ? "(none)" : prop.stringValue);
            string path = prop.propertyPath;

            return Z.Field(label, tip, Z.Dropdown(Mathf.Max(current, 0), shown, tip, i =>
            {
                string picked = shown[Mathf.Clamp(i, 0, shown.Count - 1)];
                Commit(path, p => p.stringValue = optional && picked == "(none)" ? "" : picked);
            }, 170f));
        }

        // ── LauAsset reference fields ───────────────────────────────────────────────────────

        // Known asset-ref + trailing-scalar field-name pairs that read better sharing one row than stacked —
        // matched by NAME only (not owning Type), keeping this core editor decoupled from any bridge module's
        // concrete types. null label = use the field's own nicified name.
        static readonly Dictionary<string, (string scalarName, string scalarLabel)> AssetRefScalarPairs =
            new Dictionary<string, (string, string)>
            {
                ["blast"] = ("blastFps", "FPS"),
                ["chunks"] = ("sortingOrder", null),
            };

        bool TryBuildAssetRefField(VisualElement host, SerializedProperty child, object boxedValue)
        {
            if (child.propertyType != SerializedPropertyType.ObjectReference) return false;
            var field = boxedValue.GetType().GetField(child.name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !typeof(ScriptableObject).IsAssignableFrom(field.FieldType)) return false;

            string label = ObjectNames.NicifyVariableName(child.name);
            string tip = ZuiSerialized.TooltipOf(child, $"The {field.FieldType.Name} asset used as this {label}.");

            SerializedProperty pairedScalar = null;
            string scalarLabel = null;
            if (AssetRefScalarPairs.TryGetValue(child.name, out var pair))
            {
                var peek = child.Copy();
                if (peek.NextVisible(false) && peek.name == pair.scalarName)
                {
                    pairedScalar = peek.Copy();
                    scalarLabel = pair.scalarLabel;
                }
            }

            host.Add(Z.Text(label, ZuiText.Subtle, tip));
            string path = child.propertyPath;
            var row = field.FieldType == typeof(ChunkSpec)
                ? BuildChunksRefRow(child, label, tip)
                : Z.Row(LauAssetElement.Build(child.objectReferenceValue,
                    picked => { Commit(path, p => p.objectReferenceValue = picked); Rebuild(); },
                    field.FieldType, FieldThumbs, label, "Assets", tip));
            if (pairedScalar != null)
            {
                row.Add(Z.HSpace());
                row.Add(ZuiSerialized.Field(pairedScalar, scalarLabel, width: PairedFieldWidth));
                child.NextVisible(false);   // consume the paired scalar too
            }
            host.Add(row);
            return true;
        }

        // ── Chunks reference: Public (shared library) vs Private (embedded sub-asset) ──────────
        // T-0250: a Chunks debris burst that's bespoke to one Zoe (Floating Disc's own debris spray, say)
        // shouldn't clutter the shared Chunks browser. "Private" needs no new serialized flag: a private
        // ChunkSpec is simply a SUB-ASSET embedded inside this Zoe's own .asset file (AddObjectToAsset), and
        // AssetLibrary<T>.Enumerate/AssetLibraryUntyped.Enumerate — everything the shared browser walks — only
        // ever surface a path's MAIN asset (LoadAssetAtPath<T>), never its sub-assets (verified live), so an
        // embedded ChunkSpec is automatically invisible to every picker without any exclude-list. "Public" vs
        // "Private" is therefore just read straight off where the currently-assigned asset physically lives.
        VisualElement BuildChunksRefRow(SerializedProperty prop, string label, string tip)
        {
            // The incoming property is the shared, mutable iterator the caller's property-walk keeps advancing
            // after this row is built — capturing it directly (instead of a .Copy()) into the click closures
            // below means a click fires against whatever field the iterator has since moved PAST, not "chunks"
            // (caught live, T-0250: clicking Private embedded a copy of the wrong field entirely, named after
            // the next sibling property it had drifted onto). Every other LauAsset row in this file already
            // copies for the same reason; this one just needs its own copy up front too.
            prop = prop.Copy();
            string path = prop.propertyPath;
            var current = prop.objectReferenceValue as ChunkSpec;
            Object owner = Current;
            bool isPrivate = IsPrivateChunks(current, owner);

            var row = Z.Row();
            row.Add(Z.MiniRadio(isPrivate ? 1 : 0, new[] { "Public", "Private" },
                "Public: pick a shared Chunks asset from the library. Private: an embedded copy that belongs " +
                "only to this Zoe, hidden from the shared Chunks browser, and editable only from this row.",
                i =>
                {
                    if (i == 1 && !isPrivate) MakeChunksPrivate(prop, owner, current);
                    else if (i == 0) Rebuild();   // switching display back to Public never clears/deletes anything
                }));

            if (isPrivate)
            {
                row.Add(Z.Text(current.name, ZuiText.Body, tip));
                row.Add(Z.Button("Edit", $"Open this private {label} for editing — it isn't in the shared " +
                    "browser, so this row is the only place it can be edited from.",
                    () => LauAssetEditors.Open(current)));
            }
            else
            {
                row.Add(LauAssetElement.Build(current,
                    picked => { Commit(path, p => p.objectReferenceValue = picked); Rebuild(); },
                    typeof(ChunkSpec), FieldThumbs, label, "Assets", tip));
            }
            return row;
        }

        static bool IsPrivateChunks(ChunkSpec current, Object owner) =>
            current != null && owner != null && AssetDatabase.IsSubAsset(current) &&
            AssetDatabase.GetAssetPath(current) == AssetDatabase.GetAssetPath(owner);

        /// Embeds a brand-new ChunkSpec as a sub-asset of <paramref name="owner"/> (the Zoe being edited) and
        /// assigns it. If a public asset was already referenced, its tuning is copied into the private asset
        /// first (EditorUtility.CopySerialized) so switching modes never loses authored work — and the old
        /// public asset itself is left completely untouched (not deleted, not cleared from wherever else
        /// references it), matching the "don't silently destroy data" rule.
        void MakeChunksPrivate(SerializedProperty prop, Object owner, ChunkSpec previous)
        {
            if (owner == null) return;
            var made = ScriptableObject.CreateInstance<ChunkSpec>();
            // CopySerialized also copies the source's OWN name (m_Name is serialized data, not exempt) — so
            // it must run BEFORE we set the private asset's own name, or the copy silently overwrites it back
            // to the public asset's name (caught live, T-0250: a "Sparks" copy came out named "Sparks").
            if (previous != null) EditorUtility.CopySerialized(previous, made);
            made.name = $"{owner.name} — {ObjectNames.NicifyVariableName(prop.name)} (Private)";
            Undo.RegisterCreatedObjectUndo(made, "Create Private Chunks");
            AssetDatabase.AddObjectToAsset(made, owner);
            AssetDatabase.SaveAssets();
            string path = prop.propertyPath;
            Commit(path, p => p.objectReferenceValue = made);
            Rebuild();
        }

        /// A scalar UnityEngine.Object-reference field (e.g. Zoe.faction) through the LauAsset row rather than
        /// a bare ObjectField — used both by the generic loop above and by a hand-curated BuildBody override.
        protected void BuildSingleAssetRefField(VisualElement root, SerializedProperty prop, Type fieldType,
            string labelOverride = null, string tooltipOverride = null)
        {
            string label = labelOverride ?? ObjectNames.NicifyVariableName(prop.name);
            string tip = tooltipOverride ?? ZuiSerialized.TooltipOf(prop, $"The {fieldType.Name} assigned as this {label}.");
            string path = prop.propertyPath;
            root.Add(Z.Text(label, ZuiText.Subtle, tip));
            root.Add(LauAssetElement.Build(prop.objectReferenceValue,
                picked => { Commit(path, p => p.objectReferenceValue = picked); Rebuild(); },
                fieldType, FieldThumbs, label, "Assets", tip));
        }

        /// A list of LauAsset elements (WeaponDef.ammoTypes, Zoe.weapons — any List&lt;T&gt; where T is a
        /// UnityEngine.Object): one row per element through the LauAsset picker, plus add/remove. Returns the
        /// section it built, so a caller can put a related field (Zoe's "Default Active Weapon") inside it
        /// rather than wrapping the whole thing in a second, near-identically titled container.
        ///
        /// Removing an ObjectReference array element needs Unity's documented two-step dance —
        /// DeleteArrayElementAtIndex on a non-null reference only NULLS it; the second call removes the (now
        /// null) slot. Without it the array shrinks from the END while the clicked slot just goes blank.
        protected VisualElement BuildObjectListProperty(VisualElement parent, SerializedProperty listProp,
            Type elemType, string label, string folder = null)
        {
            folder ??= DefaultFolder;
            string listPath = listProp.propertyPath;
            string tip = ZuiSerialized.TooltipOf(listProp, $"Every {label} on this asset, in order.");

            // A section, not a bare heading: it OWNS its rows, so folding it can't swallow whatever the caller
            // adds afterwards — and it means no second, near-identically titled container around it.
            var root = Z.Section($"{label}s  ({listProp.arraySize})", tip, $"{typeof(T).Name}.{listPath}");
            parent.Add(root);

            for (int i = 0; i < listProp.arraySize; i++)
            {
                int idx = i;
                var elemProp = listProp.GetArrayElementAtIndex(idx);
                string elemPath = elemProp.propertyPath;
                var row = Z.Row(LauAssetElement.Build(elemProp.objectReferenceValue,
                    picked => { Commit(elemPath, p => p.objectReferenceValue = picked); Rebuild(); },
                    elemType, FieldThumbs, $"{label} {idx}", folder, $"{label} slot {idx}."));
                row.Add(Z.Button("X", $"Remove this {label} entry.", () =>
                {
                    Commit(listPath, p =>
                    {
                        var elem = p.GetArrayElementAtIndex(idx);
                        if (elem.objectReferenceValue != null) elem.objectReferenceValue = null;   // two-step
                        p.DeleteArrayElementAtIndex(idx);
                    });
                    Rebuild();
                }).W(24f));
                root.Add(row);
            }

            root.Add(Z.Button($"+ Add {label}", $"Append another {label} entry.", () =>
            {
                Commit(listPath, p =>
                {
                    p.arraySize++;
                    p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = null;
                });
                Rebuild();
            }).W(AddButtonWidth));
            return root;
        }

        // ── reflection helpers (unchanged from the IMGUI original) ──────────────────────────

        // Reflection-discovered, cached map of every type a [CustomPropertyDrawer] targets anywhere in the
        // loaded assemblies, to the drawer type itself (CustomPropertyDrawer.m_Type is private — no public API
        // exposes "does this type have a custom drawer" directly). Scanned once, not per-build.
        static Dictionary<Type, Type> _customDrawerTypes;
        static readonly Dictionary<Type, Type> _customDrawerTypeCache = new Dictionary<Type, Type>();

        static Type GetCustomPropertyDrawerType(Type targetType)
        {
            if (targetType == null) return null;
            if (_customDrawerTypeCache.TryGetValue(targetType, out var cached)) return cached;

            if (_customDrawerTypes == null)
            {
                _customDrawerTypes = new Dictionary<Type, Type>();
                var typeField = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (!typeof(PropertyDrawer).IsAssignableFrom(t)) continue;
                        foreach (var attrObj in t.GetCustomAttributes(typeof(CustomPropertyDrawer), true))
                        {
                            var target = typeField?.GetValue((CustomPropertyDrawer)attrObj) as Type;
                            if (target != null && !_customDrawerTypes.ContainsKey(target)) _customDrawerTypes[target] = t;
                        }
                    }
                }
            }

            _customDrawerTypes.TryGetValue(targetType, out var drawerType);
            _customDrawerTypeCache[targetType] = drawerType;
            return drawerType;
        }

        /// Duck-types "an object with a `version` field pointing at something with an `animations` field whose
        /// elements each have a `name`" — the LauminaryView/ZonedLauminaryView + LauminaryVersion + Laumination shape,
        /// without naming any of those types. Returns null (not an empty array) when the shape isn't there at
        /// all, so a caller can tell "no clip picker makes sense here" from "picker applies, no clips yet."
        protected static string[] GetClipNameOptions(object owner)
        {
            var version = GetFieldValue(owner, "version");
            if (version == null) return null;

            var animations = GetFieldValue(version, "animations") as IEnumerable;
            if (animations == null) return null;

            var names = new List<string>();
            foreach (var a in animations)
            {
                var n = GetFieldValue(a, "name") as string;
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            return names.ToArray();
        }

        protected static object GetFieldValue(object owner, string fieldName)
        {
            if (owner == null) return null;
            var field = owner.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(owner);
        }

        static Type GetFieldType(Type ownerType, string fieldName)
        {
            var field = ownerType?.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.FieldType;
        }
    }

    public class ZoeWindow : ZoetropeDefWindow<Zoe>
    {
        [MenuItem("Laubrary/Zoetrope/Zoes")]
        public static void Open() => GetWindow<ZoeWindow>("Zoes");
        protected override string TypeLabel => "Zoe";
        protected override string NewAssetName => "Zoe";

        /// Open the window focused directly on a specific Zoe — same entry-point shape as PyreWindow.OpenFor/
        /// MirageWindow.OpenFor, used by LauAssetEditors' Open registration so a Zoe picked in a LauAssetField
        /// elsewhere jumps straight into its own editor.
        public static void OpenFor(Zoe zoe)
        {
            var w = GetWindow<ZoeWindow>("Zoes");
            if (zoe != null) w.SetAsset(zoe);
        }

        // Live validation badges (duplicate / empty event ids, a cue raising an id nobody declares) re-read the
        // asset and repaint their own text. Registered as the body is built, cleared before it is rebuilt, and
        // re-run whenever an id is typed — so a rename shows its consequences on the OTHER cards immediately,
        // without a rebuild yanking the text field out from under the caret.
        readonly List<Action> _validators = new List<Action>();

        // Which event card should take keyboard focus once the window has rebuilt (set by "+ New event", so a
        // freshly created event is named by typing rather than by hunting for its field).
        int _focusEventIndex = -1;

        // Rig section: which animation+frame represents each part in the schematic preview (scrubbing through
        // directions without playing anything — Mirage is the real animated preview). Keyed by part name.
        readonly Dictionary<string, int> _rigAnimIndex = new Dictionary<string, int>();
        readonly Dictionary<string, int> _rigFrameIndex = new Dictionary<string, int>();

        void RunValidators() { for (int i = 0; i < _validators.Count; i++) _validators[i]?.Invoke(); }

        // ── section toggle bar (T-0084) ─────────────────────────────────────────────────────
        // Same idiom as ChunkWindow/MirageWindow/SpriteFxStackWindow/CartographerWindow — a roster rebuilt
        // from scratch on every BuildAsset, populated by Unit() as each top-level section is built.
        readonly List<(string label, ZuiSection section)> _barUnits = new List<(string label, ZuiSection section)>();

        // Tallest layout the bar has taken at a given width, remembered for the window's lifetime — same
        // stable-workspace guarantee as ChunkWindow.ReserveBarHeight.
        float _barReservedW, _barReservedH;

        /// Run one section builder and register whatever top-level ZuiSection it added under `label`. A
        /// builder that adds nothing (e.g. BuildRigUnit on a non-composite Zoe) registers nothing rather than
        /// parking a dead button in the bar. See ChunkWindow.Unit for the full rationale.
        void Unit(VisualElement body, Zoe zoe, string label, Action<VisualElement, Zoe> build)
        {
            int before = body.childCount;
            build(body, zoe);
            for (int i = before; i < body.childCount; i++)
                if (body[i] is ZuiSection sec) { _barUnits.Add((label, sec)); return; }
        }

        /// Stable-workspace rule — identical to ChunkWindow.ReserveBarHeight: remember the tallest height the
        /// bar has laid out at the current width and pin it as the host's minHeight, so a mode switch or a
        /// solo can only ever change what is IN the bar, never its size.
        void ReserveBarHeight(VisualElement barHost, VisualElement bar)
        {
            bar.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = bar.resolvedStyle.width, h = bar.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Abs(w - _barReservedW) > 0.5f) { _barReservedW = w; _barReservedH = 0f; }
                if (h <= _barReservedH + 0.5f) return;
                _barReservedH = h;
                barHost.style.minHeight = h;
            });
        }

        // Overrides the base's plain-scroll BuildAsset (Zoe is the one Zoetrope window big enough to want the
        // toggle bar — WeaponDefWindow/AmmoDefWindow keep the base's simple loop). Same shell as ChunkWindow's
        // BuildAsset: bar host first, filled last; TagsSection re-parented below it; scroll-offset
        // preservation copied verbatim from the base (now protected there for exactly this reuse).
        protected override void BuildAsset(VisualElement root, Zoe zoe)
        {
            So = new SerializedObject(zoe);
            _validators.Clear();

            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var barHost = new VisualElement();
            barHost.style.flexShrink = 0f;
            if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;
            root.Add(barHost);

            if (TagsSection != null) root.Add(TagsSection);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            root.Add(scroll);
            var body = scroll.contentContainer;

            _barUnits.Clear();
            if (TagsSection != null) _barUnits.Add(("Tags", TagsSection));

            Unit(body, zoe, "Identity", BuildIdentity);
            Unit(body, zoe, "Stats", BuildStats);
            Unit(body, zoe, "Look", BuildLook);
            Unit(body, zoe, "Rig", BuildRigUnit);   // no-op (registers nothing) on a non-composite view
            Unit(body, zoe, "Reactions", BuildReactionsSection);
            Unit(body, zoe, "AI", BuildAiSection);

            // No enclosing section for these two: the PropertyField's own foldout already carries the name, and
            // a section titled the same thing over a single field says it twice — so it isn't bar-addressable.
            body.Add(ZuiSerialized.Property(So.FindProperty("loadout"), "Loadout",
                "Pluggable weapons + abilities the character can activate; triggered by the brain or by input. Unused today."));

            Unit(body, zoe, "Weapons", BuildWeaponsUnit);
            Unit(body, zoe, "Cues", BuildCues);

            var bar = new ZuiSectionToggleBar("Zoe", _barUnits.ToArray());
            barHost.Add(bar);
            ReserveBarHeight(barHost, bar);

            RunValidators();   // first paint of every warning badge, from the data as it stands right now

            // Keep the scroll position across rebuilds — see the base BuildAsset's own comment for the story.
            _scroll = scroll;
            scroll.verticalScroller.valueChanged += _ => { if (!_restoringScroll) _scrollOffset = scroll.scrollOffset; };
            var wanted = _scrollOffset;
            scroll.schedule.Execute(() =>
            {
                _restoringScroll = true;
                scroll.scrollOffset = wanted;
                _restoringScroll = false;
            });
        }

        // Hand-curated rather than the base's generic loop: maxHealth/invulnerableAfterHit are two short
        // related Stats fields (measured 480px wide each under the generic loop) that read far better sharing
        // one row, and the base has no notion that they belong together.
        void BuildIdentity(VisualElement root, Zoe zoe)
        {
            var identity = Z.Section("Identity", "What this character is called in-game.");
            identity.Add(Z.Field("Display Name", "The name shown to the player.",
                Z.TextInput(zoe.displayName, "The name shown to the player.",
                    v => Commit("displayName", p => p.stringValue = v), ScalarFieldWidth)));
            root.Add(identity);
        }

        void BuildStats(VisualElement root, Zoe zoe)
        {
            var stats = Z.Section("Stats", "Health, invulnerability window, and which team this character fights for.");
            stats.Add(Z.Row(
                NumField("Max Health", "maxHealth", zoe.maxHealth,
                    "How much damage this character absorbs before dying.", v => Mathf.Max(1f, v)),
                Z.HSpace(),
                NumField("Invuln. After Hit", "invulnerableAfterHit", zoe.invulnerableAfterHit,
                    "Seconds of invulnerability after a hit — stops one shot dealing many hits. 0 = none.",
                    v => Mathf.Max(0f, v))));
            BuildSingleAssetRefField(stats, So.FindProperty("faction"), typeof(Faction), "Faction",
                "Team this character belongs to (drives who can hurt it). None = an unaligned hazard.");
            root.Add(stats);
        }

        void BuildLook(VisualElement root, Zoe zoe)
        {
            var look = Z.Section("Look", "How this character is drawn — a plain sprite, a Lauminary-driven animation, a composite body.");
            BuildManagedRef(look, So.FindProperty("view"), "View", zoe);
            root.Add(look);
        }

        void BuildRigUnit(VisualElement root, Zoe zoe)
        {
            if (zoe.view is CompositeLauminaryView composite) BuildRig(root, zoe, composite);
        }

        void BuildReactionsSection(VisualElement root, Zoe zoe)
        {
            var reactions = Z.Section("Reactions",
                "What plays when this character is hurt, when it dies, and on any custom event it declares. " +
                "Hit and Death are the top two rows of ONE list that also holds every custom event below — " +
                "same storage as always, just presented together (ZOE_PALETTE_TAKE.md's \"unified list\").");
            reactions.Add(RoleHeaderRow("Hit", "HURT",
                "What happens on a non-killing hit. This is the built-in row Laubrary's own \"which hurt " +
                "look?\" question falls back to — see Custom events below for role-chipped alternatives.",
                () => ZoePalettePreview.PreviewHit(zoe)));
            BuildReactionFx(reactions, So.FindProperty("hit"), zoe);
            reactions.Add(RoleHeaderRow("Death", "DEATH",
                "What happens on the killing blow. This is the built-in row Laubrary's own \"which death " +
                "look?\" question falls back to — see Custom events below for role-chipped alternatives.",
                () => ZoePalettePreview.PreviewDeath(zoe)));
            BuildReactionFx(reactions, So.FindProperty("death"), zoe);
            // Custom events live INSIDE Reactions, under Hit and Death, because they are the same kind of
            // thing — same ReactionFx, same editor — and only differ in being raised by a name you choose.
            BuildCustomEvents(reactions, zoe);
            root.Add(reactions);
        }

        /// A section-title row with a fixed, non-editable role chip beside it — the built-in Hit/Death rows'
        /// half of the role chip (ZOE_PALETTE_TAKE.md: "the two built-in hurt and death slots get the
        /// equivalent chip drawn for them... rather than stored", so nothing already authored is touched).
        /// Also carries this row's Preview button (T-0096: "a play/preview button on each row so a state can
        /// be previewed without the game running") — Hit/Death play automatically, never by name, so they get
        /// no usage chip or copy-name button, only the preview every row gets.
        VisualElement RoleHeaderRow(string title, string roleLabel, string tip, System.Action preview)
        {
            var row = Z.Row();
            row.Add(Z.Text(title, ZuiText.Section, tip));
            row.Add(Z.HSpace());
            var chip = Z.Text($"[{roleLabel}]", ZuiText.Subtle, tip);
            row.Add(chip);
            row.Add(Z.Flexible());
            row.Add(Z.Button("▶", $"Preview {title} — spawns a throwaway character in the open scene and " +
                "plays this reaction for real, without needing Play mode.", preview).W(28f));
            return row;
        }

        void BuildAiSection(VisualElement root, Zoe zoe)
        {
            var ai = Z.Section("AI / Control", "Optional decision-making OR player input attached at spawn — " +
                "a Brain drives the character itself (enemies); a Player Controller hands it to a person " +
                "(gamepad/keyboard); Aiming decides where its shots start and which way they go. Whichever is " +
                "set is what any spawner, Mirage's Preview included, attaches.");
            BuildManagedRef(ai, So.FindProperty("brain"), "Brain", zoe);
            BuildManagedRef(ai, So.FindProperty("playerController"), "Player Controller", zoe);
            BuildManagedRef(ai, So.FindProperty("aiming"), "Aiming", zoe);
            root.Add(ai);
        }

        void BuildWeaponsUnit(VisualElement root, Zoe zoe)
        {
            var weapons = BuildWeaponSlots(root, zoe);
            weapons.Add(IntFieldClamped("Default Active Weapon", "defaultActiveWeapon", zoe.defaultActiveWeapon,
                "Which weapon slot is enabled when this character spawns.", v => Mathf.Max(0, v)));
        }

        // ── Weapons: each slot names its OWN attach part + muzzle layer ───────────────────────
        // Moved off WeaponDef 2026-08-17 (a WeaponDef baking in one specific Zoe's part/layer names could only
        // ever be correctly configured for ONE character — see ZoeWeaponSlot's own doc comment for the full
        // story). Custom list editor (not BuildObjectListProperty) because ZoeWeaponSlot is a plain serializable
        // class, not a UnityEngine.Object list — no two-step delete dance needed, that quirk is specific to
        // object-reference arrays.
        VisualElement BuildWeaponSlots(VisualElement root, Zoe zoe)
        {
            var listProp = So.FindProperty("weapons");
            var section = Z.Section($"Weapons  ({listProp.arraySize})",
                "Switchable equipped weapon slots. Each slot names its OWN attach part + muzzle layer (on THIS " +
                "Zoe), not the weapon's — so the same WeaponDef asset stays equippable by any character.",
                "Zoe.weapons");
            root.Add(section);

            var partNames = WeaponAttachmentLibrary.FindPartNames(zoe);
            var layerCandidates = WeaponAttachmentLibrary.FindMuzzleLayerCandidates(zoe);

            for (int i = 0; i < listProp.arraySize; i++)
            {
                int idx = i;
                var elemProp = listProp.GetArrayElementAtIndex(idx);
                var weaponProp = elemProp.FindPropertyRelative("weapon");
                var attachProp = elemProp.FindPropertyRelative("attachToPartName");
                var layerProp = elemProp.FindPropertyRelative("muzzleLayerId");
                var eventProp = elemProp.FindPropertyRelative("muzzleEventName");

                var card = Z.Box($"Weapon {idx + 1}", "One equipped weapon slot.");

                var weaponRow = Z.Row(LauAssetElement.Build(weaponProp.objectReferenceValue,
                    picked => { Commit(weaponProp.propertyPath, p => p.objectReferenceValue = picked); Rebuild(); },
                    typeof(WeaponDef), FieldThumbs, $"Weapon {idx}", DefaultFolder, "Which WeaponDef this slot equips."));
                weaponRow.Add(Z.Button("X", "Remove this weapon slot.", () =>
                {
                    Commit(listProp.propertyPath, p => p.DeleteArrayElementAtIndex(idx));
                    Rebuild();
                }).W(24f));
                card.Add(weaponRow);

                {
                    var ids = new List<string>(partNames);
                    var options = new List<string>(partNames.Count + 2) { "(root)" };
                    options.AddRange(partNames);
                    string current = attachProp.stringValue;
                    if (!string.IsNullOrEmpty(current) && !ids.Contains(current))
                    { ids.Insert(0, current); options.Insert(1, $"{current} (unresolved)"); }
                    int currentIdx = string.IsNullOrEmpty(current) ? 0 : Mathf.Max(0, ids.IndexOf(current) + 1);
                    const string partTip = "Which named composite body part this weapon attaches to, so its " +
                        "muzzle can read THAT part's own animation. Empty = the character root.";
                    card.Add(Z.Field("Attach To Part", partTip, Z.Dropdown(currentIdx, options, partTip,
                        v => Commit(attachProp.propertyPath, p => p.stringValue = v <= 0 ? "" : ids[v - 1]), 200f)));
                }

                {
                    var ids = new List<string>();
                    var options = new List<string> { "(none)" };
                    foreach (var c in layerCandidates) { ids.Add(c.id); options.Add($"{c.id} ({c.mode})"); }
                    string current = layerProp.stringValue;
                    if (!string.IsNullOrEmpty(current) && !ids.Contains(current))
                    { ids.Insert(0, current); options.Insert(1, $"{current} (unresolved)"); }
                    int currentIdx = string.IsNullOrEmpty(current) ? 0 : Mathf.Max(0, ids.IndexOf(current) + 1);
                    const string layerTip = "Which MetaLayer (Point or Vector, on this part's own animation) " +
                        "carries this weapon's live muzzle position. Ignored when Muzzle Event Name is set.";
                    card.Add(Z.Field("Muzzle Layer Id", layerTip, Z.Dropdown(currentIdx, options, layerTip,
                        v => Commit(layerProp.propertyPath, p => p.stringValue = v <= 0 ? "" : ids[v - 1]), 200f)));
                }

                const string eventTip = "Alternative to Layer Id — a FrameEvent name (an authored pixel " +
                    "position). Takes priority over Layer Id for the muzzle-flash VFX cue specifically.";
                card.Add(Z.Field("Muzzle Event Name", eventTip, Z.TextInput(eventProp.stringValue, eventTip,
                    v => Commit(eventProp.propertyPath, p => p.stringValue = v), ScalarFieldWidth)));

                var (lauminary, animName) = WeaponAttachmentLibrary.ResolveLauminary(zoe, attachProp.stringValue);
                var editBtn = Z.Button("Paint Muzzle...",
                    lauminary != null
                        ? $"Open the Laumination Builder on '{lauminary.name}' to paint this part's Muzzle MetaLayer."
                        : "This part has no reachable Lauminary to open yet.",
                    () => Laubrary.Launimator.Editor.LauminationBuilderWindow.OpenForEdit(lauminary, animName));
                editBtn.SetEnabled(lauminary != null);
                card.Add(editBtn);

                section.Add(card);
            }

            section.Add(Z.Button("+ Add Weapon", "Append another weapon slot.", () =>
            {
                Commit(listProp.propertyPath, p => p.arraySize++);
                Rebuild();
            }).W(AddButtonWidth));
            return section;
        }

        // ── Rig: composite body-part attachment authoring ─────────────────────────────────────
        // Only shown when Zoe.view is a CompositeLauminaryView — a single-part Zoe has nothing to attach.
        // Per T-0026's converged design: one connection per non-root part = a parent-side anchor + a
        // child-side anchor (each independently Edge or MetaLayer) + a shared offset. The schematic preview
        // is deliberately animation-free (boxes + connection dots only) — Mirage is the real, playing preview.
        void BuildRig(VisualElement root, Zoe zoe, CompositeLauminaryView composite)
        {
            var rig = Z.Section("Rig", "How this composite body's parts attach to each other. Each connection " +
                "resolves every frame from an anchor on each side — Edge (computed automatically from the " +
                "current sprite bounds, no painting needed) or MetaLayer (a named painted point). The preview " +
                "below is schematic only — boxes and connection dots, no animated art; open Mirage to see the " +
                "real, playing result.");

            rig.Add(Z.Button("Preview in Mirage", "Open Mirage with a throwaway preview of this Zoe, so you can " +
                "see the real composited art (not just the schematic boxes above) and check alignment across " +
                "every direction. The preview view is created in memory only — it is never saved as a project " +
                "asset, so it never appears in Mirage's own Browse list or anywhere else; open this button again " +
                "any time for a fresh one.", () => PreviewInMirage(zoe)));

            var viewProp = So.FindProperty("view");
            var partsProp = viewProp?.FindPropertyRelative("parts");

            if (composite.parts != null && partsProp != null)
                for (int i = 0; i < composite.parts.Count; i++)
                {
                    var part = composite.parts[i];
                    if (part == null || string.IsNullOrEmpty(part.parentPartName)) continue; // root: nothing to attach
                    var partProp = partsProp.GetArrayElementAtIndex(i);
                    var parentAnchorProp = partProp.FindPropertyRelative("parentAnchor");
                    var childAnchorProp = partProp.FindPropertyRelative("childAnchor");

                    var card = Z.Box($"{part.name} → {part.parentPartName}",
                        $"How '{part.name}' attaches to its parent part '{part.parentPartName}'.");
                    card.Add(Z.Text("Parent side", ZuiText.Small,
                        $"Where on '{part.parentPartName}''s current frame this connects."));
                    var orderProp = partProp.FindPropertyRelative("sortingOrder");
                    if (orderProp != null)
                        card.Add(NumField("Draw order", orderProp.propertyPath, orderProp.intValue,
                            "Which part draws in FRONT — higher wins. Composite parts share a position, so " +
                            "without this the order is a tie the engine breaks arbitrarily. Torso over legs " +
                            "is torso 1, legs 0."));

                    if (parentAnchorProp != null) BuildAnchorRow(card, parentAnchorProp, part.parentAnchor);
                    card.Add(Z.Text("This part's side", ZuiText.Small,
                        $"Where on '{part.name}''s own current frame the connection lands."));
                    if (childAnchorProp != null) BuildAnchorRow(card, childAnchorProp, part.childAnchor);
                    rig.Add(card);
                }

            BuildRigSchematic(rig, composite);
            root.Add(rig);
        }

        /// Opens Mirage on a throwaway <see cref="MirageView"/> holding just this Zoe, for real composited-art
        /// vetting (vs. the boxes-only schematic above). Deliberately created via ScriptableObject.CreateInstance
        /// and NEVER passed to AssetDatabase.CreateAsset — every browser/picker in the project (this window's own
        /// Browse list included) enumerates via AssetLibrary&lt;T&gt;.Enumerate → AssetDatabase.FindAssets, so an
        /// unsaved instance is structurally invisible to all of them with no separate "hidden" flag needed. Content
        /// intentionally left otherwise blank — a composite Zoe with an authored MotionPose (ProtoGuy's case)
        /// self-drives idle/aim every frame via its own MotionPoseAnimator with no Mirage-authored clip needed;
        /// use Mirage's own HUD to move/interact once it's open.
        /// <summary>Make sure the scene that actually RENDERS a Mirage preview is open.
        ///
        /// Opening the Mirage window only focuses a window and points it at a view — the live preview needs a
        /// <see cref="MirageRig"/>, which lives in the Mirage stage scene. Without this, "Preview in Mirage"
        /// dropped you into a window that silently showed nothing until you happened to know you had to open
        /// that scene by hand: a missing step, not a workflow. An editor that says it will set up a preview has
        /// to actually land you in one.
        ///
        /// No-op when a rig is already present (any scene providing one is fine — the stage is not special-cased
        /// by name at runtime), and quietly does nothing if the stage scene isn't in the project, leaving the
        /// window usable for editing the view itself.</summary>
        static void EnsureMirageStageOpen()
        {
            if (Object.FindFirstObjectByType<MirageRig>() != null) return;

            string path = null;
            foreach (var guid in AssetDatabase.FindAssets("MirageStage t:Scene"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) { path = p; break; }
            }
            if (string.IsNullOrEmpty(path)) return;

            // The user clicked a button that opens a preview, so a save prompt here is expected and theirs to
            // answer; a cancel means leave their scene alone and don't open anything.
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                path, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        static void PreviewInMirage(Zoe zoe)
        {
            if (zoe == null) return;
            EnsureMirageStageOpen();
            var view = ScriptableObject.CreateInstance<MirageView>();
            view.name = $"{zoe.name} (Rig Preview)";
            var entry = view.AddEntry(zoe, Vector2.zero);

            // THIS is the editor asking Mirage for a control surface. Manual controls are opt-in per entry, so
            // a view assembled to look at an effect or a backdrop never grows character controls — but a view
            // created BY the Zoe editor exists precisely to put a character through its paces, so it asks.
            // What the controls end up being is not decided here: the HUD derives them from the Zoe's own
            // capabilities, so a character with no weapon simply gets no fire toggle.
            if (entry != null) entry.manualControls = true;

            MirageWindow.OpenFor(view);
        }

        void BuildAnchorRow(VisualElement host, SerializedProperty anchorProp, AttachAnchor anchor)
        {
            var modeProp = anchorProp.FindPropertyRelative("mode");
            host.Add(EnumPicker(modeProp, "Mode", "Pivot = the part's own registered origin (its baked sprite " +
                "pivot) plus Offset — the right default when the artist drew both parts on a shared canvas, " +
                "since one offset then joins them in every direction and on every clip. Edge = computed from " +
                "the current sprite bounds, which move when the artwork does. MetaLayer = read from a named " +
                "painted point, for a joint that genuinely travels inside the picture."));

            if (anchor.mode == AttachAnchorMode.Edge)
            {
                var edgeProp = anchorProp.FindPropertyRelative("edge");
                host.Add(EnumPicker(edgeProp, "Edge", "Which side of the current sprite's bounds."));
            }
            else if (anchor.mode == AttachAnchorMode.MetaLayer)
            {
                var layerProp = anchorProp.FindPropertyRelative("metaLayerId");
                host.Add(Z.Field("MetaLayer Id", "The painted point's layer id (e.g. \"Waist\").",
                    Z.TextInput(layerProp.stringValue, "The painted point's layer id (e.g. \"Waist\").",
                        v => Commit(layerProp.propertyPath, p => p.stringValue = v), ScalarFieldWidth)));
            }

            var offsetProp = anchorProp.FindPropertyRelative("offset");
            var xProp = offsetProp.FindPropertyRelative("x");
            var yProp = offsetProp.FindPropertyRelative("y");
            host.Add(Z.Row(
                NumField("Offset X", xProp.propertyPath, xProp.floatValue,
                    "Fine-tune nudge in world units, added on top of the computed/painted point."),
                Z.HSpace(),
                NumField("Offset Y", yProp.propertyPath, yProp.floatValue,
                    "Fine-tune nudge in world units, added on top of the computed/painted point.")));
        }

        void BuildRigSchematic(VisualElement root, CompositeLauminaryView composite)
        {
            if (composite.parts == null || composite.parts.Count == 0) return;

            root.Add(Z.Text("Preview (schematic)", ZuiText.Section,
                "Boxes and connection dots only — no animated art. Scrub each part's direction/frame to check " +
                "alignment across the whole set before opening Mirage to see it actually play."));

            foreach (var part in composite.parts)
            {
                if (part == null) continue;
                if (!(part.view is ZonedLauminaryView zlv) || zlv.version == null || zlv.version.animations.Count == 0) continue;

                int ai = _rigAnimIndex.TryGetValue(part.name, out int a) ? a : 0;
                ai = Mathf.Clamp(ai, 0, zlv.version.animations.Count - 1);
                var animNames = zlv.version.animations.Select(x => x.name).ToList();
                var la = zlv.version.animations[ai];

                int fi = _rigFrameIndex.TryGetValue(part.name, out int f) ? f : 0;
                fi = Mathf.Clamp(fi, 0, Mathf.Max(0, la.frames.Count - 1));

                string pn = part.name;
                var row = Z.Row(Z.Text(pn, ZuiText.Small, $"Which frame represents '{pn}' in the preview below.").W(56f));
                row.Add(Z.Dropdown(ai, animNames, $"Which of '{pn}''s animations to preview.",
                    v => { _rigAnimIndex[pn] = v; _rigFrameIndex[pn] = 0; Rebuild(); }, 130f));
                row.Add(Z.Int(fi, $"Which frame of '{la.name}' to preview (0-based, {la.frames.Count} frame(s)).",
                    v => { _rigFrameIndex[pn] = Mathf.Clamp(v, 0, Mathf.Max(0, la.frames.Count - 1)); Rebuild(); }, 40f));
                root.Add(row);
            }

            var box = new IMGUIContainer(() => DrawRigSchematic(composite));
            box.style.height = 220f;
            root.Add(box);
        }

        void DrawRigSchematic(CompositeLauminaryView composite)
        {
            var rect = GUILayoutUtility.GetRect(10, 4000, 220, 220);
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));

            var sprite = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            var animOf = new Dictionary<string, Laumination>(StringComparer.OrdinalIgnoreCase);
            var frameOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in composite.parts)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                if (!(p.view is ZonedLauminaryView zlv) || zlv.version == null || zlv.version.animations.Count == 0) continue;
                int ai = _rigAnimIndex.TryGetValue(p.name, out int a) ? Mathf.Clamp(a, 0, zlv.version.animations.Count - 1) : 0;
                var la = zlv.version.animations[ai];
                if (la.frames == null || la.frames.Count == 0) continue;
                int fi = _rigFrameIndex.TryGetValue(p.name, out int f) ? Mathf.Clamp(f, 0, la.frames.Count - 1) : 0;
                sprite[p.name] = la.frames[fi];
                animOf[p.name] = la;
                frameOf[p.name] = fi;
            }

            // Resolve each part's schematic-local origin by walking connections in dependency order (root
            // first, then anything whose parent is already resolved) — same shape as CompositeZonedPlayer's
            // runtime resolver, evaluated once against the chosen frames instead of live every frame.
            var worldPos = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in composite.parts)
                if (p != null && !string.IsNullOrEmpty(p.name) && string.IsNullOrEmpty(p.parentPartName))
                { worldPos[p.name] = Vector2.zero; resolved.Add(p.name); }

            for (int pass = 0; pass < composite.parts.Count + 1; pass++)
            {
                bool any = false;
                foreach (var p in composite.parts)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || resolved.Contains(p.name)) continue;
                    if (string.IsNullOrEmpty(p.parentPartName) || !resolved.Contains(p.parentPartName)) continue;

                    Vector2 parentOrigin = worldPos.TryGetValue(p.parentPartName, out var po) ? po : Vector2.zero;
                    if (sprite.TryGetValue(p.parentPartName, out var parentSpr) && sprite.TryGetValue(p.name, out var childSpr))
                    {
                        Vector2 parentPoint = parentOrigin + ResolveAnchorLocal(parentSpr,
                            animOf.TryGetValue(p.parentPartName, out var pa) ? pa : null,
                            frameOf.TryGetValue(p.parentPartName, out var pf) ? pf : 0, p.parentAnchor);
                        Vector2 childOffsetFromOrigin = ResolveAnchorLocal(childSpr,
                            animOf.TryGetValue(p.name, out var ca) ? ca : null,
                            frameOf.TryGetValue(p.name, out var cf) ? cf : 0, p.childAnchor);
                        worldPos[p.name] = parentPoint - childOffsetFromOrigin;
                    }
                    else worldPos[p.name] = parentOrigin; // no previewable sprite on one side — just stack at the parent's origin
                    resolved.Add(p.name);
                    any = true;
                }
                if (!any) break;
            }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool any2 = false;
            foreach (var kv in worldPos)
            {
                if (!sprite.TryGetValue(kv.Key, out var s) || s == null) continue;
                var b = s.bounds;
                minX = Mathf.Min(minX, kv.Value.x + b.min.x); maxX = Mathf.Max(maxX, kv.Value.x + b.max.x);
                minY = Mathf.Min(minY, kv.Value.y + b.min.y); maxY = Mathf.Max(maxY, kv.Value.y + b.max.y);
                any2 = true;
            }
            if (!any2)
            {
                GUI.Label(rect, "No previewable parts — assign a Lauminary to each part's view.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            float spanX = Mathf.Max(0.01f, maxX - minX), spanY = Mathf.Max(0.01f, maxY - minY);
            float scale = Mathf.Min((rect.width - 20f) / spanX, (rect.height - 20f) / spanY);
            Vector2 originScreen = new Vector2(
                rect.x + rect.width * 0.5f - (minX + maxX) * 0.5f * scale,
                rect.y + rect.height * 0.5f + (minY + maxY) * 0.5f * scale); // screen Y is flipped vs world Y

            Vector2 ToScreen(Vector2 world) => new Vector2(originScreen.x + world.x * scale, originScreen.y - world.y * scale);

            foreach (var p in composite.parts)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                if (!worldPos.TryGetValue(p.name, out var wp) || !sprite.TryGetValue(p.name, out var s) || s == null) continue;

                var b = s.bounds;
                var topLeft = ToScreen(new Vector2(wp.x + b.min.x, wp.y + b.max.y));
                var size = new Vector2((b.max.x - b.min.x) * scale, (b.max.y - b.min.y) * scale);
                var boxRect = new Rect(topLeft.x, topLeft.y, size.x, size.y);
                EditorGUI.DrawRect(boxRect, new Color(0.35f, 0.55f, 0.95f, 0.12f));
                RigDrawRectOutline(boxRect, new Color(0.35f, 0.65f, 1f, 0.8f));
                GUI.Label(new Rect(boxRect.x + 2, boxRect.y + 1, boxRect.width, 14), p.name, EditorStyles.whiteMiniLabel);

                // This part's own attach dot (where it connects to ITS parent).
                if (!string.IsNullOrEmpty(p.parentPartName) && animOf.ContainsKey(p.name))
                {
                    var childDot = ToScreen(wp + ResolveAnchorLocal(s, animOf[p.name], frameOf[p.name], p.childAnchor));
                    RigDrawDot(childDot, new Color(1f, 0.55f, 0.2f));
                }
                // Every dot on THIS part where a CHILD attaches to it.
                foreach (var child in composite.parts)
                    if (child != null && !string.IsNullOrEmpty(child.name)
                        && string.Equals(child.parentPartName, p.name, StringComparison.OrdinalIgnoreCase)
                        && animOf.ContainsKey(p.name))
                    {
                        var parentDot = ToScreen(wp + ResolveAnchorLocal(s, animOf[p.name], frameOf[p.name], child.parentAnchor));
                        RigDrawDot(parentDot, new Color(0.35f, 1f, 0.45f));
                    }
            }
        }

        static void RigDrawDot(Vector2 screenPos, Color c) =>
            EditorGUI.DrawRect(new Rect(screenPos.x - 3, screenPos.y - 3, 6, 6), c);

        static void RigDrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        /// Offline (no live component) equivalent of CompositeZonedPlayer.ResolveAnchor — same anchor
        /// semantics, evaluated against a chosen Laumination frame instead of a running ZonedAnimationPlayer,
        /// for the schematic preview. Returns a LOCAL point (relative to the sprite's own pivot-centered
        /// origin), not a world position — the caller adds the part's own schematic-space origin.
        static Vector2 ResolveAnchorLocal(Sprite spr, Laumination anim, int frameIndex, AttachAnchor anchor)
        {
            if (spr == null) return anchor.offset;

            // Sprite.bounds is already pivot-centred, so the part's registered origin IS local (0,0).
            if (anchor.mode == AttachAnchorMode.Pivot) return anchor.offset;

            if (anchor.mode == AttachAnchorMode.MetaLayer)
            {
                MetaLayer layer = null;
                if (!string.IsNullOrEmpty(anchor.metaLayerId) && anim?.metaLayers != null)
                    foreach (var L in anim.metaLayers)
                        if (L != null && string.Equals(L.id, anchor.metaLayerId, StringComparison.OrdinalIgnoreCase)) { layer = L; break; }
                if (layer != null && layer.frames != null && frameIndex >= 0 && frameIndex < layer.frames.Count
                    && RigTryComputeCentroid(layer.frames[frameIndex], out double nx, out double ny))
                {
                    var mf = layer.frames[frameIndex];
                    float spx = (float)nx * (spr.rect.width / mf.w);
                    float spy = (float)ny * (spr.rect.height / mf.h);
                    Vector2 pivotPx = spr.pivot;
                    float ppu = spr.pixelsPerUnit <= 0f ? 16f : spr.pixelsPerUnit;
                    return new Vector2((spx - pivotPx.x) / ppu, (spy - pivotPx.y) / ppu) + anchor.offset;
                }
                // Nothing painted this frame — degrade to the part's own origin, NOT to a bounds edge, which is
                // the rule the runtime resolver follows (see CompositeZonedPlayer.ResolveAnchor). A bounds edge
                // is a different reference frame entirely, so degrading to one made the schematic disagree with
                // what actually rendered.
                return anchor.offset;
            }

            var b = spr.bounds;
            Vector2 basePoint;
            switch (anchor.edge)
            {
                case AttachEdge.Top: basePoint = new Vector2(b.center.x, b.max.y); break;
                case AttachEdge.Bottom: basePoint = new Vector2(b.center.x, b.min.y); break;
                case AttachEdge.Left: basePoint = new Vector2(b.min.x, b.center.y); break;
                case AttachEdge.Right: basePoint = new Vector2(b.max.x, b.center.y); break;
                default: basePoint = b.center; break;
            }
            return basePoint + anchor.offset;
        }

        static bool RigTryComputeCentroid(MetaFrame mf, out double nx, out double ny)
        {
            nx = 0; ny = 0;
            if (mf == null || mf.cells == null || mf.cells.Length < mf.w * mf.h || mf.w <= 0 || mf.h <= 0) return false;
            double sx = 0, sy = 0, sw = 0;
            for (int y = 0; y < mf.h; y++)
                for (int x = 0; x < mf.w; x++)
                {
                    int v = mf.cells[y * mf.w + x];
                    if (v <= 0) continue;
                    sx += (x + 0.5) * v; sy += (y + 0.5) * v; sw += v;
                }
            if (sw <= 0) return false;
            nx = sx / sw; ny = sy / sw;
            return true;
        }

        /// One ReactionFx (Hit or Death): the clip dropdown (sourced from the Zoe's own view, same reflection
        /// duck-typing GetClipNameOptions uses elsewhere) plus the FX list — each entry packed onto three rows
        /// (Trigger+Placement; a conditional Event/Layer row only when one is actually needed; Follow+Remove)
        /// rather than one control per row.
        void BuildReactionFx(VisualElement root, SerializedProperty reactionProp, Zoe zoe)
        {
            var clipProp = reactionProp.FindPropertyRelative("clip");
            var fxListProp = reactionProp.FindPropertyRelative("fx");
            string clipPath = clipProp.propertyPath, fxPath = fxListProp.propertyPath;

            const string clipTip = "The animation this reaction plays, and the clip whose frame events / meta-layers the FX below fire off.";
            var clipOptions = zoe.view != null ? GetClipNameOptions(zoe.view) : null;
            VisualElement clipField;
            if (clipOptions != null && clipOptions.Length > 0)
            {
                var shown = new[] { "(none)" }.Concat(clipOptions).ToList();
                int current = shown.IndexOf(string.IsNullOrEmpty(clipProp.stringValue) ? "(none)" : clipProp.stringValue);
                clipField = Z.Field("Clip", clipTip, Z.Dropdown(Mathf.Max(current, 0), shown, clipTip, i =>
                {
                    string picked = shown[Mathf.Clamp(i, 0, shown.Count - 1)];
                    Commit(clipPath, p => p.stringValue = picked == "(none)" ? "" : picked);
                    Rebuild();   // the FX rows' Event/Layer options are sourced from this clip
                }, 200f));
            }
            else
            {
                clipField = Z.Field("Clip", clipTip, Z.TextInput(clipProp.stringValue ?? "", clipTip,
                    v => Commit(clipPath, p => p.stringValue = v), 200f));
            }
            BuildEventDuration(root, reactionProp, clipField, zoe);
            BuildTargetPartField(root, reactionProp, zoe);

            int clipFrames = GetFrameCount(zoe.view, clipProp.stringValue);
            // The clip's own layers first, then every layer painted anywhere on the view: the runtime samples a
            // MetaPoint by id across the whole view (nearest painted frame, any clip), so a Fire event with NO
            // clip of its own can still spawn at a "Muzzle" painted on the aim clip — the picker must offer it.
            // WeaponAttachmentLibrary walks a COMPOSITE view's parts too, which the duck-typed single-view
            // lister cannot (a composite has no `version` of its own) — ProtoGuy's Muzzle lives on its Upper part.
            var layerIdList = new List<string>(GetPointLayerIds(zoe.view, clipProp.stringValue));
            foreach (var (id, _) in WeaponAttachmentLibrary.FindMuzzleLayerCandidates(zoe)) if (!layerIdList.Contains(id)) layerIdList.Add(id);
            string[] pointLayerIds = layerIdList.ToArray();

            root.Add(Z.Text($"Effects  ({fxListProp.arraySize})", ZuiText.Subtle,
                "Every effect this reaction fires, in order — each picks which of the event's params it reads."));

            // A dedicated host so the reorder insertion line + index math only ever see effect cards, never the
            // Add button below (mirrors SpriteFxStackView's listHost split).
            var listHost = new VisualElement();
            root.Add(listHost);
            for (int i = 0; i < fxListProp.arraySize; i++)
                BuildFxEntry(listHost, fxListProp.GetArrayElementAtIndex(i), fxPath, i, clipFrames, pointLayerIds, zoe,
                             clipProp.stringValue);

            // The Add-effect menu: a Z.Menu of icon rows listing every IEffect kind (grouped by module), so
            // picking one appends an entry with that effect already assigned — nicer than adding a blank entry and
            // hunting the type switcher. The per-card switcher below still lets you re-type an existing entry.
            var addBtn = Z.Button("+ Add effect  ▾", "Pick an effect kind to add to this reaction.", null);
            addBtn.style.width = AddButtonWidth;
            addBtn.clicked += () => ShowAddEffectMenu(addBtn, fxPath);
            root.Add(addBtn);
        }

        /// The event's own TIMEBASE, authored beside the clip that supplies it: how long this reaction lasts,
        /// and a line stating what that actually resolves to in seconds.
        ///
        /// It is here rather than on the effects because the event owns it. A SpriteFx stack is a shape over
        /// normalized life with no opinion about seconds; a Pyre burst timed to the end of the event needs to
        /// know where the end is. Something above them has to say, and the character and the moment are what
        /// know. The resolved line matters as much as the dials: an author who cannot see that "3 loops" came
        /// out as 0.6 s has no way to tell a mis-set duration from a mis-authored effect.
        void BuildEventDuration(VisualElement root, SerializedProperty reactionProp, VisualElement clipField, Zoe zoe)
        {
            var modeProp = reactionProp.FindPropertyRelative("durationMode");
            var loopsProp = reactionProp.FindPropertyRelative("loops");
            var secondsProp = reactionProp.FindPropertyRelative("seconds");
            var stunProp = reactionProp.FindPropertyRelative("stunSeconds");
            if (modeProp == null || loopsProp == null || secondsProp == null) { root.Add(clipField); return; }

            var mode = (EventDurationMode)modeProp.enumValueIndex;
            bool fixedSeconds = mode == EventDurationMode.FixedSeconds;

            const string modeTip = "How long this event lasts — the timebase everything riding it is measured " +
                "against. Clip loops = the clip played N times. Fixed seconds = an explicit length, which is the " +
                "only mode that can give a length to a character whose visual is a still.";
            const string loopsTip = "How many times the clip plays. An On-Frame effect still fires ONCE per " +
                "event, on the first pass that reaches its frame — not once per loop.";
            const string secsTip = "The event's length in seconds. With a clip, the clip repeats to fill it; " +
                "with a static sprite, this is the only thing that gives the event a length.";

            // Clip and its duration share one wrapping row: three short controls, and vertical space is the
            // scarce resource in a card that already stacks an effect list under it.
            var row = Z.Row();
            row.style.flexWrap = Wrap.Wrap;
            row.Add(clipField);
            row.Add(Z.HSpace());
            row.Add(EnumPicker(modeProp, "Lasts", modeTip));
            row.Add(Z.HSpace());

            // Both numerics stay in the row at all times with `visibility` doing the showing — switching mode
            // must not reflow the card under the cursor (the stable-workspace rule).
            var loopsField = IntFieldClamped("Loops", loopsProp.propertyPath, Mathf.Max(1, loopsProp.intValue),
                loopsTip, v => Mathf.Max(1, v));
            loopsField.style.visibility = fixedSeconds ? Visibility.Hidden : Visibility.Visible;
            var secondsField = NumField("Seconds", secondsProp.propertyPath, secondsProp.floatValue,
                secsTip, v => Mathf.Max(0f, v));
            secondsField.style.visibility = fixedSeconds ? Visibility.Visible : Visibility.Hidden;
            // Absolutely positioned over each other would be cleverer and more fragile; two reserved slots keep
            // the row's geometry identical in both modes, which is the property that matters.
            row.Add(loopsField);
            row.Add(secondsField);

            // Stun joins this row rather than starting one: it is a short numeric that belongs to the same
            // question the row already asks ("how does this reaction sit in time"), and vertical space is the
            // scarce resource in a card that stacks an effect list underneath.
            //
            // It is drawn AT ALL because it is now obeyed. The field has existed on every reaction since
            // reactions did, and was reachable only through Unity's default inspector — so the moment
            // ReactionFxPlayer started reading it, a value that changes what a character does became one the
            // tool that owns the character cannot show or set. A numeric input rather than a slider on
            // purpose: a stun has no stable natural ceiling, and inventing one to earn a slider is the trade
            // the layout rulebook explicitly says not to make (Z.Float is scrub-draggable regardless).
            if (stunProp != null)
            {
                const string stunTip = "Seconds the character is stunned when this reaction fires. Today that " +
                    "freezes its walk/idle animation for that long, so the reaction visibly INTERRUPTS rather " +
                    "than being animated straight through — it does not yet stop it moving or firing. 0 = no " +
                    "stun. Careful on a state that repeats: half a second on a full-auto weapon's fire state " +
                    "leaves the character permanently frozen.";
                row.Add(Z.HSpace());
                row.Add(NumField("Stun", stunProp.propertyPath, stunProp.floatValue, stunTip,
                                 v => Mathf.Max(0f, v)));
            }

            root.Add(row);

            root.Add(EventDurationLine(zoe, reactionProp));
        }

        /// A permanently-reserved single line saying what the duration dials resolve to — fixed height and
        /// non-wrapping, so its TEXT changes between states and its geometry never does.
        VisualElement EventDurationLine(Zoe zoe, SerializedProperty reactionProp)
        {
            var reaction = ReactionOf(zoe, reactionProp);
            var visual = ZoeEventVisual.Of(zoe, reaction);
            string clip = reaction != null ? reaction.clip : "";
            string named = string.IsNullOrEmpty(clip) ? "this character's visual" : $"\"{clip}\"";

            string text;
            if (reaction == null)
                text = "";
            else if (reaction.durationMode == EventDurationMode.FixedSeconds)
                text = visual.EventSeconds > 0f
                    ? $"Lasts {visual.EventSeconds:0.###} s — fixed, independent of any clip."
                    : "No length set — every effect timed to this event falls back to a single play.";
            else if (visual.ClipSeconds > 0f)
                text = reaction.Loops > 1
                    ? $"Lasts {visual.EventSeconds:0.###} s — {reaction.Loops} loops of {named} " +
                      $"({visual.ClipSeconds:0.###} s at {visual.Fps:0.#} fps)."
                    : $"Lasts {visual.EventSeconds:0.###} s — one play of {named} at {visual.Fps:0.#} fps.";
            else if (string.IsNullOrEmpty(clip))
                text = "No clip — pick one above, or switch to Fixed seconds to give this event a length.";
            else
                text = $"Length unknown — {named} has no measurable end, so switch to Fixed seconds.";

            var line = Z.Text(text, ZuiText.Subtle,
                "What the duration above actually comes out as, from this character's own visual. Everything " +
                "riding the event — a Body SpriteFx stack, a timed playback binding — is measured against it.");
            line.style.height = 14f;
            line.style.whiteSpace = WhiteSpace.NoWrap;
            line.style.overflow = Overflow.Hidden;
            return line;
        }

        /// Which named composite body part this reaction speaks for — whole body (the default, and the only
        /// option there has ever been) or one named part, e.g. "Legs" walking under "Upper" firing. Shown
        /// ONLY on a composite Zoe (WeaponAttachmentLibrary.FindPartNames is empty otherwise, same source
        /// BuildWeaponSlots' "Attach To Part" picker already uses) — a single-part Zoe has nothing to target,
        /// so the field would be dead chrome on every non-composite character in the project.
        void BuildTargetPartField(VisualElement root, SerializedProperty reactionProp, Zoe zoe)
        {
            var targetProp = reactionProp.FindPropertyRelative("targetPart");
            if (targetProp == null) return;
            var partNames = WeaponAttachmentLibrary.FindPartNames(zoe);
            if (partNames.Count == 0) return;

            const string tip = "Which named body part this reaction speaks for. Whole body (the default) " +
                "plays the clip on every part that knows it — exactly today's behaviour. Naming one part " +
                "confines this reaction to it, which is what lets the character show more than one thing at " +
                "once — walking legs under a firing upper body.";

            var options = new List<string>(partNames.Count + 1) { "(whole body)" };
            options.AddRange(partNames);
            string current = targetProp.stringValue ?? "";
            var ids = new List<string>(partNames);
            if (!string.IsNullOrEmpty(current) && !ids.Contains(current))
            { ids.Insert(0, current); options.Insert(1, $"{current} (unresolved)"); }
            int currentIdx = string.IsNullOrEmpty(current) ? 0 : Mathf.Max(0, ids.IndexOf(current) + 1);
            string path = targetProp.propertyPath;

            root.Add(Z.Field("Target Part", tip, Z.Dropdown(currentIdx, options, tip,
                v => Commit(path, p => p.stringValue = v <= 0 ? "" : ids[v - 1]), 200f)));
        }

        /// The live ReactionFx behind a serialized reaction property — Hit, Death, or one of the custom
        /// events. Read-only use: the duration line needs the real object to ask it for its own resolved
        /// length, which is the same method the runtime player calls.
        static ReactionFx ReactionOf(Zoe zoe, SerializedProperty reactionProp)
        {
            if (zoe == null || reactionProp == null) return null;
            string path = reactionProp.propertyPath;
            if (path == "hit") return zoe.hit;
            if (path == "death") return zoe.death;
            if (zoe.events == null || !path.StartsWith("events.Array.data[")) return null;
            int i = IndexInPath(path);
            return i >= 0 && i < zoe.events.Count ? zoe.events[i]?.reaction : null;
        }

        static int IndexInPath(string path)
        {
            int open = path.IndexOf('[');
            int close = path.IndexOf(']', open + 1);
            return open >= 0 && close > open && int.TryParse(path.Substring(open + 1, close - open - 1), out int i) ? i : -1;
        }

        // ── custom events (Zoe.events) ──────────────────────────────────────────────────────
        // A named reaction is a ReactionFx under an id you choose, so it is authored by the SAME
        // BuildReactionFx surface Hit and Death use — only the identity row differs, because that id is the
        // whole contract between this asset and everything that raises it.

        const string EventsTip = "Reactions this character can play beyond Hit and Death — a teleport, a spawn, " +
            "a taunt, a special attack. Each is raised BY NAME (from gameplay code, or from a frame cue's Raise " +
            "picker under Cues) and carries the same clip and effect list the fixed reactions do.";

        const string EventIdTip = "The name this reaction is raised by. It is typed ONCE, here; everywhere else " +
            "picks it from a list. Case does NOT matter when it is raised, and it must be unique — two events " +
            "whose names differ only in case are the same name, and only the first can ever play. Renaming it " +
            "does NOT update whatever already raises it: a frame cue pointing at the old name shows up as " +
            "undeclared under Cues, and a gameplay call raising it logs a warning and plays nothing.";

        void BuildCustomEvents(VisualElement root, Zoe zoe)
        {
            var eventsProp = So.FindProperty("events");
            if (eventsProp == null) return;
            string listPath = eventsProp.propertyPath;

            root.Add(Z.Text($"Custom events  ({eventsProp.arraySize})", ZuiText.Section, EventsTip));

            // The empty state is a screen of its own: a character with no custom events must still say what
            // this block is for and offer the one move that gets out of it.
            if (eventsProp.arraySize == 0)
                root.Add(Z.Text("None declared — this character plays only Hit and Death.", ZuiText.Subtle, EventsTip));

            // Cards only, so the reorder insertion line and its index maths never see the Add button.
            var listHost = new VisualElement();
            root.Add(listHost);
            for (int i = 0; i < eventsProp.arraySize; i++)
                BuildEventCard(listHost, eventsProp.GetArrayElementAtIndex(i), listPath, i, zoe);

            root.Add(Z.Button("+ New event",
                "Declare another named reaction on this character, and start naming it.",
                () => AddEvent(listPath, zoe)).W(AddButtonWidth));
        }

        /// One named event: a header carrying its id (the identity, editable in place) and a body that is the
        /// ordinary reaction editor — deliberately the same one Hit and Death get, since a custom event is not
        /// a different kind of thing.
        void BuildEventCard(VisualElement listHost, SerializedProperty entryProp, string listPath, int index, Zoe zoe)
        {
            var idProp = entryProp.FindPropertyRelative("id");
            var reactionProp = entryProp.FindPropertyRelative("reaction");
            if (idProp == null || reactionProp == null) return;
            string idPath = idProp.propertyPath;
            string idAtBuild = idProp.stringValue ?? "";

            // Fold state keyed by the NamedReaction INSTANCE, so it survives the rebuilds every edit triggers
            // and never drifts between cards when the list is reordered (the ZuiFoldCard contract).
            object foldKey = zoe.events != null && index < zoe.events.Count ? (object)zoe.events[index] : null;

            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder. Order is presentation only — an event is raised by name.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Commit(listPath, p => p.MoveArrayElement(from, to));
                Rebuild();
            });
            header.Add(grip);

            var idField = Z.TextInput(idAtBuild, EventIdTip, v =>
            {
                Commit(idPath, p => p.stringValue = v);
                RunValidators();   // the duplicate warning is a property of the WHOLE list, not of this card
            }, 150f);
            header.Add(Z.Field("Id", EventIdTip, idField));

            // The role chip (ZOE_PALETTE_TAKE.md/BUILD_PLAN.md task 6): is this row a legal answer to
            // Laubrary's built-in "which hurt look?" / "which death look?" question. Defaults to None, so
            // nothing already authored changes meaning — it never gates or conditions playback, it only
            // narrows which rows an IReactionLookAnswerer may name.
            var roleProp = entryProp.FindPropertyRelative("role");
            if (roleProp != null)
            {
                const string roleTip = "Is this row a legal answer to Laubrary's built-in \"which hurt look?\" " +
                    "/ \"which death look?\" question. Nothing special = an ordinary custom event, raised only " +
                    "by name like any other. This never decides WHETHER or WHEN a hurt or death happens.";
                header.Add(EnumPicker(roleProp, null, roleTip));
            }

            // The usage chip (T-0096: "each row shows whether anything in the project actually requests it").
            // Same purpose ChunkTimelineEvents.HasListeners exists for — "so a tool can honestly report" —
            // reused here for the mirror question: has anything actually asked FOR this row, by name, at any
            // point this cache has observed (Play mode, or a Preview click, which goes through the identical
            // real Raise() call). Green = yes; amber = declared and never once requested.
            var usageChip = Z.Text("", ZuiText.Subtle, "");
            header.Add(usageChip);
            _validators.Add(() => UpdateUsageChip(usageChip, zoe, index));

            // A rename has to reach the Cues pickers (they list the declared ids), but rebuilding per keystroke
            // would tear the field out from under the caret — so it waits until the field is left.
            idField.RegisterCallback<FocusOutEvent>(_ =>
            {
                string now = zoe.events != null && index < zoe.events.Count && zoe.events[index] != null
                    ? zoe.events[index].id ?? "" : idAtBuild;
                if (now == idAtBuild) return;
                rootVisualElement.schedule.Execute(Rebuild);
            });

            var badge = WarningBadge();
            header.Add(badge);
            _validators.Add(() =>
            {
                var (text, tip) = EventIdIssue(zoe, index);
                badge.text = text;
                badge.tooltip = string.IsNullOrEmpty(text) ? EventIdTip : tip;
            });

            header.Add(Z.Flexible());

            // Preview (T-0096: "a play/preview button on each row so a state can be previewed without the
            // game running"). Reads the LIVE id off the asset, not idAtBuild, so it always fires whatever is
            // currently typed even before a FocusOut rebuild.
            header.Add(Z.Button("▶", "Preview this event — spawns a throwaway character in the open scene and " +
                "raises this event for real, without needing Play mode.", () =>
                {
                    string liveId = zoe.events != null && index < zoe.events.Count && zoe.events[index] != null
                        ? zoe.events[index].id : idAtBuild;
                    ZoePalettePreview.PreviewEvent(zoe, liveId);
                    RunValidators();   // refresh this row's usage chip immediately rather than on next rebuild
                }).W(28f));

            // Copy-name (T-0096: "a copy-name button for pasting a state's name into code" — the project's
            // standing rule is a name is typed ONCE where declared, so this is how it gets into hand-typed
            // gameplay code without retyping it, e.g. `player.Raise("...")`).
            // Plain text, not an icon: the obvious copy-symbol candidates (⧉, ⎘, ❐) are missing from the
            // default editor font and render as an unreadable fallback glyph — measured live, T-0096.
            header.Add(Z.Button("Copy", "Copy this event's name to the clipboard, ready to paste into a " +
                "Raise(\"...\") call.", () =>
                {
                    string liveId = zoe.events != null && index < zoe.events.Count && zoe.events[index] != null
                        ? zoe.events[index].id : idAtBuild;
                    EditorGUIUtility.systemCopyBuffer = liveId ?? "";
                }).W(46f));

            var removeBtn = Z.Button("×",
                "Remove this event (undoable). Anything raising it by name stops working.", () =>
                {
                    if (!ConfirmRemoveEvent(zoe, index)) return;
                    Commit(listPath, p => p.DeleteArrayElementAtIndex(index));
                    Rebuild();
                }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            var body = new VisualElement();
            BuildReactionFx(body, reactionProp, zoe);
            box.Add(body);

            ZuiFoldCard.Wire(foldKey, header, body, idField, removeBtn);
            listHost.Add(box);

            if (index == _focusEventIndex)
            {
                _focusEventIndex = -1;
                idField.schedule.Execute(() => idField.Focus());
            }
        }

        /// What is wrong with the id at `index`, as a short badge plus the explanation behind it. Empty text
        /// means it is fine. Surfaced at AUTHOR time because both failures are silent at runtime: an empty id
        /// is skipped by Zoe.EventIds, and EventNamed returns the FIRST match for a duplicate.
        ///
        /// The duplicate test is case-INSENSITIVE because Zoe.EventNamed is: "Fire" and "fire" are one state
        /// at runtime, so leaving this comparison ordinal would let the window bless a pair it had just made
        /// unplayable — the second card would show a clean badge and never play for a reason nothing on
        /// screen could explain.
        static (string text, string tooltip) EventIdIssue(Zoe zoe, int index)
        {
            var list = zoe?.events;
            if (list == null || index < 0 || index >= list.Count || list[index] == null) return ("", "");

            string id = list[index].id ?? "";
            if (string.IsNullOrWhiteSpace(id))
                return ("! needs an id", "This event has no name, so nothing can raise it and it never appears in " +
                                        "a picker. Type a name to make it playable.");

            for (int i = 0; i < index; i++)
                if (list[i] != null && SameEventId(list[i].id, id))
                    return ("! duplicate — never plays",
                        $"An earlier event is already called \"{list[i].id}\", and raising that name always plays " +
                        "THAT one — names are matched ignoring case, so a different capitalisation is not a " +
                        "different name. This event can never run until it is renamed.");

            for (int i = index + 1; i < list.Count; i++)
                if (list[i] != null && SameEventId(list[i].id, id))
                    return ("! duplicate id",
                        $"Another event below is also called \"{list[i].id}\". Names are matched ignoring case, " +
                        "so raising the name plays this one and the other never runs. Ids must be unique.");

            return ("", "");
        }

        /// THE comparison, in the editor, for two event ids — the same one Zoe.EventNamed uses at runtime.
        /// Routed through one helper so the window can never drift back into disagreeing with the resolver
        /// about whether two names are the same name.
        static bool SameEventId(string a, string b) =>
            string.Equals(a ?? "", b ?? "", System.StringComparison.OrdinalIgnoreCase);

        /// The row usage chip's text/tooltip (T-0096) — from ZoePaletteUsageLog, keyed off the LIVE id at
        /// `index`, so a rename shows its own fresh (empty) usage rather than the old name's history.
        static void UpdateUsageChip(Label chip, Zoe zoe, int index)
        {
            string id = zoe?.events != null && index < zoe.events.Count && zoe.events[index] != null
                ? zoe.events[index].id : null;
            if (string.IsNullOrEmpty(id)) { chip.text = ""; chip.tooltip = ""; return; }

            var (hitCount, lastHit, missCount, _) = ZoePaletteUsageLog.GetUsage(zoe, id);
            if (hitCount > 0)
            {
                chip.text = $"● requested {hitCount}×";
                chip.tooltip = $"Last requested {lastHit} UTC (observed since this cache was last cleared — " +
                    "Play mode or this row's own Preview button both count).";
            }
            else
            {
                chip.text = "○ never requested";
                chip.tooltip = "Nothing has asked for this state by name yet, as far as this project has " +
                    "observed (Play mode or a Preview click). Not proof it is truly unused — only that nothing " +
                    "has been SEEN asking for it." + (missCount > 0
                        ? $" ({missCount} request(s) for this name missed BEFORE it was declared, or while " +
                          "mis-typed — see Laubrary/Zoetrope/Palette Health.)"
                        : "");
            }
        }

        /// Delete-warns (T-0096: "deleting a row warns if anything still depends on it") — checks the two
        /// things this window can actually know: a Cue on THIS SAME character that still raises the id (static,
        /// checked right now), and whether anything has ever been OBSERVED requesting it at runtime
        /// (ZoePaletteUsageLog — Play mode or a Preview click). Neither is exhaustive (a cue on a DIFFERENT
        /// character, or gameplay code that simply hasn't run yet, can't be seen from here), so this warns
        /// rather than blocks — same "ask, don't silently allow or silently refuse" shape as the rest of this
        /// model.
        static bool ConfirmRemoveEvent(Zoe zoe, int index)
        {
            string id = zoe?.events != null && index < zoe.events.Count && zoe.events[index] != null
                ? zoe.events[index].id : null;
            if (string.IsNullOrEmpty(id)) return true;   // nothing named yet — nothing could depend on it

            var reasons = new List<string>();

            int cueCount = 0;
            if (zoe.cues != null)
                foreach (var c in zoe.cues)
                    if (c != null && SameEventId(c.raiseEvent, id)) cueCount++;
            if (cueCount > 0)
                reasons.Add($"{cueCount} cue(s) on this character raise it");

            var (hitCount, lastHit, _, _) = ZoePaletteUsageLog.GetUsage(zoe, id);
            if (hitCount > 0)
                reasons.Add($"it has been requested {hitCount} time(s), last {lastHit} UTC");

            if (reasons.Count == 0) return true;

            return EditorUtility.DisplayDialog("Delete this event?",
                $"\"{id}\" still looks used: {string.Join("; ", reasons)}.\n\n" +
                "Deleting it makes every one of those a no-op (a cue that raises nothing, a gameplay call that " +
                "warns and plays nothing). This cannot be undone from here.",
                "Delete anyway", "Cancel");
        }

        void AddEvent(string listPath, Zoe zoe)
        {
            string id = UniqueEventId(zoe);
            int added = -1;
            Commit(listPath, p =>
            {
                p.arraySize++;
                added = p.arraySize - 1;
                // Unity grows a plain-class array by DUPLICATING the previous last element, so a new event
                // would otherwise arrive carrying the last one's clip and its whole effect list under a new
                // name. Reset every field explicitly, the same way AddEffect does.
                var e = p.GetArrayElementAtIndex(added);
                var idProp = e.FindPropertyRelative("id"); if (idProp != null) idProp.stringValue = id;
                var r = e.FindPropertyRelative("reaction");
                if (r != null)
                {
                    var clip = r.FindPropertyRelative("clip"); if (clip != null) clip.stringValue = "";
                    var stun = r.FindPropertyRelative("stunSeconds"); if (stun != null) stun.floatValue = 0f;
                    var bodyFx = r.FindPropertyRelative("bodyFx"); if (bodyFx != null) bodyFx.objectReferenceValue = null;
                    var fx = r.FindPropertyRelative("fx"); if (fx != null) fx.ClearArray();
                }
            });
            _focusEventIndex = added;
            Rebuild();
        }

        /// A starting id no other event on this character already uses — a new event is valid the moment it
        /// exists, so the first thing an author sees is a working card rather than an error to clear.
        static string UniqueEventId(Zoe zoe)
        {
            // Case-insensitive, like every other id comparison here: "Event" already taken means "event" is
            // taken too, and handing out a name the duplicate badge would immediately flag is not a start.
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (zoe?.events != null)
                foreach (var e in zoe.events)
                    if (e?.id != null) taken.Add(e.id);

            if (!taken.Contains("event")) return "event";
            for (int n = 2; n < 1000; n++)
            {
                string candidate = "event " + n;
                if (!taken.Contains(candidate)) return candidate;
            }
            return "event " + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ── cues (Zoe.cues) ─────────────────────────────────────────────────────────────────
        // Hand-built rather than a PropertyField list, so the three strings on a CueBinding stop being free
        // text: the trigger names come from the clips the view actually has, and Raise comes from this Zoe's
        // own declared event ids (Zoe.EventIds — the API that exists for exactly this).

        const string NoneOption = "(none)";
        const string UnknownSuffix = "  (not authored)";
        const string UndeclaredSuffix = "  (undeclared)";

        const string CuesTip = "Always-on effects this character plays off its OWN animation, whatever it has " +
            "equipped — a footstep puff, a cast sparkle — and the frames that raise its custom events.";

        const string CueRaiseTip = "Which of this character's custom events the cue raises when it fires: the " +
            "WHOLE reaction (its clip, its body SpriteFx, its effect list), which the Effect slot below cannot " +
            "express on its own. The list is this Zoe's own Custom events, so a name that only fails at runtime " +
            "cannot be typed here.";

        void BuildCues(VisualElement root, Zoe zoe)
        {
            var cuesProp = So.FindProperty("cues");
            if (cuesProp == null) return;
            string listPath = cuesProp.propertyPath;

            var section = Z.Section($"Cues  ({cuesProp.arraySize})", CuesTip, "Zoe.cues");
            root.Add(section);

            if (cuesProp.arraySize == 0)
                section.Add(Z.Text("None yet — this character's animation triggers nothing by itself.",
                    ZuiText.Subtle, CuesTip));

            // A cue is not bound to one clip — it fires off whatever is playing — so its pickers offer the
            // whole vocabulary the view has authored, across every clip.
            var eventNames = AllFrameEventNames(zoe.view);
            var layerIds = AllPointLayerIds(zoe.view);

            var listHost = new VisualElement();
            section.Add(listHost);
            for (int i = 0; i < cuesProp.arraySize; i++)
                BuildCueCard(listHost, cuesProp.GetArrayElementAtIndex(i), listPath, i, zoe, eventNames, layerIds);

            section.Add(Z.Button("+ New cue", "Add another animation-triggered cue to this character.",
                () => AddCue(listPath)).W(AddButtonWidth));
        }

        void BuildCueCard(VisualElement listHost, SerializedProperty entryProp, string listPath, int index,
            Zoe zoe, string[] eventNames, string[] layerIds)
        {
            var layerProp = entryProp.FindPropertyRelative("layerId");
            var eventProp = entryProp.FindPropertyRelative("eventName");
            var raiseProp = entryProp.FindPropertyRelative("raiseEvent");
            var fxProp = entryProp.FindPropertyRelative("fx");
            if (layerProp == null || eventProp == null || raiseProp == null) return;

            var binding = zoe.cues != null && index < zoe.cues.Count ? zoe.cues[index] : null;

            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder. Order is presentation only — cues fire off the animation.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Commit(listPath, p => p.MoveArrayElement(from, to));
                Rebuild();
            });
            header.Add(grip);
            header.Add(Z.Text(CueTitle(binding), ZuiText.Body,
                "What fires this cue, and what it does — the same two choices as the body below."));

            header.Add(Z.Flexible());
            var removeBtn = Z.Button("×", "Remove this cue (undoable).", () =>
            {
                Commit(listPath, p => p.DeleteArrayElementAtIndex(index));
                Rebuild();
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            var body = new VisualElement();
            body.Add(Z.Row(
                NameDropdown(eventProp, "On Event", eventNames,
                    "A single authored pixel on a single frame that fires this cue, at that pixel's position. " +
                    "Takes priority over On Layer when both are set.",
                    "(no frame events authored)", UnknownSuffix),
                Z.HSpace(),
                NameDropdown(layerProp, "On Layer", layerIds,
                    "A Point-mode meta-layer that fires this cue as the animation reaches it. Ignored while " +
                    "On Event is set.",
                    "(no point layers authored)", UnknownSuffix)));
            body.Add(BuildRaisePicker(raiseProp, zoe));
            if (fxProp != null) BuildManagedRef(body, fxProp, "Effect", zoe);
            box.Add(body);

            ZuiFoldCard.Wire(binding, header, body, removeBtn);
            listHost.Add(box);
        }

        /// A folded cue still has to say what it is: what fires it, then what it does.
        static string CueTitle(CueBinding b)
        {
            if (b == null) return "(cue)";
            string when = !string.IsNullOrEmpty(b.eventName) ? $"On event  {b.eventName}"
                        : !string.IsNullOrEmpty(b.layerId) ? $"On layer  {b.layerId}"
                        : "(no trigger)";
            string what = !string.IsNullOrEmpty(b.raiseEvent) ? $"raise  {b.raiseEvent}"
                        : b.fx != null ? ObjectNames.NicifyVariableName(b.fx.GetType().Name)
                        : "(does nothing)";
            return $"{when}  →  {what}";
        }

        /// The raise-event PICKER: this Zoe's own declared event ids, never free text. A value the Zoe no
        /// longer declares (someone renamed the event) is offered back, flagged — resetting it silently would
        /// destroy the only evidence that the link broke.
        VisualElement BuildRaisePicker(SerializedProperty prop, Zoe zoe)
        {
            string path = prop.propertyPath;
            string current = prop.stringValue ?? "";
            var declared = DeclaredEventIds(zoe);

            var row = Z.Row();
            if (declared.Count == 0 && string.IsNullOrEmpty(current))
            {
                row.Add(Z.Field("Raise", CueRaiseTip + " This character declares none yet — add one under " +
                    "Reactions ▸ Custom events.",
                    Z.Text("(no custom events declared)", ZuiText.Subtle,
                        CueRaiseTip + " This character declares none yet — add one under Reactions ▸ Custom events.")));
                return row;
            }

            var shown = new List<string> { NoneOption };
            shown.AddRange(declared);
            int index = 0;
            if (!string.IsNullOrEmpty(current))
            {
                // Ignoring case, because the runtime does: a cue storing "fire" against a state declared as
                // "Fire" resolves fine and must not be shown as "fire  (undeclared)".
                index = IndexOfIgnoreCase(shown, current);
                if (index < 0) { shown.Add(current + UndeclaredSuffix); index = shown.Count - 1; }
            }

            row.Add(Z.Field("Raise", CueRaiseTip, Z.Dropdown(index, shown, CueRaiseTip, i =>
            {
                Commit(path, p => p.stringValue = StripOption(shown, i, UndeclaredSuffix));
                Rebuild();   // the card's own header names what it raises
            }, FitWidth(shown))));

            var badge = WarningBadge();
            row.Add(badge);
            _validators.Add(() =>
            {
                var live = So?.FindProperty(path);
                string v = live != null ? live.stringValue ?? "" : current;
                bool broken = !string.IsNullOrEmpty(v) && IndexOfIgnoreCase(DeclaredEventIds(zoe), v) < 0;
                badge.text = broken ? "! no such event" : "";
                badge.tooltip = broken
                    ? $"This cue raises \"{v}\", which this character does not declare — at runtime it logs a " +
                      "warning and plays nothing. Declare an event with that id under Custom events, or pick a " +
                      "declared one."
                    : CueRaiseTip;
            });
            return row;
        }

        void AddCue(string listPath)
        {
            Commit(listPath, p =>
            {
                p.arraySize++;
                // Same duplicate-the-last-element growth as AddEvent/AddEffect — reset it to a blank cue.
                var e = p.GetArrayElementAtIndex(p.arraySize - 1);
                var layer = e.FindPropertyRelative("layerId"); if (layer != null) layer.stringValue = "";
                var ev = e.FindPropertyRelative("eventName"); if (ev != null) ev.stringValue = "";
                var raise = e.FindPropertyRelative("raiseEvent"); if (raise != null) raise.stringValue = "";
                var fx = e.FindPropertyRelative("fx"); if (fx != null) fx.managedReferenceValue = null;
            });
            Rebuild();
        }

        // ── shared picker / validation plumbing ─────────────────────────────────────────────

        /// Every id this Zoe declares, de-duplicated, straight from the model's own picker feed. The de-dupe
        /// ignores case for the same reason EventIdIssue's does — two spellings of one name resolve to one
        /// reaction, so offering both in a picker would present a choice that does not exist.
        static List<string> DeclaredEventIds(Zoe zoe)
        {
            var ids = new List<string>();
            if (zoe == null) return ids;
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var id in zoe.EventIds)
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id)) ids.Add(id);
            return ids;
        }

        /// Where `value` sits in `options`, ignoring case, or -1. Used wherever a stored id has to be found
        /// again in a picker's list: an ordinal IndexOf would fail to find a value the RUNTIME resolves
        /// perfectly well, and the picker would then badge a working reference as undeclared.
        static int IndexOfIgnoreCase(IList<string> options, string value)
        {
            if (options == null || value == null) return -1;
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i], value, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// A dropdown over an authored-name vocabulary that may legitimately be empty, and that must never
        /// silently drop a value it doesn't recognise — an unknown current value is offered back with a
        /// suffix, so a renamed source reads as a broken reference instead of quietly snapping to option one.
        VisualElement NameDropdown(SerializedProperty prop, string label, IList<string> options, string tooltip,
            string emptyMessage, string unknownSuffix)
        {
            string current = prop.stringValue ?? "";
            if ((options == null || options.Count == 0) && string.IsNullOrEmpty(current))
                return Z.Field(label, tooltip, Z.Text(emptyMessage, ZuiText.Subtle, tooltip));

            var shown = new List<string> { NoneOption };
            if (options != null)
                foreach (var o in options)
                    if (!string.IsNullOrEmpty(o) && !shown.Contains(o)) shown.Add(o);

            int index = 0;
            if (!string.IsNullOrEmpty(current))
            {
                index = shown.IndexOf(current);
                if (index < 0) { shown.Add(current + unknownSuffix); index = shown.Count - 1; }
            }

            string path = prop.propertyPath;
            return Z.Field(label, tooltip, Z.Dropdown(index, shown, tooltip, i =>
            {
                Commit(path, p => p.stringValue = StripOption(shown, i, unknownSuffix));
                Rebuild();
            }, FitWidth(shown)));
        }

        /// The real string behind a picked option row: "(none)" is the empty value, and a flagged unknown
        /// carries its suffix for display only.
        static string StripOption(List<string> shown, int i, string suffix)
        {
            string picked = shown[Mathf.Clamp(i, 0, shown.Count - 1)];
            if (picked == NoneOption) return "";
            return picked.EndsWith(suffix) ? picked.Substring(0, picked.Length - suffix.Length) : picked;
        }

        // Amber, and always PRESENT (its text is what changes, never its existence) so a warning appearing
        // never reflows the card out from under the pointer — the stable-workspace rule.
        static readonly UnityEngine.Color WarningColor = new UnityEngine.Color(1f, 0.72f, 0.25f);

        static Label WarningBadge()
        {
            var l = Z.Text("", ZuiText.Small);
            l.style.color = WarningColor;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginLeft = 6f;
            l.style.flexShrink = 0f;
            return l;
        }

        /// Every Point-mode meta-layer id authored on ANY clip of the view, de-duplicated.
        static string[] AllPointLayerIds(object view)
        {
            var clips = GetClipNameOptions(view);
            if (clips == null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var clip in clips)
                foreach (var id in GetPointLayerIds(view, clip))
                    if (!ids.Contains(id)) ids.Add(id);
            return ids.ToArray();
        }

        /// Every FrameEvent name authored on ANY clip of the view, de-duplicated.
        static string[] AllFrameEventNames(object view)
        {
            var clips = GetClipNameOptions(view);
            if (clips == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var clip in clips)
                foreach (var n in GetEventNames(view, clip))
                    if (!names.Contains(n)) names.Add(n);
            return names.ToArray();
        }

        /// One effect entry, drawn as a FOLDING card (grip / kind-name / × in a header that stays visible when
        /// collapsed, everything else in a body that folds away — the SpriteFx-stack feel). The body carries the
        /// Trigger, then ONLY the param pickers this effect actually reads (Position / Direction / Scalar, keyed
        /// off <see cref="IEventParamUser"/> so the UI never shows a Direction dropdown to a blast that ignores
        /// it), then the effect's own SerializeReference body. <paramref name="index"/> is the entry's slot in the
        /// fx array; <paramref name="listHost"/> is the reorder container (holds ONLY cards).
        void BuildFxEntry(VisualElement listHost, SerializedProperty entryProp, string fxPath, int index,
            int clipFrames, string[] pointLayerIds, Zoe zoe, string clip)
        {
            var triggerProp = entryProp.FindPropertyRelative("trigger");
            var placementProp = entryProp.FindPropertyRelative("placement");
            var frameProp = entryProp.FindPropertyRelative("frame");
            var metaLayerIdProp = entryProp.FindPropertyRelative("metaLayerId");
            var bodyPartProp = entryProp.FindPropertyRelative("bodyPart");
            var localOffsetProp = entryProp.FindPropertyRelative("localOffset");
            var mirrorOffsetProp = entryProp.FindPropertyRelative("mirrorOffsetWithFacing");
            var directionProp = entryProp.FindPropertyRelative("direction");
            var rotationProp = entryProp.FindPropertyRelative("rotation");
            var angleOffsetProp = entryProp.FindPropertyRelative("angleOffsetDeg");
            var fixedAngleProp = entryProp.FindPropertyRelative("fixedAngleDeg");
            var flipProp = entryProp.FindPropertyRelative("flipWithFacing");
            var scalarProp = entryProp.FindPropertyRelative("scalar");
            var followProp = entryProp.FindPropertyRelative("follow");
            var effectProp = entryProp.FindPropertyRelative("fx");
            bool bodyPartPlacement = (FxPlacementType)placementProp.enumValueIndex == FxPlacementType.BodyPart;
            string followPath = followProp.propertyPath;

            object effect = effectProp.managedReferenceValue;
            EventParam used = UsedParamsOf(effect);
            var (kindLabel, _, kindIcon, _) = EffectMetaFor(effect);

            var box = Z.Box(null, null);   // untitled per-item card — folded via ZuiFoldCard, header stays visible

            // ── header: grip + (icon) + kind name + × ──
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — an effect's position IS its fire order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, listHost, (from, to) =>
            {
                Commit(fxPath, p => p.MoveArrayElement(from, to));
                Rebuild();
            });
            header.Add(grip);

            // Mute checkbox (task #8): uncheck to keep the effect + its settings but stop it firing. Stops its own
            // pointer-down so a click mutes without folding the card, and dims the whole card when muted.
            var enabledProp = entryProp.FindPropertyRelative("enabled");
            string enabledPath = enabledProp.propertyPath;
            // Muting is a SETTING, not fold chrome, so it is the ZUI button-toggle like every other setting —
            // the SpriteFx stack card beside it already draws its enable this way. It used to be a raw UITK
            // Toggle wearing the section-header class for styling, which renders as a bare OS checkbox and is
            // what ZuiAudit flags as native-toggle.
            var mute = Z.Toggle("", "Enabled — uncheck to MUTE this effect (kept in the list, but it never fires).",
                enabledProp.boolValue, v =>
                {
                    Commit(enabledPath, p => p.boolValue = v);
                    box.style.opacity = v ? 1f : 0.45f;
                });
            mute.style.marginRight = 4f;
            // The header folds the card on click; muting must not also fold it.
            mute.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            header.Add(mute);
            box.style.opacity = enabledProp.boolValue ? 1f : 0.45f;

            var icon = Z.Icon(kindIcon);
            if (icon != null) header.Add(icon);
            header.Add(Z.Text(kindLabel, ZuiText.Body, kindLabel + " effect."));
            var headerGap = Z.Flexible();
            header.Add(headerGap);
            var removeBtn = Z.Button("×", "Remove this effect (undoable).", () =>
            {
                Commit(fxPath, p => p.DeleteArrayElementAtIndex(index));
                Rebuild();
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // ── body: trigger, param pickers (only those the effect reads), then the effect's own fields ──
            var body = new VisualElement();

            // "Compact" = nothing to lay out but a trigger and (maybe) one field. Counted from the effect's own
            // serialized fields rather than a hard-coded list of types, so a new one-field effect gets the
            // tight treatment automatically.
            bool compact = used == 0 && CountOwnFields(effectProp) <= 1;

            // ONE wrapping row for every when/where/how picker. They were four stacked rows (trigger,
            // position, a lone Follow toggle, direction+scalar) in a card that is already tall before the
            // effect's own fields are drawn — and vertical space is the scarce resource here, per the layout
            // rules' first pre-flight check. Wrapping means a narrow pane still breaks them sensibly.
            var picks = Z.Row();
            picks.style.flexWrap = Wrap.Wrap;
            // On the header it must shrink, never grow, or it claims the whole line and pushes the type name
            // down. In the body it is free to use the width it has.
            picks.style.flexGrow = 0f;
            picks.style.flexShrink = 1f;
            picks.style.minWidth = 0f;

            // In the non-compact layout each GROUP gets its own row: when it fires / where it spawns / how it
            // is aimed and sized. One idea per line reads; a single ragged wrap does not.
            // `picks` is the HEADER strip: the trigger (and its frame slider) for every effect, plus a compact
            // effect's single field. Everything richer gets its own body row via Group().
            VisualElement Group()
            {
                var r = Z.Row();
                r.style.flexWrap = Wrap.Wrap;
                body.Add(r);
                return r;
            }

            const string triggerTip = "When this effect fires: the moment the reaction starts, or on a chosen frame of its clip.";
            var triggerRow = picks;
            triggerRow.Add(EnumPicker(triggerProp, null, triggerTip));
            if ((FxTriggerType)triggerProp.enumValueIndex == FxTriggerType.OnFrame)
            {
                // Bounded by the clip's real length, so the choice is always a frame that exists. Falls back
                // to a generous cap only when the clip cannot be resolved — better an unbounded number than a
                // slider that refuses the frame someone actually wants.
                int max = Mathf.Max(1, clipFrames == 0 ? 32 : clipFrames);
                triggerRow.Add(Z.HSpace());
                triggerRow.Add(Z.MicroSlider($"Frame (of {Mathf.Max(1, clipFrames)})",
                    Mathf.Clamp(frameProp.intValue, 1, max), 1f, max,
                    "Which frame of this reaction's clip the effect fires on. 1 is the first frame.",
                    v => Commit(fxPath, p => p.GetArrayElementAtIndex(index)
                                              .FindPropertyRelative("frame").intValue = Mathf.RoundToInt(v)),
                    decimals: 0));
            }
            // Position picker (+ conditional Layer, + Follow) — only when the effect reads a position.
            if ((used & EventParam.Position) != 0)
            {
                var placement = (FxPlacementType)placementProp.enumValueIndex;
                bool isMetaPoint = placement == FxPlacementType.MetaPoint;

                const string posTip = "Which of the event's position params this effect spawns at — the hit point, " +
                    "the Zoe's origin, its sprite centre, or a named meta-layer point.";
                var posRow = Group();
                posRow.Add(EnumPicker(placementProp, "At", posTip));
                if (isMetaPoint)
                {
                    posRow.Add(Z.HSpace());
                    posRow.Add(StringDropdown(metaLayerIdProp, "Layer", pointLayerIds,
                        "Which painted meta-layer (Point or Vector) this effect spawns at — the nearest painted " +
                        "frame on any clip of the view, so a painted Muzzle works even for an event with no clip."));
                }
                if (bodyPartPlacement)
                {
                    var names = WeaponAttachmentLibrary.FindPartNames(zoe).ToArray();
                    posRow.Add(Z.HSpace());
                    posRow.Add(StringDropdown(bodyPartProp, "Part", names,
                        "Declared body part this effect anchors to. It is the simple muzzle-anchor seam; no extra anchor asset is needed."));
                    posRow.Add(Z.HSpace());
                    posRow.Add(ZuiSerialized.Field(localOffsetProp.Copy(), width: 130f));
                    posRow.Add(Z.HSpace());
                    posRow.Add(Z.Toggle("Mirror offset", "Mirror the local X offset when the selected body part faces left.", mirrorOffsetProp.boolValue,
                        v => Commit(mirrorOffsetProp.propertyPath, p => p.boolValue = v)));
                }
                // Follow is available for EVERY position, including Hit Position (task #4): for the fixed hit point
                // it STICKS to the Zoe (the hit point captured in the Zoe's space, riding along as it moves); for
                // the other positions it re-samples that point each frame. It now tracks the whole POSE — the
                // Rotate row below is re-resolved every frame too — so the tooltip has to say so, or an author
                // reads "Follow" as position-only and never finds why a flash keeps the angle it left with.
                var follow = Z.Toggle("Follow",
                    "Keep the effect attached to the target for as long as it plays, instead of spawning once and " +
                    "letting it live on its own. It tracks the whole pose: the point re-samples every frame, and " +
                    "the Rotate setting below is re-asked with it, so a muzzle flash rides the barrel AND keeps " +
                    "pointing where the gun points if the character turns mid-flash. Hit Position sticks the hit " +
                    "point to the Zoe (it rides along, from where the hit landed). A Random direction is rolled " +
                    "once at spawn and held, so it scatters rather than spins.",
                    followProp.boolValue, v => Commit(followPath, p => p.boolValue = v));
                posRow.Add(Z.HSpace());
                posRow.Add(follow);
            }

            // Direction + Scalar pickers share one row (both short), each shown only when the effect reads it.
            // rebuild:false — unlike Position/Trigger, neither gates a conditional row, so a rebuild would just
            // churn the whole window (and lose scroll/focus) for nothing.
            VisualElement paramRow = null;
            if ((used & EventParam.Direction) != 0)
            {
                paramRow = Group();
                var rotation = (FxRotationMode)rotationProp.enumValueIndex;
                // The hint is the EFFECT's own word on how its asset orients (a Pyre with a Vector anchor says
                // the rotation follows that anchor); it rides in the rotation tooltip AND, when present, as a
                // subtle note at the end of the row — the row is the one place a reader looks for "which way".
                string orientationHint = (effect as IEffectOrientationHint)?.OrientationHint;
                string rotateTip = "How the spawned visual is turned. None leaves it upright; Face event direction " +
                    "points its forward along the chosen direction; Fixed angle uses one absolute angle." +
                    (orientationHint != null ? " " + orientationHint : " Without a Pyre anchor the forward is +X.");
                paramRow.Add(EnumPicker(rotationProp, "Rotate", rotateTip));
                if (rotation == FxRotationMode.FaceEventDirection)
                {
                    paramRow.Add(Z.HSpace());
                    paramRow.Add(EnumPicker(directionProp, "Direction",
                        "Which of the event's directions to face: the hit's push, the general event direction " +
                        "(a shot's aim), the angle toward the Zoe's centre, or a fresh random angle.", rebuild: false));
                    paramRow.Add(Z.HSpace());
                    // A degrees offset has a real, stable range — a slider, not a bare number.
                    paramRow.Add(Z.MicroSlider("Offset°", angleOffsetProp.floatValue, -180f, 180f,
                        "Degrees added on top of the faced direction, for art whose forward is not where its " +
                        "anchor (or +X) says. 0 = face the direction exactly.",
                        v => Commit(angleOffsetProp.propertyPath, p => p.floatValue = v), 150f, decimals: 0));
                }
                else if (rotation == FxRotationMode.FixedAngle)
                {
                    paramRow.Add(Z.HSpace());
                    paramRow.Add(Z.MicroSlider("Angle°", fixedAngleProp.floatValue, 0f, 360f,
                        "The absolute angle the visual's forward points at, in degrees (0 = right, 90 = up).",
                        v => Commit(fixedAngleProp.propertyPath, p => p.floatValue = v), 150f, decimals: 0));
                }
                // Mirroring reads for the CURRENT rotation mode: with a rotation it mirrors a left-pointing
                // result instead of over-rotating it; without one it mirrors with the body's facing.
                string flipTip = rotation == FxRotationMode.None
                    ? "Mirror the spawned visual when the body (or the chosen body part) faces left."
                    : "Mirror instead of over-rotating: a result pointing left shows the MIRRORED visual at a " +
                      "small angle rather than the right-facing art rotated 180° and drawn upside down.";
                paramRow.Add(Z.HSpace());
                paramRow.Add(Z.Toggle("Mirror left", flipTip, flipProp.boolValue,
                    v => Commit(flipProp.propertyPath, p => p.boolValue = v)));
                if (orientationHint != null)
                {
                    paramRow.Add(Z.HSpace());
                    paramRow.Add(Z.Text(orientationHint, ZuiText.Subtle,
                        "Set on the Pyre asset itself (Pyre window → Canvas → Anchor)."));
                }
            }
            if ((used & EventParam.Scalar) != 0)
            {
                var scalarPick = EnumPicker(scalarProp, "Scalar",
                    "Which of the event's scalar params sizes / strengthens this effect. Amount = the damage " +
                    "dealt; None = zero.", rebuild: false);
                if (paramRow == null) paramRow = Group(); else paramRow.Add(Z.HSpace());
                paramRow.Add(scalarPick);
            }
            // The trigger rides the HEADER for EVERY effect — it is two short segments, every effect has one,
            // and beside the name is where the Zound card already put it. A compact effect's single field goes
            // there too, which is what makes Zound one line.
            //
            // Everything richer (At / Aim / Scalar / multi-field effects) gets a body row per GROUP instead.
            // Cramming Spawn Chunks' four picker sets and five fields into one wrapping row gave ragged lines,
            // labels floating far from the controls they name, and a × orphaned on a line of its own — the
            // header never wraps now, which is what had pushed it down.
            header.Insert(header.IndexOf(headerGap), picks);

            // The effect's own fields, drawn INLINE — no foldable "Effect" sub-section (task #2): the effect IS the
            // whole card, its kind is already named in the header, so a nested "Effect" fold + type button was just
            // redundant chrome. A concrete effect with no custom drawer (the Spawn-Pyre / Spawn-Chunk palette kinds)
            // flows through TryBuildAssetRefField, so its Pyre/Chunk asset field renders as a LauAsset picker+preview
            // rather than a plain ObjectField. (Re-type an entry by removing it and adding the kind you want.)
            if (effect != null)
            {
                // A one-field effect puts that field on the header beside its trigger — Zound in a single
                // line, which is the shape to copy. Anything with more fields gets its own row, because five
                // fields wrapped in behind a name and a trigger is the ragged mess this replaced.
                BuildManagedRefChildren(compact ? picks : Group(), effectProp, effect, zoe);

                // A Spawn Chunks (or the bundled Pyre + Chunks) effect samples its colours AND (T-0252) its
                // debris pieces live off the Zoe's own current sprite — never authored data — so an author
                // staring at this card cannot otherwise tell what will actually get cut/sampled at runtime.
                // Matched by TYPE NAME, not the concrete type (see the s_effectMeta comment above): both live
                // in the ZoetropePyre bridge module, which this core Zoetrope editor asmdef deliberately does
                // not reference.
                string typeName = effect.GetType().Name;
                if (typeName == "SpawnChunkFx" || typeName == "PyreChunksFx")
                    BuildSpawnChunkLivePreview(body, effect, zoe, clip);
            }

            BuildFxOverrides(body, entryProp, zoe);

            // No body at all for an effect whose fields all fit the header (Zound, Invulnerable) — an empty
            // container still costs padding, and a card with nothing under its header should look like it.
            if (body.childCount == 0) body.style.display = DisplayStyle.None;
            box.Add(body);

            // Fold the whole card to its header, keyed per effect instance so the state survives window rebuilds
            // (undo / reorder / re-type). The grip guards its own drag; the × must not fold on click.
            ZuiFoldCard.Wire(effect, header, body, mute, removeBtn);
            listHost.Add(box);
        }

        // ── FxOverride slots (default+override effects, ZOE_PALETTE_BUILD_PLAN.md task 6 §4) ───────────────
        // Every FxEntry keeps its ONE required default effect (above, unchanged); this draws the OPTIONAL
        // named alternatives — "the same muzzle flash, but the flamethrower one while that powerup is up".
        // Small and separate from the main effect card on purpose: an override slot is rare, and giving it
        // the full card treatment (grip, mute, fold) would outweigh what it actually needs — a name and a
        // type-switching effect picker, reusing BuildManagedRef exactly as every other pluggable field does.
        const string OverridesTip = "Optional named ALTERNATIVES to the effect above. A request to show this " +
            "state may carry one override name (ReactionRequest.OverrideName, or ReactionFxPlayer.Raise's " +
            "override overload); a matching slot here plays INSTEAD of the default. No slots = the default " +
            "always plays, exactly as before this existed. An unmatched or empty name also plays the default " +
            "— an override SWAPS the effect, it never gates it.";

        void BuildFxOverrides(VisualElement body, SerializedProperty entryProp, Zoe zoe)
        {
            var overridesProp = entryProp.FindPropertyRelative("overrides");
            if (overridesProp == null) return;
            string listPath = overridesProp.propertyPath;

            var section = Z.Section($"Overrides  ({overridesProp.arraySize})", OverridesTip,
                $"{entryProp.propertyPath}.overrides");

            for (int i = 0; i < overridesProp.arraySize; i++)
            {
                int idx = i;
                var slotProp = overridesProp.GetArrayElementAtIndex(idx);
                var nameProp = slotProp.FindPropertyRelative("name");
                var fxProp = slotProp.FindPropertyRelative("fx");
                if (nameProp == null || fxProp == null) continue;

                var card = Z.Box(null, null);
                var row = Z.Row();
                row.Add(Z.Field("Name", "The name a request carries to select this slot instead of the default.",
                    Z.TextInput(nameProp.stringValue ?? "", OverridesTip,
                        v => Commit(nameProp.propertyPath, p => p.stringValue = v), PairedFieldWidth)));
                row.Add(Z.Flexible());
                row.Add(Z.Button("×", "Remove this override slot (undoable).", () =>
                {
                    Commit(listPath, p => p.DeleteArrayElementAtIndex(idx));
                    Rebuild();
                }).W(22f));
                card.Add(row);
                BuildManagedRef(card, fxProp, "Effect", zoe);
                section.Add(card);
            }

            section.Add(Z.Button("+ Add override", "Declare another named alternative to this effect's default.",
                () =>
                {
                    Commit(listPath, p =>
                    {
                        p.arraySize++;
                        var slot = p.GetArrayElementAtIndex(p.arraySize - 1);
                        slot.FindPropertyRelative("name").stringValue = "";
                        slot.FindPropertyRelative("fx").managedReferenceValue = null;
                    });
                    Rebuild();
                }).W(AddButtonWidth));
            body.Add(section);
        }

        // ── Spawn Chunks live-sample preview (T-0252) ───────────────────────────────────────────────────────
        // What a Spawn Chunks effect actually pulls its colours AND its sampled debris pieces from is never
        // authored — it is the Zoe's own CURRENT sprite at burst time (SpawnChunkFx.Apply / DebrisScatter.Fire,
        // via ChunkModuleContext.SampleSourceOverride). Nothing showed that fact before this existed, for either
        // the colour path (which predates this task) or the new sprite-pieces path, so both get one small
        // preview here: the frame that stands in for "the Zoe's current sprite" while authoring (a representative
        // mid-clip frame — there is no live game frame to read outside Play mode), the palette it would sample,
        // and — only when the recipe has a Sampled-visual Debris Scatter — a few example cut pieces.
        const int LivePreviewThumbPx = 48;

        void BuildSpawnChunkLivePreview(VisualElement body, object spawnChunkFx, Zoe zoe, string clip)
        {
            var chunks = GetFieldValue(spawnChunkFx, "chunks") as ChunkSpec;
            bool sampleColours = (bool)(GetFieldValue(spawnChunkFx, "sampleLauminaryColours") ?? true);
            int sampleCount = Mathf.Max(1, (int)(GetFieldValue(spawnChunkFx, "sampleCount") ?? 6));

            var box = Z.BoxKeyed("Live sample preview",
                "What this effect will actually sample at burst time — the Zoe's own current sprite, never " +
                "authored art. Read-only: it exists so the colours/pieces below are never a surprise in Play mode.",
                "Zoetrope.fx." + (chunks != null ? chunks.GetInstanceID().ToString() : "none") + ".liveSample");

            Sprite liveSprite = ResolveLiveSprite(zoe, clip);
            if (liveSprite == null)
            {
                box.Add(Z.Text("No previewable frame on this Zoe's view yet — nothing to sample.", ZuiText.Subtle,
                    "The Zoe's view has no clip/frame to preview, so the live sprite it would sample at runtime " +
                    "can't be shown here."));
                body.Add(box);
                return;
            }

            var row = Z.Row();
            row.Add(ThumbOf(liveSprite, "The frame standing in for the Zoe's CURRENT sprite while authoring — " +
                "the runtime samples whatever frame is actually showing at burst time, which changes as the Zoe " +
                "animates."));

            var side = new VisualElement();
            side.style.marginLeft = 6f;
            row.Add(side);

            if (sampleColours)
            {
                side.Add(Z.Text("Palette sampled from this frame:", ZuiText.Subtle,
                    "The colours DebrisScatter tints its chunks with, sampled live off this sprite each burst."));
                var swatches = Z.Row();
                foreach (var c in SampleColours(liveSprite, sampleCount))
                {
                    var sw = new VisualElement();
                    sw.style.width = 16f; sw.style.height = 16f; sw.style.marginRight = 2f;
                    sw.style.backgroundColor = c;
                    swatches.Add(sw);
                }
                if (swatches.childCount == 0)
                    swatches.Add(Z.Text("(texture not Read/Write enabled)", ZuiText.Subtle,
                        "Tick 'Read/Write Enabled' on the sprite's import settings to sample its pixels."));
                side.Add(swatches);
            }
            else
            {
                side.Add(Z.Text("Colour sampling is off for this effect (uses the recipe's own colours).",
                    ZuiText.Subtle, "'Sample lauminary colours' is unticked on this effect."));
            }

            var debris = FindSampledDebris(chunks);
            if (debris != null)
            {
                side.Add(Z.Text("Example pieces this Debris Scatter would cut:", ZuiText.Subtle,
                    "A few example sampled cuts — actual bursts pick fresh random spots each time."));
                var pieces = Z.Row();
                int ppu = Mathf.Max(1, Mathf.RoundToInt(debris.EffectivePixelsPerUnit));
                for (int i = 0; i < 5; i++)
                {
                    var cut = SampledChunkSprites.Sample(liveSprite, debris.samplePxMin, debris.samplePxMax, ppu,
                        debris.tintMode, debris.tintColor, debris.tintStrength, debris.edgeThicknessPx, debris.modifiers);
                    if (cut == null) continue;
                    pieces.Add(ThumbOf(cut, "One example cut. A real burst samples fresh random spots.", 28f));
                }
                if (pieces.childCount == 0)
                    pieces.Add(Z.Text("(no opaque pixels found to cut)", ZuiText.Subtle,
                        "Every attempted cut landed on empty space, or the texture isn't Read/Write enabled."));
                side.Add(pieces);
            }
            else if (chunks != null)
            {
                side.Add(Z.Text("This recipe has no Sampled-visual Debris Scatter — nothing else to preview.",
                    ZuiText.Subtle, "Add a Debris Scatter capability set to 'Sampled' to cut pieces from this sprite."));
            }

            box.Add(row);
            body.Add(box);
        }

        // A representative frame standing in for "the Zoe's current sprite" while authoring — the reaction's own
        // clip (mid-clip, so a directional/attack pose reads better than the very first frame) when it has one,
        // else the view's own default preview. Same duck-typed view interfaces ZoeEventVisual.Of reads, without
        // needing a full ReactionFx (only its clip name matters here).
        static Sprite ResolveLiveSprite(Zoe zoe, string clip)
        {
            if (zoe == null || zoe.view == null) return null;
            Sprite[] frames = zoe.view is IClipPreviewableView byClip ? byClip.PreviewFrames(clip ?? "")
                             : zoe.view is IPreviewableView plain ? plain.PreviewFrames()
                             : null;
            if (frames == null || frames.Length == 0) return null;
            return frames[frames.Length / 2];
        }

        /// The first enabled Sampled-visual Debris Scatter in the recipe, or null. Mirrors the priority a real
        /// burst would give an ambiguous recipe (first match wins) without claiming to resolve which ONE the
        /// runtime actually fires (a recipe can carry several).
        static DebrisScatter FindSampledDebris(ChunkSpec chunks)
        {
            if (chunks == null || chunks.capabilities == null) return null;
            foreach (var cap in chunks.capabilities)
                if (cap is DebrisScatter d && d.enabled && d.visual == DebrisVisual.Sampled) return d;
            return null;
        }

        /// Up to `count` opaque colours off a sprite's own pixels — the editor-preview twin of SpawnChunkFx's
        /// private SampleRenderer, minus the SpriteRenderer tint (nothing live to read one from here). Returns
        /// empty, never throws, when the texture isn't Read/Write enabled.
        static List<Color> SampleColours(Sprite sprite, int count)
        {
            var out_ = new List<Color>(count);
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable) return out_;
            Rect tr = sprite.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            if (w <= 0 || h <= 0) return out_;
            Color[] block;
            try { block = sprite.texture.GetPixels(x, y, w, h); } catch (UnityException) { return out_; }
            int stride = Mathf.Max(1, block.Length / (count * 8));
            for (int i = 0; i < block.Length && out_.Count < count; i += stride)
            {
                var c = block[i];
                if (c.a <= 40f / 255f) continue;
                out_.Add(c);
            }
            return out_;
        }

        static VisualElement ThumbOf(Sprite sprite, string tooltip, float size = LivePreviewThumbPx)
        {
            var slot = new VisualElement { tooltip = tooltip };
            slot.style.width = size; slot.style.height = size; slot.style.marginRight = 4f;
            slot.style.backgroundColor = new Color(0.11f, 0.12f, 0.15f);
            var image = new UnityEngine.UIElements.Image { scaleMode = ScaleMode.ScaleToFit, sprite = sprite };
            image.style.width = size; image.style.height = size;
            slot.Add(image);
            return slot;
        }

        // EnumPicker moved to ZoetropeDefWindow (the base) 2026-08-02, so the base's generic managed-ref child
        // drawer (TryBuildPlaybackMode) can use it too. It still draws exactly here, unchanged.

        // ── Zoe-event effect palette metadata (nice label / tooltip / icon / menu section per IEffect kind) ─────
        // Keyed by TYPE NAME (a string), not the concrete Type, so this core editor stays decoupled from the
        // bridge modules that own the Pyre/Chunks effect types (the Zoetrope editor asmdef deliberately does not
        // reference ZoetropePyre). An unknown kind falls back to its nicified name under "More".
        struct EffectMetaInfo { public string label, tooltip, icon, section; }

        static readonly Dictionary<string, EffectMetaInfo> s_effectMeta = new Dictionary<string, EffectMetaInfo>
        {
            ["SpawnPyreFx"] = new EffectMetaInfo { label = "Spawn Pyre", icon = "flame", section = "Spawn VFX",
                tooltip = "Play a Pyre blast at a position param, sized by a scalar param." },
            ["SpawnChunkFx"] = new EffectMetaInfo { label = "Spawn Chunks", icon = "shapes", section = "Spawn VFX",
                tooltip = "Throw a Chunks debris burst — aimed by a direction, sized by a scalar, colour-sampled from the Zoe's live lauminary." },
            ["PyreChunksFx"] = new EffectMetaInfo { label = "Pyre + Chunks", icon = "bomb", section = "Spawn VFX",
                tooltip = "The bundled blast + debris effect (the original combined VFX; still used by committed assets)." },
            ["BodySpriteFxEffect"] = new EffectMetaInfo { label = "Body SpriteFx", icon = "sparkle", section = "On the Zoe",
                tooltip = "Flash / tint / dissolve the Zoe's own sprite (a SpriteFx stack on the body renderer) — " +
                          "played once, looped for the event, run at its end, or ping-ponged." },
            ["PlayZoundEffect"] = new EffectMetaInfo { label = "Zound", icon = "speaker-high", section = "On the Zoe",
                tooltip = "Play a Zound. Right-click the picker to hear the current one." },
            ["InvulnerableEffect"] = new EffectMetaInfo { label = "Invulnerable", icon = "shield", section = "On the Zoe",
                tooltip = "Grant the Zoe i-frames for a moment — a hit that buys recovery, or a death that stops the corpse being shot apart." },
            ["PushbackEffect"] = new EffectMetaInfo { label = "Pushback", icon = "arrow-fat-right", section = "On the Zoe",
                tooltip = "Knock the Zoe a set DISTANCE over a set DURATION along a direction param." },
            ["PlayLauminationEffect"] = new EffectMetaInfo { label = "Play Lauminary", icon = "film-lauminary", section = "On the Zoe",
                tooltip = "Play a named clip on the Zoe's animated view." },
        };

        static readonly string[] s_sectionOrder = { "Spawn VFX", "On the Zoe", "More" };

        static (string label, string tooltip, string icon, string section) EffectMetaFor(object effect)
        {
            if (effect == null) return ("(no effect)", "No effect chosen yet — pick a kind, or use Add effect below.", null, "");
            if (s_effectMeta.TryGetValue(effect.GetType().Name, out var m)) return (m.label, m.tooltip, m.icon, m.section);
            string nn = ObjectNames.NicifyVariableName(effect.GetType().Name);
            return (nn, nn + " effect.", null, "More");
        }

        /// Which typed in-params an effect reads, so the editor shows exactly those pickers. An effect declaring
        /// <see cref="IEventParamUser"/> is authoritative; a legacy <see cref="ICombatFx"/> (spawn-VFX-at-a-point)
        /// that predates the interface defaults to Position + Direction so its placement authoring is unchanged;
        /// anything else reads nothing (Play-Lauminary, Body-SpriteFx).
        static EventParam UsedParamsOf(object effect)
        {
            if (effect == null) return EventParam.None;
            if (effect is IEventParamUser u) return u.UsedParams;
            if (effect is ICombatFx) return EventParam.Position | EventParam.Direction;
            return EventParam.None;
        }

        // Every concrete IEffect kind the Add menu offers, discovered by reflection (so a new kind appears with no
        // hand-maintained list) and ordered by section. Scanned once per domain and cached, like SpriteFxStackView.
        static List<(Type type, EffectMetaInfo meta)> s_addableEffects;
        static IEnumerable<(Type type, EffectMetaInfo meta)> AddableEffects()
        {
            if (s_addableEffects != null) return s_addableEffects;
            var found = new List<(Type, EffectMetaInfo)>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<IEffect>())
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                // The combined "Spawn Pyre Chunk" (PyreChunksFx) is retired from the palette (task #1): Spawn Pyre +
                // Spawn Chunk stack, so the bundle is redundant. The TYPE stays (committed assets still deserialize
                // it, and the per-card type switcher can still show it); it's just no longer offered in Add-effect.
                if (t.Name == "PyreChunksFx") continue;
                if (!s_effectMeta.TryGetValue(t.Name, out var m))
                {
                    string nn = ObjectNames.NicifyVariableName(t.Name);
                    m = new EffectMetaInfo { label = nn, tooltip = nn + " effect.", icon = null, section = "More" };
                }
                found.Add((t, m));
            }
            found.Sort((a, b) =>
            {
                int sa = Array.IndexOf(s_sectionOrder, a.Item2.section); if (sa < 0) sa = int.MaxValue;
                int sb = Array.IndexOf(s_sectionOrder, b.Item2.section); if (sb < 0) sb = int.MaxValue;
                return sa != sb ? sa.CompareTo(sb) : string.CompareOrdinal(a.Item2.label, b.Item2.label);
            });
            s_addableEffects = found;
            return s_addableEffects;
        }

        void ShowAddEffectMenu(VisualElement anchor, string fxPath)
        {
            var menu = Z.Menu(anchor).Width(240f);
            string lastSection = null;
            foreach (var (type, m) in AddableEffects())
            {
                if (m.section != lastSection) { menu.Section(m.section); lastSection = m.section; }
                var t = type;
                menu.Item(m.label, m.tooltip, () => AddEffect(fxPath, t), icon: m.icon);
            }
            menu.Show();
        }

        void AddEffect(string fxPath, Type type)
        {
            Commit(fxPath, p =>
            {
                p.arraySize++;
                // Unity's array growth DUPLICATES the previous last element for a plain-class array — reset every
                // field explicitly so a new entry starts clean, then assign the chosen concrete effect.
                //
                // Via Field(), not FindPropertyRelative directly: a renamed field makes that return NULL and the
                // assignment then throws a bare NullReferenceException naming only a line number. That is exactly
                // how this broke when FxEntry.eventName became `frame` — the card UI was updated and this reset
                // was not. Now a stale name says which name, once, and the rest of the entry still initialises.
                var e = p.GetArrayElementAtIndex(p.arraySize - 1);
                var trigger = Field(e, "trigger"); if (trigger != null) trigger.enumValueIndex = (int)FxTriggerType.Immediate;
                var frame = Field(e, "frame"); if (frame != null) frame.intValue = 1;
                var placement = Field(e, "placement"); if (placement != null) placement.enumValueIndex = (int)FxPlacementType.HitPosition;
                var layer = Field(e, "metaLayerId"); if (layer != null) layer.stringValue = "";
                var dir = Field(e, "direction"); if (dir != null) dir.enumValueIndex = (int)DirectionParam.HitDirection;
                var scalar = Field(e, "scalar"); if (scalar != null) scalar.enumValueIndex = (int)ScalarParam.Amount;
                var follow = Field(e, "follow"); if (follow != null) follow.boolValue = false;
                var enabled = Field(e, "enabled"); if (enabled != null) enabled.boolValue = true;
                var inst = Activator.CreateInstance(type);
                // NEW Body-SpriteFx cards start on Loop (the design default: "loop for the event's duration").
                // Set at the creation site — never via the class's field initializer — so pre-existing serialized
                // entries, which never wrote a playback field, keep deserializing to Once (enum 0) and behave
                // exactly as before.
                if (inst is BodySpriteFxEffect bodyFx) bodyFx.playback = FxPlaybackMode.Loop;
                var fx = Field(e, "fx"); if (fx != null) fx.managedReferenceValue = inst;
            });
            Rebuild();
        }

        /// How many serialized fields the concrete effect itself declares — what decides whether it can live
        /// on one line. Counted rather than hard-coded per type, so a new one-field effect gets the tight
        /// treatment without anyone remembering to add it to a list.
        static int CountOwnFields(SerializedProperty managedRef)
        {
            if (managedRef == null) return 0;
            int n = 0;
            var end = managedRef.GetEndProperty();
            var child = managedRef.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end)) { enter = false; n++; }
            return n;
        }

        /// A relative property, or null WITH a named warning. Silent nulls from a renamed field are how a
        /// reset like AddEffect's turns into an unexplained NullReferenceException.
        static SerializedProperty Field(SerializedProperty owner, string name)
        {
            var p = owner.FindPropertyRelative(name);
            if (p == null) Debug.LogWarning($"[Zoetrope] FxEntry has no field '{name}' — it was probably renamed. Skipping it.");
            return p;
        }

        VisualElement StringDropdown(SerializedProperty prop, string label, string[] options, string tooltip)
        {
            if (options == null || options.Length == 0)
                return Z.Field(label, tooltip, Z.Text("(none authored)", ZuiText.Subtle, tooltip));

            var choices = options.ToList();
            int current = choices.IndexOf(prop.stringValue);
            string path = prop.propertyPath;
            return Z.Field(label, tooltip, Z.Dropdown(Mathf.Max(current, 0), choices, tooltip,
                i => Commit(path, p => p.stringValue = choices[Mathf.Clamp(i, 0, choices.Count - 1)]),
                FitWidth(choices)));
        }

        /// Size a dropdown against its OWN longest option, so nothing clips whichever one is picked — the
        /// retained-mode equivalent of the IMGUI original's ZUI.FitWidth call.
        ///
        /// Measured by character count on purpose. `EditorStyles.label.CalcSize` would be exact, but
        /// EditorStyles is only valid inside an IMGUI pass and is NULL while a retained panel is being built
        /// (real crash: a NullReferenceException out of CreateGUI, which left the whole window blank).
        const float ApproxCharWidth = 7.5f;      // ~12px editor font, averaged
        const float DropdownChrome = 30f;        // the arrow + the field's own padding
        static float FitWidth(IList<string> options, float min = 90f, float max = 260f)
        {
            int longest = 0;
            foreach (var o in options) longest = Mathf.Max(longest, (o ?? "").Length);
            return Mathf.Clamp(longest * ApproxCharWidth + DropdownChrome, min, max);
        }

        /// Every authored FrameEvent name (de-duplicated) on the named clip — reflection duck-typing, same
        /// shape as GetClipNameOptions, so this stays decoupled from Launimator.
        /// How many frames the named clip has, or 0 if it cannot be resolved. Bounds the On Frame picker so
        /// an author picks a frame that EXISTS instead of typing a number into the dark.
        static int GetFrameCount(object view, string clipName)
        {
            var anim = FindAnimationByName(view, clipName);
            var frames = anim != null ? GetFieldValue(anim, "frames") as System.Collections.ICollection : null;
            return frames?.Count ?? 0;
        }

        static string[] GetEventNames(object view, string clipName)
        {
            var anim = FindAnimationByName(view, clipName);
            var events = anim != null ? GetFieldValue(anim, "events") as IEnumerable : null;
            if (events == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var e in events)
            {
                var n = GetFieldValue(e, "name") as string;
                if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            }
            return names.ToArray();
        }

        /// Every Point- or Vector-mode MetaLayer id on the named clip — the two kinds a MetaPoint placement can
        /// sample (EventContext.TryResolveMetaPoint asks the data which one it is). Shape layers are excluded —
        /// their centroid isn't a meaningful "the hit origin" marker.
        static string[] GetPointLayerIds(object view, string clipName)
        {
            var anim = FindAnimationByName(view, clipName);
            var layers = anim != null ? GetFieldValue(anim, "metaLayers") as IEnumerable : null;
            if (layers == null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var L in layers)
            {
                var mode = GetFieldValue(L, "mode");
                var id = GetFieldValue(L, "id") as string;
                if (!string.IsNullOrEmpty(id) && mode != null && (mode.ToString() == "Point" || mode.ToString() == "Vector")) ids.Add(id);
            }
            return ids.ToArray();
        }

        static object FindAnimationByName(object view, string clipName)
        {
            if (view == null || string.IsNullOrEmpty(clipName)) return null;
            var version = GetFieldValue(view, "version");
            var animations = version != null ? GetFieldValue(version, "animations") as IEnumerable : null;
            if (animations == null) return null;
            foreach (var a in animations)
                if (string.Equals(GetFieldValue(a, "name") as string, clipName, StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }
    }

    /// Hand-curated rather than the base's generic loop: several short, related scalar fields that would
    /// otherwise each claim a full row. muzzleOffset is a genuine spatial X/Y pair (a directional offset, not
    /// a row-packing case) — a drag pad, not two float fields.
    public class WeaponDefWindow : ZoetropeDefWindow<WeaponDef>
    {
        [MenuItem("Laubrary/Zoetrope/Weapons")]
        public static void Open() => GetWindow<WeaponDefWindow>("Weapons");
        protected override string TypeLabel => "Weapon";
        protected override string NewAssetName => "Weapon";

        /// Same entry-point shape as ZoeWindow.OpenFor — lets a LauAssetField's Edit button jump straight in.
        public static void OpenFor(WeaponDef weapon)
        {
            var w = GetWindow<WeaponDefWindow>("Weapons");
            if (weapon != null) w.SetAsset(weapon);
        }

        protected override void BuildBody(VisualElement root, WeaponDef w)
        {
            var identity = Z.Section("Identity", "What this weapon is called in-game.");
            identity.Add(Z.Field("Display Name", "The name shown to the player.",
                Z.TextInput(w.displayName, "The name shown to the player.",
                    v => Commit("displayName", p => p.stringValue = v), ScalarFieldWidth)));
            root.Add(identity);

            var fire = Z.Section("Fire", "How fast and how hard this weapon shoots.");
            fire.Add(Z.Row(
                NumField("Fire Rate", "fireRate", w.fireRate, "Shots per second.", v => Mathf.Max(0.01f, v)),
                Z.HSpace(),
                NumField("Damage", "damage", w.damage, "Damage each projectile deals on hit.", v => Mathf.Max(0f, v))));
            fire.Add(Z.Row(
                NumField("Speed", "projectileSpeed", w.projectileSpeed, "How fast each projectile travels.",
                    v => Mathf.Max(0f, v)),
                Z.HSpace(),
                NumField("Spread", "spreadDeg", w.spreadDeg, "Cone of random aim error, in degrees.",
                    v => Mathf.Clamp(v, 0f, 180f)),
                Z.HSpace(),
                IntFieldClamped("Count", "projectilesPerShot", w.projectilesPerShot,
                    "How many projectiles leave the muzzle per shot.", v => Mathf.Max(1, v), 50f)));
            root.Add(fire);

            BuildObjectListProperty(root, So.FindProperty("ammoTypes"), typeof(AmmoDef), "Ammo Type");

            var muzzle = Z.Section("Muzzle", "The flash/smoke played at the muzzle each shot. WHERE it plays " +
                "(which part, which MetaLayer) is each equipping Zoe's own choice now — see that Zoe's Weapons " +
                "list — so this same weapon stays equippable by any character.");
            BuildManagedRef(muzzle, So.FindProperty("muzzle"), "Effect");
            muzzle.Add(Z.Text("Offset", ZuiText.Subtle,
                "Fallback muzzle position for a shooter with no meta-layers — x is forward along the aim, y is up."));
            muzzle.Add(Z.Pad(w.muzzleOffset, new Rect(-2f, -2f, 4f, 4f),
                "Fallback muzzle position for a shooter with no meta-layers — x is forward along the aim, y is up.",
                v => Commit("muzzleOffset", p => p.vector2Value = v), 70f));
            root.Add(muzzle);

            var audio = Z.Section("Audio", "What this weapon sounds like.");
            string zoundLabel = string.IsNullOrEmpty(w.fireZoundName) ? "— zound —" : w.fireZoundName;
            const string zoundTip = "Zound played once per successful shot, at the same moment as the muzzle effect. Click to pick.";
            var zoundButton = Z.Button(zoundLabel, zoundTip, null);
            zoundButton.style.width = 160f;
            zoundButton.clicked += () => ZoundPickerPopup.Show(zoundButton.worldBound.position, picked =>
            {
                // The popup's callback fires on a LATER event than the click that opened it, so it commits
                // (and rebuilds) on its own rather than riding on this build pass.
                Commit("fireZoundName", p => p.stringValue = picked);
                Rebuild();
            });
            audio.Add(Z.Field("Fire Zound", zoundTip, zoundButton));
            root.Add(audio);
        }
    }

    public class AmmoDefWindow : ZoetropeDefWindow<AmmoDef>
    {
        [MenuItem("Laubrary/Zoetrope/Ammo")]
        public static void Open() => GetWindow<AmmoDefWindow>("Ammo");
        protected override string TypeLabel => "Ammo";
        protected override string NewAssetName => "Ammo";

        /// Same entry-point shape as ZoeWindow.OpenFor — lets a LauAssetField's Edit button jump straight in.
        public static void OpenFor(AmmoDef ammo)
        {
            var w = GetWindow<AmmoDefWindow>("Ammo");
            if (ammo != null) w.SetAsset(ammo);
        }

        protected override void BuildBody(VisualElement root, AmmoDef a)
        {
            var identity = Z.Section("Identity", "What this ammo is called in-game.");
            identity.Add(Z.Field("Display Name", "The name shown to the player.",
                Z.TextInput(a.displayName, "The name shown to the player.",
                    v => Commit("displayName", p => p.stringValue = v), ScalarFieldWidth)));
            root.Add(identity);

            var visual = Z.Section("Visual", "What the projectile looks like in flight.");
            const string visualTip = "A Pyre blast or a Lauminary animation — anything a chunk can play. Only its first frame shows on the projectile today.";
            visual.Add(LauAssetElement.Build(a.visual,
                picked => { Commit("visual", p => p.objectReferenceValue = picked); Rebuild(); },
                typeof(IChunkAnimation), FieldThumbs,
                string.IsNullOrWhiteSpace(a.displayName) ? a.name : a.displayName,
                "Assets/Zoetrope/AmmoVisuals", visualTip));

            visual.Add(NumField("Scale", "scale", a.scale, "Size multiplier applied to the projectile's sprite."));
            visual.Add(Z.Row(
                Z.Toggle("Spin", "Spin the sprite while it flies (rotating rocket / grenade).", a.spin,
                    v => { Commit("spin", p => p.boolValue = v); Rebuild(); }),
                Z.HSpace(),
                NumField("Speed", "spinSpeed", a.spinSpeed, "Spin rate in degrees per second."),
                Z.HSpace(),
                Z.Toggle("Face Travel", "Point the sprite along its travel direction (bolts/arrows).",
                    a.faceTravel, v => Commit("faceTravel", p => p.boolValue = v))));
            root.Add(visual);

            var flight = Z.Section("Flight", "How long the projectile lives and how it moves.");
            flight.Add(Z.Row(
                NumField("Lifetime", "lifetime", a.lifetime, "Seconds before the projectile expires on its own.",
                    v => Mathf.Max(0.05f, v)),
                Z.HSpace(),
                Z.Toggle("Pierce", "Pass through targets instead of dying on the first hit.", a.pierce,
                    v => Commit("pierce", p => p.boolValue = v))));
            flight.Add(Z.Row(
                NumField("Gravity", "gravity", a.gravity, "Not wired into flight yet — a future ballistic motion would consume it."),
                Z.HSpace(),
                Z.Toggle("Homing", "Not wired into flight yet — a future homing motion would consume it.", a.homing,
                    v => Commit("homing", p => p.boolValue = v))));
            BuildManagedRef(flight, So.FindProperty("motion"), "Motion");
            root.Add(flight);

            var impact = Z.Section("Impact", "What plays where the projectile hits.");
            BuildManagedRef(impact, So.FindProperty("impact"), "Effect");
            root.Add(impact);
        }
    }
}

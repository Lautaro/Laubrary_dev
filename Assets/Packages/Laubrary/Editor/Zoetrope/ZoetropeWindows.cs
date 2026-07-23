using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;
using Laubrary.Chunks;
using Laubrary.Combat2D;
using Laubrary.LaunimatorZounds.Editor;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The enemy-authoring hub: browse / create / duplicate / rename / delete Zoetrope Defs, and configure the
    /// selected one — all from the shared AssetKit base (empty-state shows the library). The per-asset body is drawn
    /// through a SerializedObject so Unity renders the pluggable <c>[SerializeReference]</c> pickers (the view type,
    /// the effect types) and edits get Undo for free.
    /// </summary>
    public abstract class ZoetropeDefWindow<T> : LaubraryAssetWindow<T> where T : ScriptableObject
    {
        protected override string DefaultFolder => "Assets/Zoetrope";

        SerializedObject _so;

        // A scalar UnityEngine.Object-reference field (e.g. a Zoe field pointing at a WeaponDef) goes through
        // LauAssetField (thumbnail + Recall/New/Edit) instead of Unity's bare default ObjectField — same
        // "presented everywhere" treatment the rest of Laubrary's LauAsset fields already got. A List<T> of
        // Object references (e.g. Zoe.weapons) gets the same treatment per-element via DrawObjectListProperty
        // below — NextVisible(false) treats the whole list as one opaque Generic-typed property, never
        // descending into its elements on its own, so that method reaches in via GetArrayElementAtIndex
        // instead of relying on this loop's own NextVisible cursor.
        readonly Dictionary<Object, Texture2D> _fieldThumbs = new Dictionary<Object, Texture2D>();

        // SerializedObject-driven so every field edit is Undo-able for free. [SerializeReference] fields
        // (the "which ICharacterView" type picker) go through ZUI.PolymorphicFoldout — a real, single-row
        // fold+type-switcher control (built 2026-07-20; it existed only in zui.md's docs before that, which
        // is why this used to fall back to Unity's default PropertyField rendering, whose own type-switch
        // affordance for a managed reference is genuinely hard to find/use).
        protected override void DrawAsset(T asset)
        {
            if (_so == null || _so.targetObject != asset) _so = new SerializedObject(asset);
            _so.Update();
            var it = _so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;

                if (it.propertyType == SerializedPropertyType.ManagedReference)
                {
                    bool open = ZUI.PolymorphicFoldout(it, out var boxedValue);
                    if (open && boxedValue != null) DrawManagedReferenceChildren(it, boxedValue, asset);
                    continue;
                }

                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var fieldType = GetFieldType(asset.GetType(), it.name);
                    if (fieldType != null && typeof(Object).IsAssignableFrom(fieldType))
                    {
                        DrawSingleAssetRefField(it.Copy(), fieldType, asset);
                        continue;
                    }
                }

                // A List<T> of UnityEngine.Object references (e.g. Zoe.weapons) — NextVisible(false) treats
                // the whole list as one opaque Generic/isArray property (see this class's own doc comment
                // above), so it never reaches the ObjectReference branch above as individual elements. Was
                // falling through to Unity's default reorderable-list PropertyField (bare ObjectField per
                // row, no thumbnail/Recall/New/Edit) before this — the same "presented everywhere" LauAsset
                // treatment the scalar case above already gets.
                if (it.isArray && it.propertyType == SerializedPropertyType.Generic)
                {
                    var fieldType = GetFieldType(asset.GetType(), it.name);
                    var elemType = fieldType != null && fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>)
                        ? fieldType.GetGenericArguments()[0] : null;
                    if (elemType != null && typeof(Object).IsAssignableFrom(elemType))
                    {
                        DrawObjectListProperty(it.Copy(), elemType, ObjectNames.NicifyVariableName(it.name));
                        continue;
                    }
                }

                DrawScalarField(it);
            }
            _so.ApplyModifiedProperties();
        }

        // Draws every child field of an expanded [SerializeReference] value, indented — PolymorphicFoldout
        // only draws the header row itself, matching zui.md's stated convention (the caller indents whatever
        // it draws below the open foldout). No curated per-concrete-type layout here (that's
        // `ZUI.StackedField`/a registered-forms pattern for a specific tool that wants one) — this generic
        // window just lists whatever fields the chosen concrete type actually has, EXCEPT a field literally
        // named "idleClip" (or "clip"), which gets a Popup of clip names instead of a free-text field —
        // see TryDrawClipPopup.
        /// Draws a single [SerializeReference] property as a PolymorphicFoldout + its expanded children —
        /// the piece WeaponDefWindow/AmmoDefWindow reuse directly for their own hand-curated layouts (muzzle/
        /// motion/impact) instead of relying on the generic per-property DrawAsset loop above.
        /// <paramref name="topLevelAsset"/> (optional) is the OWNING asset (e.g. the Zoe itself) — passed
        /// through to TryDrawClipPopup as a fallback clip-name source for a boxed value with no "version"
        /// field of its own (e.g. Zoe.hitReaction's SimpleHitReaction) — see that method's own doc comment.
        /// <param name="suppressHeader">Pass true when the caller already drew its own section label
        /// immediately above this call (see PolymorphicFoldout's own doc comment) — avoids the field's
        /// [Header] auto-drawing a second time right under it.</param>
        protected static void DrawManagedReferenceField(SerializedProperty prop, object topLevelAsset = null, bool suppressHeader = false)
        {
            if (prop == null) return;
            bool open = ZUI.PolymorphicFoldout(prop, out var boxedValue, suppressHeader);
            if (open && boxedValue != null) DrawManagedReferenceChildren(prop, boxedValue, topLevelAsset);
        }

        static void DrawManagedReferenceChildren(SerializedProperty managedRefProperty, object boxedValue, object topLevelAsset)
        {
            // If the boxed concrete type has its OWN registered [CustomPropertyDrawer] (e.g. ZonedReelViewDrawer
            // for its "Open in Animation Builder" button), invoke that drawer DIRECTLY via a manually-reserved
            // Rect instead of routing through EditorGUILayout.PropertyField -- calling PropertyField on this
            // SAME property re-triggers Unity's own [Header] DECORATOR rendering for the field (decorators fire
            // unconditionally off the field's attributes, independent of the GUIContent passed and independent
            // of PolymorphicFoldout's own suppressHeader, which only controls ZUI's OWN header echo). Real bug:
            // Zoe.view carries [Header("Look")] — ZoeWindow already draws its own "Look" section label above
            // this call, so the decorator re-firing here showed "Look" a second time, right inside the open
            // foldout, even with suppressHeader:true. Calling the drawer's OnGUI directly bypasses Unity's
            // ScriptAttributeUtility (and its decorator pass) entirely.
            var drawerType = GetCustomPropertyDrawerType(boxedValue.GetType());
            if (drawerType != null)
            {
                var drawer = (PropertyDrawer)System.Activator.CreateInstance(drawerType);
                float height = drawer.GetPropertyHeight(managedRefProperty, GUIContent.none);
                Rect rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, height));
                EditorGUI.indentLevel++;
                drawer.OnGUI(rect, managedRefProperty, GUIContent.none);
                EditorGUI.indentLevel--;
                return;
            }

            // EditorGUI.indentLevel, not a manual GUILayout.Space(15f) inside a forced HorizontalScope per
            // child (the original shape) -- that forced EVERY special-case renderer onto one shared row with
            // whatever came before it, which broke TryDrawAssetRefField's Label+LauAssetField.Draw pair (two
            // separate calls, LauAssetField.Draw opens its OWN internal row) into one clumped row with zero
            // gap between the label and the picker -- a real "Controls clumped with no breathing room"
            // violation, caught auditing against ui-layout-rules.md, and inconsistent with how a Label +
            // LauAssetField.Draw pair is used everywhere ELSE in this codebase (label on its own line, picker
            // below it). indentLevel is Unity's own mechanism for this and correctly applies to a renderer
            // that draws more than one row, unlike a one-shot Space call scoped to a single forced row.
            var end = managedRefProperty.GetEndProperty();
            var child = managedRefProperty.Copy();
            bool enter = true;
            EditorGUI.indentLevel++;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (TryDrawClipPopup(child, boxedValue, topLevelAsset)) continue;
                if (TryDrawAssetRefField(child, boxedValue)) continue;
                DrawScalarField(child);
            }
            EditorGUI.indentLevel--;
        }

        // A bare Float/Int PropertyField with no width option stretches to fill the whole window (measured
        // 604px in a 616px-wide ZoeWindow — the same "unconstrained default expand" shape as the Popup fix
        // above), for any field this generic per-child walk reaches (e.g. PyreChunksFx.blastFps/sortingOrder)
        // that isn't special-cased by TryDrawClipPopup/TryDrawAssetRefField above. A single total Width (label
        // + value together, same approach as the Popup fix — Unity splits the reserved label width out of it
        // correctly) keeps it compact; other property types (bool/string/enum/vector) are left at their
        // default layout, which doesn't have this blowout.
        const float ScalarFieldWidth = 220f;
        const float PairedScalarFieldWidth = 140f;   // beside a rich picker/another control — needs less budget

        static void DrawScalarField(SerializedProperty prop)
        {
            if (prop.propertyType != SerializedPropertyType.Float && prop.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUILayout.PropertyField(prop, true);
                return;
            }
            using (ZUI.HRow())
            {
                DrawScalarControl(prop, null, ScalarFieldWidth);
                GUILayout.FlexibleSpace();
            }
        }

        // Bare control only (no row/FlexibleSpace) — reused standalone (DrawScalarField above) and paired
        // beside another control (TryDrawAssetRefField's blast+FPS / chunks+sortingOrder pairing below).
        // ZUI.NarrowLabel is mandatory here: EditorGUIUtility.labelWidth is a GLOBAL ambient value, and without
        // pinning it to the label's own text width, Unity reserves its ambient (often much larger) labelWidth
        // out of the fixed `width` budget passed below, leaving the actual value control little or no room —
        // real bug: "FPS"/"Sorting Order" showed their label text but the field beside it was cut off/invisible
        // no matter how wide the window was, since the leftover space shrinks, not grows, as labelWidth grows.
        static void DrawScalarControl(SerializedProperty prop, string labelOverride, float width)
        {
            var content = string.IsNullOrEmpty(labelOverride) ? new GUIContent(ObjectNames.NicifyVariableName(prop.name)) : new GUIContent(labelOverride);
            using (ZUI.NarrowLabel(content.text))
                EditorGUILayout.PropertyField(prop, content, true, GUILayout.Width(width));
        }

        // A field on a [SerializeReference] value that's itself a reference to a LauAsset-registered
        // ScriptableObject type (e.g. PyreChunksFx.blast : Pyre, PyreChunksFx.chunks : ChunkSpec) was
        // rendering as Unity's bare default ObjectField — no thumbnail, no Recall browser, no New/Edit
        // shortcuts, unlike every OTHER LauAsset-typed field in this codebase (real bug, caught by inspection:
        // "if those are fields for picking a certain LauAsset type then why are they object pickers?").
        // Resolved by REFLECTION on the field's declared type (no compile-time reference to Pyre/Chunks —
        // same core/bridge decoupling TryDrawClipPopup's "version" duck-typing already keeps), scoped to
        // actual ScriptableObject Object-reference fields only, so this stays generically useful rather than
        // hardcoded to PyreChunksFx specifically. LauAssetField.Draw's own "✎ Edit" button already opens the
        // asset in its registered editor (PyreWindow for a Pyre, ChunkWindow for a ChunkSpec) — no
        // separate "Preview in X" button needed, unlike the Rect-based drawer this replaced.
        static readonly Dictionary<Object, Texture2D> _assetRefThumbs = new Dictionary<Object, Texture2D>();

        // Known asset-ref + trailing-scalar field-name pairs (PyreChunksFx: blast+blastFps, chunks+
        // sortingOrder) that read far better sharing one row than stacked, per user request — matched by NAME
        // only (not owning Type), keeping this core Zoetrope editor decoupled from any bridge module's
        // concrete types, same reflection-friendly approach TryDrawClipPopup's hurtClip+deathClip pairing (and
        // its own "version" duck-typing) already use. null scalarLabel = use the field's own nicified name.
        static readonly Dictionary<string, (string scalarName, string scalarLabel)> _assetRefScalarPairs =
            new Dictionary<string, (string, string)>
        {
            ["blast"] = ("blastFps", "FPS"),
            ["chunks"] = ("sortingOrder", null),
        };

        static bool TryDrawAssetRefField(SerializedProperty child, object boxedValue)
        {
            if (child.propertyType != SerializedPropertyType.ObjectReference) return false;
            var field = boxedValue.GetType().GetField(child.name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !typeof(ScriptableObject).IsAssignableFrom(field.FieldType)) return false;

            string label = ObjectNames.NicifyVariableName(child.name);
            ZUI.Label(label, ZUI.ZTextStyle.Subtle);
            var prop = child.Copy();

            SerializedProperty pairedScalar = null;
            string scalarLabel = null;
            if (_assetRefScalarPairs.TryGetValue(child.name, out var pair))
            {
                var peek = child.Copy();
                if (peek.NextVisible(false) && peek.name == pair.scalarName)
                {
                    pairedScalar = peek.Copy();
                    scalarLabel = pair.scalarLabel;
                }
            }

            using (ZUI.HRow())
            {
                LauAssetField.Draw(prop.objectReferenceValue, picked =>
                {
                    prop.serializedObject.Update();
                    prop.objectReferenceValue = picked;
                    prop.serializedObject.ApplyModifiedProperties();
                }, field.FieldType, _assetRefThumbs, label, "Assets");

                if (pairedScalar != null)
                {
                    ZUI.HorizontalSpace();
                    DrawScalarControl(pairedScalar, scalarLabel, PairedScalarFieldWidth);
                }
            }
            if (pairedScalar != null) child.NextVisible(false);   // consume the paired scalar too
            return true;
        }

        // Reflection-discovered, cached map of every type a [CustomPropertyDrawer] targets anywhere in the
        // loaded assemblies, to the drawer type itself (CustomPropertyDrawer.m_Type is private -- no public API
        // exposes "does this type have a custom drawer" directly, so this is the standard workaround). Scanned
        // once, not per-draw.
        static Dictionary<System.Type, System.Type> _customDrawerTypes;
        static readonly Dictionary<System.Type, System.Type> _customDrawerTypeCache = new Dictionary<System.Type, System.Type>();

        static System.Type GetCustomPropertyDrawerType(System.Type targetType)
        {
            if (targetType == null) return null;
            if (_customDrawerTypeCache.TryGetValue(targetType, out var cached)) return cached;

            if (_customDrawerTypes == null)
            {
                _customDrawerTypes = new Dictionary<System.Type, System.Type>();
                var typeField = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (!typeof(PropertyDrawer).IsAssignableFrom(t)) continue;
                        foreach (var attrObj in t.GetCustomAttributes(typeof(CustomPropertyDrawer), true))
                        {
                            var target = typeField?.GetValue((CustomPropertyDrawer)attrObj) as System.Type;
                            if (target != null && !_customDrawerTypes.ContainsKey(target)) _customDrawerTypes[target] = t;
                        }
                    }
                }
            }

            _customDrawerTypes.TryGetValue(targetType, out var drawerType);
            _customDrawerTypeCache[targetType] = drawerType;
            return drawerType;
        }

        // A field named "idleClip"/"clip"/"hurtClip"/"deathClip" on a [SerializeReference] value (e.g.
        // ReelView/ZonedReelView, or Zoe.hitReaction's SimpleHitReaction) is really picking one name out of a
        // Reel version's animation list — not free text. Resolved purely by REFLECTION over `boxedValue` (no
        // compile-time reference to Launimator/ZoetropeLaunimator types) so this stays a core-Zoetrope-editor
        // concern, decoupled from whatever bridge module actually provides the concrete view type — same
        // core-interface/bridge-implementation boundary the runtime side already keeps (ICharacterView,
        // ICueSink, ...). `boxedValue` itself (e.g. SimpleHitReaction) has no "version" field of its own to
        // duck-type against — falls back to `topLevelAsset.view`'s version (a hit reaction's clips are
        // authored on the same Reel the character's own view plays, in practice) when that's available.
        // Falls back further to a plain text field (returns false) when NEITHER shape is there, so this
        // degrades gracefully for any OTHER string field that happens to share one of these names.
        static bool TryDrawClipPopup(SerializedProperty child, object boxedValue, object topLevelAsset = null)
        {
            if (child.propertyType != SerializedPropertyType.String) return false;
            if (child.name != "idleClip" && child.name != "clip" && child.name != "hurtClip" && child.name != "deathClip")
                return false;

            var options = GetClipNameOptions(boxedValue);
            if (options == null && topLevelAsset != null)
            {
                var view = GetFieldValue(topLevelAsset, "view");
                if (view != null) options = GetClipNameOptions(view);
            }
            if (options == null) return false;

            bool optional = child.name == "hurtClip";
            string label = ObjectNames.NicifyVariableName(child.name);
            if (options.Length == 0)
            {
                EditorGUILayout.LabelField(label, "(no clips authored)");
                return true;
            }

            // hurtClip+deathClip (SimpleHitReaction's pair) read far better sharing one row than stacked, per
            // user request — paired only when deathClip immediately follows, so a lone hurtClip/idleClip/clip
            // still falls back to its own standalone row.
            SerializedProperty pairedDeathClip = null;
            if (child.name == "hurtClip")
            {
                var peek = child.Copy();
                if (peek.NextVisible(false) && peek.name == "deathClip") pairedDeathClip = peek.Copy();
            }
            float width = pairedDeathClip != null ? 180f : 260f;

            using (ZUI.HRow())
            {
                DrawClipPopupControl(child, options, optional, width);
                if (pairedDeathClip != null)
                {
                    ZUI.HorizontalSpace();
                    DrawClipPopupControl(pairedDeathClip, options, optional: false, width);
                }
                GUILayout.FlexibleSpace();
            }
            if (pairedDeathClip != null) child.NextVisible(false);   // consume deathClip too
            return true;
        }

        // Bare popup control only (no row/FlexibleSpace of its own) — shared by TryDrawClipPopup's standalone
        // and paired-with-deathClip cases above. Same ambient-labelWidth trap as DrawScalarControl above (see
        // its comment) — without NarrowLabel this is exactly why Hurt Clip/Death Clip rendered "tons of space"
        // but the popup itself showed only a single truncated letter: Unity's ambient labelWidth ate nearly all
        // of `width`, leaving almost nothing for the popup's own text.
        static void DrawClipPopupControl(SerializedProperty prop, string[] options, bool optional, float width)
        {
            string label = ObjectNames.NicifyVariableName(prop.name);
            string[] shown = optional ? new[] { "(none)" }.Concat(options).ToArray() : options;
            int current = System.Array.IndexOf(shown, string.IsNullOrEmpty(prop.stringValue) ? "(none)" : prop.stringValue);
            using (ZUI.NarrowLabel(label))
            {
                int chosen = EditorGUILayout.Popup(label, Mathf.Max(current, 0), shown, GUILayout.Width(width));
                string picked = shown[Mathf.Clamp(chosen, 0, shown.Length - 1)];
                prop.stringValue = optional && picked == "(none)" ? "" : picked;
            }
        }

        // Duck-types "an object with a `version` field pointing at something with an `animations` field
        // whose elements each have a `name`" — the exact ReelView/ZonedReelView + ReelVersion + AnimationDef
        // shape, without naming any of those types. Returns null (not an empty array) when the shape isn't
        // there at all, so the caller can tell "no clip picker makes sense here" apart from "picker applies,
        // there just aren't any clips yet."
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
            var field = owner.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(owner);
        }

        // Same reflection shape as GetFieldValue above, but for a top-level property's declared TYPE rather
        // than an instance's current value — SerializedProperty.name equals the C# field name exactly for a
        // top-level (non-nested, non-array-element) property, which is all this generic loop ever visits.
        static System.Type GetFieldType(System.Type ownerType, string fieldName)
        {
            var field = ownerType?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.FieldType;
        }

        // A scalar UnityEngine.Object-reference field (e.g. Zoe.faction) through LauAssetField instead of
        // Unity's bare default ObjectField — used both by the generic per-property loop above and directly by
        // a hand-curated DrawAsset override (e.g. ZoeWindow) that wants this one field placed on its own.
        protected void DrawSingleAssetRefField(SerializedProperty prop, System.Type fieldType, Object owningAsset)
        {
            string label = ObjectNames.NicifyVariableName(prop.name);
            ZUI.Label(label, ZUI.ZTextStyle.Subtle);
            LauAssetField.Draw(prop.objectReferenceValue, picked =>
            {
                prop.objectReferenceValue = picked;
                prop.serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(owningAsset);
            }, fieldType, _fieldThumbs, label, "Assets");
        }

        // A reorderable list of LauAsset elements (WeaponDef.ammoTypes, Zoe.weapons — anything List<T> where T
        // is a UnityEngine.Object) — one index-labelled row per element, each drawn through LauAssetField
        // (thumbnail + Recall/New ▾/Edit ✎) instead of the bare ObjectField Unity's default array drawer
        // would give it. Same per-element card shape RulesEditorWindow.DrawList already established for its
        // own (unrelated) reflection-driven list editor. Used both by the generic per-property loop above
        // (Zoe.weapons) and directly by WeaponDefWindow (ammoTypes, which has its own hand-curated DrawAsset
        // and never reaches that loop).
        //
        // Removing an ObjectReference array element needs Unity's documented two-step dance — DeleteArrayElementAtIndex
        // on a non-null object reference only nulls it out; the SECOND call actually removes the (now null)
        // slot — or the row silently "removes" the wrong thing (the array shrinks by one from the END, not
        // at the clicked index, while the clicked slot itself just goes blank).
        protected void DrawObjectListProperty(SerializedProperty listProp, System.Type elemType, string label, string folder = null)
        {
            folder ??= DefaultFolder;
            ZUI.Label($"{label}  ({listProp.arraySize})", ZUI.ZTextStyle.SectionHeader);

            int removeAt = -1;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var idx = i;
                var elemProp = listProp.GetArrayElementAtIndex(idx).Copy();
                using (ZUI.HRow())
                {
                    LauAssetField.Draw(elemProp.objectReferenceValue, picked =>
                    {
                        elemProp.objectReferenceValue = picked;
                        elemProp.serializedObject.ApplyModifiedProperties();
                    }, elemType, _fieldThumbs, $"{label} {idx}", folder);
                    ZUI.HorizontalSpace();
                    if (ZUI.Button(new GUIContent("X", $"Remove this {label} entry."), ZUI.Style.Default, GUILayout.Width(22f)))
                        removeAt = idx;
                    GUILayout.FlexibleSpace();
                }
            }
            if (removeAt >= 0)
            {
                var elem = listProp.GetArrayElementAtIndex(removeAt);
                if (elem.objectReferenceValue != null) elem.objectReferenceValue = null;   // see the two-step note above
                listProp.DeleteArrayElementAtIndex(removeAt);
                listProp.serializedObject.ApplyModifiedProperties();
            }

            ZUI.VerticalSpace(0.5f);
            if (ZUI.Button(new GUIContent($"+ Add {label}", $"Append another {label} entry.")))
            {
                listProp.arraySize++;
                listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = null;
                listProp.serializedObject.ApplyModifiedProperties();
            }
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
        /// elsewhere (e.g. Mirage's own entry Content field) jumps straight into its own editor.
        public static void OpenFor(Zoe zoe)
        {
            var w = GetWindow<ZoeWindow>("Zoes");
            if (zoe != null) w.SetAsset(zoe);
        }

        SerializedObject _so;

        // Hand-curated, matching the WeaponDefWindow/AmmoDefWindow precedent below — maxHealth/
        // invulnerableAfterHit are two short related Stats fields (measured 480px wide, full window, under
        // the fully-generic base loop) that read far better sharing one row than stacked. The base class's
        // generic per-property loop can't make that call itself (it has no notion that these two fields
        // "belong together" — see this class's own git history for the fuller reasoning), so this override
        // places them explicitly instead. Pluggable [SerializeReference] fields (view/hit/death/hitReaction/
        // brain) still go through the shared DrawManagedReferenceField helper for their foldout/Undo/clip-
        // popup behavior; loadout/cues are left on the base's default PropertyField rendering (a
        // [SerializeReference] list and a plain serializable list respectively — Unity's own list UI, not the
        // one-field-per-row problem this override exists to fix).
        protected override void DrawAsset(Zoe zoe)
        {
            if (_so == null || _so.targetObject != zoe) _so = new SerializedObject(zoe);
            _so.Update();

            ZUI.Label("Identity", ZUI.ZTextStyle.Header);
            zoe.displayName = EditorGUILayout.TextField("Display Name", zoe.displayName);

            ZUI.VerticalSpace();
            ZUI.Label("Stats", ZUI.ZTextStyle.Header);
            var statsForm = ZUI.Form();
            var statsRow = ZUI.Row("Max Health / Invuln. After Hit");
            statsRow.Add(70f, ZUI.FloatField(() => zoe.maxHealth, v => zoe.maxHealth = Mathf.Max(1f, v)));
            statsRow.Add(70f, ZUI.FloatField(() => zoe.invulnerableAfterHit, v => zoe.invulnerableAfterHit = Mathf.Max(0f, v)));
            statsForm.Add(statsRow);
            statsForm.Draw();
            DrawSingleAssetRefField(_so.FindProperty("faction"), typeof(Faction), zoe);

            ZUI.VerticalSpace();
            ZUI.Label("Look", ZUI.ZTextStyle.Header);
            DrawManagedReferenceField(_so.FindProperty("view"), zoe, suppressHeader: true);

            ZUI.VerticalSpace();
            ZUI.Label("Reactions", ZUI.ZTextStyle.Header);
            ZUI.Label("Hit", ZUI.ZTextStyle.SectionHeader);
            DrawReactionFx(_so.FindProperty("hit"), zoe);
            ZUI.VerticalSpace(0.5f);
            ZUI.Label("Death", ZUI.ZTextStyle.SectionHeader);
            DrawReactionFx(_so.FindProperty("death"), zoe);

            ZUI.VerticalSpace();
            ZUI.Label("AI", ZUI.ZTextStyle.Header);
            DrawManagedReferenceField(_so.FindProperty("brain"), zoe, suppressHeader: true);

            ZUI.VerticalSpace();
            // GUIContent.none suppresses the property's OWN foldout label (nicified from the field name,
            // "Loadout") -- Zoe.cs's [Header("Loadout")] decorator on this field still draws above it
            // regardless of the GUIContent passed (decorators fire off the field's attributes, independent of
            // the label — same trap already fixed for Zoe.view's "Look" duplicate), so leaving the label in
            // showed "Loadout" twice, stacked, for no reason.
            EditorGUILayout.PropertyField(_so.FindProperty("loadout"), GUIContent.none, true);

            ZUI.VerticalSpace();
            ZUI.Label("Weapons", ZUI.ZTextStyle.Header);
            DrawObjectListProperty(_so.FindProperty("weapons"), typeof(WeaponDef), "Weapon");
            var activeWeaponRow = ZUI.Row("Default Active Weapon");
            activeWeaponRow.Add(70f, ZUI.IntField(() => zoe.defaultActiveWeapon, v => zoe.defaultActiveWeapon = Mathf.Max(0, v)));
            ZUI.Form().Add(activeWeaponRow).Draw();

            ZUI.VerticalSpace();
            // Same "Header decorator + own foldout label" duplicate as loadout above -- Zoe.cs's [Header("Cues")].
            EditorGUILayout.PropertyField(_so.FindProperty("cues"), GUIContent.none, true);

            _so.ApplyModifiedProperties();
            if (GUI.changed) EditorUtility.SetDirty(zoe);
        }

        /// <summary>Draws one <see cref="ReactionFx"/> (Hit or Death): the Clip popup (sourced from the Zoe's
        /// own view, same reflection duck-typing <see cref="GetClipNameOptions"/> already uses elsewhere) plus
        /// the FX list — each entry packed onto 3 rows (Trigger+Placement together; a conditional Event/Layer
        /// row only when one of them actually needs it; Follow+Remove together) instead of one control per row,
        /// per the "wasting a lot of vertical space" flag. Every popup here is sized via <see cref="ZUI.FitWidth"/>
        /// against its OWN widest possible option instead of a guessed fixed pixel width, so nothing clips
        /// regardless of which option ends up selected.</summary>
        static void DrawReactionFx(SerializedProperty reactionProp, Zoe zoe)
        {
            var clipProp = reactionProp.FindPropertyRelative("clip");
            var fxListProp = reactionProp.FindPropertyRelative("fx");

            var clipOptions = zoe.view != null ? GetClipNameOptions(zoe.view) : null;
            using (ZUI.HRow())
            {
                if (clipOptions != null)
                {
                    string[] shown = new[] { "(none)" }.Concat(clipOptions).ToArray();
                    int current = System.Array.IndexOf(shown, string.IsNullOrEmpty(clipProp.stringValue) ? "(none)" : clipProp.stringValue);
                    float width = ZUI.FitWidth("Clip", Widest(shown), 120f, 320f);
                    using (ZUI.NarrowLabel("Clip"))
                    {
                        int chosen = EditorGUILayout.Popup("Clip", Mathf.Max(current, 0), shown, GUILayout.Width(width));
                        string picked = shown[Mathf.Clamp(chosen, 0, shown.Length - 1)];
                        clipProp.stringValue = picked == "(none)" ? "" : picked;
                    }
                }
                else
                {
                    float width = ZUI.FitWidth("Clip", clipProp.stringValue, 120f, 320f);
                    using (ZUI.NarrowLabel("Clip"))
                        EditorGUILayout.PropertyField(clipProp, new GUIContent("Clip"), GUILayout.Width(width));
                }
                GUILayout.FlexibleSpace();
            }

            string[] eventNames = GetEventNames(zoe.view, clipProp.stringValue);
            string[] pointLayerIds = GetPointLayerIds(zoe.view, clipProp.stringValue);

            ZUI.Label($"FX  ({fxListProp.arraySize})", ZUI.ZTextStyle.Subtle);
            int removeAt = -1;
            for (int i = 0; i < fxListProp.arraySize; i++)
            {
                var entryProp = fxListProp.GetArrayElementAtIndex(i);
                using (ZUI.Box($"FX {i + 1}"))
                    if (DrawFxEntry(entryProp, eventNames, pointLayerIds, zoe)) removeAt = i;
            }
            if (removeAt >= 0)
            {
                fxListProp.DeleteArrayElementAtIndex(removeAt);   // a plain [Serializable] element -- no two-step dance needed (that's an Object-reference-array-only quirk)
                fxListProp.serializedObject.ApplyModifiedProperties();
            }

            ZUI.VerticalSpace(0.25f);
            if (ZUI.Button(new GUIContent("+ Add FX", "Append another FX entry to this reaction.")))
            {
                fxListProp.arraySize++;
                // Unity's array growth DUPLICATES the previous last element's data for a plain-class array
                // (unlike an Object-reference array, which inserts null) -- reset explicitly so "+ Add FX"
                // always starts from a clean default instead of cloning whatever the last entry had configured.
                var newEntry = fxListProp.GetArrayElementAtIndex(fxListProp.arraySize - 1);
                newEntry.FindPropertyRelative("trigger").enumValueIndex = (int)FxTriggerType.Immediate;
                newEntry.FindPropertyRelative("eventName").stringValue = "";
                newEntry.FindPropertyRelative("placement").enumValueIndex = (int)FxPlacementType.HitPosition;
                newEntry.FindPropertyRelative("metaLayerId").stringValue = "";
                newEntry.FindPropertyRelative("follow").boolValue = false;
                newEntry.FindPropertyRelative("fx").managedReferenceValue = null;
                fxListProp.serializedObject.ApplyModifiedProperties();
            }
        }

        /// Returns true if the user clicked Remove.
        static bool DrawFxEntry(SerializedProperty entryProp, string[] eventNames, string[] pointLayerIds, Zoe zoe)
        {
            var triggerProp = entryProp.FindPropertyRelative("trigger");
            var eventNameProp = entryProp.FindPropertyRelative("eventName");
            var placementProp = entryProp.FindPropertyRelative("placement");
            var metaLayerIdProp = entryProp.FindPropertyRelative("metaLayerId");
            var followProp = entryProp.FindPropertyRelative("follow");
            var fxProp = entryProp.FindPropertyRelative("fx");

            using (ZUI.HRow())
            {
                DrawEnumPopupField(triggerProp, "Trigger");
                ZUI.HorizontalSpace();
                DrawEnumPopupField(placementProp, "Placement");
                GUILayout.FlexibleSpace();
            }

            bool showEvent = (FxTriggerType)triggerProp.enumValueIndex == FxTriggerType.FrameEvent;
            bool isMetaPoint = (FxPlacementType)placementProp.enumValueIndex == FxPlacementType.MetaPoint;
            bool isHitPosition = (FxPlacementType)placementProp.enumValueIndex == FxPlacementType.HitPosition;
            if (isHitPosition) followProp.boolValue = false;   // a fixed world point -- Follow has nothing to re-sample

            if (showEvent || isMetaPoint)
            {
                using (ZUI.HRow())
                {
                    if (showEvent) { DrawStringPopupField(eventNameProp, "Event", eventNames); ZUI.HorizontalSpace(); }
                    if (isMetaPoint) { DrawStringPopupField(metaLayerIdProp, "Layer", pointLayerIds); ZUI.HorizontalSpace(); }
                    GUILayout.FlexibleSpace();
                }
            }

            bool remove = false;
            using (ZUI.HRow())
            {
                using (new EditorGUI.DisabledScope(isHitPosition))
                    followProp.boolValue = EditorGUILayout.ToggleLeft(
                        new GUIContent("Follow", "Keep re-sampling Placement every frame and move the effect with it, instead of spawning once and letting it live on its own."),
                        followProp.boolValue, GUILayout.Width(60f));
                GUILayout.FlexibleSpace();
                if (ZUI.Button(new GUIContent("Remove", "Remove this FX entry."), ZUI.Style.Default, GUILayout.Width(70f)))
                    remove = true;
            }

            ZUI.VerticalSpace(0.15f);
            DrawManagedReferenceField(fxProp, zoe, suppressHeader: true);
            return remove;
        }

        static void DrawEnumPopupField(SerializedProperty prop, string label)
        {
            float width = ZUI.FitWidth(label, Widest(prop.enumDisplayNames), 90f, 260f);
            using (ZUI.NarrowLabel(label))
                EditorGUILayout.PropertyField(prop, new GUIContent(label), GUILayout.Width(width));
        }

        static void DrawStringPopupField(SerializedProperty prop, string label, string[] options)
        {
            if (options == null || options.Length == 0)
            {
                using (ZUI.NarrowLabel(label))
                    EditorGUILayout.LabelField(label, "(none authored)", GUILayout.Width(ZUI.FitWidth(label, "(none authored)")));
                return;
            }
            int current = System.Array.IndexOf(options, prop.stringValue);
            float width = ZUI.FitWidth(label, Widest(options), 90f, 260f);
            using (ZUI.NarrowLabel(label))
            {
                int chosen = EditorGUILayout.Popup(label, Mathf.Max(current, 0), options, GUILayout.Width(width));
                prop.stringValue = options[Mathf.Clamp(chosen, 0, options.Length - 1)];
            }
        }

        /// The widest string in <paramref name="options"/> by rendered label width — sizing a popup/field
        /// against this (via <see cref="ZUI.FitWidth"/>) guarantees no clipping no matter which option a user
        /// picks, not just whichever happens to be selected right now.
        static string Widest(IList<string> options)
        {
            if (options == null || options.Count == 0) return "";
            string best = options[0];
            float bestW = EditorStyles.label.CalcSize(new GUIContent(best ?? "")).x;
            for (int i = 1; i < options.Count; i++)
            {
                float w = EditorStyles.label.CalcSize(new GUIContent(options[i] ?? "")).x;
                if (w > bestW) { bestW = w; best = options[i]; }
            }
            return best ?? "";
        }

        /// Every authored FrameEvent name (de-duplicated) on the named clip — reflection duck-typing, same
        /// shape as GetClipNameOptions, so this stays decoupled from Launimator (see that method's own comment).
        static string[] GetEventNames(object view, string clipName)
        {
            var anim = FindAnimationByName(view, clipName);
            var events = anim != null ? GetFieldValue(anim, "events") as IEnumerable : null;
            if (events == null) return System.Array.Empty<string>();
            var names = new List<string>();
            foreach (var e in events)
            {
                var n = GetFieldValue(e, "name") as string;
                if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            }
            return names.ToArray();
        }

        /// Every Point-mode MetaLayer id on the named clip (Shape layers are excluded — their centroid isn't a
        /// meaningful "the hit origin" marker; see MetaLayerMode's own doc comment).
        static string[] GetPointLayerIds(object view, string clipName)
        {
            var anim = FindAnimationByName(view, clipName);
            var layers = anim != null ? GetFieldValue(anim, "metaLayers") as IEnumerable : null;
            if (layers == null) return System.Array.Empty<string>();
            var ids = new List<string>();
            foreach (var L in layers)
            {
                var mode = GetFieldValue(L, "mode");
                var id = GetFieldValue(L, "id") as string;
                if (!string.IsNullOrEmpty(id) && mode != null && mode.ToString() == "Point") ids.Add(id);
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
                if (string.Equals(GetFieldValue(a, "name") as string, clipName, System.StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }
    }

    // Hand-curated layouts for WeaponDef/AmmoDef — both have several short, related scalar fields that the
    // base class's generic one-property-per-row loop would otherwise lay out exactly like the "223 raw
    // calls, one field per row" anti-pattern authoring.md #11 was written to fix. ZoeWindow above got the
    // same treatment 2026-07-22 once its own Stats fields (maxHealth/invulnerableAfterHit) turned out to have
    // the identical problem — a stale version of this comment used to claim Zoe's fields didn't need it;
    // they did, just hadn't been measured yet. [SerializeReference] fields (muzzle/motion/impact) still go
    // through the shared DrawManagedReferenceField helper. muzzleOffset is a genuine
    // spatial X/Y pair (a directional offset, not a row-packing case) — PositionPad, not two FloatFields.
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

        SerializedObject _so;

        protected override void DrawAsset(WeaponDef w)
        {
            Undo.RecordObject(w, "Edit Weapon");

            ZUI.Label("Identity", ZUI.ZTextStyle.Header);
            w.displayName = EditorGUILayout.TextField("Display Name", w.displayName);

            ZUI.VerticalSpace();
            ZUI.Label("Fire", ZUI.ZTextStyle.Header);
            var fireForm = ZUI.Form();
            var rateDamage = ZUI.Row("Fire Rate / Damage");
            rateDamage.Add(70f, ZUI.FloatField(() => w.fireRate, v => w.fireRate = Mathf.Max(0.01f, v)));
            rateDamage.Add(70f, ZUI.FloatField(() => w.damage, v => w.damage = Mathf.Max(0f, v)));
            fireForm.Add(rateDamage);
            var flightRow = ZUI.Row("Speed / Spread / Count");
            flightRow.Add(70f, ZUI.FloatField(() => w.projectileSpeed, v => w.projectileSpeed = Mathf.Max(0f, v)));
            flightRow.Add(70f, ZUI.FloatField(() => w.spreadDeg, v => w.spreadDeg = Mathf.Clamp(v, 0f, 180f)));
            flightRow.Add(50f, ZUI.IntField(() => w.projectilesPerShot, v => w.projectilesPerShot = Mathf.Max(1, v)));
            fireForm.Add(flightRow);
            fireForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Ammo", ZUI.ZTextStyle.Header);
            if (_so == null || _so.targetObject != w) _so = new SerializedObject(w);
            _so.Update();
            // Each ammo entry now goes through LauAssetField (thumbnail + Recall/New ▾/Edit ✎) instead of
            // Unity's default reorderable-list ObjectField row — same shared control every other LauAsset
            // field in this window already uses.
            DrawObjectListProperty(_so.FindProperty("ammoTypes"), typeof(AmmoDef), "Ammo Type");
            _so.ApplyModifiedProperties();

            ZUI.VerticalSpace();
            ZUI.Label("Muzzle", ZUI.ZTextStyle.Header);
            _so.Update();
            DrawManagedReferenceField(_so.FindProperty("muzzle"), suppressHeader: true);
            _so.ApplyModifiedProperties();

            w.muzzleLayerId = EditorGUILayout.TextField("Layer Id", w.muzzleLayerId);
            ZUI.Label("Muzzle Offset (fallback only — a ZonedReelView shooter uses the MetaLayer instead)", ZUI.ZTextStyle.Subtle);
            w.muzzleOffset = ZUI.PositionPad(w.muzzleOffset, new Rect(-2f, -2f, 4f, 4f), 70f);

            ZUI.VerticalSpace();
            ZUI.Label("Audio", ZUI.ZTextStyle.Header);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Fire Zound", GUILayout.Width(EditorGUIUtility.labelWidth));
                string zoundLabel = string.IsNullOrEmpty(w.fireZoundName) ? "— zound —" : w.fireZoundName;
                if (Button(new GUIContent(zoundLabel, "Zound played once per successful shot, at the same moment as the muzzle effect. Click to pick."),
                    ZUI.Style.Default, GUILayout.Width(150)))
                {
                    // Deferred callback (fires from the popup, a later event outside this DrawAsset call) — its
                    // own Undo.RecordObject/SetDirty, since the "Edit Weapon" record taken at the top of this
                    // method won't have re-run by the time the popup selection lands.
                    ZoundPickerPopup.Show(Event.current.mousePosition, picked =>
                    {
                        Undo.RecordObject(w, "Set Fire Zound");
                        w.fireZoundName = picked;
                        EditorUtility.SetDirty(w);
                    });
                }
            }

            if (GUI.changed) EditorUtility.SetDirty(w);
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

        SerializedObject _so;
        readonly Dictionary<Object, Texture2D> _visualThumbs = new Dictionary<Object, Texture2D>();

        protected override void DrawAsset(AmmoDef a)
        {
            Undo.RecordObject(a, "Edit Ammo");

            ZUI.Label("Identity", ZUI.ZTextStyle.Header);
            a.displayName = EditorGUILayout.TextField("Display Name", a.displayName);

            ZUI.VerticalSpace();
            ZUI.Label("Visual", ZUI.ZTextStyle.Header);
            // One shared control (thumbnail + Recall + New ▾ + Edit ✎) instead of the bare interface-
            // constrained ObjectField + bespoke per-kind New buttons this grew out of — any LauAssetEditors-
            // registered IChunkAnimation kind (sprite, Pyre blast, Reel) shows up in Recall/New alike now.
            LauAssetField.Draw(a.visual, picked => a.visual = picked, typeof(IChunkAnimation), _visualThumbs,
                string.IsNullOrWhiteSpace(a.displayName) ? a.name : a.displayName, "Assets/Zoetrope/AmmoVisuals");

            if (_so == null || _so.targetObject != a) _so = new SerializedObject(a);

            var visForm = ZUI.Form();
            visForm.Add("Scale", 70f, ZUI.FloatField(() => a.scale, v => a.scale = v));
            var spinRow = ZUI.Row("Spin / Speed");
            spinRow.Add(50f, ZUI.Toggle(() => a.spin, v => a.spin = v));
            spinRow.Add(70f, ZUI.FloatField(() => a.spinSpeed, v => a.spinSpeed = v));
            visForm.Add(spinRow);
            visForm.Add("Face Travel", 50f, ZUI.Toggle(() => a.faceTravel, v => a.faceTravel = v));
            visForm.Draw();

            ZUI.VerticalSpace();
            ZUI.Label("Flight", ZUI.ZTextStyle.Header);
            var flightForm = ZUI.Form();
            var lifePierceRow = ZUI.Row("Lifetime / Pierce");
            lifePierceRow.Add(70f, ZUI.FloatField(() => a.lifetime, v => a.lifetime = Mathf.Max(0.05f, v)));
            lifePierceRow.Add(50f, ZUI.Toggle(() => a.pierce, v => a.pierce = v));
            flightForm.Add(lifePierceRow);
            var futureRow = ZUI.Row("Gravity / Homing (unused)");
            futureRow.Add(70f, ZUI.FloatField(() => a.gravity, v => a.gravity = v));
            futureRow.Add(50f, ZUI.Toggle(() => a.homing, v => a.homing = v));
            flightForm.Add(futureRow);
            flightForm.Draw();

            _so.Update();
            DrawManagedReferenceField(_so.FindProperty("motion"));
            _so.ApplyModifiedProperties();

            ZUI.VerticalSpace();
            ZUI.Label("Impact", ZUI.ZTextStyle.Header);
            _so.Update();
            DrawManagedReferenceField(_so.FindProperty("impact"), suppressHeader: true);
            _so.ApplyModifiedProperties();

            if (GUI.changed) EditorUtility.SetDirty(a);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Laubrary.AssetKit.Editor;
using Laubrary.Chunks;
using Laubrary.Combat2D;
using Laubrary.LaunimatorZounds.Editor;
using Laubrary.Zui;
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
    ///  • a concrete type carrying its own <c>[CustomPropertyDrawer]</c> (ZonedReelViewDrawer's "Open in
    ///    Animation Builder", PyreChunksFxDrawer's "Preview in Pyre") is drawn by that drawer inside an
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

        protected SerializedObject So { get; private set; }

        // Thumbnails for every LauAsset-typed field this window draws — owned here, cleared on the way out.
        protected readonly Dictionary<Object, Texture2D> FieldThumbs = new Dictionary<Object, Texture2D>();

        protected sealed override void BuildAsset(VisualElement root, T asset)
        {
            So = new SerializedObject(asset);

            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            BuildBody(scroll.contentContainer, asset);
            root.Add(scroll);
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
        /// (e.g. a hit reaction, whose clips are authored on the same Reel the character's view plays).
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
                if (TryBuildAssetRefField(host, child, boxedValue)) continue;
                host.Add(ZuiSerialized.Field(child.Copy(), width: ScalarFieldWidth));
            }
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
        // of a Reel version's animation list, not free text. Resolved purely by REFLECTION over the boxed
        // value (no compile-time reference to Launimator/ZoetropeLaunimator types), so this stays a core-
        // Zoetrope concern, decoupled from whichever bridge module supplies the concrete view type.

        static readonly string[] ClipFieldNames = { "idleClip", "clip", "hurtClip", "deathClip" };

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
            var row = Z.Row(LauAssetElement.Build(child.objectReferenceValue,
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
        /// elements each have a `name`" — the ReelView/ZonedReelView + ReelVersion + AnimationDef shape,
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

        // Hand-curated rather than the base's generic loop: maxHealth/invulnerableAfterHit are two short
        // related Stats fields (measured 480px wide each under the generic loop) that read far better sharing
        // one row, and the base has no notion that they belong together.
        protected override void BuildBody(VisualElement root, Zoe zoe)
        {
            var identity = Z.Section("Identity", "What this character is called in-game.");
            identity.Add(Z.Field("Display Name", "The name shown to the player.",
                Z.TextInput(zoe.displayName, "The name shown to the player.",
                    v => Commit("displayName", p => p.stringValue = v), ScalarFieldWidth)));
            root.Add(identity);

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

            var look = Z.Section("Look", "How this character is drawn — a plain sprite, a Reel-driven animation, a composite body.");
            BuildManagedRef(look, So.FindProperty("view"), "View", zoe);
            root.Add(look);

            var reactions = Z.Section("Reactions", "What plays when this character is hurt, and when it dies.");
            reactions.Add(Z.Text("Hit", ZuiText.Section, "What happens on a non-killing hit."));
            BuildReactionFx(reactions, So.FindProperty("hit"), zoe);
            reactions.Add(Z.Text("Death", ZuiText.Section, "What happens on the killing blow."));
            BuildReactionFx(reactions, So.FindProperty("death"), zoe);
            root.Add(reactions);

            var ai = Z.Section("AI", "Optional decision-making attached at spawn.");
            BuildManagedRef(ai, So.FindProperty("brain"), "Brain", zoe);
            root.Add(ai);

            // No enclosing section for these two: the PropertyField's own foldout already carries the name, and
            // a section titled the same thing over a single field says it twice.
            root.Add(ZuiSerialized.Property(So.FindProperty("loadout"), "Loadout",
                "Pluggable weapons + abilities the character can activate; triggered by the brain or by input. Unused today."));

            var weapons = BuildObjectListProperty(root, So.FindProperty("weapons"), typeof(WeaponDef), "Weapon");
            weapons.Add(IntFieldClamped("Default Active Weapon", "defaultActiveWeapon", zoe.defaultActiveWeapon,
                "Which weapon slot is enabled when this character spawns.", v => Mathf.Max(0, v)));

            root.Add(ZuiSerialized.Property(So.FindProperty("cues"), "Cues",
                "Always-on effects this character plays off its own animation, whatever it has equipped."));
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
            if (clipOptions != null && clipOptions.Length > 0)
            {
                var shown = new[] { "(none)" }.Concat(clipOptions).ToList();
                int current = shown.IndexOf(string.IsNullOrEmpty(clipProp.stringValue) ? "(none)" : clipProp.stringValue);
                root.Add(Z.Field("Clip", clipTip, Z.Dropdown(Mathf.Max(current, 0), shown, clipTip, i =>
                {
                    string picked = shown[Mathf.Clamp(i, 0, shown.Count - 1)];
                    Commit(clipPath, p => p.stringValue = picked == "(none)" ? "" : picked);
                    Rebuild();   // the FX rows' Event/Layer options are sourced from this clip
                }, 200f)));
            }
            else
            {
                root.Add(Z.Field("Clip", clipTip, Z.TextInput(clipProp.stringValue ?? "", clipTip,
                    v => Commit(clipPath, p => p.stringValue = v), 200f)));
            }

            string[] eventNames = GetEventNames(zoe.view, clipProp.stringValue);
            string[] pointLayerIds = GetPointLayerIds(zoe.view, clipProp.stringValue);

            root.Add(Z.Text($"Effects  ({fxListProp.arraySize})", ZuiText.Subtle,
                "Every effect this reaction fires, in order — each picks which of the event's params it reads."));

            // A dedicated host so the reorder insertion line + index math only ever see effect cards, never the
            // Add button below (mirrors SpriteFxStackView's listHost split).
            var listHost = new VisualElement();
            root.Add(listHost);
            for (int i = 0; i < fxListProp.arraySize; i++)
                BuildFxEntry(listHost, fxListProp.GetArrayElementAtIndex(i), fxPath, i, eventNames, pointLayerIds, zoe);

            // The Add-effect menu: a Z.Menu of icon rows listing every IEffect kind (grouped by module), so
            // picking one appends an entry with that effect already assigned — nicer than adding a blank entry and
            // hunting the type switcher. The per-card switcher below still lets you re-type an existing entry.
            var addBtn = Z.Button("+ Add effect  ▾", "Pick an effect kind to add to this reaction.", null);
            addBtn.style.width = AddButtonWidth;
            addBtn.clicked += () => ShowAddEffectMenu(addBtn, fxPath);
            root.Add(addBtn);
        }

        /// One effect entry, drawn as a FOLDING card (grip / kind-name / × in a header that stays visible when
        /// collapsed, everything else in a body that folds away — the SpriteFx-stack feel). The body carries the
        /// Trigger, then ONLY the param pickers this effect actually reads (Position / Direction / Scalar, keyed
        /// off <see cref="IEventParamUser"/> so the UI never shows a Direction dropdown to a blast that ignores
        /// it), then the effect's own SerializeReference body. <paramref name="index"/> is the entry's slot in the
        /// fx array; <paramref name="listHost"/> is the reorder container (holds ONLY cards).
        void BuildFxEntry(VisualElement listHost, SerializedProperty entryProp, string fxPath, int index,
            string[] eventNames, string[] pointLayerIds, Zoe zoe)
        {
            var triggerProp = entryProp.FindPropertyRelative("trigger");
            var placementProp = entryProp.FindPropertyRelative("placement");
            var eventNameProp = entryProp.FindPropertyRelative("eventName");
            var metaLayerIdProp = entryProp.FindPropertyRelative("metaLayerId");
            var directionProp = entryProp.FindPropertyRelative("direction");
            var scalarProp = entryProp.FindPropertyRelative("scalar");
            var followProp = entryProp.FindPropertyRelative("follow");
            var effectProp = entryProp.FindPropertyRelative("fx");
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
            var mute = new Toggle { tooltip = "Enabled — uncheck to MUTE this effect (kept in the list, but it never fires)." };
            mute.AddToClassList("zui-section__toggle");
            mute.style.marginRight = 4f;
            mute.SetValueWithoutNotify(enabledProp.boolValue);
            mute.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            mute.RegisterValueChangedCallback(e =>
            {
                Commit(enabledPath, p => p.boolValue = e.newValue);
                box.style.opacity = e.newValue ? 1f : 0.45f;
            });
            header.Add(mute);
            box.style.opacity = enabledProp.boolValue ? 1f : 0.45f;

            var icon = Z.Icon(kindIcon);
            if (icon != null) header.Add(icon);
            header.Add(Z.Text(kindLabel, ZuiText.Body, kindLabel + " effect."));
            header.Add(Z.Flexible());
            var removeBtn = Z.Button("×", "Remove this effect (undoable).", () =>
            {
                Commit(fxPath, p => p.DeleteArrayElementAtIndex(index));
                Rebuild();
            }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // ── body: trigger, param pickers (only those the effect reads), then the effect's own fields ──
            var body = new VisualElement();

            const string triggerTip = "When this effect fires: right away, or in sync with a named frame event as the clip plays.";
            bool showEvent = (FxTriggerType)triggerProp.enumValueIndex == FxTriggerType.FrameEvent;
            var triggerRow = Z.Row(EnumPicker(triggerProp, "Trigger", triggerTip));
            if (showEvent)
            {
                triggerRow.Add(Z.HSpace());
                triggerRow.Add(StringDropdown(eventNameProp, "Event", eventNames,
                    "Which authored frame event on the clip fires this effect."));
            }
            body.Add(triggerRow);

            // Position picker (+ conditional Layer, + Follow) — only when the effect reads a position.
            if ((used & EventParam.Position) != 0)
            {
                bool isMetaPoint = (FxPlacementType)placementProp.enumValueIndex == FxPlacementType.MetaPoint;

                const string posTip = "Which of the event's position params this effect spawns at — the hit point, " +
                    "the Zoe's origin, its sprite centre, or a named meta-layer point.";
                var posRow = Z.Row(EnumPicker(placementProp, "Position", posTip));
                if (isMetaPoint)
                {
                    posRow.Add(Z.HSpace());
                    posRow.Add(StringDropdown(metaLayerIdProp, "Layer", pointLayerIds,
                        "Which Point-mode meta-layer on the clip this effect spawns at."));
                }
                body.Add(posRow);

                // Follow is now available for EVERY position, including Hit Position (task #4): for the fixed hit
                // point it STICKS to the Zoe (the hit point captured in the Zoe's space, riding along as it moves);
                // for the other positions it re-samples that point each frame.
                var follow = Z.Toggle("Follow",
                    "Keep the effect attached to the target instead of spawning once. Hit Position sticks the hit " +
                    "point to the Zoe (it rides along, from where the hit landed); the other positions re-sample " +
                    "every frame.",
                    followProp.boolValue, v => Commit(followPath, p => p.boolValue = v));
                body.Add(follow);
            }

            // Direction + Scalar pickers share one row (both short), each shown only when the effect reads it.
            // rebuild:false — unlike Position/Trigger, neither gates a conditional row, so a rebuild would just
            // churn the whole window (and lose scroll/focus) for nothing.
            VisualElement paramRow = null;
            if ((used & EventParam.Direction) != 0)
                paramRow = Z.Row(EnumPicker(directionProp, "Direction",
                    "Which of the event's direction params aims this effect. Hit Direction = away from the " +
                    "attacker; None fires omni-directionally.", rebuild: false));
            if ((used & EventParam.Scalar) != 0)
            {
                var scalarPick = EnumPicker(scalarProp, "Scalar",
                    "Which of the event's scalar params sizes / strengthens this effect. Amount = the damage " +
                    "dealt; None = zero.", rebuild: false);
                if (paramRow == null) paramRow = Z.Row(scalarPick);
                else { paramRow.Add(Z.HSpace()); paramRow.Add(scalarPick); }
            }
            if (paramRow != null) body.Add(paramRow);

            // The effect's own fields, drawn INLINE — no foldable "Effect" sub-section (task #2): the effect IS the
            // whole card, its kind is already named in the header, so a nested "Effect" fold + type button was just
            // redundant chrome. A concrete effect with no custom drawer (the Spawn-Pyre / Spawn-Chunk palette kinds)
            // flows through TryBuildAssetRefField, so its Pyre/Chunk asset field renders as a LauAsset picker+preview
            // rather than a plain ObjectField. (Re-type an entry by removing it and adding the kind you want.)
            if (effect != null) BuildManagedRefChildren(body, effectProp, effect, zoe);
            box.Add(body);

            // Fold the whole card to its header, keyed per effect instance so the state survives window rebuilds
            // (undo / reorder / re-type). The grip guards its own drag; the × must not fold on click.
            ZuiFoldCard.Wire(effect, header, body, removeBtn);
            listHost.Add(box);
        }

        // An enum picker — segmented for a short single-line set, a wrapping MiniRadio for a longer one. NEVER a
        // dropdown (ui-layout-rules: enum → radios/segmented, a dropdown is only for dynamic authored-name lists,
        // which is what StringDropdown below stays as).
        VisualElement EnumPicker(SerializedProperty prop, string label, string tooltip, bool rebuild = true)
        {
            var choices = prop.enumDisplayNames;
            string path = prop.propertyPath;
            void Pick(int i) { Commit(path, p => p.enumValueIndex = i); if (rebuild) Rebuild(); }   // Position/Trigger gate conditional rows
            VisualElement control = choices.Length <= 3
                ? Z.Segmented(prop.enumValueIndex, choices, tooltip, Pick)
                : Z.MiniRadio(prop.enumValueIndex, choices, tooltip, Pick, wrap: true);
            return Z.Field(label, tooltip, control);
        }

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
                tooltip = "Throw a Chunks debris burst — aimed by a direction, sized by a scalar, colour-sampled from the Zoe's live reel." },
            ["PyreChunksFx"] = new EffectMetaInfo { label = "Pyre + Chunks", icon = "bomb", section = "Spawn VFX",
                tooltip = "The bundled blast + debris effect (the original combined VFX; still used by committed assets)." },
            ["BodySpriteFxEffect"] = new EffectMetaInfo { label = "Body SpriteFx", icon = "sparkle", section = "On the Zoe",
                tooltip = "Flash / tint / dissolve the Zoe's own sprite for a duration (a SpriteFx stack on the body renderer)." },
            ["PushbackEffect"] = new EffectMetaInfo { label = "Pushback", icon = "arrow-fat-right", section = "On the Zoe",
                tooltip = "Shove the Zoe along a direction param — knockback, strengthened by a scalar param." },
            ["PlayReelEffect"] = new EffectMetaInfo { label = "Play Reel", icon = "film-reel", section = "On the Zoe",
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
        /// anything else reads nothing (Play-Reel, Body-SpriteFx).
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
                var e = p.GetArrayElementAtIndex(p.arraySize - 1);
                e.FindPropertyRelative("trigger").enumValueIndex = (int)FxTriggerType.Immediate;
                e.FindPropertyRelative("eventName").stringValue = "";
                e.FindPropertyRelative("placement").enumValueIndex = (int)FxPlacementType.HitPosition;
                e.FindPropertyRelative("metaLayerId").stringValue = "";
                e.FindPropertyRelative("direction").enumValueIndex = (int)DirectionParam.HitDirection;
                e.FindPropertyRelative("scalar").enumValueIndex = (int)ScalarParam.Amount;
                e.FindPropertyRelative("follow").boolValue = false;
                e.FindPropertyRelative("fx").managedReferenceValue = Activator.CreateInstance(type);
            });
            Rebuild();
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

        /// Every Point-mode MetaLayer id on the named clip (Shape layers are excluded — their centroid isn't a
        /// meaningful "the hit origin" marker).
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

            var muzzle = Z.Section("Muzzle", "The flash/smoke played at the muzzle each shot, and where it spawns.");
            BuildManagedRef(muzzle, So.FindProperty("muzzle"), "Effect");
            muzzle.Add(Z.Field("Layer Id",
                "Which meta-layer on the shooter's animation the muzzle effect spawns at. Ignored when Event Name is set.",
                Z.TextInput(w.muzzleLayerId,
                    "Which meta-layer on the shooter's animation the muzzle effect spawns at. Ignored when Event Name is set.",
                    v => Commit("muzzleLayerId", p => p.stringValue = v), ScalarFieldWidth)));
            muzzle.Add(Z.Field("Event Name",
                "A frame event with an authored pixel position that spawns the muzzle effect instead. Takes priority over Layer Id.",
                Z.TextInput(w.muzzleEventName,
                    "A frame event with an authored pixel position that spawns the muzzle effect instead. Takes priority over Layer Id.",
                    v => Commit("muzzleEventName", p => p.stringValue = v), ScalarFieldWidth)));
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
            const string visualTip = "A Pyre blast or a Reel animation — anything a chunk can play. Only its first frame shows on the projectile today.";
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

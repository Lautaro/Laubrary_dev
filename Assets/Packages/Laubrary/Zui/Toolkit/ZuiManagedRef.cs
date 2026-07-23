// ZuiManagedRef — the retained-mode control for a [SerializeReference] polymorphic field: one header row
// (fold caret + title + a button naming the current concrete type, which pops the type menu) over a body the
// caller fills with whatever fields THAT type has.
//
// This is the UI Toolkit counterpart of the IMGUI ZUI.PolymorphicFoldout. Unity's own PropertyField does
// render a managed reference, but its type-switch affordance is genuinely hard to find (the reason
// PolymorphicFoldout was built in the first place) and it gives the caller no way to substitute a better
// control for a child field — which is exactly what Zoetrope needs (clip-name dropdowns, LauAsset pickers).
// So the header is ours and the body is the caller's.
//
// Fold state rides on `property.isExpanded`, so it persists like every other Unity foldout.
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiManagedRef : VisualElement
    {
        readonly VisualElement _body;
        readonly Label _caret;
        readonly SerializedObject _so;
        readonly string _path;

        /// Children go into the body, below the header.
        public override VisualElement contentContainer => _body;

        /// The concrete value currently boxed in the reference (null when the field is empty).
        public object BoxedValue { get; }

        /// Fires after the user picks a different concrete type — the caller must rebuild, since the whole
        /// set of child fields just changed.
        public event Action OnTypeChanged;

        public ZuiManagedRef(SerializedProperty property, string title, string tooltip)
        {
            _so = property.serializedObject;
            _path = property.propertyPath;
            BoxedValue = property.managedReferenceValue;

            AddToClassList("zui-mref");

            var header = new VisualElement();
            header.AddToClassList("zui-mref__header");
            header.tooltip = tooltip;

            _caret = new Label(property.isExpanded ? "▾" : "▸") { pickingMode = PickingMode.Ignore };
            _caret.AddToClassList("zui-mref__caret");
            header.Add(_caret);

            var titleLabel = new Label(title) { pickingMode = PickingMode.Ignore, tooltip = tooltip };
            titleLabel.AddToClassList("zui-mref__title");
            header.Add(titleLabel);

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1f;
            header.Add(spacer);

            string typeLabel = BoxedValue != null
                ? ObjectNames.NicifyVariableName(BoxedValue.GetType().Name)
                : "None";
            var typeButton = new Button { text = typeLabel + " ▾", tooltip = $"Which kind this is ({typeLabel}). Click to switch it for another." };
            typeButton.AddToClassList("zui-mref__type");
            typeButton.clicked += () => ShowTypeMenu(property);
            header.Add(typeButton);

            // Clickable (not a raw PointerDownEvent) for the same reason ZuiSection uses it — a bare
            // PointerDownEvent does not reliably reach a header sitting inside a ScrollView.
            header.AddManipulator(new Clickable(() =>
            {
                var p = _so.FindProperty(_path);
                if (p == null) return;
                p.isExpanded = !p.isExpanded;
                Apply(p.isExpanded);
            }));
            hierarchy.Add(header);

            _body = new VisualElement();
            _body.AddToClassList("zui-mref__body");
            hierarchy.Add(_body);

            Apply(property.isExpanded);
        }

        public bool IsOpen => _body.style.display != DisplayStyle.None;

        void Apply(bool open)
        {
            _caret.text = open ? "▾" : "▸";
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ShowTypeMenu(SerializedProperty property)
        {
            var fieldType = FieldTypeOf(property);
            if (fieldType == null) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("None"), property.managedReferenceValue == null, () => Assign(null));

            foreach (var t in TypeCache.GetTypesDerivedFrom(fieldType))
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;   // Activator needs a parameterless ctor
                var concrete = t;
                bool isCurrent = property.managedReferenceValue?.GetType() == concrete;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(concrete.Name)), isCurrent,
                    () => Assign(Activator.CreateInstance(concrete)));
            }
            menu.ShowAsContext();
        }

        void Assign(object value)
        {
            var p = _so.FindProperty(_path);
            if (p == null) return;
            _so.Update();
            p.managedReferenceValue = value;
            _so.ApplyModifiedProperties();   // registers the Undo step itself
            OnTypeChanged?.Invoke();
        }

        /// `managedReferenceFieldTypename` is "AssemblyName TypeFullName" for the field's DECLARED type (the
        /// interface/base class), independent of whatever concrete type is boxed in it right now.
        public static Type FieldTypeOf(SerializedProperty property)
        {
            string typenames = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(typenames)) return null;
            var parts = typenames.Split(' ');
            if (parts.Length != 2) return null;
            try { return Assembly.Load(parts[0])?.GetType(parts[1]); }
            catch { return null; }
        }
    }
}

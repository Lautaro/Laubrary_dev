using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Cabinets.Editor
{
    /// Augments Cabinet's inspector with the numbers you cannot get by reading the fields: how much WORLD
    /// and how many CELLS a screenful covers, and whether the chosen canvas actually lands cleanly on real
    /// hardware.
    ///
    /// This block is the reason the asset is usable rather than merely correct. "320 x 200" tells an author
    /// nothing about whether their room fits, or whether a Steam Deck will show it with black bars — those
    /// are two multiplications away, and an author who has to do them by hand will not do them at all, will
    /// guess, and will discover the answer after building a level at the wrong size.
    ///
    /// Follows the package's established inspector shape: Unity's default inspector for the fields (their
    /// [Tooltip]s and [Range]s carry the explanations), plus one Z.Attach'ed block for the derived readout.
    [CustomEditor(typeof(Cabinet))]
    [CanEditMultipleObjects]
    public class CabinetEditor : UnityEditor.Editor
    {
        /// Real screens worth checking against. A handheld first, because that is the one with a fixed
        /// panel where an exact fit is actually achievable and actually matters.
        static readonly (string Name, Vector2Int Size)[] Displays =
        {
            ("Steam Deck", new Vector2Int(1280, 800)),
            ("1080p",      new Vector2Int(1920, 1080)),
            ("1440p",      new Vector2Int(2560, 1440)),
        };

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // Z.Attach FIRST on any root ZUI does not own, or every control renders unstyled.
            var block = new VisualElement();
            Z.Attach(block);
            block.style.paddingTop = 6f;
            root.Add(block);

            var section = Z.Section("Fit",
                "What this cabinet works out to in practice — how much world is on screen, and how it lands " +
                "on real hardware.");
            block.Add(section);

            var world = Z.Text("", ZuiText.Body, "How much world one screenful covers, in Unity units.");
            var cells = Z.Text("", ZuiText.Body,
                "How many grid cells fit on screen, at the two tile sizes this project actually uses. " +
                "This is the number to check a room's bounds against.");
            section.Add(world);
            section.Add(cells);

            var perDisplay = new Label[Displays.Length];
            for (int i = 0; i < Displays.Length; i++)
            {
                perDisplay[i] = Z.Text("", ZuiText.Subtle,
                    $"How this canvas scales onto a {Displays[i].Name} panel ({Displays[i].Size.x}x{Displays[i].Size.y}).");
                section.Add(perDisplay[i]);
            }

            var note = Z.Text("", ZuiText.Subtle,
                "Perspective cabinets cannot have their visible world derived — distance decides it, so the " +
                "numbers above do not apply.");
            section.Add(note);

            // The walk-cold gap this closes: an author with a Cabinet selected has NO way to discover that a
            // CabinetStage component is how you apply one. Being told "add a CabinetStage" is a missing
            // affordance, not a workflow — so the affordance is a button that performs the act, named for
            // what it DOES rather than for the panel it would otherwise open.
            var apply = Z.Button("Apply to scene camera",
                "Put this cabinet in force in the open scene — adds a Cabinet Stage to the main camera, or " +
                "repoints the existing one at this cabinet.", null);
            apply.clicked += () => ApplyToSceneCamera(target as Cabinet);
            section.Add(apply);

            var applied = Z.Text("", ZuiText.Subtle, "Whether the open scene is currently running this cabinet.");
            section.Add(applied);

            void Refresh()
            {
                var cab = target as Cabinet;
                if (cab == null) return;

                bool flat = cab.IsFlat;
                world.Shown(flat);
                cells.Shown(flat);
                note.Shown(!flat);

                if (flat)
                {
                    var wu = cab.WorldUnitsVisible;
                    world.text = $"{wu.x:0.##} x {wu.y:0.##} world units    (ortho size {cab.OrthographicSize:0.##})";

                    var c16 = cab.CellsVisible(16);
                    var c32 = cab.CellsVisible(32);
                    cells.text = $"{c16.x:0.##} x {c16.y:0.##} cells at 16px      {c32.x:0.##} x {c32.y:0.##} at 32px";
                }

                for (int i = 0; i < Displays.Length; i++)
                {
                    var (name, size) = Displays[i];
                    int scale = cab.MaxIntegerScale(size);
                    bool exact = cab.FitsExactly(size);
                    int usedX = cab.virtualResolution.x * scale, usedY = cab.virtualResolution.y * scale;
                    int barX = size.x - usedX, barY = size.y - usedY;

                    perDisplay[i].text = exact
                        ? $"{name}  {scale}x  exact fit"
                        : $"{name}  {scale}x  {barX}x{barY}px letterbox";
                    perDisplay[i].Shown(true);
                }

                var stage = Object.FindFirstObjectByType<CabinetStage>(FindObjectsInactive.Include);
                applied.text = stage == null
                    ? "Not in force — no Cabinet Stage in the open scene."
                    : stage.cabinet == cab
                        ? $"In force on '{stage.name}'."
                        : $"'{stage.name}' is running {(stage.cabinet == null ? "no cabinet" : "'" + stage.cabinet.name + "'")}.";
            }

            Refresh();
            // Poll rather than bind: several of these read DERIVED values that no single SerializedProperty
            // change notification covers, and the cost is a few multiplications a few times a second.
            root.schedule.Execute(Refresh).Every(200);
            return root;
        }

        /// Put this cabinet in force in the open scene. Reuses an existing stage rather than adding a second
        /// one — two stages in a scene is an ambiguity Cabinet.Active resolves arbitrarily, so the button
        /// must never be able to create that state.
        internal static void ApplyToSceneCamera(Cabinet cab)
        {
            if (cab == null) return;

            var stage = Object.FindFirstObjectByType<CabinetStage>(FindObjectsInactive.Include);
            if (stage == null)
            {
                var cam = Camera.main;
                if (cam == null) cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
                if (cam == null)
                {
                    Debug.LogWarning("[Cabinet] No camera in the open scene to apply this cabinet to.");
                    return;
                }
                stage = Undo.AddComponent<CabinetStage>(cam.gameObject);
            }

            Undo.RecordObject(stage, "Apply Cabinet");
            stage.cabinet = cab;
            stage.Apply();
            EditorUtility.SetDirty(stage);
            Undo.SetCurrentGroupName("Apply Cabinet");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }
    }
}

// ZuiRuntimeSheet — runtime-side default-sheet resolution for ZuiRuntime.Zui's sheet-aware overloads
// (Panel(rect, styleName, ...), see ZuiPanels.cs). Deliberately separate from the editor's
// ZUI.DefaultSheet (Zui/Scripts/Editor/ZUI.cs): that one loads via AssetDatabase, which does not exist
// in a built player. This one loads via Resources, which works in both the Editor and a Player — and
// keeping it a SEPARATE default (not shared with whichever sheet the Style Editor happens to have open)
// means restyling the in-game HUD never depends on editor-only state.
//
// The backing asset ships at Zui/SystemAssets/Resources/ZUIRuntimeDefaultSheet.asset — same
// ZUIStyleSheetAsset type as the editor toolkit, editable live via the Style Editor ("Zeditor") like any
// other sheet. See ZUI_API_AND_RUNTIME_ROADMAP.md Task 2 for the asset-authoring step this pairs with.

using UnityEngine;

namespace ZuiRuntime
{
    public static partial class Zui
    {
        const string k_RuntimeDefaultSheetResourcesPath = "ZUIRuntimeDefaultSheet";
        static ZUIStyleSheetAsset _runtimeDefaultSheet;
        static bool _triedLoadRuntimeDefaultSheet;

        /// <summary>The fallback sheet for sheet-aware runtime draw calls that don't pass one explicitly.
        /// Loaded once via Resources.Load (works in a build, unlike the editor's AssetDatabase-based
        /// ZUI.DefaultSheet). Assign at init (e.g. from a bootstrap script) to override. Null if no
        /// Resources asset was shipped and nothing was assigned — sheet-aware calls degrade gracefully
        /// rather than throwing (see Zui.Panel's sheet-aware overload).</summary>
        public static ZUIStyleSheetAsset DefaultSheet
        {
            get
            {
                if (_runtimeDefaultSheet != null) return _runtimeDefaultSheet;
                if (!_triedLoadRuntimeDefaultSheet)
                {
                    _triedLoadRuntimeDefaultSheet = true;
                    _runtimeDefaultSheet = Resources.Load<ZUIStyleSheetAsset>(k_RuntimeDefaultSheetResourcesPath);
                }
                return _runtimeDefaultSheet;
            }
            set
            {
                _runtimeDefaultSheet = value;
                _triedLoadRuntimeDefaultSheet = true;
            }
        }
    }
}

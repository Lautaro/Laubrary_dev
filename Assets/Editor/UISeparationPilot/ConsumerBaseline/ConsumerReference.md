# Frozen BackSplash consumer pair

`FrozenZuiAssetWindow<T>` is copied from the pre-extraction `ZuiAssetWindow.cs.txt` reference and uses the frozen `FoundationFactoryBaseline.Z` factory alias. `FrozenBackSplashWindow` is copied from the unchanged BackSplash consumer and derives from that frozen shell. Menu attributes were removed so the pair can compile beside the real BackSplash window without a duplicate menu item.

The copied shell still derives from the current `ZuiWindow`, which was unchanged for this comparison. It can receive the coordinator's attached sheets, but its old root and part classes do not match the new `lau-asset-browser` selectors. Therefore this pair isolates the old shell/factory behavior; it does not independently prove the current asset-browser stylesheet path. The comparison fixture must record that shared-dependency limit alongside visual results.

No asmref is added here. The coordinator's comparison fixture compiles in `Assembly-CSharp-Editor`, which can reference the package tool assemblies used by the copied consumer.

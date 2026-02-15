# StatefulUI Demo Setup Instructions

## Assembly Definition Files

You need to create two assembly definition files manually (Unity doesn't allow creation via script):

### 1. Runtime Assembly Definition

**Path:** `/Assets/Packages/Laubrary/Runtime/StatefulUI/StatefulUI.asmdef`

**Content:**
```json
{
    "name": "com.Lautaro-Arino.Laubrary.StatefulUI",
    "rootNamespace": "Laubrary.StatefulUI",
    "references": [
        "GUID:6055be8ebefd69e48b49212b09b47b2f"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

**Steps:**
1. In Unity, navigate to `/Assets/Packages/Laubrary/Runtime/StatefulUI/`
2. Right-click → Create → Assembly Definition
3. Name it `StatefulUI`
4. Select it and in the Inspector, ensure:
   - Name: `com.Lautaro-Arino.Laubrary.StatefulUI`
   - Root Namespace: `Laubrary.StatefulUI`
   - References: Add `Unity.TextMeshPro` (the GUID reference)

### 2. Editor Assembly Definition

**Path:** `/Assets/Packages/Laubrary/Editor/StatefulUI/StatefulUIEditor.asmdef`

**Content:**
```json
{
    "name": "com.Lautaro-Arino.Laubrary.StatefulUI.Editor",
    "rootNamespace": "Laubrary.StatefulUI.Editor",
    "references": [
        "GUID:cb075350ea02fe04a8074ca6d610022d"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

**Steps:**
1. In Unity, navigate to `/Assets/Packages/Laubrary/Editor/StatefulUI/`
2. Right-click → Create → Assembly Definition
3. Name it `StatefulUIEditor`
4. Select it and in the Inspector, ensure:
   - Name: `com.Lautaro-Arino.Laubrary.StatefulUI.Editor`
   - Root Namespace: `Laubrary.StatefulUI.Editor`
   - Platforms: **Editor only** (check the Editor box)
   - References: Add the Runtime assembly `com.Lautaro-Arino.Laubrary.StatefulUI`

## Demo Scene Setup

A basic demo scene has been created at `/Assets/Demos/StatefulUIDemo/StatefulUIDemo.unity` but you'll need to manually create it:

1. Create a new scene: `File → New Scene`
2. Save it as `/Assets/Demos/StatefulUIDemo/StatefulUIDemo.unity`
3. The scene needs:
   - Main Camera
   - EventSystem (required for UI input)
   - Canvas (for UI elements)

### Quick Demo Setup

1. **Create a Canvas**
   - Hierarchy → Right-click → UI → Canvas
   - This will auto-create an EventSystem

2. **Create a Simple Button Gadget**
   - Under Canvas, create an empty GameObject called "SimpleButton"
   - Add component: StatefulUI
   - Configure:
     - Enable Normal (white color)
     - Enable Hover (light blue, scale 1.1)
     - Enable Clicked (dark blue, scale 0.95)

3. **Add Background Visual**
   - Under SimpleButton, create UI → Image (name it "Background")
   - Add component: StatefulVisual
   - Set "Responds To Dimension" to "Interaction"
   - Leave "Use Default" for all states

4. **Test**
   - Enter Play Mode
   - Hover over the button → should scale up and change color
   - Click the button → should scale down and darken

### Advanced Demo: Toggle with Navigation

1. **Create Toggle Button**
   - Under Canvas, create empty GameObject "ToggleButton"
   - Add StatefulUI component
   - Configure Interaction states (Normal, Hover)
   - Configure Toggled dimension (On/Off)

2. **Add Children**
   - Background (Image + StatefulVisual → Interaction)
   - Checkmark (Image + StatefulVisual → Toggled)
     - Set Toggled (On): GameObject Enabled = true
     - Set Toggled (Off): GameObject Enabled = false
   - NavCircle (Image + StatefulVisual → Navigated)
     - Set Navigated (True): Scale = 1.2
     - Set Navigated (False): Scale = 0

3. **Test**
   - Click to toggle the checkmark on/off
   - Call `SetNavigated(true)` from code to show nav circle

### Radio Button Group Demo

1. **Create 3 Buttons**
   - OptionA, OptionB, OptionC
   - Each has StatefulUI component
   - Set same `radioGroupId` = "Options"
   - Set `groupBehavior` = SetToggled

2. **Add Visuals**
   - Each button has a checkmark responding to Toggled dimension

3. **Test**
   - Click one → it toggles on, others toggle off
   - Only one can be selected at a time

## Files Created

### Runtime Scripts
- `/Assets/Packages/Laubrary/Runtime/StatefulUI/StateDimension.cs` ✅
- `/Assets/Packages/Laubrary/Runtime/StatefulUI/StatefulUI.cs` ✅
- `/Assets/Packages/Laubrary/Runtime/StatefulUI/StatefulVisual.cs` ✅
- `/Assets/Packages/Laubrary/Runtime/StatefulUI/README.md` ✅

### Editor Scripts
- `/Assets/Packages/Laubrary/Editor/StatefulUI/StatefulUIEditor.cs` ✅
- `/Assets/Packages/Laubrary/Editor/StatefulUI/StatefulVisualEditor.cs` ✅

### To Be Created Manually
- `/Assets/Packages/Laubrary/Runtime/StatefulUI/StatefulUI.asmdef` ⚠️
- `/Assets/Packages/Laubrary/Editor/StatefulUI/StatefulUIEditor.asmdef` ⚠️
- `/Assets/Demos/StatefulUIDemo/StatefulUIDemo.unity` ⚠️

## Verification

After creating the assembly definitions and demo scene:

1. Check Console for compilation errors (should be none)
2. Select a GameObject and check if "StatefulUI" appears in Add Component menu
3. Add StatefulUI component and verify custom inspector appears
4. Create a child with Image and StatefulVisual, verify it works

## Next Steps

Once setup is complete, refer to the README.md for detailed usage instructions and examples.

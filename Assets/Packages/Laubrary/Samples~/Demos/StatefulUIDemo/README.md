# StatefulUI Demo

This demo showcases the StatefulUI system - a flexible, composition-based UI state management system for complex interactive UI gadgets.

## Demo Overview

The StatefulUI system allows you to create sophisticated UI elements where multiple visual components respond independently to different state dimensions.

## What's Included

### Scripts
- **StatefulUITester.cs**: Helper script for testing state changes via keyboard input
  - Press `N` to toggle Navigated state
  - Press `T` to toggle Toggled state  
  - Press `F` to toggle Focused state

### Examples to Build

Follow the `SETUP_INSTRUCTIONS.md` to create these demo scenarios:

1. **Simple Button** - Basic hover and click states
2. **Toggle Button** - On/off state with visual feedback
3. **Radio Button Group** - Multiple buttons with exclusive selection
4. **Complex Gadget** - Multiple dimensions working together

## Key Features Demonstrated

### Orthogonal State Dimensions
- **Interaction**: Normal, Hover, Selected, Clicked, Disabled
- **Navigated**: Show/hide navigation indicator
- **Toggled**: Toggle button on/off state
- **Focused**: Input focus indicator

### Visual Modifiers
- Color/Alpha (Interaction only)
- Position offset
- Scale
- Rotation
- Sprite swap
- GameObject enable/disable
- Component enable/disable

### Composition Pattern
Each visual element chooses which dimension it responds to:
- Background → Interaction (color changes on hover)
- Checkmark → Toggled (shows when toggled on)
- NavCircle → Navigated (shows when navigation targets it)

## Quick Start

1. **Complete Setup** (see `SETUP_INSTRUCTIONS.md`)
   - Create assembly definition files
   - Create demo scene with Canvas + EventSystem

2. **Create First Gadget**
   ```
   Canvas
   └── SimpleButton (StatefulUI)
       └── Background (Image + StatefulVisual)
   ```

3. **Configure StatefulUI**
   - Enable Normal, Hover, Clicked states
   - Set colors and scales for each

4. **Configure StatefulVisual**
   - Set "Responds To" = Interaction
   - Leave as "Use Default"

5. **Play and Test**
   - Hover to see color/scale change
   - Click to see click state

## Testing Boolean Dimensions

Add the `StatefulUITester` component to any GameObject:

```csharp
1. Create empty GameObject "Tester"
2. Add StatefulUITester component
3. Assign your StatefulUI to "Target UI"
4. Play and press N/T/F keys
```

This lets you test Navigated, Toggled, and Focused states without building a full navigation system.

## Radio Button Groups

To create exclusive selection (only one can be selected):

1. Create multiple StatefulUI objects
2. Set the same `radioGroupId` on all (e.g., "Options")
3. Set `groupBehavior` to SetToggled or SetSelected
4. Click one → others automatically deselect

## Architecture Benefits

- **No State Explosion**: 4 dimensions can represent complex states without defining every combination
- **Reusable**: Create gadget prefabs with pre-configured states
- **Designer-Friendly**: All configuration in Inspector
- **Performance**: Only children subscribed to changed dimension get notified

## Next Steps

- Read the full documentation in `/Runtime/StatefulUI/README.md`
- Experiment with different modifier combinations
- Create prefabs for common gadget patterns
- Hook up events to gameplay systems

## Common Patterns

### Hover Button
```
Interaction:
  Normal: Color=White
  Hover: Color=LightBlue, Scale=1.1
  Clicked: Color=DarkBlue, Scale=0.95
```

### Checkbox
```
Interaction:
  Normal: Color=Gray
  Hover: Color=LightGray

Toggled:
  On: Checkmark GameObject Enabled=true
  Off: Checkmark GameObject Enabled=false
```

### Navigable Toggle
```
Interaction: (background colors)
Toggled: (checkmark visibility)
Navigated: (outline/circle visibility)
```

## Support

For questions or issues, refer to the main README at `/Runtime/StatefulUI/README.md`

# StatefulUI System

A flexible, composition-based UI state management system for Unity that handles complex interactive UI gadgets with multiple visual states.

## Overview

StatefulUI allows you to create complex interactive UI elements (gadgets) where different visual components respond independently to different state dimensions. Unlike traditional button systems that apply a single state to one visual, StatefulUI broadcasts state changes to multiple child components, each choosing which dimension it responds to.

## Core Concepts

### State Dimensions

The system uses **4 orthogonal state dimensions**:

1. **Interaction** (enum with priority)
   - Normal (baseline)
   - Hover (mouse over)
   - Selected (persistent selection)
   - Clicked (pointer down)
   - Disabled (non-interactive)

2. **Navigated** (boolean)
   - True: Gamepad/keyboard navigation is targeting this element
   - False: Not targeted

3. **Toggled** (boolean)
   - On: Toggle button is active
   - Off: Toggle button is inactive

4. **Focused** (boolean)
   - True: Element has input focus
   - False: Element does not have focus

### Components

#### **StatefulUI** (Root Controller)
- Placed on the root GameObject of your gadget
- Handles input events (pointer, click, etc.)
- Manages all state dimensions
- Broadcasts state changes to child StatefulVisual components
- Supports radio button groups via `radioGroupId`
- Can have a StatefulVisual on the same GameObject

#### **StatefulVisual** (Child Visual Reactor)
- Placed on GameObjects with visual components (Image, RawImage, TextMeshProUGUI)
- Can be on the same GameObject as StatefulUI or on child GameObjects
- Responds to **one dimension** only (selectable via inspector)
- Applies visual modifiers based on current state
- Can override parent defaults or use parent configuration

## State Modifiers

### Available for Interaction States:
- Color (with alpha)
- Position (offset from original)
- Scale (multiplier)
- Rotation (degrees)
- Sprite (sprite swap)
- GameObject Enabled (show/hide entire GameObject)
- Component Enabled (enable/disable visual component)

### Available for Boolean Dimensions (Navigated, Toggled, Focused):
- Position
- Scale
- Rotation
- Sprite
- GameObject Enabled
- Component Enabled

*Note: Color/Alpha is intentionally restricted for boolean dimensions to keep visual feedback clear.*

## Usage

### Basic Setup

1. **Create UI Hierarchy**
   
   **Option A: Children only**
   ```
   GadgetRoot (StatefulUI)
   ├── Background (Image + StatefulVisual)
   ├── Icon (Image + StatefulVisual)
   ├── Label (TextMeshProUGUI + StatefulVisual)
   └── NavigationCircle (Image + StatefulVisual)
   ```
   
   **Option B: Same GameObject + children**
   ```
   Button (Image + StatefulUI + StatefulVisual)
   ├── Icon (Image + StatefulVisual)
   └── Label (TextMeshProUGUI + StatefulVisual)
   ```
   
   *StatefulVisual can be on the same GameObject as StatefulUI if that GameObject has a visual component!*

2. **Configure StatefulUI (Parent)**
   - Enable desired states (e.g., Hover, Selected)
   - For each enabled state, select which modifiers to use
   - Set default values for each modifier
   - These become the defaults for all children (and same GameObject)

3. **Configure StatefulVisual (Children or Same GameObject)**
   - Select which dimension to respond to:
     - Background → Interaction
     - NavigationCircle → Navigated
   - Choose to use parent defaults or override specific values

### Example: Simple Button

**StatefulUI Configuration:**
- Normal: Color = White
- Hover: Color = Light Blue, Scale = 1.1
- Clicked: Color = Dark Blue, Scale = 0.95

**Child (Background Image):**
- Responds To: Interaction
- Use parent defaults

Result: Background changes color and scale when hovered/clicked.

### Example: Toggle Button with Navigation

**StatefulUI Configuration:**
- Interaction States:
  - Normal: Color = Gray
  - Selected: Color = Green
- Boolean Dimensions:
  - Toggled (On): GameObject Enabled = true (for checkmark)
  - Toggled (Off): GameObject Enabled = false
  - Navigated (True): Scale = 1.1 (for nav circle)

**Children:**
- Background (Image) → Responds to: Interaction
- Checkmark (Image) → Responds to: Toggled
- NavCircle (Image) → Responds to: Navigated

### Radio Button Groups

Set the same `radioGroupId` string on multiple StatefulUI components:

```
Button1.radioGroupId = "DifficultySelect"
Button2.radioGroupId = "DifficultySelect"
Button3.radioGroupId = "DifficultySelect"
```

When one is clicked, others in the group automatically deselect.

**Radio Group Behavior:**
- `SetToggled`: Sets the Toggled dimension (default)
- `SetSelected`: Sets the Selected interaction state

## Events

StatefulUI exposes UnityEvents for state changes:

```csharp
OnInteractionStateChanged<InteractionState>
OnNavigatedChanged<bool>
OnToggledChanged<bool>
OnFocusedChanged<bool>
```

Hook these up in the inspector or via code to respond to state changes.

## Programmatic Control

```csharp
StatefulUI gadget = GetComponent<StatefulUI>();

// Set states programmatically
gadget.SetNavigated(true);      // From external navigation system
gadget.SetToggled(true);         // Toggle on
gadget.SetFocused(true);         // Give focus

// Query current state
bool isToggled = gadget.IsToggled;
InteractionState state = gadget.CurrentInteractionState;
```

## Inspector Features

### StatefulUI
- **Force Settings to All Children**: Applies parent configuration to all child StatefulVisual components, removing overrides
- **Refresh Child Visuals**: Re-scans hierarchy for StatefulVisual components

### StatefulVisual
- **Revert Button**: Per-state revert to parent default
- **Revert All to Default**: Removes all overrides with confirmation dialog

## Best Practices

1. **Use Different GameObjects for Different Dimensions**
   - Background responds to Interaction
   - CheckMark responds to Toggled
   - NavCircle responds to Navigated
   
2. **Keep Boolean Dimension Visuals Simple**
   - Use GameObject/Component enabled states
   - Avoid complex color animations

3. **Radio Groups**
   - Use consistent naming convention for group IDs
   - Empty `radioGroupId` means independent behavior

4. **Performance**
   - Child visuals are cached on Awake
   - State changes only notify children responding to that dimension
   - Use "Refresh Child Visuals" if you add children at runtime

## Architecture Notes

- **Orthogonal States**: Each dimension is independent, preventing state explosion
- **Composition Over Inheritance**: Build complex gadgets from simple visual pieces
- **Designer-Friendly**: All configuration in Inspector, no code required for common cases
- **Extensible**: Hook into events for custom behavior

## License

Part of the Laubrary package by Lautaro Ariño.

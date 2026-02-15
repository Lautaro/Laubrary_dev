# Simple Menu System

A runtime attribute-based menu system for Unity that creates UI menus automatically from method and field attributes.

## Quick Start

1. Create a class that inherits from `SimpleMenuBase`
2. Add attributes to fields and methods to define your menu
3. Add the component to a GameObject in your scene
4. Enter Play mode - the menu UI is created automatically

```csharp
using UnityEngine;
using SimpleMenuSystem;

[SimpleMenu("My Game Menu")]
public class MyMenu : SimpleMenuBase
{
    [SimpleMenuButton("Start")]
    private void StartGame()
    {
        Debug.Log("Game started!");
    }
    
    [SimpleMenuToggle("Sound")]
    private bool soundEnabled = true;
}
```

The `[SimpleMenu]` attribute is optional and sets a friendly name for the menu header.

## Features

### Menu Header
Use the `[SimpleMenu]` attribute on your menu class to set a custom header name:

```csharp
[SimpleMenu("Audio Settings")]
public class AudioMenu : SimpleMenuBase
{
    // Your controls...
}
```

Without the attribute, the class name is used as the header.

### Buttons
Use `[SimpleMenuButton]` on methods to create clickable buttons:

```csharp
[SimpleMenuButton("Start Game")]
private void StartGame()
{
    Debug.Log("Game started!");
}
```

### Toggles
Use `[SimpleMenuToggle]` on bool fields to create toggle controls:

```csharp
[SimpleMenuToggle("Enable Sound", nameof(OnSoundChanged))]
private bool soundEnabled = true;

private void OnSoundChanged(bool enabled)
{
    AudioListener.volume = enabled ? 1f : 0f;
}
```

### Sliders
Use `[SimpleMenuSlider]` on float or int fields to create slider controls:

```csharp
[SimpleMenuSlider("Master Volume", 0f, 1f, onValueChanged: nameof(OnVolumeChanged))]
private float masterVolume = 0.8f;

// Optional: Show title and/or value labels
[SimpleMenuSlider("Music Volume", 0f, 1f, showTitleLabel: true, showValueLabel: true)]
private float musicVolume = 0.8f;

private void OnVolumeChanged(float volume)
{
    Debug.Log($"Volume: {volume}");
}
```

**Slider parameters:**
- `showTitleLabel` - Shows label above the slider (default: `true`)
- `showValueLabel` - Shows current value next to slider (default: `false`)

### Dropdowns
Use `[SimpleMenuDropdown]` on string, int, or enum fields to create dropdown controls:

```csharp
// Static options
[SimpleMenuDropdown("Graphics Quality", "Low", "Medium", "High", "Ultra")]
private string graphicsQuality = "High";

// Runtime options with callback
[SimpleMenuDropdown("Resolution", nameof(GetResolutions), nameof(OnResolutionChanged))]
private string resolution = "1920x1080";

private string[] GetResolutions()
{
    return new string[] { "1920x1080", "1280x720", "800x600" };
}

private void OnResolutionChanged(string newResolution)
{
    Debug.Log($"Resolution: {newResolution}");
}
```

### Input Fields
Use `[SimpleMenuInputField]` on string fields to create text input controls:

```csharp
[SimpleMenuInputField("Player Name", "Enter your name...")]
private string playerName = "Player1";
```

### Labels
Use `[SimpleMenuLabel]` on any field to display static text:

```csharp
[SimpleMenuLabel("Version 1.0.0")]
private int version;  // Field value is ignored
```

### Text Boxes
Use `[SimpleMenuTextBox]` on any field to display informational text (with background):

```csharp
[SimpleMenuTextBox("This is an important message that will be displayed in a text box with a background.")]
private int infoBox;  // Field value is ignored
```

### Submenus
Any field of type `SimpleMenuBase` (or derived) automatically creates a navigation button. The `[SimpleMenuLink]` attribute is **optional** and only needed for custom labels.

```csharp
[SimpleMenu("Main Menu")]
public class MainMenu : SimpleMenuBase
{
    // Custom label using attribute
    [SimpleMenuLink("Settings")]
    private SettingsMenu settingsMenu;
    
    // Auto-label from [SimpleMenu] or class name - NO attribute needed!
    private AudioMenu audioMenu;  // Shows "Audio Settings" if AudioMenu has [SimpleMenu("Audio Settings")]
    
    private AdvancedSettings advanced;  // Shows class name "AdvancedSettings"
}

[SimpleMenu("Advanced Settings")]
public class AdvancedSettings : SimpleMenuBase
{
    [SimpleMenuSlider("Volume", 0f, 1f)]
    private float volume = 0.8f;
}
```

**The field itself doesn't need to be initialized** - just declare it and the menu link is created automatically.

## Accessing Controls Programmatically

You can access the UI controls directly using the `GetControl<T>()` method:

```csharp
var toggle = GetControl<Toggle>(nameof(soundEnabled));
if (toggle != null) 
{
    toggle.isOn = true;
}

var slider = GetControl<Slider>(nameof(masterVolume));
if (slider != null)
{
    slider.value = 0.5f;
}

var dropdown = GetControl<TMP_Dropdown>(nameof(graphicsQuality));
if (dropdown != null)
{
    dropdown.value = 2;
}
```

## Callbacks

All callbacks are optional and support two signatures:

```csharp
// Without parameters
private void OnValueChanged()
{
    Debug.Log("Value changed!");
}

// With the new value as parameter
private void OnValueChanged(bool newValue)
{
    Debug.Log($"New value: {newValue}");
}
```

## Menu Lifecycle

Since menus are shown/hidden by activating/deactivating GameObjects, you can use Unity's standard lifecycle methods:

```csharp
[SimpleMenu("Audio Settings")]
public class AudioMenu : SimpleMenuBase
{
    protected override void OnEnable()
    {
        base.OnEnable();
        Debug.Log("Audio menu opened");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Debug.Log("Audio menu closed");
    }
}
```

## Customization

### SimpleMenuSettings
All menus use a `SimpleMenuSettings` ScriptableObject for visual customization. Create one via:
**Assets > Create > SimpleMenu > Settings**

Settings include:
- Control prefabs (button, slider, toggle, etc.)
- Layout spacing and padding
- Control text style override
- Menu header prefab

### Padding
Customize menu content padding in SimpleMenuSettings:
- `paddingLeft` - Left padding in pixels
- `paddingRight` - Right padding in pixels
- `paddingTop` - Top padding in pixels
- `paddingBottom` - Bottom padding in pixels

### Control Text Style Override
Apply consistent text styling to all menu controls (excludes headers and textboxes):
1. Enable `Use Control Text Style Override` in SimpleMenuSettings
2. Assign a prefab with a TextMeshProUGUI component as the style source
3. All control text will inherit font, size, color, spacing, and gradient settings

## Examples

See the Examples folder for complete implementations:
- `GameSettingsExample.cs` - Comprehensive settings menu with all control types
- `AudioMenu.cs` - Audio settings example
- `VideoMenu.cs` - Video settings example

## Creating Your Own Menu

```csharp
using UnityEngine;
using SimpleMenuSystem;

[SimpleMenu("Main Menu")]
public class MyMainMenu : SimpleMenuBase
{
    [SimpleMenuButton("Play Game")]
    private void Play()
    {
        Debug.Log("Starting game!");
    }

    [SimpleMenuLink("Options")]
    private OptionsMenu optionsMenu;

    [SimpleMenuButton]
    private void QuitGame()
    {
        Application.Quit();
    }
}
```

## Attributes

### SimpleMenuAttribute (Class-level)
Defines a custom friendly name for the menu header and auto-labels.

```csharp
[SimpleMenu("Audio Settings")]
public class AudioMenu : SimpleMenuBase
{
    // Menu header will display "Audio Settings"
}
```

### SimpleMenuButtonAttribute
Creates a UI button that calls the decorated method when clicked.

```csharp
[SimpleMenuButton("Custom Label")]
private void MyMethod()
{
    // Your code here
}
```

If no label is provided, the method name is used as the button label.

### SimpleMenuSliderAttribute
Creates a slider control. Always uses vertical layout.

**Parameters:**
- `label` - Label text
- `minValue` - Minimum value (default: 0)
- `maxValue` - Maximum value (default: 1)
- `wholeNumbers` - Use integers instead of floats (default: false)
- `onValueChanged` - Callback method name
- `showTitleLabel` - Show label above slider (default: true)
- `showValueLabel` - Show value label (default: false)

### SimpleMenuLabelAttribute
Displays static text without any background or interaction.

```csharp
[SimpleMenuLabel("Version 1.0.0")]
private int dummy;  // Field type and value don't matter
```

### SimpleMenuTextBoxAttribute
Displays informational text with a background (info box style).

```csharp
[SimpleMenuTextBox("This is important information displayed with a background.")]
private int dummy;  // Field type and value don't matter
```

### SimpleMenuLinkAttribute - OPTIONAL
**You don't need this attribute!** Any field of type `SimpleMenuBase` automatically becomes a menu link.

Only use this attribute if you want a custom label:

```csharp
// Without attribute - uses class name or [SimpleMenu] label
private SettingsMenu settingsMenu;  // Button shows "SettingsMenu" or its [SimpleMenu] label

// With attribute - custom label
[SimpleMenuLink("Game Settings")]
private SettingsMenu settingsMenu;  // Button shows "Game Settings"
```

### MenuIdAttribute
Optionally defines a custom identifier for a menu class (for programmatic switching).

```csharp
[MenuId("MainOptions")]
public class OptionsMenu : SimpleMenuBase
{
    // ...
}
```

### SubMenuAttribute (DEPRECATED)
*The old method-based submenu navigation is deprecated. Use `[SimpleMenuLink]` on fields instead.*

```csharp
// OLD WAY (deprecated):
[SubMenu(typeof(SettingsMenu), "Settings")]
private void OpenSettings() { }

// NEW WAY (recommended):
[SimpleMenuLink("Settings")]
private SettingsMenu settingsMenu;
```

## Programmatic Menu Switching

Switch to a menu by type:
```csharp
SwitchToMenu(typeof(AudioMenu));
```

Switch to a menu by string ID (class name or MenuId):
```csharp
SwitchToMenu("AudioMenu");
// or if using MenuIdAttribute
SwitchToMenu("MainOptions");
```

## Architecture

- **Main Menu GameObject**: Always enabled, runs the menu scripts
- **UI Containers**: Child GameObjects that contain the visual elements
  - Main menu UI is in a child container
  - Submenus are created in child containers of the main canvas
- **Menu Navigation**: When a submenu is opened, it's checked if it already exists. If not, it's created and cached for future use

## Example Hierarchy

```
GameObject (SimpleMenu script)
  └── MenuCanvas
      ├── MainMenu_Container (Main menu UI)
      ├── SettingsMenu_Container (Settings submenu UI)
      └── AudioMenu_Container (Audio submenu UI)
```

## Notes

- The main menu GameObject stays enabled at all times
- Only the active menu's UI container is visible
- Submenus automatically include a "Back" button
- All menus are cached and reused when switching between them
- Menu headers are automatically created from the `[SimpleMenu]` attribute or class name

# SimpleUI Demo Scene

This demo scene showcases the complete **SimpleUI** POCO→TextMeshPro auto-binding system.

## Scene Overview

The scene contains a character sheet UI that demonstrates all major SimpleUI features:

### UI Hierarchy
```
Canvas
└── Panel (SimpleUIView<CharacterSheet> component)
    ├── Title
    ├── characterName (TMP_Text) - Binds to string
    ├── HealthLabel
    ├── health (TMP_Text) - Binds to int
    ├── StaminaLabel
    ├── stamina (Slider) - Binds to float with two-way binding
    ├── AliveLabel
    ├── isAlive (Toggle) - Binds to bool with two-way binding
    ├── ClassLabel
    ├── characterClass (TMP_Dropdown) - Binds to enum with auto-population
    ├── level (TMP_Text) - Uses [SimpleUIFormat] attribute
    ├── gold (TMP_Text) - Uses [SimpleUIFormat] with number formatting
    ├── StatsLabel
    └── Stats
        └── Details
            └── ExperienceBar (TMP_Text) - Uses [SimpleUIPath] attribute
```

## Features Demonstrated

### 1. Convention-Based Binding
Most fields use automatic name-based resolution:
- `characterName` → GameObject named "characterName"
- `health` → GameObject named "health"
- `stamina` → GameObject named "stamina"
- etc.

### 2. Type Inference
SimpleUI automatically selects the correct component:
- `string characterName` → TMP_Text
- `int health` → TMP_Text
- `float stamina` → Slider
- `bool isAlive` → Toggle
- `CharacterClass characterClass` → TMP_Dropdown

### 3. Two-Way Binding
Interactive components automatically sync back to POCO:
- **Slider** (stamina) - Drag to change stamina value
- **Toggle** (isAlive) - Click to toggle alive state
- **Dropdown** (characterClass) - Select to change class

### 4. Format Strings
```csharp
[SimpleUIFormat("Level {0}")]
public int level;  // Displays as "Level 5"

[SimpleUIFormat("Gold: {0:N0}")]
public int gold;  // Displays as "Gold: 12,500"

[SimpleUIFormat("XP: {0}/1000")]
public int experience;  // Displays as "XP: 750/1000"
```

### 5. Explicit Path Resolution
```csharp
[SimpleUIPath("Stats/Details/ExperienceBar")]
public int experience;  // Resolves via path instead of name
```

### 6. Field Ignoring
```csharp
[SimpleUIIgnore]
public float cachedDamage;  // Not bound to any UI
```

## Controls

Press the following keys to test the system:

- **SPACE** - Take 15 damage
- **R** - Heal 20 HP
- **G** - Add random gold (10-100)
- **E** - Add 50 experience (levels up at 1000 XP)

You can also:
- Drag the **stamina slider** to see two-way binding
- Toggle the **alive checkbox**
- Change the **class dropdown**
- Edit values in the Inspector (Enter Play mode first)

## Code Structure

### POCO Definition
```csharp
[SimpleUI]
public class CharacterSheet
{
    public string characterName;
    public int health;
    public float stamina;
    public bool isAlive;
    public CharacterClass characterClass;
    
    [SimpleUIFormat("Level {0}")]
    public int level;
    
    [SimpleUIFormat("Gold: {0:N0}")]
    public int gold;
    
    [SimpleUIPath("Stats/Details/ExperienceBar")]
    [SimpleUIFormat("XP: {0}/1000")]
    public int experience;
    
    [SimpleUIIgnore]
    public float cachedDamage;
}
```

### Controller Usage
```csharp
// Get the view component
_view = GetComponent<SimpleUIView<CharacterSheet>>();

// Create data
_character = new CharacterSheet { ... };

// Update UI
_view.UpdateUI(_character);

// Later, when data changes
_character.health -= 15;
_view.UpdateUI(_character);
```

## How to Run

1. **IMPORTANT**: First add TextMeshPro assembly reference:
   - Select `/Assets/Packages/Laubrary/Runtime/SimpleUI/SimpleUI.asmdef`
   - In Inspector → Assembly Definition References → Add `Unity.TextMeshPro`
   - Click Apply

2. Open the scene: `/Assets/Demos/SimpleUI/SimpleUI.unity`

3. Enter Play mode

4. Check the Console for binding report:
   ```
   [SimpleUI] CharacterSheet binding report:
   Summary: 8 Success, 0 Errors
   ✓ characterName → characterName (TextMeshProUGUI) [Strong]
   ✓ health → health (TextMeshProUGUI) [Strong]
   ✓ stamina → stamina (Slider) [Weak]
   ✓ isAlive → isAlive (Toggle) [Weak]
   ✓ characterClass → characterClass (TMP_Dropdown) [Weak]
   ✓ level → level (TextMeshProUGUI) [Strong]
   ✓ gold → gold (TextMeshProUGUI) [Strong]
   ✓ experience → Stats/Details/ExperienceBar (TextMeshProUGUI) [Explicit]
   ```

5. Select the Panel GameObject and inspect the **SimpleUIView** component in the Inspector to see detailed binding diagnostics

6. Use keyboard controls to test functionality

## Expected Behavior

✓ Character name displays at top  
✓ Health displays in green text  
✓ Stamina slider shows 85.5%  
✓ Alive toggle is checked  
✓ Class dropdown shows "Warrior" (auto-populated with enum values)  
✓ Level shows "Level 5" (formatted)  
✓ Gold shows "Gold: 12,500" (number formatting)  
✓ Experience shows "XP: 750/1000" (formatted and pathed)  

## Troubleshooting

**Problem**: Compilation errors  
**Solution**: Add TextMeshPro assembly reference (see step 1 above)

**Problem**: No binding report in Console  
**Solution**: Make sure you're in Play mode

**Problem**: Binding errors shown  
**Solution**: Check the Inspector diagnostics on the Panel GameObject for detailed error messages and suggested fixes

**Problem**: UI doesn't update when pressing keys  
**Solution**: Make sure the Panel GameObject has the `SimpleUIDemo` component attached

## Learning Points

This demo illustrates:
- How SimpleUI automatically resolves bindings
- The difference between one-way and two-way bindings
- How to use attributes for customization
- How to handle format strings
- How to use explicit paths for complex hierarchies
- How diagnostics help debug binding issues

Experiment with the scene by:
- Renaming GameObjects to see resolution fail
- Adding duplicate GameObjects to test ambiguity detection
- Removing components to see missing component errors
- Modifying the POCO to add new fields

---

**SimpleUI Demo** - A comprehensive showcase of POCO→UI auto-binding in Unity.

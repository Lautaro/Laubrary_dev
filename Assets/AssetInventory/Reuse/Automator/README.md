# Automator

Workflow automation framework for Unity tools.

## Features

- Define and execute action sequences
- Parameterized steps with string, int, bool, and multiline string types
- Variable system with `$varname` syntax
- Built-in internal variables (e.g., `$Application.unityVersion`, `$DateTime.Now`)
- Extensible variable groups via `VariableGroupRegistry`
- JSON and SQLite persistence options
- Reflection-based step discovery
- Editor window for creating and editing actions

## Installation

Add the package to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wetzold.automator": "1.0.0"
  }
}
```

## Usage

### Creating Custom Steps

```csharp
using Automator;

[Serializable]
public sealed class MyCustomStep : ActionStep
{
    public MyCustomStep()
    {
        Key = "MyCustomStep";
        Name = "My Custom Step";
        Description = "Does something custom.";
        Category = ActionCategory.Misc;
        Parameters.Add(new StepParameter
        {
            Name = "Input",
            Description = "Input value for the step."
        });
    }

    public override async Task Run(List<ParameterValue> parameters)
    {
        string input = parameters[0].stringValue;
        // Do something with input
        await Task.Yield();
    }
}
```

### Registering Custom Variable Groups

```csharp
using ImpossibleRobert.Common;

// Register any object as a variable group at initialization
// The lambda is called each time a variable is resolved, so values are always fresh
VariableGroupRegistry.Register("Config", () => MySettings.Instance);

// Now $Config.PropertyName will resolve to MySettings.Instance.PropertyName
```

### Using the Action Runner

```csharp
using Automator;

// Create a repository
var repository = new JsonActionRepository("path/to/actions.json");

// Create and run an action
var runner = new ActionRunner(repository);
await runner.RunAction(actionId);
```

## Built-in Steps

### Files and Folders
- Copy File
- Move File
- Delete File
- Create Folder
- Move Folder
- Delete Folder
- Compress Folder
- Extract Folder
- Reveal Folder

### Settings
- Add Define Symbol
- Remove Define Symbol
- Add Compiler Argument
- Remove Compiler Argument
- Set Project Property

### Packaging
- Install Registry Package by Name
- Install Registry Package by Path
- Uninstall Registry Package by Name
- Uninstall Feature by Name
- Update Registry Package by Name
- Install TMP

### Miscellaneous
- Run Command Line
- Debug Log
- Message Dialog
- Set Text Variable
- Restart Editor
- Run Action

## Persistence

Automator supports multiple persistence backends:

- **JsonActionRepository**: Stores actions in a JSON file
- **SqliteActionRepository**: Stores actions in an SQLite database (requires external connection)

## License

Copyright © 2024 Impossible Robert. All rights reserved.

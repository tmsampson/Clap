# Clap

**Clap** is a lightweight and simple command-line argument parser for C#. Designed to minimise boilerplate while offering powerful multi-target parsing capabilities, Clap makes it easy to map command-line arguments directly into your objects using attributes.

Features:

- Simple attribute-based configuration.
- Supports parsing into multiple objects simultaneously.
- Single-pass and multi-pass parsing (ignore unknown args in first pass).
- Optional argument names (defaults to property name via reflection).
- Dependency options with `RequiredUnless`.

## Installation

Clap is a single-file library. Simply add Clap.cs into your project.

## Basic Example

Parses command line arguments into `AppOptions` object, with a required `Output` property:

```csharp
class AppOptions
{
    [Option(Description = "Enables verbose logging")]
    public bool Verbose { get; set; }

    [Option(Description = "Specifies the output file", Required = true)]
    public string Output { get; set; }
}

static void Main(string[] args)
{
    var options = new AppOptions();
    var request = new ParseRequest
    {
        InputArgs = args,
        TargetObjects = [ options ]
    };

    var parser = new Parser(request);
    var result = parser.Parse();

    if (result.Status == ParseStatus.Succeeded)
    {
        Console.WriteLine($"Verbose: {options.Verbose}");
        Console.WriteLine($"Output: {options.Output}");
    }
    else
    {
        parser.PrintUsage();
    }
}
```

### Example Usage

```bash
app.exe --Verbose --Output result.txt
```

## Optional: Explicit Argument Names

Overrides the property name by specifying `Name` via the `Option` attribute:

```csharp
class RenamedOptions
{
    [Option(Name = "log-level", Description = "Set log level")]
    public string Level { get; set; }
}

static void Main(string[] args)
{
    var options = new RenamedOptions();
    var request = new ParseRequest
    {
        InputArgs = args,
        TargetObjects = [ options ]
    };

    var parser = new Parser(request);
    var result = parser.Parse();

    if (result.Status == ParseStatus.Succeeded)
    {
        Console.WriteLine($"Log Level: {options.Level}");
    }
    else
    {
        parser.PrintUsage();
    }
}
```

### Example Command

```bash
app.exe --log-level debug
```

## Parsing Multiple Target Objects

Parses arguments into several objects at once:

```csharp
class GeneralOptions
{
    [Option(Description = "Enable debug mode")]
    public bool Debug { get; set; }
}

class FileOptions
{
    [Option(Description = "Input file path", Required = true)]
    public string Input { get; set; }

    [Option(Description = "Output file path", RequiredUnless = "DryRun")]
    public string Output { get; set; }

    [Option(Description = "Dry run mode")]
    public bool DryRun { get; set; }
}

static void Main(string[] args)
{
    var generalOptions = new GeneralOptions();
    var fileOptions = new FileOptions();
    var request = new ParseRequest
    {
        InputArgs = args,
        TargetObjects = [ generalOptions, fileOptions ]
    };

    var parser = new Parser(request);
    var result = parser.Parse();

    if (result.Status == ParseStatus.Succeeded)
    {
        Console.WriteLine($"Debug: {generalOptions.Debug}");
        Console.WriteLine($"Input: {fileOptions.Input}");
        Console.WriteLine($"Output: {fileOptions.Output}");
        Console.WriteLine($"DryRun: {fileOptions.DryRun}");
    }
    else
    {
        parser.PrintUsage();
    }
}
```

### Example usage

```bash
app.exe --Debug --Input data.txt --DryRun
```

## Multi-Pass Parsing: Ignoring Unknown Arguments in First Pass

Parses arguments into several objects across multiple passes, using `ParseOptions.IgnoreUnrecognised` to prevent early (partial) parse from failing.

```csharp
class CoreOptions
{
    [Option(Description = "Specifies which command to invoke")]
    public string CommandName { get; set; }
}

class PushCommandOptions
{
    [Option(Description = "Specifies Push URL", Required = true)]
    public string PushUrl { get; set; }
}

static int Main(string[] args)
{
    // 1. Parse core options
    var core = new CoreOptions();
    var firstPassRequest = new ParseRequest
    {
        InputArgs = args,
        TargetObjects = [ core ]
    };

    var parser = new Parser(firstPassRequest);
    var firstPassResult = parser.Parse(new ParseOptions { IgnoreUnrecognised = true });
    if (firstPassResult.Status != ParseStatus.Succeeded)
    {
        parser.PrintUsage();
        return;
    }

    // 2. Optionally parse push command args using unconsumed arguments from first pass
    if(core.CommandName == "Push")
    {
        var pushOptions = new PushCommandOptions();
        var secondPassRequest = new ParseRequest
        {
            InputArgs = firstPassResult.RemainingArgs,
            TargetObjects = [ pushOptions ]
        };

        var secondParser = new Parser(secondPassRequest);
        var secondPassResult = secondParser.Parse();
        if (secondPassResult.Status != ParseStatus.Succeeded)
        {
            secondParser.PrintUsage();
            return;
        }
    }
}
```

### Example Usage

```bash
app.exe --CommandName Push --PushUrl "http://foo.bar"
```

## Dependencies with `RequiredUnless`

Conditionally requires an `Output` option, only if `DryRun` option *isn't* present, using `RequiredUnless`.

```csharp
class ConditionalOptions
{
    [Option(Description = "Output file", RequiredUnless = "DryRun")]
    public string Output { get; set; }

    [Option(Description = "Perform a dry run")]
    public bool DryRun { get; set; }
}
```

## Printing Usage Information

You can print a generated usage guide at any time:

```csharp
parser.PrintUsage();
```

---

## Limitations

- Only supports primitive types: `string`, `bool`, `int`, `float`, `double`, `char`.
- No short-form arguments (e.g., `-v`) — long-form only (`--Verbose`).

## License

MIT License.
# Clap

Command Line Argument Parser

## Overview

A single-file, minimal C# command line argument parser which supports parsing to multiple option classes.

## Setup
Create a simple class to hold command line options.
```csharp
class MyOptions
{
    [Clap.Option("my-flag", HelpText = "Some flag", Required = true)]
    public bool MyFlag { get; set; } = false;
    [Clap.Option("my-string", HelpText = "Some string")]
    public string? MyString { get; set; } = null;
    [Clap.Option("my-int", HelpText = "Some int")]
    public int MyInt { get; set; } = -1;
}
```

## Parsing

Basic parsing example.

```csharp
MyOptions _options = new();

Clap.Parser parser = new(_options);
if(!parser.Parse(Environment.GetCommandLineArgs()))
{
    parser.PrintUsage();
    return false;
}
```

## Parsing Multiple Option Classes

Parse to multiple objects in a single pass.

```csharp
MyOptionsA _optionsA = new();
MyOptionsB _optionsB = new();

Clap.Parser parser = new(_optionsA);
parser.AddOptionsObject(_optionsB);
if(!parser.Parse(Environment.GetCommandLineArgs()))
{
    parser.PrintUsage();
    return false;
}
```

## Usage

```bash
--my-flag --my-string "foo" --my-int 10
```

## Example Output

```bash
Error: required option --my-flag was not provided.
Usage: app [options]
Options:
  --my-flag     Some flag
  --some-string Some string
  --some-int    Some int
```

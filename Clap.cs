using System.Reflection;

namespace Clap;

[AttributeUsage(AttributeTargets.Property)]
public class OptionAttribute() : Attribute
{
	public string? Name { get; set; } = null;
	public bool Required { get; set; } = false;
	public string? Description { get; set; } = null;
	public string? RequiredUnless { get; set; } = null;
}

public class ParseRequest(string[] inputArgs)
{
	public string[] InputArgs = inputArgs;
	public bool IgnoreUnrecognised { get; init; } = false;
}

public enum ParseStatus
{
	Failed,
	Succeeded
}

public class ParseResult(ParseStatus status, ParseRequest request, HashSet<int> usedIndices)
{
	public readonly ParseStatus Status = status;
	public readonly ParseRequest Request = request;
	public readonly List<string> InputArgs = request.InputArgs.ToList();
	public readonly List<string> ProcessedArgs = request.InputArgs.Where((item, index) => usedIndices.Contains(index)).ToList();
	public readonly List<string> RemainingArgs = request.InputArgs.Where((item, index) => !usedIndices.Contains(index)).ToList();
}

public class Parser
{
	public Parser(object obj)
	{
		AddOptionsObject(obj);
	}

	internal class OptionDefinition
	{
		public required PropertyInfo Property { get; set; }
		public required OptionAttribute Attribute { get; set; }
		public required object Instance { get; set; }
		public bool WasSpecified { get; set; } = false;
		public string GetLongArg()
		{
			string longArgName = Attribute.Name ?? Property.Name;
			return $"--{longArgName}";
		}
	}

	private readonly List<OptionDefinition> _options = [];

	public void AddOptionsObject(object obj)
	{
		var type = obj.GetType();
		var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

		foreach (var prop in props)
		{
			var attr = prop.GetCustomAttribute<OptionAttribute>();
			if (attr != null)
			{
				_options.Add(new OptionDefinition
				{
					Property = prop,
					Attribute = attr,
					Instance = obj,
					WasSpecified = false
				});
			}
		}
	}

	public ParseResult Parse(string[] inputArgs)
	{
		ParseRequest request = new(inputArgs);
		return Parse(request);
	}

	public ParseResult Parse(ParseRequest request)
	{
		// Track which token indices we've consumed (for detecting unrecognized options)
		var usedIndices = new HashSet<int>();
		ParseStatus status = ParseStatus.Succeeded;

		// For each known option, look for its presence in the CLI args
		foreach (OptionDefinition option in _options)
		{
			// Validate option
			string longArg = option.GetLongArg();
			if (option.Attribute.Required && !string.IsNullOrEmpty(option.Attribute.RequiredUnless))
			{
				PrintError($"Error: option {longArg} cannot be both required and have a requiredUnless dependency.");
				status = ParseStatus.Failed;
			}

			// Check if that argument is present
			int index = Array.FindIndex(request.InputArgs, a => string.Equals(a, longArg, StringComparison.OrdinalIgnoreCase));
			if (index >= 0)
			{
				usedIndices.Add(index);
				option.WasSpecified = true;

				// If it's bool, treat presence as "true" unless next token is explicitly "false"
				if (option.Property.PropertyType == typeof(bool))
				{
					// Look ahead to see if the user typed "true"/"false"
					// e.g. "--silent false"
					if (index + 1 < request.InputArgs.Length && !request.InputArgs[index + 1].StartsWith("-"))
					{
						if (bool.TryParse(request.InputArgs[index + 1], out bool boolVal))
						{
							option.Property.SetValue(option.Instance, boolVal);
							usedIndices.Add(index + 1);
						}
						else
						{
							Console.Error.WriteLine($"Error: invalid boolean value '{request.InputArgs[index + 1]}' for option {longArg}. Expected 'true' or 'false'.");
							status = ParseStatus.Failed;
						}
					}
					else
					{
						// No next token or next is another option => set to true
						option.Property.SetValue(option.Instance, true);
					}
				}
				else
				{
					// For non-bool, we expect the next token to be the value
					int valIndex = index + 1;
					if (valIndex >= request.InputArgs.Length || request.InputArgs[valIndex].StartsWith("-"))
					{
						PrintError($"Error: option {longArg} needs a value.");
						status = ParseStatus.Failed;
					}
					usedIndices.Add(valIndex);

					string rawValue = request.InputArgs[valIndex];
					object? converted = ConvertValue(rawValue, option.Property.PropertyType);
					if (converted != null)
					{
						option.Property.SetValue(option.Instance, converted);
					}
					else
					{
						PrintError($"Error: cannot convert '{rawValue}' to {option.Property.PropertyType.Name} for {longArg}.");
						status = ParseStatus.Failed;
					}
				}
			}
		}

		// Mark RequiredUnless options as required if their dependencies weren't met
		foreach (OptionDefinition option in _options)
		{
			if (string.IsNullOrEmpty(option.Attribute.RequiredUnless))
			{
				continue;
			}

			string otherOptionName = option.Attribute.RequiredUnless;
			var otherOption = _options.FirstOrDefault(o => o.Attribute.Name == otherOptionName);
			if (otherOption == null)
			{
				PrintError($"Configuration error: --{option.Attribute.Name} references " +
						$"invalid option --{otherOptionName}");
				status = ParseStatus.Failed;
			}
			else if (!otherOption.WasSpecified && !option.WasSpecified)
			{
				// If the other option wasn't specified, this one becomes required
				option.Attribute.Required = true;
			}
		}

		// Report any missing options
		List<OptionDefinition> missingOptions = _options.Where(o => o.Attribute.Required && !o.WasSpecified).ToList();
		if (missingOptions.Count > 0)
		{
			foreach (OptionDefinition option in missingOptions)
			{
				PrintError($"Error: required option --{option.Attribute.Name} was not provided.");
				status = ParseStatus.Failed;
			}
		}

		// Check for leftover unrecognized arguments
		if (!request.IgnoreUnrecognised)
		{
			for (int i = 0; i < request.InputArgs.Length; i++)
			{
				if (!usedIndices.Contains(i) && request.InputArgs[i].StartsWith('-'))
				{
					PrintError($"Error: unrecognized option '{request.InputArgs[i]}'.");
					status = ParseStatus.Failed;
				}
			}
		}

		// Success
		return new(status, request, usedIndices);
	}

	/// <summary>
	/// Print usage info for all registered options.
	/// </summary>
	public void PrintUsage()
	{
		Console.WriteLine("Usage: app [options]");
		Console.WriteLine("Options:");
		const int indentWidth = 6;
		int longestNameLength = _options.Where(o => !string.IsNullOrEmpty(o.Attribute?.Name))
										.Select(o => ("--" + o.Attribute.Name)?.Length)
										.DefaultIfEmpty(0).Max() ?? 12;
		foreach (var optDef in _options)
		{
			var attr = optDef.Attribute;
			string longName = $"--{attr.Name}";
			string indent = new(' ', longestNameLength - longName.Length + indentWidth);
			string requiredText = attr.Required ? " [required]" : "";
			Console.WriteLine($"  {longName}{indent}{attr.Description}{requiredText}");
		}
	}

	public static void PrintError(string error)
	{
		const string Red = "\u001b[31m", Reset = "\u001b[0m";
		Console.Error.WriteLine(Red + error + Reset);
	}

	/// <summary>
	/// Convert a raw string to the given type (string, bool, int, float, double, char).
	/// Return null if conversion fails or is unsupported.
	/// </summary>
	private static object? ConvertValue(string raw, Type targetType)
	{
		try
		{
			if (targetType == typeof(string))
			{
				return raw;
			}
			if (targetType == typeof(bool))
			{
				return bool.TryParse(raw, out bool bVal) ? bVal : null;
			}
			if (targetType == typeof(int))
			{
				return int.TryParse(raw, out int iVal) ? iVal : null;
			}
			if (targetType == typeof(float))
			{
				return float.TryParse(raw, out float fVal) ? fVal : null;
			}
			if (targetType == typeof(double))
			{
				return double.TryParse(raw, out double dVal) ? dVal : null;
			}
			if (targetType == typeof(char))
			{
				return raw.Length == 1 ? raw[0] : (char?)null;
			}
		}
		catch
		{
			// If parsing fails, fall through to return null
		}
		return null;
	}
}
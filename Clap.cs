using System.Reflection;

namespace Clap;

[AttributeUsage(AttributeTargets.Property)]
public class OptionAttribute(string name, bool required = false, string requiredUnless = "", string helpText = "") : Attribute
{
	public string? Name { get; set; } = name;
	public bool Required { get; set; } = required;
	public string? HelpText { get; set; } = helpText;
	public string RequiredUnless { get; set; } = requiredUnless;
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

	public bool Parse(string[] args)
	{
		// Track which token indices we've consumed (for detecting unrecognized options)
		var usedIndices = new HashSet<int>();

		// For each known option, look for its presence in the CLI args
		foreach (OptionDefinition option in _options)
		{
			// Validate option
			string longArg = "--" + option.Attribute.Name;
			if(option.Attribute.Required && !string.IsNullOrEmpty(option.Attribute.RequiredUnless))
			{
				PrintError($"Error: option {longArg} cannot be both required and have a requiredUnless dependency.");
				return false;
			}

			// Check if that argument is present
			int index = Array.FindIndex(args, a => a == longArg);
			if (index >= 0)
			{
				usedIndices.Add(index);
				option.WasSpecified = true;

				// If it's bool, treat presence as "true" unless next token is explicitly "false"
				if (option.Property.PropertyType == typeof(bool))
				{
					// Look ahead to see if the user typed "true"/"false"
					// e.g. "--silent false"
					if (index + 1 < args.Length && !args[index + 1].StartsWith("-"))
					{
						if (bool.TryParse(args[index + 1], out bool boolVal))
						{
							option.Property.SetValue(option.Instance, boolVal);
							usedIndices.Add(index + 1);
						}
						else
						{
							// If it's not "true"/"false", ignore and set "true"
							option.Property.SetValue(option.Instance, true);
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
					if (valIndex >= args.Length || args[valIndex].StartsWith("-"))
					{
						PrintError($"Error: option {longArg} needs a value.");
						return false;
					}
					usedIndices.Add(valIndex);

					string rawValue = args[valIndex];
					object? converted = ConvertValue(rawValue, option.Property.PropertyType);
					if (converted != null)
					{
						option.Property.SetValue(option.Instance, converted);
					}
					else
					{
						PrintError($"Error: cannot convert '{rawValue}' to {option.Property.PropertyType.Name} for {longArg}.");
						return false;
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
				return false;
			}

			// If the other option wasn't specified, this one becomes required
			if (!otherOption.WasSpecified && !option.WasSpecified)
			{
				option.Attribute.Required = true;
			}
		}

		// Report any missing options
		List<OptionDefinition> missingOptions = _options.Where(o => o.Attribute.Required && !o.WasSpecified).ToList();
		if(missingOptions.Count > 0)
		{
			foreach (OptionDefinition option in missingOptions)
			{
				PrintError($"Error: required option --{option.Attribute.Name} was not provided.");
			}
			return false;
		}

		// Check for leftover unrecognized arguments
		for (int i = 0; i < args.Length; i++)
		{
			if (!usedIndices.Contains(i) && args[i].StartsWith("-"))
			{
				PrintError($"Error: unrecognized option '{args[i]}'.");
				return false;
			}
		}

		return true; // success
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
			Console.WriteLine($"  {longName}{indent}{attr.HelpText}{requiredText}");
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
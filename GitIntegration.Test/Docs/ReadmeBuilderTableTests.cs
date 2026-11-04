// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Holds the README's builder options table to the public API, so a rename such as the move to US
/// spelling cannot leave the quick reference naming a member that no longer exists.
/// </summary>
[TestClass]
public partial class ReadmeBuilderTableTests
{
	/// <summary>A table row whose first cell names a builder interface: the interface, then its options cell.</summary>
	[GeneratedRegex(@"^\|\s*`(?<type>IGit\w+Builder)`\s*\|(?<options>[^|]*)\|", RegexOptions.Multiline)]
	private static partial Regex BuilderRow();

	/// <summary>A method named in an options cell, such as <c>`WithDepth(int)`</c>.</summary>
	[GeneratedRegex(@"`(?<name>\w+)\(")]
	private static partial Regex OptionName();

	private static string Readme() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "README.md"));

	private static HashSet<string> MethodNames(Type type) =>
		[.. type.GetInterfaces().Prepend(type).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)).Select(m => m.Name)];

	[TestMethod]
	public void EveryBuilderOptionInTheReadmeExistsOnItsInterface()
	{
		Assembly library = typeof(GitOptions).Assembly;
		MatchCollection rows = BuilderRow().Matches(Readme());
		Assert.IsGreaterThan(20, rows.Count, "The README's builder options table was not found.");

		List<string> missing = [];
		foreach (Match row in rows)
		{
			string typeName = row.Groups["type"].Value;
			Type? type = library.GetType($"ktsu.GitIntegration.{typeName}");
			if (type is null)
			{
				missing.Add(typeName);
				continue;
			}

			HashSet<string> methods = MethodNames(type);
			missing.AddRange(OptionName().Matches(row.Groups["options"].Value)
				.Select(option => option.Groups["name"].Value)
				.Where(name => !methods.Contains(name))
				.Select(name => $"{typeName}.{name}()"));
		}

		Assert.IsEmpty(missing, $"The README names members that do not exist: {string.Join(", ", missing)}");
	}
}

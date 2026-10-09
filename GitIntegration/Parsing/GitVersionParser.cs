// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Globalization;

/// <summary>
/// Reads <c>git --version</c>.
/// </summary>
internal static class GitVersionParser
{
	private const string Prefix = "git version ";

	/// <summary>
	/// Parses the output of <c>git --version</c>.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed version.</returns>
	/// <exception cref="GitParseException">The output did not have the expected shape.</exception>
	internal static GitVersion Parse(string output)
	{
		Ensure.NotNull(output);

		string trimmed = output.Trim();

		if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
		{
			throw new GitParseException($"Unrecognized 'git --version' output: '{trimmed}'.");
		}

		string raw = trimmed[Prefix.Length..];
		string[] components = raw.Split('.');

		// The major component must be a number for the value to mean anything. Minor and patch
		// default to zero, because git has shipped two-component versions and because a build
		// suffix such as ".windows.1" makes trailing components non-numeric by design.
		if (!int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
		{
			throw new GitParseException($"Unrecognized git version number: '{raw}'.");
		}

		return new GitVersion
		{
			Major = major,
			Minor = ReadComponent(components, 1),
			Patch = ReadComponent(components, 2),
			Raw = raw,
		};
	}

	// Reads the component's leading digits, so a build note glued to the last component still
	// leaves its number: Apple's git reports "2.39.3 (Apple Git-145)", whose third component is
	// "3 (Apple Git-145)", and an rc build reports "0-rc0". A component with no leading digit,
	// such as "windows", reads as zero.
	private static int ReadComponent(string[] components, int index)
	{
		if (index >= components.Length)
		{
			return 0;
		}

		string component = components[index];
		int length = 0;
		while (length < component.Length && char.IsAsciiDigit(component[length]))
		{
			length++;
		}

		return int.TryParse(component.AsSpan(0, length), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
			? value
			: 0;
	}
}

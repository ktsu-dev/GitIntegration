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
			throw new GitParseException($"Unrecognised 'git --version' output: '{trimmed}'.");
		}

		string raw = trimmed[Prefix.Length..];
		string[] components = raw.Split('.');

		// The major component must be a number for the value to mean anything. Minor and patch
		// default to zero, because git has shipped two-component versions and because a build
		// suffix such as ".windows.1" makes trailing components non-numeric by design.
		if (components.Length == 0 ||
			!int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
		{
			throw new GitParseException($"Unrecognised git version number: '{raw}'.");
		}

		return new GitVersion
		{
			Major = major,
			Minor = ReadComponent(components, 1),
			Patch = ReadComponent(components, 2),
			Raw = raw,
		};
	}

	private static int ReadComponent(string[] components, int index) =>
		index < components.Length &&
		int.TryParse(components[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
			? value
			: 0;
}

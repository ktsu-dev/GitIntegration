// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Buffers;

using ktsu.Semantics.Strings;

/// <summary>
/// Validates that a value is a branch name git would accept, following the rules
/// <c>git check-ref-format --branch</c> enforces.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a name git forbids constructs successfully and fails only later, inside git, as a
/// generic <see cref="GitException"/> that does not say which value was wrong.
/// </para>
/// <para>
/// The rule must be no stricter than git's own, because the parsers build
/// <see cref="GitBranchName"/> from git's output, and a name git accepted but this rejected would
/// turn every <c>Status()</c> or <c>Branches()</c> in that repository into a
/// <see cref="GitParseException"/>. That is why the bare name <c>@</c> is allowed: git's refname
/// rules reject only the whole refname <c>@</c>, so <c>git branch @</c> creates
/// <c>refs/heads/@</c> and <c>status</c> then reports <c>branch.head @</c> (verified against
/// git 2.43). Non-ASCII names are allowed for the same reason.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IsGitBranchNameAttribute : SemanticStringValidationAttribute
{
	/// <summary>
	/// The printable ASCII characters git forbids anywhere in a refname.
	/// </summary>
	private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create("~^:?*[\\");

	/// <summary>
	/// Validates that the supplied value is a branch name git would accept.
	/// </summary>
	/// <param name="semanticString">The semantic string to validate.</param>
	/// <returns>
	/// <see langword="true"/> when the value is non-empty and breaks none of git's refname rules;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	public override bool Validate(ISemanticString semanticString)
	{
		string? value = semanticString?.WeakString;

		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		if (value.AsSpan().ContainsAny(ForbiddenCharacters)
			|| value.Contains("..", StringComparison.Ordinal)
			|| value.Contains("@{", StringComparison.Ordinal)
			|| value.EndsWith('.'))
		{
			return false;
		}

		foreach (char character in value)
		{
			// Space and every ASCII control character, DEL included.
			if (character is <= ' ' or '\u007f')
			{
				return false;
			}
		}

		// Splitting on '/' also catches a leading or trailing slash and "//", each of which leaves
		// an empty component.
		foreach (string component in value.Split('/'))
		{
			if (component.Length == 0
				|| component[0] == '.'
				|| component.EndsWith(".lock", StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}
}

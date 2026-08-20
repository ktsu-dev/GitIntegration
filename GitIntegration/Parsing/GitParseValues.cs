// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Turns raw git output into <c>ktsu.Semantics</c> values, or explains why it could not.
/// </summary>
internal static class GitParseValues
{
	/// <summary>
	/// Converts a raw field into a semantic string, throwing when it fails that type's validation.
	/// </summary>
	/// <typeparam name="TSemantic">The semantic string type to produce.</typeparam>
	/// <param name="value">The raw field as git printed it.</param>
	/// <param name="description">What the field is, used in the failure message.</param>
	/// <returns>The converted value.</returns>
	/// <exception cref="GitParseException">
	/// <paramref name="value"/> is empty or fails the type's validation.
	/// </exception>
	internal static TSemantic ToSemantic<TSemantic>(string value, string description)
		where TSemantic : SemanticString<TSemantic>, new()
	{
		// Called on the constructed base type rather than as TSemantic.TryCreate: TryCreate is a
		// plain static method, not a static abstract interface member, so invoking it through the
		// type parameter is CS0704. The explicit null test is what keeps the return from being
		// CS8603, which this repository treats as an error.
		if (!string.IsNullOrEmpty(value) &&
			SemanticString<TSemantic>.TryCreate(value, out TSemantic? result) &&
			result is not null)
		{
			return result;
		}

		throw new GitParseException($"git reported a {description} that is not valid: '{value}'.");
	}

	/// <summary>
	/// Converts a raw path field into a repository-relative path.
	/// </summary>
	/// <remarks>
	/// Two failures are folded together here. An empty field is a malformed record — and
	/// <see cref="RelativeFilePath"/> accepts the empty string, so nothing else would catch it. A
	/// path containing a control character is one git permits and <see cref="RelativeFilePath"/>
	/// refuses on Windows; reporting it beats dropping the entry, which would make
	/// <see cref="GitStatus.IsClean"/> claim a dirty tree is clean.
	/// </remarks>
	/// <param name="value">The raw path as git printed it.</param>
	/// <returns>The converted path.</returns>
	/// <exception cref="GitParseException">
	/// <paramref name="value"/> is empty or cannot be represented as a relative file path.
	/// </exception>
	internal static RelativeFilePath ToRelativeFilePath(string value)
	{
		if (!string.IsNullOrEmpty(value) &&
			RelativeFilePath.TryCreate(value, out RelativeFilePath? path) &&
			path is not null)
		{
			return path;
		}

		throw new GitParseException(
			$"git reported a path that cannot be represented as a relative file path: '{value}'.");
	}
}

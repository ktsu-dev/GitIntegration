// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The placeholder a record's <c>ToString</c> prints in place of a secret.
/// </summary>
internal static class Redacted
{
	/// <summary>The text printed for a secret that is present.</summary>
	internal const string Placeholder = "***";

	/// <summary>
	/// Returns <see cref="Placeholder"/> for a present secret and an empty string for an absent one,
	/// matching how a record prints a <see langword="null"/> member.
	/// </summary>
	/// <param name="secret">The secret, or <see langword="null"/>.</param>
	/// <returns>The text to print in the secret's place.</returns>
	internal static string Of(string? secret) => secret is null ? string.Empty : Placeholder;
}

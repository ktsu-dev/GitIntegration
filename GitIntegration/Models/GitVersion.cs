// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The version of the git binary being invoked.
/// </summary>
/// <remarks>
/// Git appends a platform suffix on some builds — <c>2.50.1.windows.1</c> — so the numeric
/// components are exposed separately from <see cref="Raw"/>, which keeps whatever git printed.
/// </remarks>
public sealed record GitVersion
{
	/// <summary>Gets the major version.</summary>
	public required int Major { get; init; }

	/// <summary>Gets the minor version, or zero when git did not report one.</summary>
	public required int Minor { get; init; }

	/// <summary>Gets the patch version, or zero when git did not report one.</summary>
	public required int Patch { get; init; }

	/// <summary>Gets the version exactly as git printed it, without the <c>git version </c> prefix.</summary>
	public required string Raw { get; init; }

	/// <summary>
	/// Decides whether this version is at least the given major and minor version.
	/// </summary>
	/// <remarks>
	/// Feature gates in git are documented against a major and minor pair — <c>fetch --porcelain</c>
	/// arrived in 2.41 — so the patch component is deliberately not part of the comparison.
	/// </remarks>
	/// <param name="major">The required major version.</param>
	/// <param name="minor">The required minor version.</param>
	/// <returns><see langword="true"/> when this version is at least that version.</returns>
	public bool AtLeast(int major, int minor) => Major != major ? Major > major : Minor >= minor;
}

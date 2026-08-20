// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The outcome of initialising a repository.
/// </summary>
public sealed record GitInitResult
{
	/// <summary>Gets the repository, ready to run further commands against.</summary>
	public required GitRepository Repository { get; init; }

	/// <summary>
	/// Gets a value indicating whether a repository was already present at the target path.
	/// </summary>
	/// <remarks>
	/// <c>git init</c> is idempotent: run against an existing repository it reinitialises and exits
	/// zero, announcing the difference only in prose that this library does not parse. It also
	/// silently ignores <c>--initial-branch</c> on that path, so a caller that asked for a
	/// particular initial branch and got <see langword="true"/> here did not get the branch it
	/// asked for. The value comes from a <c>rev-parse</c> probe taken before <c>init</c> runs.
	/// </remarks>
	public required bool AlreadyExisted { get; init; }
}

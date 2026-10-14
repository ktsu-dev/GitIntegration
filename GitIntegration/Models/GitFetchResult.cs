// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What a fetch brought in.
/// </summary>
public sealed record GitFetchResult
{
	/// <summary>Gets the references the fetch changed, in the order git listed them.</summary>
	public required IReadOnlyList<GitRefUpdate> Updates { get; init; }

	/// <summary>
	/// Gets a value indicating whether git was able to report which references changed.
	/// </summary>
	/// <remarks>
	/// <c>fetch --porcelain</c> arrived in git 2.41. Below that the fetch still runs and still
	/// succeeds, but no machine-readable account of it is available, and this library does not
	/// parse the human-facing alternative. So <see cref="Updates"/> is empty for two entirely
	/// different reasons, and this flag is what separates them.
	/// </remarks>
	public required bool DetailAvailable { get; init; }

	/// <summary>
	/// Gets a value indicating whether the fetch changed nothing.
	/// </summary>
	/// <remarks>
	/// False when <see cref="DetailAvailable"/> is false, whatever <see cref="Updates"/> holds:
	/// an empty list that could not be gathered is not evidence that nothing happened, and
	/// reporting it as "up to date" would be a silent lie.
	/// </remarks>
	public bool IsUpToDate => DetailAvailable && Updates.Count == 0;

	/// <summary>
	/// Gets a value indicating whether git refused to update any of the references.
	/// </summary>
	/// <remarks>
	/// A fetch that refuses one reference, such as a tag the remote moved, exits non-zero while
	/// still updating the rest and reporting every one of them, so this can be true on a result the
	/// caller obtained without an exception. See <c>IGitFetchBuilder</c>, where <c>ExecuteAsync</c>
	/// and <c>TryExecuteAsync</c> deliberately treat rejection differently.
	/// </remarks>
	public bool HasRejections => Updates.Any(static update => update.IsRejected);
}

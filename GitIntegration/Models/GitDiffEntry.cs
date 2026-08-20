// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;

/// <summary>
/// One path changed between two states of the repository.
/// </summary>
public sealed record GitDiffEntry
{
	/// <summary>Gets what happened to the path.</summary>
	public required GitChangeKind Kind { get; init; }

	/// <summary>Gets the path as it exists after the change, relative to the repository root.</summary>
	public required RelativeFilePath Path { get; init; }

	/// <summary>
	/// Gets the path this file came from for a rename or a copy, or <see langword="null"/>
	/// otherwise.
	/// </summary>
	public RelativeFilePath? OriginalPath { get; init; }

	/// <summary>
	/// Gets git's similarity score for a rename or a copy, from 0 to 100, or <see langword="null"/>
	/// when git reported none.
	/// </summary>
	public int? SimilarityPercent { get; init; }
}

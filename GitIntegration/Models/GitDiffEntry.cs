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

	/// <summary>
	/// Gets how many lines were added to this path, or <see langword="null"/> when git reported no
	/// count.
	/// </summary>
	/// <remarks>
	/// <see langword="null"/> in two cases, and they are worth telling apart from zero. Line counts
	/// are opt-in, so this is unset unless <c>IGitDiffBuilder.WithLineCounts()</c> was called; and
	/// git declines to count a binary file at all, printing <c>-</c> where a number would go. A
	/// binary change is not a zero-line change, so reporting 0 for one would state that nothing
	/// changed in a file git simply did not measure.
	/// </remarks>
	public int? Insertions { get; init; }

	/// <summary>
	/// Gets how many lines were removed from this path, or <see langword="null"/> when git reported
	/// no count.
	/// </summary>
	/// <remarks>See <see cref="Insertions"/> for when this is unset.</remarks>
	public int? Deletions { get; init; }
}

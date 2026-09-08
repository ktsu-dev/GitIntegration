// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// How far two revisions have diverged from each other, counted in commits.
/// </summary>
/// <remarks>
/// The same pair of numbers <see cref="GitStatus.Ahead"/> and <see cref="GitStatus.Behind"/> report,
/// for an arbitrary pair of revisions rather than for HEAD against its configured upstream. The
/// vocabulary is deliberately <see cref="GitStatus"/>'s rather than git's own left-and-right, which
/// names the two sides by their position in a <c>a...b</c> expression: a caller comparing this
/// against a <see cref="GitStatus"/> is comparing like with like, and does not have to work out
/// which side of the expression became which number.
/// </remarks>
/// <param name="Ahead">
/// How many commits the local revision has that the upstream one does not.
/// </param>
/// <param name="Behind">
/// How many commits the upstream revision has that the local one does not.
/// </param>
public readonly record struct GitDivergence(int Ahead, int Behind)
{
	/// <summary>
	/// Gets a value indicating whether the two revisions name the same set of commits.
	/// </summary>
	/// <value><see langword="true"/> when neither side holds a commit the other lacks.</value>
	public bool IsInSync => Ahead == 0 && Behind == 0;
}

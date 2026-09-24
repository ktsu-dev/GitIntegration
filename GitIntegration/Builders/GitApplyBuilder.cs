// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Puts a patch into the index or the working tree.
/// </summary>
public interface IGitApplyBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Applies the patch to the index rather than the working tree.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitApplyBuilder ToIndex();

	/// <summary>Applies the patch in reverse, which is what unstages or reverts a change.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitApplyBuilder Reversed();

	/// <summary>Reports whether the patch would apply, without changing anything.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitApplyBuilder Checked();
}

/// <summary>
/// Builds <c>git apply</c>.
/// </summary>
/// <remarks>
/// Git reads a patch from standard input or from a file, and <see cref="GitProcessRequest"/> carries
/// no standard input, so the patch text goes to a temporary file whose path is computed once, in the
/// constructor. <c>AppendVerbArguments</c> only names that path as an operand. Nothing is written
/// to disk until <see cref="ExecuteAsync"/> or <see cref="TryExecuteAsync"/> actually runs git, and
/// the file is always removed afterwards, in a <c>finally</c>, whether git succeeded or failed.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="patchText">The patch to apply.</param>
internal sealed class GitApplyBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, string patchText)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitApplyBuilder
{
	private readonly string _patchText = Ensure.NotNull(patchText);
	private readonly string _temporaryPath = Path.Combine(Path.GetTempPath(), $"ktsu-git-apply-{Path.GetRandomFileName()}.patch");

	private bool _toIndex;
	private bool _reversed;
	private bool _checked;

	/// <inheritdoc />
	public IGitApplyBuilder ToIndex()
	{
		_toIndex = true;
		return this;
	}

	/// <inheritdoc />
	public IGitApplyBuilder Reversed()
	{
		_reversed = true;
		return this;
	}

	/// <inheritdoc />
	public IGitApplyBuilder Checked()
	{
		_checked = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("apply");

		// Staging content already on disk is not the moment to enforce a whitespace policy the user
		// configured for authoring.
		arguments.Add("--whitespace=nowarn");

		if (_toIndex)
		{
			arguments.Add("--cached");
		}

		if (_reversed)
		{
			arguments.Add("--reverse");
		}

		if (_checked)
		{
			arguments.Add("--check");
		}

		AppendOperands(arguments, _temporaryPath);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };

	/// <inheritdoc />
	public override async Task<GitCompleted> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			WritePatchFile();

			return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			DeletePatchFile();
		}
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCompleted>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			WritePatchFile();

			return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			DeletePatchFile();
		}
	}

	/// <summary>
	/// Writes the patch text to <see cref="_temporaryPath"/> as UTF-8 with no byte order mark,
	/// preserving its line endings exactly.
	/// </summary>
	/// <remarks>
	/// A rewritten line ending, or a byte order mark git reads as part of the first line, makes the
	/// patch unapplyable. Called inside the <c>try</c> whose <c>finally</c> deletes the file, so a
	/// write that fails partway through still gets cleaned up.
	/// </remarks>
	private void WritePatchFile() =>
		File.WriteAllText(_temporaryPath, _patchText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

	/// <summary>
	/// Removes the temporary patch file, and says nothing when it cannot.
	/// </summary>
	/// <remarks>
	/// A delete that throws from inside a <c>finally</c> replaces whatever git reported with an I/O
	/// error about a file the caller never knew existed. Git has already applied or refused the
	/// patch by this point, so its own outcome is the one the caller needs, and a file left in the
	/// temporary directory is the smaller cost. Deleting a path that was never written is already a
	/// no-op.
	/// </remarks>
	private void DeletePatchFile()
	{
		try
		{
			File.Delete(_temporaryPath);
		}
		catch (IOException)
		{
			// Nothing useful can be done here, and the caller is owed git's result rather than this.
		}
	}
}

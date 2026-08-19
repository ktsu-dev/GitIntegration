// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// Reports the version of the git binary being invoked.
/// </summary>
/// <remarks>
/// Internal, unlike its sibling builder interfaces: no public member anywhere returns
/// <see cref="IGitVersionBuilder"/> — <see cref="IGitClient.GetVersionAsync"/> returns a
/// <see cref="GitVersion"/> task directly — so a consumer could neither obtain one nor usefully
/// implement one.
/// </remarks>
internal interface IGitVersionBuilder : IGitCommandBuilder<GitVersion>
{
}

/// <summary>
/// Builds <c>git --version</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
internal sealed class GitVersionBuilder(IGitProcessRunner runner)
	: GitCommandBuilder<GitVersion>(runner, repositoryPath: null), IGitVersionBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments) =>
		Ensure.NotNull(arguments).Add("--version");

	/// <inheritdoc />
	protected override GitVersion ParseResult(GitProcessResult result) =>
		GitVersionParser.Parse(Ensure.NotNull(result).StandardOutput);
}

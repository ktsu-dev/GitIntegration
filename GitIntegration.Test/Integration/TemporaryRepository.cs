// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// A throwaway directory on the real filesystem, removed when the test finishes.
/// </summary>
internal sealed class TemporaryRepository : IDisposable
{
	public TemporaryRepository()
	{
		// A GUID rather than a test name, so parallel runs of the same test cannot collide.
		RootPath = Path.Combine(Path.GetTempPath(), "ktsu-git-it-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(RootPath);
	}

	/// <summary>Gets the directory as the library's path type.</summary>
	public AbsoluteDirectoryPath Root => RootPath.As<AbsoluteDirectoryPath>();

	/// <summary>Gets the directory as a plain string, for direct file operations.</summary>
	public string RootPath { get; }

	/// <summary>Writes a file inside the repository, creating any directories it needs.</summary>
	/// <param name="relativePath">The path relative to the repository root.</param>
	/// <param name="contents">What to write.</param>
	public void WriteFile(string relativePath, string contents)
	{
		string full = CombineUnderRoot(relativePath);
		string? directory = Path.GetDirectoryName(full);

		if (!string.IsNullOrEmpty(directory))
		{
			_ = Directory.CreateDirectory(directory);
		}

		File.WriteAllText(full, contents);
	}

	/// <summary>Deletes a file inside the repository.</summary>
	/// <remarks>
	/// Deleting through the filesystem rather than through <c>git rm</c>, so the change reaches the
	/// index only via the <c>Add().All()</c> the tests already run — which is what exercises the
	/// deletion path of the verb under test rather than a second git command's.
	/// </remarks>
	/// <param name="relativePath">The path relative to the repository root.</param>
	public void DeleteFile(string relativePath) => File.Delete(CombineUnderRoot(relativePath));

	/// <summary>
	/// Combines a relative path with <see cref="RootPath"/>, refusing a rooted one.
	/// </summary>
	/// <remarks>
	/// <see cref="Path.Combine(string, string)"/> silently discards every earlier argument when a
	/// later one is rooted, so combining <see cref="RootPath"/> with <c>/etc</c> or <c>C:\Windows</c>
	/// yields that path rather than one inside the throwaway repository. For <see cref="WriteFile"/>
	/// that would write outside the fixture; for <see cref="DeleteFile"/> it would <em>delete</em>
	/// outside it. Rejecting a rooted path is the same guard this library used to need in
	/// <c>GitProvider</c> for a repository name a host reported, and it is worth having here for the
	/// same reason: the failure is silent and the blast radius is a real filesystem.
	/// </remarks>
	/// <param name="relativePath">The path relative to the repository root.</param>
	/// <returns>The combined path, guaranteed to be under <see cref="RootPath"/>.</returns>
	/// <exception cref="ArgumentException"><paramref name="relativePath"/> is rooted.</exception>
	private string CombineUnderRoot(string relativePath)
	{
		if (Path.IsPathRooted(relativePath))
		{
			throw new ArgumentException(
				$"'{relativePath}' is rooted, so combining it would escape the temporary repository.",
				nameof(relativePath));
		}

		return Path.Combine(RootPath, relativePath);
	}

	public void Dispose()
	{
		try
		{
			DeleteRecursively(RootPath);
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing a passing test over. Windows in
			// particular can hold a git pack file open briefly after the process exits.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static void DeleteRecursively(string path)
	{
		if (!Directory.Exists(path))
		{
			return;
		}

		// git marks everything under .git/objects read-only, and Directory.Delete refuses those on
		// Windows. Clearing the attribute first is what makes cleanup reliable there.
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(path, recursive: true);
	}
}

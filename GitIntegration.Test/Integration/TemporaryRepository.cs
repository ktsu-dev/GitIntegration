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
		string full = Path.Combine(RootPath, relativePath);
		string? directory = Path.GetDirectoryName(full);

		if (!string.IsNullOrEmpty(directory))
		{
			_ = Directory.CreateDirectory(directory);
		}

		File.WriteAllText(full, contents);
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

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

using Testably.Abstractions.Testing;

[TestClass]
public class FakeFileSystemProviderTests
{
	private static string DestinationPath =>
		OperatingSystem.IsWindows() ? @"C:\dest" : "/dest";

	[TestMethod]
	public void ReportsAMissingDirectoryAsAbsent()
	{
		MockFileSystem mock = new();
		FakeFileSystemProvider fileSystem = new(mock);

		Assert.IsFalse(fileSystem.Directory.Exists(DestinationPath));
	}

	[TestMethod]
	public void DistinguishesAnEmptyDirectoryFromANonEmptyOne()
	{
		// These are the only two questions Clone asks the filesystem, so they are the two the fake
		// has to answer correctly.
		MockFileSystem mock = new();
		FakeFileSystemProvider fileSystem = new(mock);

		_ = fileSystem.Directory.CreateDirectory(DestinationPath);
		Assert.IsTrue(fileSystem.Directory.Exists(DestinationPath));
		Assert.AreEqual(0, fileSystem.Directory.GetFileSystemEntries(DestinationPath).Length);

		fileSystem.File.WriteAllText(fileSystem.Path.Combine(DestinationPath, "f.txt"), "x");
		Assert.AreEqual(1, fileSystem.Directory.GetFileSystemEntries(DestinationPath).Length);
	}
}

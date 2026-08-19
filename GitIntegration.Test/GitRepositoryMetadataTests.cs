// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryMetadataTests
{
	[TestMethod]
	public void MetadataIsNullWhenNotSupplied()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
		};

		Assert.IsNull(repository.Name);
		Assert.IsNull(repository.WebURI);
		Assert.IsNull(repository.RemotePath);
	}

	[TestMethod]
	public void MetadataRoundTripsWhenSupplied()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			Name = "GitIntegration".As<GitRepositoryName>(),
			WebURI = "https://github.com/ktsu-dev/GitIntegration".As<GitRepositoryWebURI>(),
			RemotePath = "https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
		};

		Assert.AreEqual("GitIntegration", repository.Name?.WeakString);
		Assert.AreEqual("https://github.com/ktsu-dev/GitIntegration", repository.WebURI?.WeakString);
		Assert.AreEqual("https://github.com/ktsu-dev/GitIntegration.git", repository.RemotePath?.WeakString);
	}

	[TestMethod]
	public void OpenWebClientTolerablyReturnsWhenWebUriIsNull()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
		};

		// This verifies the null guard returns rather than dereferencing. It cannot observe
		// whether anything was launched; IsBrowsableUri is asserted directly for that.
		repository.OpenWebClient();
	}

	[TestMethod]
	[DataRow("file:///C:/Windows/System32/calc.exe")]
	[DataRow(@"C:\Windows\System32\calc.exe")]
	[DataRow("./relative/path")]
	[DataRow("ktsu-dev/GitIntegration")]
	public void OpenWebClientReturnsWithoutLaunchingForRejectedValue(string value)
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			WebURI = value.As<GitRepositoryWebURI>(),
		};

		// The gate rejects the value, so this must return rather than shell-executing it.
		Assert.IsFalse(GitRepository.IsBrowsableUri(value, out Uri? _));
		repository.OpenWebClient();
	}

	[TestMethod]
	[DataRow("http://github.com/ktsu-dev/GitIntegration")]
	[DataRow("https://github.com/ktsu-dev/GitIntegration")]
	public void BrowsableUriAcceptsHttpAndHttps(string value)
	{
		Assert.IsTrue(GitRepository.IsBrowsableUri(value, out Uri? uri));
		Assert.IsNotNull(uri);
	}

	[TestMethod]
	[DataRow("file:///C:/Windows/System32/calc.exe")]
	[DataRow(@"C:\Windows\System32\calc.exe")]
	[DataRow("/usr/bin/xterm")]
	[DataRow("./relative/path")]
	[DataRow("ktsu-dev/GitIntegration")]
	[DataRow("ms-settings:privacy")]
	public void BrowsableUriRejectsEverythingElse(string value)
	{
		Assert.IsFalse(GitRepository.IsBrowsableUri(value, out Uri? uri));
		Assert.IsNull(uri);
	}

	[TestMethod]
	public void BrowsableUriRejectsNull()
	{
		Assert.IsFalse(GitRepository.IsBrowsableUri(null, out Uri? uri));
		Assert.IsNull(uri);
	}
}

/// <summary>Paths that exist on every platform the tests run on.</summary>
internal static class TestPaths
{
	public static AbsoluteDirectoryPath Root { get; } =
		(OperatingSystem.IsWindows() ? @"C:\" : "/").As<AbsoluteDirectoryPath>();
}

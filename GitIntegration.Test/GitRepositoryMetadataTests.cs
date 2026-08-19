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
	}

	[TestMethod]
	public void OpenWebClientDoesNothingWhenWebUriIsNull()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
		};

		// Must not throw, and must not launch anything.
		repository.OpenWebClient();
	}
}

/// <summary>Paths that exist on every platform the tests run on.</summary>
internal static class TestPaths
{
	public static AbsoluteDirectoryPath Root { get; } =
		(OperatingSystem.IsWindows() ? @"C:\" : "/").As<AbsoluteDirectoryPath>();
}

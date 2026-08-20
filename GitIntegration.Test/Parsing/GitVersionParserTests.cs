// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitVersionParserTests
{
	[TestMethod]
	public void ParsesAPlainThreePartVersion()
	{
		GitVersion version = GitVersionParser.Parse("git version 2.41.0\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(41, version.Minor);
		Assert.AreEqual(0, version.Patch);
		Assert.AreEqual("2.41.0", version.Raw);
	}

	[TestMethod]
	public void ParsesTheWindowsBuildSuffix()
	{
		// Captured verbatim from the git this library was developed against. The trailing
		// ".windows.1" is why Raw exists and why the parser cannot assume three components.
		GitVersion version = GitVersionParser.Parse("git version 2.50.1.windows.1\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
		Assert.AreEqual(1, version.Patch);
		Assert.AreEqual("2.50.1.windows.1", version.Raw);
	}

	[TestMethod]
	public void ParsesAVersionWithNoPatchComponent()
	{
		GitVersion version = GitVersionParser.Parse("git version 3.0\n");

		Assert.AreEqual(3, version.Major);
		Assert.AreEqual(0, version.Minor);
		Assert.AreEqual(0, version.Patch);
	}

	[TestMethod]
	public void RejectsOutputWithoutTheExpectedPrefix()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitVersionParser.Parse("2.41.0\n"));
	}

	[TestMethod]
	public void RejectsANonNumericMajorComponent()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitVersionParser.Parse("git version next\n"));
	}
}

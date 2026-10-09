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
	public void ParsesApplesBuildNoteAfterThePatchComponent()
	{
		// The stock git on macOS and GitHub's macOS runners. The note follows the patch number
		// with a space, so the third component is "3 (Apple Git-145)", not "3".
		GitVersion version = GitVersionParser.Parse("git version 2.39.3 (Apple Git-145)\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(39, version.Minor);
		Assert.AreEqual(3, version.Patch);
		Assert.AreEqual("2.39.3 (Apple Git-145)", version.Raw);
	}

	[TestMethod]
	public void ParsesAReleaseCandidateSuffixOnThePatchComponent()
	{
		GitVersion version = GitVersionParser.Parse("git version 2.41.1-rc0\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(41, version.Minor);
		Assert.AreEqual(1, version.Patch);
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

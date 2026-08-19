// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitVersionTests
{
	private static GitVersion Version(int major, int minor, int patch) =>
		new() { Major = major, Minor = minor, Patch = patch, Raw = $"{major}.{minor}.{patch}" };

	[TestMethod]
	public void AtLeastAcceptsTheExactVersion() =>
		Assert.IsTrue(Version(2, 41, 0).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastAcceptsAHigherMinor() =>
		Assert.IsTrue(Version(2, 50, 1).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastAcceptsAHigherMajor() =>
		Assert.IsTrue(Version(3, 0, 0).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastRejectsALowerMinor() =>
		Assert.IsFalse(Version(2, 40, 9).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastRejectsALowerMajor() =>
		Assert.IsFalse(Version(1, 99, 0).AtLeast(2, 41));
}

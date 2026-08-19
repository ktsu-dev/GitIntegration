// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Diagnostics.CodeAnalysis;

[TestClass]
public class SanityTests
{
	[TestMethod]
	[SuppressMessage("Assertions", "MSTEST0032:Assertion condition is always true", Justification = "This is a deliberate always-true sanity check that the test project runs at all.")]
	public void TestProjectRuns()
	{
		Assert.AreEqual(2, 1 + 1);
	}
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

/// <summary>
/// Covers the switch that turns a missing git binary from a skipped suite into a failing one.
/// </summary>
/// <remarks>
/// Worth its own tests because it is the guard against the worst possible CI outcome: a runner
/// with no git reporting a green suite while the tier-3 layer silently exercised nothing. These
/// assert the decision function directly rather than setting the real environment variable, which
/// is process-wide state that parallel tests would race.
/// </remarks>
[TestClass]
public class GitRequirementSwitchTests
{
	[TestMethod]
	public void TreatsAnAbsentVariableAsNotRequired() =>
		Assert.IsFalse(GitRoundTripTests.IsGitRequired(null));

	[TestMethod]
	public void TreatsAnEmptyOrBlankVariableAsNotRequired()
	{
		// GitHub Actions writes an empty string for a variable that is declared but unset, so this
		// is the shape a misconfigured workflow actually produces.
		Assert.IsFalse(GitRoundTripTests.IsGitRequired(string.Empty));
		Assert.IsFalse(GitRoundTripTests.IsGitRequired("   "));
	}

	[TestMethod]
	public void TreatsExplicitFalsehoodAsNotRequired()
	{
		Assert.IsFalse(GitRoundTripTests.IsGitRequired("0"));
		Assert.IsFalse(GitRoundTripTests.IsGitRequired("false"));
		Assert.IsFalse(GitRoundTripTests.IsGitRequired("FALSE"));
	}

	[TestMethod]
	public void TreatsAnyOtherValueAsRequired()
	{
		// Deliberately permissive: a workflow that spells it "1", "true", or "yes" all mean the
		// same thing to whoever wrote it, and guessing wrong here would silently disable the guard.
		Assert.IsTrue(GitRoundTripTests.IsGitRequired("1"));
		Assert.IsTrue(GitRoundTripTests.IsGitRequired("true"));
		Assert.IsTrue(GitRoundTripTests.IsGitRequired("yes"));
	}
}

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
/// <remarks>
/// Deliberately not marked <c>[TestCategory("Integration")]</c> despite living beside the tests it
/// guards: it needs no git binary and must keep running when the integration filter is not applied.
/// </remarks>
[TestClass]
public class GitRequirementSwitchTests
{
	[TestMethod]
	public void TreatsAnAbsentVariableAsNotRequired() =>
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired(null));

	[TestMethod]
	public void TreatsAnEmptyOrBlankVariableAsNotRequired()
	{
		// GitHub Actions writes an empty string for a variable that is declared but unset, so this
		// is the shape a misconfigured workflow actually produces.
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired(string.Empty));
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired("   "));
	}

	[TestMethod]
	public void TreatsExplicitFalsehoodAsNotRequired()
	{
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired("0"));
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired("false"));
		Assert.IsFalse(IntegrationGitFixture.IsGitRequired("FALSE"));
	}

	[TestMethod]
	public void TreatsAnyOtherValueAsRequired()
	{
		// Deliberately permissive: a workflow that spells it "1", "true", or "yes" all mean the
		// same thing to whoever wrote it, and guessing wrong here would silently disable the guard.
		Assert.IsTrue(IntegrationGitFixture.IsGitRequired("1"));
		Assert.IsTrue(IntegrationGitFixture.IsGitRequired("true"));
		Assert.IsTrue(IntegrationGitFixture.IsGitRequired("yes"));
	}
}

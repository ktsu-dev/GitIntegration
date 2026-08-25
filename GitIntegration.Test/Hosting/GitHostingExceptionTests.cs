// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Net;
using ktsu.Semantics.Strings;

[TestClass]
public class GitHostingExceptionTests
{
	[TestMethod]
	public void AnAuthenticationFailureCarriesItsContext()
	{
		GitHostingAuthenticationException exception = new(
			"unauthorized", "GitHub".As<GitProviderName>(), HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");

		Assert.AreEqual("GitHub".As<GitProviderName>(), exception.ProviderName);
		Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Bad credentials");
		Assert.IsInstanceOfType<GitHostingException>(exception);
	}

	[TestMethod]
	public void ANotFoundFailureCarriesItsContext()
	{
		GitHostingNotFoundException exception = new(
			"missing", "GitHub".As<GitProviderName>(), HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

		Assert.AreEqual("GitHub".As<GitProviderName>(), exception.ProviderName);
		Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Not Found");
		Assert.IsInstanceOfType<GitHostingException>(exception);
	}

	[TestMethod]
	public void ARequestFailureCarriesItsContext()
	{
		GitHostingRequestException exception = new(
			"bad request", "AzureDevOps".As<GitProviderName>(), HttpStatusCode.BadRequest, "{\"message\":\"Invalid parameter\"}");

		Assert.AreEqual("AzureDevOps".As<GitProviderName>(), exception.ProviderName);
		Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Invalid parameter");
		Assert.IsInstanceOfType<GitHostingException>(exception);
	}

	[TestMethod]
	public void ARateLimitFailureCarriesItsResetTime()
	{
		DateTimeOffset reset = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
		GitHostingRateLimitException exception = new(
			"rate limited", "AzureDevOps".As<GitProviderName>(), HttpStatusCode.TooManyRequests, "{}", reset);

		Assert.AreEqual("AzureDevOps".As<GitProviderName>(), exception.ProviderName);
		Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
		Assert.AreEqual("{}", exception.ResponseBody);
		Assert.AreEqual(reset, exception.ResetsAt);
		Assert.IsInstanceOfType<GitHostingException>(exception);
	}

	[TestMethod]
	public void ARateLimitFailureToleratesAnUnknownResetTime()
	{
		GitHostingRateLimitException exception = new(
			"rate limited", "AzureDevOps".As<GitProviderName>(), HttpStatusCode.TooManyRequests, "{}", resetsAt: null);

		Assert.IsNull(exception.ResetsAt);
	}

	[TestMethod]
	public void TheHostingHierarchyIsSeparateFromTheProcessHierarchy()
	{
		GitHostingNotFoundException exception = new(
			"missing", "GitHub".As<GitProviderName>(), HttpStatusCode.NotFound, "{}");

		Assert.IsNotInstanceOfType<GitException>(exception);
	}
}

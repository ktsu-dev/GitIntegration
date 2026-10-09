// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

[TestClass]
public sealed class CredentialRedactionTests
{
	private const string Secret = "gho_s3cretTokenValue";

	[TestMethod]
	public void ATokenCredentialDoesNotPrintItsToken()
	{
		string printed = HostingCredential.FromToken(Secret).ToString();

		Assert.DoesNotContain(Secret, printed);
		Assert.AreEqual("HostingCredential { Kind = Token, Token = ***, Username = , Password =  }", printed);
	}

	[TestMethod]
	public void ABearerCredentialDoesNotPrintItsToken()
	{
		string printed = HostingCredential.FromBearerToken(Secret).ToString();

		Assert.DoesNotContain(Secret, printed);
		Assert.Contains("Kind = BearerToken", printed);
	}

	[TestMethod]
	public void AUsernamePasswordCredentialPrintsTheUsernameButNotThePassword()
	{
		HostingCredential credential = new()
		{
			Kind = HostingCredentialKind.UsernamePassword,
			Username = "octocat",
			Password = Secret,
		};

		string printed = credential.ToString();

		Assert.DoesNotContain(Secret, printed);
		Assert.AreEqual("HostingCredential { Kind = UsernamePassword, Token = , Username = octocat, Password = *** }", printed);
	}

	[TestMethod]
	public void InterpolatingACredentialDoesNotLeakItsToken()
	{
		HostingCredential credential = HostingCredential.FromToken(Secret);

		string message = $"Signed in: {credential}";

		Assert.DoesNotContain(Secret, message);
	}

	[TestMethod]
	public void ADeviceCodePrintsTheUserCodeButNotTheDeviceCode()
	{
		GitHubDeviceCode code = new()
		{
			UserCode = "WXYZ-1234",
			DeviceCode = Secret,
			VerificationUri = new Uri("https://github.com/login/device"),
			ExpiresIn = TimeSpan.FromSeconds(900),
			Interval = TimeSpan.FromSeconds(5),
		};

		string printed = code.ToString();

		Assert.DoesNotContain(Secret, printed);
		Assert.Contains("UserCode = WXYZ-1234", printed);
		Assert.Contains("DeviceCode = ***", printed);
		Assert.Contains("VerificationUri = https://github.com/login/device", printed);
	}
}

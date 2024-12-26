namespace ktsu.GitIntegration;

using ktsu.CredentialCache;
using ktsu.Extensions;
using Octokit;

public class GitHubProvider : GitProvider
{
	public override GitProviderName Name => "GitHub".As<GitProviderName>();
	private GitHubClient GitHubClient { get; } = new(new ProductHeaderValue(AppDomain.CurrentDomain.FriendlyName));

	public override void RefreshRemoteRepositories()
	{
		if (TryGetCredential(out var credential) && credential is CredentialWithUsernamePassword credentialUsernamePassword)
		{
			GitHubClient.Credentials = new(credentialUsernamePassword.Username, credentialUsernamePassword.Password);
		}
	}
}

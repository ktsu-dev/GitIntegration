namespace ktsu.GitIntegration;

using ktsu.CredentialCache;
using ktsu.Extensions;

using Octokit;

/// <summary>
/// Provides integration with GitHub repositories using the Octokit library.
/// Handles authentication and repository management for GitHub remote sources.
/// </summary>
public class GitHubProvider : GitProvider
{
	/// <summary>
	/// Gets the name of this Git provider.
	/// </summary>
	public override GitProviderName Name => "GitHub".As<GitProviderName>();

	/// <summary>
	/// Gets the GitHub client instance used for API communications.
	/// </summary>
	private GitHubClient GitHubClient { get; } = new(new ProductHeaderValue(AppDomain.CurrentDomain.FriendlyName));

	/// <summary>
	/// Refreshes the list of remote repositories from GitHub.
	/// If credentials are available, authenticates the GitHub client before making API calls.
	/// </summary>
	public override void RefreshRemoteRepositories()
	{
		if (TryGetCredential(out var credential) && credential is CredentialWithUsernamePassword credentialUsernamePassword)
		{
			GitHubClient.Credentials = new(credentialUsernamePassword.Username, credentialUsernamePassword.Password);
		}
	}
}

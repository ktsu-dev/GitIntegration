// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Essentials;

using Microsoft.Extensions.DependencyInjection;

[TestClass]
public class ServiceCollectionExtensionsTests
{
	[TestMethod]
	public void RegistersProcessRunner()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.IsNotNull(provider.GetService<IGitProcessRunner>());
	}

	[TestMethod]
	public void RegistersFileSystemProvider()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.IsNotNull(provider.GetService<IFileSystemProvider>());
	}

	[TestMethod]
	public void AppliesConfiguredOptions()
	{
		ServiceCollection services = new();
		services.AddGitIntegration(options => options.ExecutablePath = "/custom/git");

		using ServiceProvider provider = services.BuildServiceProvider();
		GitOptions options = provider.GetRequiredService<GitOptions>();

		Assert.AreEqual("/custom/git", options.ExecutablePath);
	}

	[TestMethod]
	public void DefaultsToGitOnPath()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();
		GitOptions options = provider.GetRequiredService<GitOptions>();

		Assert.AreEqual("git", options.ExecutablePath);
	}

	[TestMethod]
	public void RegistrationIsIdempotent()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();
		services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.AreEqual(1, provider.GetServices<IGitProcessRunner>().Count());
	}

	[TestMethod]
	public void ConcreteAndInterfaceResolveToTheSameInstance()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		RunCommandGitProcessRunner concrete = provider.GetRequiredService<RunCommandGitProcessRunner>();
		IGitProcessRunner viaInterface = provider.GetRequiredService<IGitProcessRunner>();

		Assert.AreSame(concrete, viaInterface);
	}

	[TestMethod]
	public void SecondCallWithConfigurationIsIgnored()
	{
		ServiceCollection services = new();
		services.AddGitIntegration();
		services.AddGitIntegration(options => options.ExecutablePath = "custom-git");

		using ServiceProvider provider = services.BuildServiceProvider();

		// Registration is idempotent, so the first call wins and later configuration is discarded.
		Assert.AreEqual("git", provider.GetRequiredService<GitOptions>().ExecutablePath);
	}

	[TestMethod]
	public void RegistersTheGitClientByBothConcreteTypeAndInterface()
	{
		ServiceCollection services = new();
		_ = services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		GitClient concrete = provider.GetRequiredService<GitClient>();
		IGitClient asInterface = provider.GetRequiredService<IGitClient>();

		// One singleton reached two ways, not two instances: a second client would mean two
		// independent runners once Phase 5 gives the client per-instance state.
		Assert.AreSame(concrete, asInterface);
	}

	[TestMethod]
	public void TheRegisteredClientUsesTheConfiguredExecutablePath()
	{
		ServiceCollection services = new();
		_ = services.AddGitIntegration(options => options.ExecutablePath = "/usr/local/bin/git");

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.AreEqual("/usr/local/bin/git", provider.GetRequiredService<GitOptions>().ExecutablePath);
		Assert.IsNotNull(provider.GetRequiredService<IGitClient>());
	}
}

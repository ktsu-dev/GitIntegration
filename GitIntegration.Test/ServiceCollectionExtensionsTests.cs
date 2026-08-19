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
}

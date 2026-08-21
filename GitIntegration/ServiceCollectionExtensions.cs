// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

using ktsu.Essentials.FileSystemProviders.Native;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Dependency injection registration for git integration.
/// </summary>
/// <remarks>
/// Registers only the local layer. The hosting layer (<see cref="GitHubProvider"/>,
/// <see cref="AzureDevOpsProvider"/>) is deliberately unregistered: neither type has a constructor
/// dependency this container could supply — <see cref="GitProvider.Owner"/> is a caller-supplied
/// value with no sensible container-wide default, credential resolution goes through the
/// process-wide <c>CredentialCache.Instance</c> singleton rather than an injected service, and
/// <see cref="GitProvider.CreateHttpClient"/> constructs its own transport rather than resolving one.
/// A registered factory delegate here (e.g. <c>Func&lt;GitProviderOwner, GitHubProvider&gt;</c>)
/// would do nothing a caller cannot already do by writing <c>new GitHubProvider { Owner = owner }</c>
/// — it would wire nothing, so it was not added. Construct a provider directly instead.
/// </remarks>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers git integration with default options, invoking the <c>git</c> found on
	/// <c>PATH</c>.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns>The same service collection, to allow chaining.</returns>
	public static IServiceCollection AddGitIntegration(this IServiceCollection services) =>
		services.AddGitIntegration(static _ => { });

	/// <summary>
	/// Registers git integration with configured options.
	/// </summary>
	/// <remarks>
	/// Registrations are singletons exposed by both concrete type and interface, and calling this
	/// more than once is a no-op, matching the conventions in <c>ktsu.Essentials</c>. Idempotency
	/// applies per service, not per call: the first call to register <see cref="GitOptions"/> wins,
	/// so a later call carrying different configuration is silently ignored rather than merged into
	/// or rejected against the first — it neither takes effect nor raises an error.
	/// </remarks>
	/// <param name="services">The service collection to add to.</param>
	/// <param name="configure">Mutates the options before they are registered.</param>
	/// <returns>The same service collection, to allow chaining.</returns>
	public static IServiceCollection AddGitIntegration(this IServiceCollection services, Action<GitOptions> configure)
	{
		Ensure.NotNull(services);
		Ensure.NotNull(configure);

		GitOptions options = new();
		configure(options);

		services.TryAddSingleton(options);
		services.TryAddSingleton<RunCommandGitProcessRunner>();
		services.TryAddSingleton<IGitProcessRunner>(static provider =>
			provider.GetRequiredService<RunCommandGitProcessRunner>());

		// Registered by concrete type first and then projected onto the interface, so both
		// resolutions return the same singleton rather than two independently-constructed clients.
		services.TryAddSingleton<GitClient>();
		services.TryAddSingleton<IGitClient>(static provider => provider.GetRequiredService<GitClient>());

		// Registered for Init and Clone, which act on a destination where no repository
		// exists yet to be asked. Discovery itself resolves the working-tree root via
		// `git rev-parse --show-toplevel`, which does the upward walk inside git — see GitClient's
		// remarks — so it needs no filesystem abstraction of its own.
		services.AddNativeFileSystemProvider();

		return services;
	}
}

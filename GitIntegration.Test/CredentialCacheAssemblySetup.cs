// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;

/// <summary>
/// Points the process-wide <see cref="CredentialCache"/> singleton at an in-memory store once,
/// before any test in this assembly runs.
/// </summary>
/// <remarks>
/// <c>AssemblyInfo.cs</c> declares <c>[assembly: Parallelize(Workers = 0, Scope =
/// ExecutionScope.MethodLevel)]</c> — method-level parallelism across the whole assembly, not just
/// within one class. A previous version of this configuration lived in a static constructor on each
/// test class that needed it (<see cref="GitProviderTests"/>, <see cref="GitHubProviderTests"/>).
/// That looked safe in isolation — a CLR type initializer runs at most once — but two classes each
/// doing it raced against each other under that assembly-wide parallelism: whichever one's
/// initializer fired second would call <see cref="CredentialCache.ResetSingletonForTesting"/> and
/// wipe whatever credential the other class's test had just seeded via <c>AddOrReplace</c>, and if
/// any test read <see cref="CredentialCache.Instance"/> in the window between that reset and the
/// following <see cref="CredentialCache.ConfigureStore"/> call, the singleton would rebuild itself
/// against the platform's real secret manager — after which the pending <c>ConfigureStore</c> call
/// throws from inside a type initializer, having already been in the process of writing to a real
/// keyring. A single <see cref="AssemblyInitializeAttribute"/> method — which MSTest guarantees runs
/// exactly once, before any test method in the assembly starts — removes the race by removing the
/// second caller, not by hoping the CLR schedules two independent initializers apart from each
/// other.
/// </remarks>
[TestClass]
public sealed class CredentialCacheAssemblySetup
{
	/// <summary>Configures the credential cache for the whole test run.</summary>
	/// <param name="context">Unused, but required by <see cref="AssemblyInitializeAttribute"/>'s signature.</param>
	[AssemblyInitialize]
	public static void Initialize(TestContext context)
	{
		CredentialCache.ResetSingletonForTesting();
		CredentialCache.ConfigureStore(new InMemoryCredentialStore());
	}
}

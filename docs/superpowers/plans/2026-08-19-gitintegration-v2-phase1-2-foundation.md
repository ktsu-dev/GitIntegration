# GitIntegration v2 — Phases 1–2: Foundation and Execution Core

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `ktsu.GitIntegration` off `ktsu.StrongStrings`/`ktsu.StrongPaths`, `LibGit2Sharp`, and `ktsu.AppDataStorage`, and stand up the execution core that every git verb will be built on: an `IGitProcessRunner` backed by `ktsu.RunCommand`, the error and result types, the builder base, and DI registration.

**Scope:** Tasks 1–7 of the five-phase effort described in the spec. Phases 3–5 (the verbs themselves and the hosting providers) get their own plans — see [Scope of this plan](#scope-of-this-plan-and-what-follows) at the end.

**Architecture:** Two independent layers in one package. The *local layer* builds an argument vector, hands it to `IGitProcessRunner` (implemented over `ktsu.RunCommand`), and parses git's machine-readable output into typed records. The *hosting layer* talks to GitHub and Azure DevOps REST APIs to enumerate repositories. They meet only where a hosting provider's `GitRepository` is passed to `IGitClient.Clone`.

**Tech Stack:** .NET 10 / .NET 9, `ktsu.Sdk`, `ktsu.Semantics.Strings`, `ktsu.Semantics.Paths`, `ktsu.RunCommand`, `ktsu.Essentials`, `ktsu.CredentialCache`, Octokit, `Microsoft.TeamFoundationServer.Client`, MSTest via `MSTest.Sdk`.

**Spec:** `docs/superpowers/specs/2026-08-19-gitintegration-v2-design.md`

## Global Constraints

Every task's requirements implicitly include this section.

- **Indentation is tabs**, not spaces, in all `.cs` files. Line endings CRLF.
- **File-scoped namespaces**, and `using` directives go **inside** the namespace, after the namespace line. This matches every existing file in this repo.
- **Every file starts with** `// Copyright (c) 2023-2026 ktsu-dev contributors` as the first line.
- **Nullable reference types enabled; warnings are errors.** The build fails on any warning.
- **No `this.` qualifiers.** Always specify accessibility modifiers explicitly.
- **XML doc comments are required on every public member.** The SDK treats missing docs as an error.
- **No global warning suppressions**, including in project properties. If a suppression is unavoidable, use a targeted `[SuppressMessage]` attribute with a real justification string.
- **Use `Ensure.NotNull(x)`** (from the `Polyfill` package, supplied by `ktsu.Sdk`) for parameter validation.
- **Tests use MSTest** with semantic assertions — `Assert.AreEqual`, `Assert.IsNotNull`, `Assert.ThrowsExactly`, `CollectionAssert.AreEqual`. Never `Assert.IsTrue(a == b)`.
- **Commit message tags:** this work is additive and breaking-ish; use `[minor]` on feature commits. Never add `Co-Authored-By` lines.
- **Do not edit** `VERSION.md`, `CHANGELOG.md`, `LICENSE.md` — they are generated.
- **Target frameworks** for the library stay `net10.0;net9.0`. The test project is `net10.0` only.
- **Build command:** `dotnet build`. **Test command:** `dotnet test`.

### Exact package versions

Add these to `Directory.Packages.props` verbatim:

```xml
<PackageVersion Include="ktsu.Semantics.Strings" Version="3.0.1" />
<PackageVersion Include="ktsu.Semantics.Paths" Version="3.0.1" />
<PackageVersion Include="ktsu.RunCommand" Version="1.4.26" />
<PackageVersion Include="ktsu.Essentials" Version="2.0.0" />
<PackageVersion Include="ktsu.Essentials.FileSystemProviders.Native" Version="2.0.0" />
<PackageVersion Include="ktsu.CredentialCache" Version="1.3.21" />
<PackageVersion Include="ktsu.Extensions" Version="1.6.2" />
<PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.10" />
<PackageVersion Include="Microsoft.TeamFoundationServer.Client" Version="19.225.2" />
<PackageVersion Include="Microsoft.VisualStudio.Services.Client" Version="19.225.2" />
```

Removed entirely: `LibGit2Sharp`, `ktsu.AppDataStorage`, `ktsu.StrongPaths`, `ktsu.StrongStrings`.

> **Transitive version note:** `ktsu.CredentialCache` 1.3.21 depends on `ktsu.Semantics.Strings` 2.9.11. Referencing 3.0.1 directly makes NuGet unify upward to 3.0.1. This is expected and safe: the Semantics 3.0 breaking changes removed ten first-class-type validation attributes (`[IsGuid]`, `[IsUri]`, …), and `CredentialCache`'s `PersonaGUID` is a plain `SemanticString<PersonaGUID>` with no attributes. Task 1 verifies this by building.

### Namespaces and key API signatures

Copy these exactly; they are verified against the installed packages.

```csharp
// ktsu.Semantics.Strings
public abstract record SemanticString<TDerived> : ISemanticString
public static TDerived Create(string? value)
public static bool TryCreate(string? value, out TDerived? result)
protected virtual string MakeCanonical(string input)
// extension, from SemanticStringExtensions:
public static TDerived As<TDerived>(this string? value)
// attributes (all in namespace ktsu.Semantics.Strings):
[HasNonWhitespaceContent] [RegexMatch(pattern, RegexOptions)] [StartsWith(prefix)] [EndsWith(suffix)]

// ktsu.Semantics.Paths  (namespace ktsu.Semantics.Paths)
public sealed record AbsoluteDirectoryPath : SemanticDirectoryPath<AbsoluteDirectoryPath>, IAbsoluteDirectoryPath
public sealed record AbsoluteFilePath   : SemanticFilePath<AbsoluteFilePath>,   IAbsoluteFilePath
public sealed record RelativeFilePath   : SemanticFilePath<RelativeFilePath>,   IRelativeFilePath
// AbsoluteDirectoryPath carries [IsAbsolutePath]; RelativeFilePath carries [IsRelativePath].
// AbsoluteDirectoryPath exposes .Parent (a root is its own parent) and .IsRoot.

// ktsu.RunCommand  (namespace ktsu.RunCommand)
public static async Task<int> ExecuteAsync(
    string fileName,
    IEnumerable<string> arguments,
    OutputHandler outputHandler,
    Elevation elevation,
    CancellationToken cancellationToken)
public OutputHandler(Action<string>? onStandardOutput = null,
                     Action<string>? onStandardError = null,
                     Encoding? encoding = null)
public enum Elevation { Default, Elevated }

// ktsu.Essentials  (namespace ktsu.Essentials)
public interface IFileSystemProvider : System.IO.Abstractions.IFileSystem { }
// registration extension, from ktsu.Essentials.FileSystemProviders.Native:
services.AddNativeFileSystemProvider();

// ktsu.CredentialCache  (namespace ktsu.CredentialCache)
public sealed record class PersonaGUID : SemanticString<PersonaGUID> { }
public static CredentialCache Instance { get; }
public static PersonaGUID CreatePersonaGUID()
public bool TryGet(PersonaGUID persona, out Credential? credential)
public sealed class CredentialWithUsernamePassword : Credential  // .Username, .Password
```

---

## File Structure

All paths are relative to the repository root.

### `GitIntegration/` (library)

| File | Responsibility |
|---|---|
| `SemanticTypes/GitProviderTypes.cs` | `GitProviderGUID`, `GitProviderName`, `GitProviderOwner`, `AzureDevOpsProjectName` |
| `SemanticTypes/GitRepositoryTypes.cs` | `GitRepositoryName`, `GitRepositoryWebURI`, `GitRepositoryRemotePath` |
| `SemanticTypes/GitRefTypes.cs` | `GitBranchName`, `GitRemoteName`, `GitRefName`, `GitCommitSha` |
| `SemanticTypes/GitCommitTypes.cs` | `GitCommitMessage`, `GitAuthorName`, `GitAuthorEmail` |
| `Execution/GitOptions.cs` | Executable path and timeout |
| `Execution/GitProcessResult.cs` | Exit code, stdout, stderr, argv |
| `Execution/IGitProcessRunner.cs` | The execution contract |
| `Execution/RunCommandGitProcessRunner.cs` | The `ktsu.RunCommand` implementation |
| `Execution/GitExceptions.cs` | `GitException`, `GitCommandException`, `GitRepositoryNotFoundException`, `GitExecutableNotFoundException` |
| `Execution/GitResult.cs` | `GitResult<T>`, `GitCommandError` |
| `Builders/GitCommandBuilder.cs` | `IGitCommandBuilder<T>` and the abstract base holding global-argument injection |
| `Builders/*Builder.cs` | One file per verb |
| `Models/*.cs` | One file per result record or enum |
| `Parsing/*Parser.cs` | One `internal static class` per output format |
| `GitClient.cs` / `IGitClient.cs` | Entry point: version, discovery, open, init, clone |
| `GitRepository.cs` | Merged local handle + hosting metadata |
| `Hosting/IGitHostingProvider.cs` | Hosting contract |
| `Hosting/GitProvider.cs` | Abstract base with credential plumbing |
| `Hosting/GitHubProvider.cs` | Octokit implementation |
| `Hosting/AzureDevOpsProvider.cs` | `VssConnection` implementation |
| `ServiceCollectionExtensions.cs` | `AddGitIntegration`, `AddGitHubProvider`, `AddAzureDevOpsProvider` |
| `AssemblyInfo.cs` | Assembly attributes (per repo convention) |

Deleted: `GitProvider.cs`, `GitHubProvider.cs`, `GitRepository.cs` at the project root — their contents move into the structure above.

### `GitIntegration.Test/` (new)

| File | Responsibility |
|---|---|
| `AssemblyInfo.cs` | `[assembly: Parallelize(...)]` |
| `Fakes/RecordingGitProcessRunner.cs` | Captures argv, returns canned output |
| `Fakes/GitFixtures.cs` | Captured git output strings used by parser tests |
| `Parsing/*ParserTests.cs` | Tier 1 — pure parser tests |
| `Builders/*BuilderTests.cs` | Tier 2 — argv assertions |
| `Integration/*Tests.cs` | Tier 3 — real git, `[TestCategory("Integration")]` |
| `Integration/TempRepository.cs` | Disposable temp-repo helper |

---

## Phase 1 — Foundation

Goal: the existing code compiles against Semantics with `LibGit2Sharp` and `AppDataStorage` gone, and a test project exists. No new git functionality yet.

### Task 1: Package manifest and test project

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `GitIntegration/GitIntegration.csproj`
- Modify: `GitIntegration.sln`
- Create: `GitIntegration.Test/GitIntegration.Test.csproj`
- Create: `GitIntegration.Test/AssemblyInfo.cs`
- Create: `GitIntegration.Test/SanityTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable solution with a runnable test project. Later tasks add files to `GitIntegration.Test`.

- [ ] **Step 1: Rewrite `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="ktsu.CredentialCache" Version="1.3.21" />
    <PackageVersion Include="ktsu.Essentials" Version="2.0.0" />
    <PackageVersion Include="ktsu.Essentials.FileSystemProviders.Native" Version="2.0.0" />
    <PackageVersion Include="ktsu.Extensions" Version="1.6.2" />
    <PackageVersion Include="ktsu.RunCommand" Version="1.4.26" />
    <PackageVersion Include="ktsu.Semantics.Paths" Version="3.0.1" />
    <PackageVersion Include="ktsu.Semantics.Strings" Version="3.0.1" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.10" />
    <PackageVersion Include="Microsoft.TeamFoundationServer.Client" Version="19.225.2" />
    <PackageVersion Include="Microsoft.VisualStudio.Services.Client" Version="19.225.2" />
    <PackageVersion Include="Octokit" Version="14.0.0" />
    <PackageVersion Include="Polyfill" Version="10.8.1" />
    <PackageVersion Include="Microsoft.Testing.Extensions.CodeCoverage" Version="17.14.2" />
    <PackageVersion Include="Microsoft.Testing.Extensions.CrashDump" Version="1.7.2" />
    <PackageVersion Include="Microsoft.Testing.Extensions.Fakes" Version="17.14.1" />
    <PackageVersion Include="Microsoft.Testing.Extensions.HangDump" Version="1.7.2" />
    <PackageVersion Include="Microsoft.Testing.Extensions.HotReload" Version="1.7.2" />
    <PackageVersion Include="Microsoft.Testing.Extensions.Retry" Version="1.7.2" />
    <PackageVersion Include="Microsoft.Testing.Extensions.TrxReport" Version="1.7.2" />
  </ItemGroup>
</Project>
```

Note what left: `LibGit2Sharp`, `ktsu.AppDataStorage`, `ktsu.StrongPaths`, `ktsu.StrongStrings`.

- [ ] **Step 2: Rewrite `GitIntegration/GitIntegration.csproj`**

```xml
<Project>
  <Sdk Name="Microsoft.NET.Sdk" />
  <Sdk Name="ktsu.Sdk" />

  <PropertyGroup>
    <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ktsu.CredentialCache" />
    <PackageReference Include="ktsu.Essentials" />
    <PackageReference Include="ktsu.Essentials.FileSystemProviders.Native" />
    <PackageReference Include="ktsu.Extensions" />
    <PackageReference Include="ktsu.RunCommand" />
    <PackageReference Include="ktsu.Semantics.Paths" />
    <PackageReference Include="ktsu.Semantics.Strings" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.TeamFoundationServer.Client" />
    <PackageReference Include="Microsoft.VisualStudio.Services.Client" />
    <PackageReference Include="Octokit" />
    <PackageReference Include="Polyfill" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `GitIntegration.Test/GitIntegration.Test.csproj`**

```xml
<Project>
  <Sdk Name="MSTest.Sdk" />
  <Sdk Name="ktsu.Sdk" />

  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
    <TargetFramework>net10.0</TargetFramework>
    <TargetFrameworks></TargetFrameworks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\GitIntegration\GitIntegration.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `GitIntegration.Test/AssemblyInfo.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
```

- [ ] **Step 5: Create `GitIntegration.Test/SanityTests.cs`**

This proves the test project runs before any real tests depend on it.

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class SanityTests
{
	[TestMethod]
	public void TestProjectRuns()
	{
		Assert.AreEqual(2, 1 + 1);
	}
}
```

- [ ] **Step 6: Add the test project to `GitIntegration.sln`**

Run: `dotnet sln GitIntegration.sln add GitIntegration.Test/GitIntegration.Test.csproj`

- [ ] **Step 7: Verify the build fails as expected**

Run: `dotnet build`
Expected: FAIL. `GitProvider.cs`, `GitHubProvider.cs`, and `GitRepository.cs` still reference `ktsu.StrongStrings` and `ktsu.StrongPaths`, which are no longer referenced. Errors are `CS0246` on `StrongStringAbstract<>` and on the `ktsu.StrongPaths` using directive. This confirms the removals landed; Task 2 fixes it.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props GitIntegration/GitIntegration.csproj GitIntegration.sln GitIntegration.Test/
git commit -m "[minor] Swap package manifest to Semantics and add test project"
```

### Task 2: Migrate semantic types

**Files:**
- Create: `GitIntegration/SemanticTypes/GitProviderTypes.cs`
- Create: `GitIntegration/SemanticTypes/GitRepositoryTypes.cs`
- Create: `GitIntegration/SemanticTypes/GitRefTypes.cs`
- Create: `GitIntegration/SemanticTypes/GitCommitTypes.cs`
- Modify: `GitIntegration/GitProvider.cs` (remove the type declarations at its top)
- Modify: `GitIntegration/GitRepository.cs` (remove the type declarations at its top)
- Test: `GitIntegration.Test/SemanticTypes/SemanticTypeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: all semantic string types used by every later task. Exact names and validation:
  - `GitProviderGUID`, `GitProviderName`, `GitProviderOwner`, `AzureDevOpsProjectName` — `[HasNonWhitespaceContent]`
  - `GitRepositoryName`, `GitRepositoryWebURI`, `GitRepositoryRemotePath` — `[HasNonWhitespaceContent]`
  - `GitBranchName`, `GitRemoteName`, `GitRefName` — `[HasNonWhitespaceContent]`
  - `GitCommitSha` — `[RegexMatch("^[0-9a-fA-F]{4,40}$")]`, canonicalised to lowercase
  - `GitCommitMessage` — `[HasNonWhitespaceContent]`
  - `GitAuthorName` — `[HasNonWhitespaceContent]`
  - `GitAuthorEmail` — `[HasNonWhitespaceContent]`

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/SemanticTypes/SemanticTypeTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class SemanticTypeTests
{
	[TestMethod]
	public void GitCommitShaAcceptsFullSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c");

		Assert.AreEqual("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaAcceptsAbbreviatedSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4");

		Assert.AreEqual("3f2a1b4", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaLowercasesUppercaseInput()
	{
		GitCommitSha sha = GitCommitSha.Create("3F2A1B4C");

		Assert.AreEqual("3f2a1b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaRejectsNonHexadecimal()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("zzzzzzz", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitCommitShaRejectsTooShortValue()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("3f2", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameRejectsWhitespaceOnlyValue()
	{
		Assert.IsFalse(GitBranchName.TryCreate("   ", out GitBranchName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameAcceptsSlashSeparatedName()
	{
		GitBranchName branch = GitBranchName.Create("feature/git-v2");

		Assert.AreEqual("feature/git-v2", branch.WeakString);
	}

	[TestMethod]
	public void SemanticTypesAreDistinctAtCompileTime()
	{
		GitBranchName branch = "main".As<GitBranchName>();
		GitRemoteName remote = "origin".As<GitRemoteName>();

		Assert.AreNotEqual<object>(branch, remote);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SemanticTypeTests"`
Expected: FAIL to compile — `GitCommitSha`, `GitBranchName`, and `GitRemoteName` do not exist yet.

- [ ] **Step 3: Create `GitIntegration/SemanticTypes/GitRefTypes.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed git branch name, such as <c>main</c> or <c>feature/git-v2</c>.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitBranchName : SemanticString<GitBranchName> { }

/// <summary>
/// A strongly-typed git remote name, such as <c>origin</c>.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRemoteName : SemanticString<GitRemoteName> { }

/// <summary>
/// A strongly-typed git reference, which may be a branch, tag, SHA, or revision expression.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRefName : SemanticString<GitRefName> { }

/// <summary>
/// A strongly-typed git object identifier, either abbreviated or full length.
/// </summary>
/// <remarks>
/// Values are canonicalised to lowercase, because git emits lowercase but accepts either case as
/// input, and callers should be able to compare two SHAs for equality without normalising first.
/// </remarks>
[RegexMatch("^[0-9a-fA-F]{4,40}$")]
public sealed record GitCommitSha : SemanticString<GitCommitSha>
{
	/// <inheritdoc />
	protected override string MakeCanonical(string input) => input.Trim().ToLowerInvariant();
}
```

- [ ] **Step 4: Create `GitIntegration/SemanticTypes/GitCommitTypes.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed git commit message.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitCommitMessage : SemanticString<GitCommitMessage> { }

/// <summary>
/// A strongly-typed name recorded in a commit's author or committer signature.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitAuthorName : SemanticString<GitAuthorName> { }

/// <summary>
/// A strongly-typed email address recorded in a commit's author or committer signature.
/// </summary>
/// <remarks>
/// This deliberately does not validate as an email address. Git records whatever
/// <c>user.email</c> is set to, including values that are not valid addresses, and a commit that
/// already exists in history must remain readable.
/// </remarks>
[HasNonWhitespaceContent]
public sealed record GitAuthorEmail : SemanticString<GitAuthorEmail> { }
```

- [ ] **Step 5: Create `GitIntegration/SemanticTypes/GitProviderTypes.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed identifier for a git hosting provider.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderGUID : SemanticString<GitProviderGUID> { }

/// <summary>
/// A strongly-typed name for a git hosting provider, such as <c>GitHub</c> or <c>AzureDevOps</c>.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderName : SemanticString<GitProviderName> { }

/// <summary>
/// A strongly-typed owner of repositories within a hosting provider: a GitHub user or
/// organisation, or an Azure DevOps organisation.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderOwner : SemanticString<GitProviderOwner> { }

/// <summary>
/// A strongly-typed Azure DevOps project name. Azure DevOps nests repositories under a project,
/// which GitHub has no equivalent of.
/// </summary>
[HasNonWhitespaceContent]
public sealed record AzureDevOpsProjectName : SemanticString<AzureDevOpsProjectName> { }
```

- [ ] **Step 6: Create `GitIntegration/SemanticTypes/GitRepositoryTypes.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed repository name.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRepositoryName : SemanticString<GitRepositoryName> { }

/// <summary>
/// A strongly-typed browser-facing URI for a repository.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRepositoryWebURI : SemanticString<GitRepositoryWebURI> { }

/// <summary>
/// A strongly-typed remote path for a repository, which may be an HTTPS URL, an SSH URL, or a
/// local filesystem path.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRepositoryRemotePath : SemanticString<GitRepositoryRemotePath> { }
```

- [ ] **Step 7: Delete the old type declarations**

In `GitIntegration/GitProvider.cs`, delete the three `record class` declarations for `GitProviderGUID`, `GitProviderName`, and `GitProviderOwner`, and change the using directive from `using ktsu.StrongStrings;` to nothing (the types now live elsewhere in the same namespace).

In `GitIntegration/GitRepository.cs`, delete the three `record class` declarations for `GitRepositoryName`, `GitRepositoryWebURI`, and `GitRepositoryRemotePath`, and replace `using ktsu.StrongPaths;` with `using ktsu.Semantics.Paths;`, removing `using ktsu.StrongStrings;`.

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SemanticTypeTests"`
Expected: PASS, 8 tests.

- [ ] **Step 9: Commit**

```bash
git add GitIntegration/SemanticTypes/ GitIntegration/GitProvider.cs GitIntegration/GitRepository.cs GitIntegration.Test/SemanticTypes/
git commit -m "[minor] Migrate semantic types from StrongStrings to Semantics"
```

### Task 3: Fix construction and cross-platform browser launch

**Files:**
- Modify: `GitIntegration/GitRepository.cs`
- Modify: `GitIntegration/GitProvider.cs`
- Test: `GitIntegration.Test/GitRepositoryMetadataTests.cs`

**Interfaces:**
- Consumes: the semantic types from Task 2.
- Produces: a `GitRepository` whose metadata is nullable and whose `LocalPath` is `required`; a portable `OpenWebClient`. Task 8 replaces this file wholesale with the merged type, so keep the change minimal here.

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/GitRepositoryMetadataTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryMetadataTests
{
	[TestMethod]
	public void MetadataIsNullWhenNotSupplied()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
		};

		Assert.IsNull(repository.Name);
		Assert.IsNull(repository.WebURI);
		Assert.IsNull(repository.RemotePath);
	}

	[TestMethod]
	public void MetadataRoundTripsWhenSupplied()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			Name = "GitIntegration".As<GitRepositoryName>(),
			WebURI = "https://github.com/ktsu-dev/GitIntegration".As<GitRepositoryWebURI>(),
			RemotePath = "https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
		};

		Assert.AreEqual("GitIntegration", repository.Name?.WeakString);
		Assert.AreEqual("https://github.com/ktsu-dev/GitIntegration", repository.WebURI?.WeakString);
	}

	[TestMethod]
	public void OpenWebClientDoesNothingWhenWebUriIsNull()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
		};

		// Must not throw, and must not launch anything.
		repository.OpenWebClient();
	}
}

/// <summary>Paths that exist on every platform the tests run on.</summary>
internal static class TestPaths
{
	public static AbsoluteDirectoryPath Root { get; } =
		(OperatingSystem.IsWindows() ? @"C:\" : "/").As<AbsoluteDirectoryPath>();
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryMetadataTests"`
Expected: FAIL to compile — `LocalPath` is not `required` and the metadata properties are non-nullable, so `Assert.IsNull` on them is a compile error under nullable reference types.

- [ ] **Step 3: Rewrite `GitIntegration/GitRepository.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Diagnostics;

using ktsu.Semantics.Paths;

/// <summary>
/// Represents a git repository: where its working copy is, and what is known about the host it
/// came from.
/// </summary>
public class GitRepository
{
	/// <summary>
	/// Gets the local filesystem path where the repository is, or is intended to be, cloned.
	/// </summary>
	public required AbsoluteDirectoryPath LocalPath { get; init; }

	/// <summary>
	/// Gets the repository name, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryName? Name { get; init; }

	/// <summary>
	/// Gets the browser-facing URI, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryWebURI? WebURI { get; init; }

	/// <summary>
	/// Gets the remote path, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryRemotePath? RemotePath { get; init; }

	/// <summary>
	/// Opens <see cref="WebURI"/> in the default browser. Does nothing when it is
	/// <see langword="null"/>.
	/// </summary>
	public void OpenWebClient()
	{
		if (WebURI is null)
		{
			return;
		}

		// UseShellExecute with the URI as FileName is the portable form. The previous
		// implementation hardcoded "explorer", which does not exist on Linux or macOS.
		_ = Process.Start(new ProcessStartInfo
		{
			FileName = WebURI.WeakString,
			UseShellExecute = true,
		});
	}
}
```

`IsCloned` is deliberately removed here rather than reimplemented: it was a `Directory.Exists` stand-in with a TODO admitting it was wrong, and Task 9 replaces it with `IsClonedAsync`, which asks git.

- [ ] **Step 4: Update `GitProvider.cs` for the removed member**

`GitProvider.Repositories` stays `ConcurrentBag<GitRepository>` for now; Task 17 replaces it. No change is needed in this task beyond Task 2's using-directive edit. Verify by building.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryMetadataTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Run the whole suite and build**

Run: `dotnet build && dotnet test`
Expected: build succeeds with zero warnings; all tests pass. This is the Phase 1 exit gate — the repository now compiles with LibGit2Sharp and AppDataStorage gone.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/GitRepository.cs GitIntegration.Test/GitRepositoryMetadataTests.cs
git commit -m "[minor] Make repository metadata nullable and fix cross-platform browser launch"
```

---

## Phase 2 — Execution core

Goal: an `IGitProcessRunner` backed by `ktsu.RunCommand`, the exception and result types, the builder base with global-argument injection, and DI registration.

### Task 4: Execution contract and result types

**Files:**
- Create: `GitIntegration/Execution/GitOptions.cs`
- Create: `GitIntegration/Execution/GitProcessResult.cs`
- Create: `GitIntegration/Execution/IGitProcessRunner.cs`
- Create: `GitIntegration/Execution/GitExceptions.cs`
- Create: `GitIntegration/Execution/GitResult.cs`
- Test: `GitIntegration.Test/Execution/GitResultTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, relied on by every later task:
  - `GitOptions` with `string ExecutablePath` (default `"git"`) and `TimeSpan? Timeout` (default 5 minutes)
  - `GitProcessResult` with `int ExitCode`, `string StandardOutput`, `string StandardError`, `IReadOnlyList<string> Arguments`, `bool Success`
  - `IGitProcessRunner.RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)` returning `Task<GitProcessResult>`
  - `GitException` → `GitCommandException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)` → `GitRepositoryNotFoundException`; and `GitExecutableNotFoundException(string message, Exception innerException)`
  - `GitResult<T>` with `bool Success`, `T? Value`, `GitCommandError? Error`, plus `GitResult<T>.FromValue(T)` and `GitResult<T>.FromError(GitCommandError)`
  - `GitCommandError` with `int ExitCode`, `IReadOnlyList<string> Arguments`, `string StandardError`

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/Execution/GitResultTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitResultTests
{
	[TestMethod]
	public void FromValueReportsSuccess()
	{
		GitResult<string> result = GitResult<string>.FromValue("ok");

		Assert.IsTrue(result.Success);
		Assert.AreEqual("ok", result.Value);
		Assert.IsNull(result.Error);
	}

	[TestMethod]
	public void FromErrorReportsFailure()
	{
		GitCommandError error = new()
		{
			ExitCode = 128,
			Arguments = ["status"],
			StandardError = "fatal: not a git repository",
		};

		GitResult<string> result = GitResult<string>.FromError(error);

		Assert.IsFalse(result.Success);
		Assert.IsNull(result.Value);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public void ProcessResultReportsSuccessOnZeroExitCode()
	{
		GitProcessResult result = new()
		{
			ExitCode = 0,
			StandardOutput = string.Empty,
			StandardError = string.Empty,
			Arguments = ["status"],
		};

		Assert.IsTrue(result.Success);
	}

	[TestMethod]
	public void ProcessResultReportsFailureOnNonZeroExitCode()
	{
		GitProcessResult result = new()
		{
			ExitCode = 1,
			StandardOutput = string.Empty,
			StandardError = string.Empty,
			Arguments = ["status"],
		};

		Assert.IsFalse(result.Success);
	}

	[TestMethod]
	public void CommandExceptionCarriesDiagnosticContext()
	{
		GitCommandException exception = new("git failed", 128, ["status"], "fatal: not a git repository");

		Assert.AreEqual(128, exception.ExitCode);
		Assert.AreEqual("fatal: not a git repository", exception.StandardError);
		CollectionAssert.AreEqual(new[] { "status" }, exception.Arguments.ToArray());
	}

	[TestMethod]
	public void RepositoryNotFoundIsACommandException()
	{
		GitRepositoryNotFoundException exception = new("not a repo", 128, ["status"], "fatal:");

		Assert.IsInstanceOfType<GitCommandException>(exception);
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitResultTests"`
Expected: FAIL to compile — none of these types exist.

- [ ] **Step 3: Create `GitIntegration/Execution/GitOptions.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

/// <summary>
/// Configures how the git executable is located and invoked.
/// </summary>
public sealed class GitOptions
{
	/// <summary>
	/// Gets or sets the git executable to invoke. A bare name is resolved through <c>PATH</c>;
	/// an absolute path is used as given.
	/// </summary>
	public string ExecutablePath { get; set; } = "git";

	/// <summary>
	/// Gets or sets a wall-clock bound on a single git invocation, or <see langword="null"/> to
	/// leave invocations unbounded.
	/// </summary>
	/// <remarks>
	/// A bound matters because <c>ktsu.RunCommand</c> cannot set environment variables, so
	/// <c>GIT_TERMINAL_PROMPT=0</c> cannot be applied and a remote operation may otherwise block
	/// indefinitely waiting for credentials that will never be typed.
	/// </remarks>
	public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

- [ ] **Step 4: Create `GitIntegration/Execution/GitProcessResult.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// The raw outcome of one git invocation, before any parsing.
/// </summary>
public sealed record GitProcessResult
{
	/// <summary>Gets the process exit code.</summary>
	public required int ExitCode { get; init; }

	/// <summary>Gets everything the process wrote to standard output.</summary>
	public required string StandardOutput { get; init; }

	/// <summary>Gets everything the process wrote to standard error.</summary>
	public required string StandardError { get; init; }

	/// <summary>Gets the argument vector that was passed to git, for diagnostics.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>Gets a value indicating whether git exited with code zero.</summary>
	public bool Success => ExitCode == 0;
}
```

- [ ] **Step 5: Create `GitIntegration/Execution/IGitProcessRunner.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs the git executable with a given argument vector.
/// </summary>
/// <remarks>
/// The contract takes an argument vector rather than a command string on purpose. Flattening
/// arguments into a single string would hand them to a shell for re-splitting, which corrupts any
/// argument containing a quote, backtick, dollar sign, or ampersand — a commit message, for
/// instance — and turns caller-supplied text into a shell injection vector.
/// </remarks>
public interface IGitProcessRunner
{
	/// <summary>
	/// Runs git with the supplied arguments and captures its output.
	/// </summary>
	/// <param name="arguments">The argument vector, each element unquoted and unescaped.</param>
	/// <param name="cancellationToken">Cancels the invocation, terminating the process tree.</param>
	/// <returns>The exit code and captured output.</returns>
	/// <exception cref="GitExecutableNotFoundException">The git executable could not be started.</exception>
	public Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Create `GitIntegration/Execution/GitExceptions.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// The base type for every failure originating in this library.
/// </summary>
public class GitException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	public GitException() { }

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The git executable could not be started. Carries no exit code, because nothing ran.
/// </summary>
public sealed class GitExecutableNotFoundException : GitException
{
	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	public GitExecutableNotFoundException() { }

	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitExecutableNotFoundException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitExecutableNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Git ran and exited with a non-zero code.
/// </summary>
public class GitCommandException : GitException
{
	/// <summary>Gets the exit code git returned.</summary>
	public int ExitCode { get; }

	/// <summary>Gets the argument vector that produced the failure.</summary>
	public IReadOnlyList<string> Arguments { get; } = [];

	/// <summary>Gets everything git wrote to standard error.</summary>
	public string StandardError { get; } = string.Empty;

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	public GitCommandException() { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitCommandException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitCommandException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitCommandException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message)
	{
		ExitCode = exitCode;
		Arguments = arguments;
		StandardError = standardError;
	}
}

/// <summary>
/// The path given is not inside a git working tree.
/// </summary>
public sealed class GitRepositoryNotFoundException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	public GitRepositoryNotFoundException() { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitRepositoryNotFoundException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitRepositoryNotFoundException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitRepositoryNotFoundException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }
}
```

The multiple constructors are not ceremony: the analyzers enabled by `ktsu.Sdk` require the three standard exception constructors on every public exception type, and the build treats that as an error.

- [ ] **Step 7: Create `GitIntegration/Execution/GitResult.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// Describes a git invocation that exited non-zero.
/// </summary>
public sealed record GitCommandError
{
	/// <summary>Gets the exit code git returned.</summary>
	public required int ExitCode { get; init; }

	/// <summary>Gets the argument vector that produced the failure.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>Gets everything git wrote to standard error.</summary>
	public required string StandardError { get; init; }
}

/// <summary>
/// The outcome of a git command that was allowed to fail without throwing.
/// </summary>
/// <typeparam name="T">The parsed result type on success.</typeparam>
public readonly record struct GitResult<T>
{
	/// <summary>Gets a value indicating whether the command succeeded.</summary>
	public bool Success { get; private init; }

	/// <summary>Gets the parsed result, or <see langword="null"/> when the command failed.</summary>
	public T? Value { get; private init; }

	/// <summary>Gets the failure detail, or <see langword="null"/> when the command succeeded.</summary>
	public GitCommandError? Error { get; private init; }

	/// <summary>Creates a successful result.</summary>
	/// <param name="value">The parsed result.</param>
	/// <returns>A successful result carrying <paramref name="value"/>.</returns>
	public static GitResult<T> FromValue(T value) => new() { Success = true, Value = value };

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">The failure detail.</param>
	/// <returns>A failed result carrying <paramref name="error"/>.</returns>
	public static GitResult<T> FromError(GitCommandError error) => new() { Success = false, Error = error };
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~GitResultTests"`
Expected: PASS, 6 tests.

- [ ] **Step 9: Commit**

```bash
git add GitIntegration/Execution/ GitIntegration.Test/Execution/
git commit -m "[minor] Add git execution contract, result and exception types"
```

### Task 5: RunCommand-backed process runner

**Files:**
- Create: `GitIntegration/Execution/RunCommandGitProcessRunner.cs`
- Test: `GitIntegration.Test/Execution/RunCommandGitProcessRunnerTests.cs`

**Interfaces:**
- Consumes: `GitOptions`, `GitProcessResult`, `IGitProcessRunner`, `GitExecutableNotFoundException` from Task 4.
- Produces: `RunCommandGitProcessRunner`, a public sealed class with constructor `RunCommandGitProcessRunner(GitOptions options)`. Task 7 registers it in DI; Task 8 consumes it via `IGitProcessRunner`.

These tests run real processes but not git, so they work anywhere.

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/Execution/RunCommandGitProcessRunnerTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class RunCommandGitProcessRunnerTests
{
	[TestMethod]
	public async Task CapturesStandardOutputAndExitCodeAsync()
	{
		// Uses the host's own dotnet executable rather than git, so this test does not
		// require git to be installed.
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsTrue(result.Success);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.StandardOutput));
	}

	[TestMethod]
	public async Task EchoesArgumentVectorBackOnTheResultAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token);

		CollectionAssert.AreEqual(new[] { "--version" }, result.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ThrowsExecutableNotFoundWhenBinaryIsMissingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "definitely-not-a-real-executable-9f3a2b",
		});

		await Assert.ThrowsExactlyAsync<GitExecutableNotFoundException>(
			async () => await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token));
	}

	[TestMethod]
	public async Task ReportsNonZeroExitCodeWithoutThrowingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(
			["--this-flag-does-not-exist"],
			TestContext.CancellationTokenSource.Token);

		Assert.AreNotEqual(0, result.ExitCode);
		Assert.IsFalse(result.Success);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RunCommandGitProcessRunnerTests"`
Expected: FAIL to compile — `RunCommandGitProcessRunner` does not exist.

- [ ] **Step 3: Create `GitIntegration/Execution/RunCommandGitProcessRunner.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ktsu.RunCommand;

/// <summary>
/// Runs git through <c>ktsu.RunCommand</c>, using its argument-vector overload so that no shell
/// is involved and no argument needs manual quoting.
/// </summary>
/// <param name="options">Configures the executable location and the per-invocation timeout.</param>
public sealed class RunCommandGitProcessRunner(GitOptions options) : IGitProcessRunner
{
	private GitOptions Options { get; } = Ensure.NotNull(options);

	/// <inheritdoc />
	public async Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(arguments);

		StringBuilder standardOutput = new();
		StringBuilder standardError = new();

		OutputHandler outputHandler = new(
			onStandardOutput: chunk => standardOutput.Append(chunk),
			onStandardError: chunk => standardError.Append(chunk));

		using CancellationTokenSource linked = CreateLinkedTokenSource(cancellationToken);

		int exitCode;
		try
		{
			// Fully qualified on purpose: `RunCommand` names both the namespace
			// `ktsu.RunCommand` and the static class `ktsu.RunCommand.RunCommand`, so the short
			// form is ambiguous to read even where it compiles.
			exitCode = await ktsu.RunCommand.RunCommand.ExecuteAsync(
				Options.ExecutablePath,
				arguments,
				outputHandler,
				Elevation.Default,
				linked.Token).ConfigureAwait(false);
		}
		catch (Win32Exception ex)
		{
			// Thrown when the executable cannot be found or started at all.
			throw new GitExecutableNotFoundException(
				$"Could not start the git executable '{Options.ExecutablePath}'. Is git installed and on PATH?",
				ex);
		}

		return new GitProcessResult
		{
			ExitCode = exitCode,
			StandardOutput = standardOutput.ToString(),
			StandardError = standardError.ToString(),
			Arguments = arguments,
		};
	}

	private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken)
	{
		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (Options.Timeout is TimeSpan timeout)
		{
			linked.CancelAfter(timeout);
		}

		return linked;
	}
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RunCommandGitProcessRunnerTests"`
Expected: PASS, 4 tests.

If `ThrowsExecutableNotFoundWhenBinaryIsMissingAsync` fails with a different exception type, inspect what `RunCommand` actually surfaces for a missing binary on this platform and widen the `catch` accordingly — the intent is that a missing executable never escapes as a raw `Win32Exception`.

- [ ] **Step 5: Commit**

```bash
git add GitIntegration/Execution/RunCommandGitProcessRunner.cs GitIntegration.Test/Execution/RunCommandGitProcessRunnerTests.cs
git commit -m "[minor] Add RunCommand-backed git process runner"
```

### Task 6: Builder base with global argument injection

**Files:**
- Create: `GitIntegration/Builders/IGitCommandBuilder.cs`
- Create: `GitIntegration/Builders/GitCommandBuilder.cs`
- Create: `GitIntegration.Test/Fakes/RecordingGitProcessRunner.cs`
- Test: `GitIntegration.Test/Builders/GitCommandBuilderTests.cs`

**Interfaces:**
- Consumes: `IGitProcessRunner`, `GitProcessResult`, `GitResult<T>`, `GitCommandError`, `GitCommandException`, `GitRepositoryNotFoundException` from Tasks 4–5.
- Produces, relied on by every verb builder in Phases 3–5:
  - `IGitCommandBuilder<TResult>` with `IReadOnlyList<string> BuildArguments()`, `Task<TResult> ExecuteAsync(CancellationToken)`, `Task<GitResult<TResult>> TryExecuteAsync(CancellationToken)`
  - `abstract class GitCommandBuilder<TResult>(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)` with abstract `protected abstract void AppendVerbArguments(List<string> arguments);` and abstract `protected abstract TResult ParseResult(GitProcessResult result);`
  - `RecordingGitProcessRunner` test fake with `IReadOnlyList<string>? LastArguments`, and settable `StandardOutput`, `StandardError`, `ExitCode`

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/Fakes/RecordingGitProcessRunner.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Captures the argument vector a builder produces and replays canned output, so builder tests
/// never need a git binary.
/// </summary>
internal sealed class RecordingGitProcessRunner : IGitProcessRunner
{
	public IReadOnlyList<string>? LastArguments { get; private set; }

	public string StandardOutput { get; set; } = string.Empty;

	public string StandardError { get; set; } = string.Empty;

	public int ExitCode { get; set; }

	public Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
	{
		LastArguments = arguments;

		return Task.FromResult(new GitProcessResult
		{
			ExitCode = ExitCode,
			StandardOutput = StandardOutput,
			StandardError = StandardError,
			Arguments = arguments,
		});
	}
}
```

Create `GitIntegration.Test/Builders/GitCommandBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitCommandBuilderTests
{
	/// <summary>A minimal concrete builder, exercising only the base class behaviour.</summary>
	private sealed class EchoBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)
		: GitCommandBuilder<string>(runner, repositoryPath)
	{
		protected override void AppendVerbArguments(List<string> arguments) => arguments.Add("status");

		protected override string ParseResult(GitProcessResult result) => result.StandardOutput;
	}

	[TestMethod]
	public void InjectsGlobalArgumentsBeforeTheVerb()
	{
		RecordingGitProcessRunner runner = new();
		EchoBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		CollectionAssert.AreEqual(
			new[]
			{
				"-C", TestPaths.Root.WeakString,
				"--no-pager",
				"-c", "core.quotepath=false",
				"-c", "color.ui=false",
				"status",
			},
			arguments.ToArray());
	}

	[TestMethod]
	public void OmitsRepositoryScopingWhenPathIsNull()
	{
		RecordingGitProcessRunner runner = new();
		EchoBuilder builder = new(runner, repositoryPath: null);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		CollectionAssert.AreEqual(
			new[]
			{
				"--no-pager",
				"-c", "core.quotepath=false",
				"-c", "color.ui=false",
				"status",
			},
			arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteReturnsParsedResultOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "clean" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		string result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token);

		Assert.AreEqual("clean", result);
	}

	[TestMethod]
	public async Task ExecuteThrowsCommandExceptionOnFailureAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "boom" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token));

		Assert.AreEqual(1, exception.ExitCode);
		Assert.AreEqual("boom", exception.StandardError);
	}

	[TestMethod]
	public async Task ExecuteThrowsRepositoryNotFoundWhenGitSaysSoAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: not a git repository (or any of the parent directories): .git",
		};
		EchoBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitRepositoryNotFoundException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token));
	}

	[TestMethod]
	public async Task TryExecuteReturnsErrorInsteadOfThrowingAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "boom" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitResult<string> result = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);
		Assert.AreEqual("boom", result.Error?.StandardError);
	}

	[TestMethod]
	public async Task TryExecuteReturnsValueOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "clean" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitResult<string> result = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token);

		Assert.IsTrue(result.Success);
		Assert.AreEqual("clean", result.Value);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitCommandBuilderTests"`
Expected: FAIL to compile — `GitCommandBuilder<>` does not exist.

- [ ] **Step 3: Create `GitIntegration/Builders/IGitCommandBuilder.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Builds and runs one git command, returning a parsed result.
/// </summary>
/// <typeparam name="TResult">The parsed result type.</typeparam>
public interface IGitCommandBuilder<TResult>
{
	/// <summary>
	/// Gets the exact argument vector this builder will pass to git.
	/// </summary>
	/// <remarks>
	/// This is a pure computation with no I/O, which makes the produced command directly
	/// assertable in tests and inspectable when diagnosing an unexpected result.
	/// </remarks>
	/// <returns>The argument vector.</returns>
	public IReadOnlyList<string> BuildArguments();

	/// <summary>Runs the command, throwing when git exits non-zero.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed result.</returns>
	/// <exception cref="GitCommandException">Git exited with a non-zero code.</exception>
	public Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs the command, reporting a non-zero exit as a result rather than an exception.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed result, or the failure detail.</returns>
	public Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create `GitIntegration/Builders/GitCommandBuilder.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The shared behaviour of every git command builder: global argument injection, execution, and
/// failure translation.
/// </summary>
/// <typeparam name="TResult">The parsed result type.</typeparam>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">
/// The repository to scope the command to, or <see langword="null"/> for commands that are not
/// repository-scoped, such as <c>init</c>, <c>clone</c>, and <c>--version</c>.
/// </param>
public abstract class GitCommandBuilder<TResult>(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)
	: IGitCommandBuilder<TResult>
{
	private IGitProcessRunner Runner { get; } = Ensure.NotNull(runner);

	/// <summary>
	/// Gets the repository this command is scoped to, if any.
	/// </summary>
	protected AbsoluteDirectoryPath? RepositoryPath { get; } = repositoryPath;

	/// <summary>
	/// Appends the verb and its options to the argument vector, after the global arguments.
	/// </summary>
	/// <param name="arguments">The vector being assembled.</param>
	protected abstract void AppendVerbArguments(List<string> arguments);

	/// <summary>
	/// Turns a successful invocation's output into the result type.
	/// </summary>
	/// <param name="result">The raw invocation outcome.</param>
	/// <returns>The parsed result.</returns>
	protected abstract TResult ParseResult(GitProcessResult result);

	/// <inheritdoc />
	public IReadOnlyList<string> BuildArguments()
	{
		List<string> arguments = [];

		if (RepositoryPath is not null)
		{
			// RunCommand cannot set a process working directory, so the repository is selected
			// with -C rather than by launching git inside it.
			arguments.Add("-C");
			arguments.Add(RepositoryPath.WeakString);
		}

		// Git must never block on a pager, must not octal-escape non-ASCII paths, and must not
		// emit ANSI colour codes, or the output stops being parseable.
		arguments.Add("--no-pager");
		arguments.Add("-c");
		arguments.Add("core.quotepath=false");
		arguments.Add("-c");
		arguments.Add("color.ui=false");

		AppendVerbArguments(arguments);

		return arguments;
	}

	/// <inheritdoc />
	public async Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<string> arguments = BuildArguments();
		GitProcessResult result = await Runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return ParseResult(result);
	}

	/// <inheritdoc />
	public async Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<string> arguments = BuildArguments();
		GitProcessResult result = await Runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);

		return result.Success
			? GitResult<TResult>.FromValue(ParseResult(result))
			: GitResult<TResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	private static GitCommandException CreateException(GitProcessResult result)
	{
		string message = $"git exited with code {result.ExitCode}: {result.StandardError.Trim()}";

		// Git reports a missing working tree with a stable phrase and exit code 128. Surfacing it
		// as a distinct type lets callers distinguish "wrong directory" from "command failed".
		return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
			? new GitRepositoryNotFoundException(message, result.ExitCode, result.Arguments, result.StandardError)
			: new GitCommandException(message, result.ExitCode, result.Arguments, result.StandardError);
	}
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~GitCommandBuilderTests"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/ GitIntegration.Test/Fakes/ GitIntegration.Test/Builders/
git commit -m "[minor] Add git command builder base with global argument injection"
```

### Task 7: Dependency injection registration

**Files:**
- Create: `GitIntegration/ServiceCollectionExtensions.cs`
- Test: `GitIntegration.Test/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `GitOptions`, `IGitProcessRunner`, `RunCommandGitProcessRunner` from Tasks 4–5.
- Produces: `AddGitIntegration(this IServiceCollection)` and `AddGitIntegration(this IServiceCollection, Action<GitOptions>)`. Task 8 extends this to also register `IGitClient`; Task 18 adds the provider registrations.

- [ ] **Step 1: Write the failing test**

Create `GitIntegration.Test/ServiceCollectionExtensionsTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`
Expected: FAIL to compile — `AddGitIntegration` does not exist.

- [ ] **Step 3: Create `GitIntegration/ServiceCollectionExtensions.cs`**

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

using ktsu.Essentials.FileSystemProviders.Native;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Dependency injection registration for git integration.
/// </summary>
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
	/// more than once is a no-op, matching the conventions in <c>ktsu.Essentials</c>.
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

		// Filesystem access goes through an injected abstraction so that discovery, clone, and
		// init can be tested without touching disk.
		services.AddNativeFileSystemProvider();

		return services;
	}
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet build && dotnet test`
Expected: build clean, all tests pass. This is the Phase 2 exit gate.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/ServiceCollectionExtensions.cs GitIntegration.Test/ServiceCollectionExtensionsTests.cs
git commit -m "[minor] Add dependency injection registration for git integration"
```

---


## Scope of this plan, and what follows

This plan covers **Phases 1 and 2 only** — Tasks 1 through 7. At its end the repository builds
clean with `LibGit2Sharp` and `ktsu.AppDataStorage` removed, every type migrated to
`ktsu.Semantics`, and a working execution core: `IGitProcessRunner` backed by `ktsu.RunCommand`,
the exception and result hierarchy, the builder base with global-argument injection, and DI
registration. That is working, testable software on its own, even though no git verb is exposed
yet.

Phases 3 to 5 each get their own plan document, written when the preceding phase completes:

| Plan | Covers | Exit gate |
|---|---|---|
| `…-v2-phase3-read-only-verbs.md` | Result models; parsers and builders for `status`, `log`, `diff`, `for-each-ref`, `remote -v`, `rev-parse`, `--version`; `IGitClient`; the merged `GitRepository` with `IsClonedAsync` and origin back-fill | The read-only half of the fluent API is usable end to end |
| `…-v2-phase4-mutating-verbs.md` | `init`, `clone`, `add`, `commit`, branch create/delete, `checkout`, remote add/remove/set-url | Tier-3 integration tests pass against a real git binary |
| `…-v2-phase5-remote-and-hosting.md` | `fetch`, `pull`, `push`; `IGitHostingProvider`; the `GitProvider` async refactor; `GitHubProvider`; `AzureDevOpsProvider`; provider DI; documentation | README examples compile as written |

Splitting this way is deliberate rather than a deferral. Each phase's tasks depend on decisions
made while implementing the one before it — the exact shape of a verb builder is much better
specified once the base class exists in code rather than only on paper, and the parser fixtures
should be captured from the git actually installed on the machine rather than transcribed from
memory. Writing Tasks 8 through 25 now would mean writing them against assumptions that Phases 1
and 2 are about to test.

The spec at `docs/superpowers/specs/2026-08-19-gitintegration-v2-design.md` remains the single
source of truth for all five phases; it already fixes the verb list, the invocation for each,
the result-model shapes, and the parsing formats, so the later plans expand that detail into
steps rather than re-deciding anything.

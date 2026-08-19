# ktsu.GitIntegration

> A .NET library that unifies access to hosted Git providers behind a single abstraction, with semantic types for Git identifiers.

[![License](https://img.shields.io/github/license/ktsu-dev/GitIntegration.svg?label=License&logo=nuget)](LICENSE.md)
[![NuGet Version](https://img.shields.io/nuget/v/ktsu.GitIntegration?label=Stable&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![NuGet Version](https://img.shields.io/nuget/vpre/ktsu.GitIntegration?label=Latest&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ktsu.GitIntegration?label=Downloads&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/GitIntegration?label=Commits&logo=github)](https://github.com/ktsu-dev/GitIntegration/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/GitIntegration?label=Contributors&logo=github)](https://github.com/ktsu-dev/GitIntegration/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/GitIntegration/dotnet.yml?branch=main&label=Build&logo=github)](https://github.com/ktsu-dev/GitIntegration/actions)

## Introduction

`ktsu.GitIntegration` provides a common surface for talking to hosted Git providers. Instead of
writing one code path against Octokit for GitHub and another against the Team Foundation Server
client for Azure DevOps, you work against a single `GitProvider` abstraction and let the concrete
implementation deal with authentication and API differences.

Alongside the provider abstraction, the library replaces the stringly-typed values that pervade Git
tooling — branch names, commit SHAs, remote names, author emails — with validated semantic types
built on `ktsu.Semantics`. A `GitBranchName` can no longer be accidentally passed where a
`GitCommitSha` is expected.

## Features

- **Provider Abstraction**: `GitProvider` defines a common contract for enumerating and refreshing
  remote repositories, with concrete implementations per host.
- **GitHub Support**: `GitHubProvider` wraps Octokit and authenticates from cached credentials.
- **Azure DevOps Support**: Built on the Microsoft Team Foundation Server and Visual Studio Services
  clients.
- **Credential Resolution**: Integrates with `ktsu.CredentialCache`, so credentials come from the
  host's native keyring rather than configuration files.
- **Semantic Git Types**: Validated wrappers for the identifiers Git tooling passes around, so
  mismatched arguments fail at compile time rather than at runtime.
- **Repository Model**: `GitRepository` captures a repository's remote path, web URI, and owner in
  a strongly-typed form.

## Installation

### Package Manager Console

```powershell
Install-Package ktsu.GitIntegration
```

### .NET CLI

```bash
dotnet add package ktsu.GitIntegration
```

### Package Reference

```xml
<PackageReference Include="ktsu.GitIntegration" Version="x.y.z" />
```

## Usage Examples

### Basic Example

```csharp
using ktsu.GitIntegration;

GitProvider provider = new GitHubProvider();

// Pulls credentials from the credential cache, then authenticates the client.
provider.RefreshRemoteRepositories();
```

### Working with Semantic Types

```csharp
using ktsu.Extensions;
using ktsu.GitIntegration;

GitBranchName branch = "main".As<GitBranchName>();
GitRemoteName remote = "origin".As<GitRemoteName>();
GitCommitSha sha = "9fceb02d0ae598e95dc970b74767f19372d61af8".As<GitCommitSha>();

// These are distinct types — passing a GitBranchName where a GitCommitSha
// is expected is a compile error, not a runtime surprise.
```

### Opening a Repository in the Browser

```csharp
using ktsu.GitIntegration;

GitRepository repository = /* obtained from a provider */;
repository.OpenWebClient();
```

## API Reference

### `GitProvider`

Abstract base class describing a hosted Git provider.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `Name` | `GitProviderName` | Display name of the provider. |

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `RefreshRemoteRepositories()` | `void` | Refreshes the provider's view of the remote repositories, authenticating first if a credential is available. |
| `TryGetCredential(out Credential?)` | `bool` | Attempts to resolve a credential for this provider from the credential cache. |

### `GitHubProvider`

`GitProvider` implementation backed by Octokit. Authenticates the underlying `GitHubClient` from a
`CredentialWithUsernamePassword` resolved via `TryGetCredential`.

### `GitRepository`

Strongly-typed representation of a repository on a remote host.

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `OpenWebClient()` | `void` | Opens the repository's web URI in the default browser. |

### Semantic Types

| Type | Wraps |
|------|-------|
| `GitAuthorEmail` | Commit author email address |
| `GitAuthorName` | Commit author name |
| `GitBranchName` | Branch name |
| `GitCommitMessage` | Commit message |
| `GitCommitSha` | Commit hash |
| `GitProviderGUID` | Provider-assigned unique identifier |
| `GitProviderName` | Provider display name |
| `GitProviderOwner` | Account or organization owning a repository |
| `GitRefName` | Fully qualified ref name |
| `GitRemoteName` | Remote name |
| `GitRepositoryName` | Repository name |
| `GitRepositoryRemotePath` | Clone path or URL |
| `GitRepositoryWebURI` | Repository web address |
| `AzureDevOpsProjectName` | Azure DevOps project name |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.

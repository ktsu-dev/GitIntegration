# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore, build, and test (standard workflow)
dotnet restore
dotnet build
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestName"

# Build specific configuration
dotnet build -c Release
```

**Do not add `--nologo` to `dotnet test`.** The test project uses Microsoft Testing Platform (via
`MSTest.Sdk`), and under that platform `dotnet test --nologo` silently runs zero tests and exits
with code 5 instead of failing loudly. Always invoke plain `dotnet test`.

## Project Structure

This is a .NET library (`ktsu.GitIntegration`) with two layers: a local layer that wraps the `git`
executable found on `PATH`, and a hosting layer that talks to remote Git providers (currently
GitHub, via Octokit). The solution uses:

- **ktsu.Sdk** — custom SDK providing shared build configuration
- **MSTest.Sdk** — test project SDK with Microsoft Testing Platform
- `GitIntegration` targets `net10.0;net9.0`; `GitIntegration.Test` targets `net10.0` only

### Key Files

- `GitIntegration/IGitClient.cs`, `GitIntegration/GitClient.cs` — entry point to the local layer:
  `GetVersionAsync`, `IsRepositoryAsync`, `OpenAsync`, `DiscoverAsync`.
- `GitIntegration/GitRepository.cs` — carries `LocalPath` plus optional hosting metadata, and
  exposes one builder factory per verb: `Status()`, `Log()`, `Diff()`, `Branches()`, `Remotes()`,
  `RevParse(...)`, plus `IsClonedAsync` and `OpenWebClient`.
- `GitIntegration/Builders/` — public verb-builder interfaces and their internal implementations.
  `IGitVersionBuilder` is `internal` — it is not part of the public API surface, because nothing
  public ever returns one.
- `GitIntegration/Models/` — result records and enums: `GitStatus`, `GitStatusEntry`, `GitCommit`,
  `GitSignature`, `GitBranch`, `GitRemote`, `GitDiffEntry`, `GitVersion`, `GitFileState`,
  `GitChangeKind`, `GitUntrackedFilesMode`.
- `GitIntegration/Execution/` — `GitOptions`, `IGitProcessRunner`, `RunCommandGitProcessRunner`,
  `GitResult<T>`, and the exception hierarchy (`GitException` → `GitExecutableNotFoundException`,
  `GitTimeoutException`, `GitParseException`, `GitCommandException` → `GitRepositoryNotFoundException`).
- `GitIntegration/Parsing/` — internal parsers turning raw git output into the `Models/` records.
- `GitIntegration/SemanticTypes/` — the 13 `ktsu.Semantics` wrapper types for git identifiers.
- `GitIntegration/GitProvider.cs`, `GitIntegration/GitHubProvider.cs` — the hosting layer.
- `GitIntegration/ServiceCollectionExtensions.cs` — `AddGitIntegration()` DI registration.

### Dependencies

- `ktsu.RunCommand` — runs the git executable without a shell, via an argument-vector overload.
- `ktsu.Semantics.Strings`, `ktsu.Semantics.Paths` — the semantic string/path base types.
- `ktsu.Essentials`, `ktsu.Essentials.FileSystemProviders.Native` — registered for the filesystem
  abstraction that Phase 4's `Init`/`Clone` will need; discovery itself needs none, since
  `git rev-parse --show-toplevel` does its own upward walk.
- `ktsu.CredentialCache` — resolves hosting-provider credentials from the host's native keyring.
- `Octokit` — GitHub API client backing `GitHubProvider`.
- `Microsoft.Extensions.DependencyInjection.Abstractions` — DI registration surface.
- `Polyfill` (`PrivateAssets="all"`) — backports newer BCL APIs to the older target frameworks.

## Architecture

**Local layer.** `GitClient` implements `IGitClient` by running everything through an
`IGitProcessRunner`. Repository discovery is delegated to `git rev-parse --show-toplevel`, which
performs the upward directory walk itself — this is why `GitClient` needs no filesystem abstraction
of its own. Every verb builder (`GitStatusBuilder`, `GitLogBuilder`, `GitDiffBuilder`, etc.) derives
from `GitCommandBuilder<TResult>`, which owns argument assembly, execution, and failure translation.
A builder is single-use and not thread-safe; the underlying `IGitProcessRunner` is a shared,
thread-safe singleton.

Two non-obvious, load-bearing design points:

1. **Every command is scoped with `git -C <path>`, never a process working directory.**
   `ktsu.RunCommand` has no notion of a working directory, and scoping this way means a failing
   command's exact argument vector — captured on `GitCommandException.Arguments` — can be copied out
   and rerun verbatim on a command line (`git` + the arguments) to reproduce the failure exactly.

2. **Every invocation runs with `GIT_TERMINAL_PROMPT=0` and `LC_ALL=C`.** The former stops git
   blocking forever on a credential prompt that output redirection makes impossible to answer. The
   latter forces English, machine-stable output — this is the whole reason the parsers in
   `Parsing/` can safely match on fixed English phrases (e.g. `"not a git repository"`); without it,
   every message-matching decision would silently degrade on a non-English host.

**Hosting layer.** `GitProvider` is an abstract base with a `GitHubProvider` implementation over
Octokit. `IsAuthenticated` and `RefreshRemoteRepositories()` both go through `TryGetCredential`,
which resolves a `Credential` from `ktsu.CredentialCache` keyed by `PersonaGUID`. Azure DevOps
hosting support is **not implemented** — `AzureDevOpsProjectName` exists as a semantic type but
there is no `AzureDevOpsProvider`. The two Azure DevOps client packages were deliberately not
referenced, because they pull `System.Data.SqlClient` (a package with a known high-severity
advisory) into the published package as a direct dependency. Do not add Azure DevOps hosting
support without resolving that dependency concern first.

**Not yet implemented (do not document as present):** `init`, `clone`, `add`, `commit`, branch
creation, `checkout`, `fetch`, `pull`, `push`. These are tracked for later phases.

## Testing

Uses MSTest via `MSTest.Sdk`. Two fakes drive the builder and client tests without invoking a real
git binary:

- `Fakes/RecordingGitProcessRunner.cs` — captures the argument vector(s) passed to it, for asserting
  exactly what a builder sends to git.
- `Fakes/ScriptedGitProcessRunner.cs` — returns pre-scripted output/exit codes per call, for testing
  parsing and multi-invocation flows (e.g. `GitClient.DiscoverAsync` running `rev-parse` then
  `remote get-url origin`).

`GitRepositoryMetadataTests.TestPaths` provides a cross-platform `AbsoluteDirectoryPath` root used
across the builder and repository tests.

**Remember:** plain `dotnet test`, never `dotnet test --nologo` — see Build Commands above.

## CI/CD

Uses `scripts/PSBuild.psm1` PowerShell module for CI pipeline. Version increments are controlled by
commit message tags: `[major]`, `[minor]`, `[patch]`, `[pre]`.

## Code Quality

Do not add global suppressions for warnings. Use explicit suppression attributes with justifications
when needed, with preprocessor defines only as fallback. Make the smallest, most targeted
suppressions possible.

This repository currently has **zero** `[SuppressMessage]` attributes anywhere in `GitIntegration/`.
That is deliberate, not incidental — every analyzer complaint encountered so far has been fixable
by changing the code rather than suppressing the warning. Reach for a suppression only after
confirming the complaint genuinely cannot be addressed by a code change, and keep that bar high.

## Code Style

- `.cs` files use **LF** line endings, not the CRLF that is the general ktsu convention. The repo's
  `.gitattributes` sets `* text=auto eol=lf` for all text files, which overrides `core.autocrlf` so
  the working tree is byte-identical across Windows, Linux, and macOS.
- Tabs for indentation.
- File-scoped namespaces (`namespace ktsu.GitIntegration;`).
- `using` directives go **inside** the namespace, after the namespace declaration — see any file
  under `GitIntegration/` for the pattern.

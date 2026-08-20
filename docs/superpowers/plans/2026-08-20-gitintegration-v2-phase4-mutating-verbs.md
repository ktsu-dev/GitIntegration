# GitIntegration v2 — Phase 4: Mutating Local Verbs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the mutating half of the local layer — `init`, `clone`, `add`, `commit`, branch create/delete, `checkout`, and the remote add/remove/set-url commands — plus the tier-3 integration tests that exercise a real git binary end to end.

**Scope:** Phase 4 of the five phases in the spec. Phases 1–3 are merged and released as 2.1.0. Phase 5 (remote sync: `fetch`/`pull`/`push`, plus the hosting providers) follows and gets its own plan.

**Architecture:** Mutating verbs reuse Phase 3's shape — a builder derives from `GitCommandBuilder<TResult>`, contributes only its verb's arguments, and returns a typed result. They differ in three ways. Most produce no parseable output at all, so they return the unit record `GitCompleted` and let the exit code carry the outcome. Two need a second invocation: `Commit` runs `commit` and then reads the new commit back with the pinned `log -1` format, and `Init` probes with `rev-parse` first so it can report whether the repository already existed without parsing git's prose. `Clone` and `Init` are the first users of `ktsu.Essentials.IFileSystemProvider`, which `AddGitIntegration` has registered since Phase 2 but nothing has consumed.

**Tech Stack:** .NET 10 / .NET 9, `ktsu.Sdk` 2.25.0, `ktsu.Semantics.Strings` 3.0.1, `ktsu.Semantics.Paths` 3.0.1, `ktsu.RunCommand` 1.5.0, `ktsu.Essentials` 2.0.0, MSTest via `MSTest.Sdk`, `Testably.Abstractions.Testing` 7.0.2 (test project only).

**Spec:** `docs/superpowers/specs/2026-08-19-gitintegration-v2-design.md`

**Prior plans:** `docs/superpowers/plans/2026-08-19-gitintegration-v2-phase1-2-foundation.md`, `docs/superpowers/plans/2026-08-20-gitintegration-v2-phase3-read-only-verbs.md`

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Indentation is tabs**, not spaces, in all `.cs` files. **Line endings are LF** — this repo's `.gitattributes` sets `* text=auto eol=lf`, which overrides the CRLF default in the global CLAUDE.md.
- **File-scoped namespaces.** `using` directives go **inside** the namespace. Namespace is `ktsu.GitIntegration` for the library and `ktsu.GitIntegration.Test` for tests, regardless of folder.
- **Every file starts with** `// Copyright (c) 2023-2026 ktsu-dev contributors`, then a blank line.
- **Nullable reference types enabled; warnings are errors.** The build fails on any warning.
- **Zero `[SuppressMessage]` attributes.** The repository has none across all three shipped phases and must keep none. Every analyzer complaint so far (CA1002, CA1062, CA1716, CA1859, CA1861, CA2007, IDE0300, IDE0305, MSTEST0032, MSTEST0065) turned out to be genuinely fixable. Fix the code, not the analyzer.
- **No `this.` qualifiers.** Explicit accessibility modifiers everywhere, including `public` on interface members.
- **XML doc comments on every public member.** The SDK treats a missing one as an error.
- **Use `Ensure.NotNull(x)`** (from `Polyfill`) in the **library**. `Polyfill` is `PrivateAssets="all"`, so `Ensure` is **not visible to the test project** — tests use `ArgumentNullException.ThrowIfNull`.
- **Every `await` in the library gets `.ConfigureAwait(false)`** (CA2007). Test code too.
- **Tests use MSTest** with semantic assertions. Async test methods end in `Async` (MSTEST0032/0065). Classes needing a token declare `public TestContext TestContext { get; set; } = null!;`.
- **When asserting that a value-returning call throws, discard the result** so the lambda binds to `Action`: `Assert.ThrowsExactly<T>(() => _ = thing.Method(null!));`
- **Commit message tags:** `[minor]` on feature commits. Recognised tags are `[major]`, `[minor]`, `[patch]`, `[pre]` — **`[fix]` is not one of them** and silently fails to signal a version bump. Never add `Co-Authored-By` lines.
- **Do not edit** `VERSION.md`, `CHANGELOG.md`, `LATEST_CHANGELOG.md`, `LICENSE.md` — generated.
- **`ktsu.Sdk` regenerates `.gitignore` on every `dotnet build`.** Do not hand-edit or commit it.
- **Build:** `dotnet build`. **Test:** `dotnet test` — **never `dotnet test --nologo`**, which silently runs zero tests under Microsoft.Testing.Platform and exits 5.
- **Target frameworks** stay `net10.0;net9.0` for the library, `net10.0` for the test project.
- **Central package management is on.** A new package needs a `<PackageVersion>` entry in `Directory.Packages.props` *and* a `<PackageReference>` (no version) in the consuming `.csproj`.

### Design invariants carried from Phases 1–3

- **Every repository-scoped command uses `git -C <path>`**, never a process working directory, so a failing command can be copied out of a `GitCommandException` and rerun verbatim.
- **Every invocation runs with `GIT_TERMINAL_PROMPT=0` and `LC_ALL=C`.** The forced C locale is what makes message-matching classification dependable — and Phase 4 relies on it more heavily than Phase 3 did, because several mutating verbs report their one interesting failure only in prose.
- **Caller-supplied operands go through `GitCommandBuilder<TResult>.AppendOperands`**, which emits `--end-of-options` first. Library-chosen literals do not need it.
- **Builders are mutable, single-use, not thread-safe**, and return `this` typed as the interface. Each `GitRepository.Verb()` call returns a fresh builder.
- **Builder classes are `internal sealed`; their interfaces are public.** `GitRepository` and `IGitClient` return the interface.
- **The API is asynchronous only.**

---

## Findings from probing the installed git

Captured from `git version 2.50.1.windows.1` with `LC_ALL=C`. **These are the behaviours the tasks are built on — do not re-derive them.**

### Exit codes are not uniform, and that matters

Phase 3's verbs failed with 128 almost universally. Phase 4's do not:

| Command | Failure | Exit | Stream |
|---|---|---|---|
| `init` | *(idempotent — re-init succeeds)* | **0** | stdout prose |
| `clone` | destination exists and is non-empty | 128 | stderr |
| `clone` | source is not a repository | 128 | stderr |
| `clone` | `--branch` names no such branch | 128 | stderr |
| `add` | pathspec matched nothing | 128 | stderr |
| `commit` | **nothing staged** | **1** | **stdout** |
| `branch <name>` | branch already exists | 128 | stderr |
| `branch -d` | branch not fully merged | **1** | stderr |
| `branch -d` | branch is checked out | **1** | stderr |
| `checkout` | no such ref | **1** | stderr |
| `remote add` | remote already exists | **3** | stderr |
| `remote set-url` | no such remote | **2** | stderr |
| `remote remove` | no such remote | **2** | stderr |

Nothing in the library keys off a specific non-zero code — `GitCommandException.ExitCode` carries whatever git returned — but the table is here so no task invents an assertion that only holds for 128.

### `git commit` reports "nothing to commit" on **stdout**, and the base class only reads stderr

This is the single most important finding in the phase. `GitCommandBuilder<TResult>.CreateException` inspects `result.StandardError`. Both of git's "nothing to commit" messages go to **standard output**, with stderr completely empty:

```
$ git commit -m x            # clean tree
On branch main
nothing to commit, working tree clean
                             exit 1, stderr empty

$ git commit -m x            # untracked files present, nothing staged
On branch main
Untracked files:
  (use "git add <file>..." to include in what will be committed)
	brand-new-untracked.txt

nothing added to commit but untracked files present (use "git add" to track)
                             exit 1, stderr empty
```

The two phrases are different and **neither contains the other**: `nothing to commit` and `nothing added to commit`. A classifier must match both.

### `git init` is idempotent and says so only in prose

```
$ git init --initial-branch=main <new>
Initialized empty Git repository in .../.git/          exit 0

$ git init --initial-branch=main <same dir again>
warning: re-init: ignored --initial-branch=main
Reinitialized existing Git repository in .../.git/     exit 0
```

"Initialized" versus "Reinitialized" is the only signal, and it is exactly the human output the design forbids parsing. Hence the two-invocation design in Task 8: probe with `rev-parse --is-inside-work-tree` first, then run `init`.

Note also that a second `init` **silently ignores `--initial-branch`** — another reason the caller wants to know the repository already existed.

### `git clone` writes everything to stderr and cleans up after itself

```
$ git clone <src> <dst> 1>out.txt 2>err.txt
--stdout--   (empty)
--stderr--
Cloning into '<dst>'...
done.
```

Two consequences. Progress reporting must come from **stderr**, which `RunCommandGitProcessRunner` already routes into `GitProcessRequest.Progress` alongside stdout. And a failed clone leaves **no directory behind** — verified for a bad source and a bad `--branch` — so no cleanup logic is needed.

### `git checkout` reports success on stderr

`Switched to branch 'x'` and `Switched to a new branch 'x'` both go to stderr, exit 0. Nothing to parse; the exit code is the result.

### Pathspecs are relative to the `-C` directory

`git -C <repo-root> add --end-of-options sub/nested.txt` stages `sub/nested.txt` relative to the root. Since `GitRepository.LocalPath` is always the working-tree root (`GitClient` resolves it with `rev-parse --show-toplevel`), a `RelativeFilePath` maps directly onto a pathspec with no translation.

`--end-of-options` is accepted by `add`, `branch`, `checkout`, and `remote add`.

### `git add -A` can print `error:` on stderr and still exit 0

Observed when the tree contained a nested repository. **The exit code is authoritative; stderr content is not.** No task may treat non-empty stderr as failure.

### Author and committer can be set independently

- `--author="Name <email>"` sets the **author** only; the committer still comes from config.
- `-c user.name=... -c user.email=...` before the verb sets the **committer**.

Both matter: `--author` is the `Commit` builder's option, and the `-c` form is how the integration tests get a deterministic identity without touching the host's global gitconfig.

### `Testably.Abstractions`, not `TestableIO`

`ktsu.Essentials.IFileSystemProvider` is a marker interface deriving from `System.IO.Abstractions.IFileSystem`, and that interface comes from **`Testably.Abstractions`** 10.0.0, not the more commonly seen `TestableIO.System.IO.Abstractions`. The in-memory implementation is `Testably.Abstractions.Testing.MockFileSystem`, whose newest version is **7.0.2** — a lower major than the abstractions package, which looks wrong but is correct for this vendor.

Adding it was measured, not assumed. It resolves cleanly and the bump is **confined to the test project**:

| Package | Library project | Test project |
|---|---|---|
| `Testably.Abstractions` | 10.0.0 | 10.0.0 |
| `Testably.Abstractions.Interface` | 10.0.0-pre.1 | **10.3.0** |
| `Testably.Abstractions.FileSystem.Interface` | 10.0.0 | **10.3.0** |

The shipped package's dependency graph is unchanged, and the full suite passed with the package present. `IFileSystem` exposes nine properties, so the `IFileSystemProvider` fake is nine delegating lines.

---

## File Structure

**Library — `GitIntegration/`**

| File | Responsibility |
|---|---|
| `Models/GitCompleted.cs` | The unit result for verbs whose only outcome is success or failure |
| `Models/GitInitResult.cs` | `GitInitResult` — the repository plus whether it already existed |
| `Execution/GitExceptions.cs` | **Modified** — add `GitNothingToCommitException` |
| `Builders/GitCommandBuilder.cs` | **Modified** — add the `Progress` seam so a long-running verb can report incremental output |
| `Builders/GitAddBuilder.cs` | `IGitAddBuilder` + `GitAddBuilder` |
| `Builders/GitCommitBuilder.cs` | `IGitCommitBuilder` + `GitCommitBuilder` (two invocations) |
| `Builders/GitBranchCreateBuilder.cs` | `IGitBranchCreateBuilder` + `GitBranchCreateBuilder` |
| `Builders/GitBranchDeleteBuilder.cs` | `IGitBranchDeleteBuilder` + `GitBranchDeleteBuilder` |
| `Builders/GitCheckoutBuilder.cs` | `IGitCheckoutBuilder` + `GitCheckoutBuilder` |
| `Builders/GitRemoteAddBuilder.cs` | `IGitRemoteAddBuilder` + `GitRemoteAddBuilder` |
| `Builders/GitRemoteRemoveBuilder.cs` | `IGitRemoteRemoveBuilder` + `GitRemoteRemoveBuilder` |
| `Builders/GitRemoteSetUrlBuilder.cs` | `IGitRemoteSetUrlBuilder` + `GitRemoteSetUrlBuilder` |
| `Builders/GitInitBuilder.cs` | `IGitInitBuilder` + `GitInitBuilder` (two invocations + filesystem) |
| `Builders/GitCloneBuilder.cs` | `IGitCloneBuilder` + `GitCloneBuilder` (filesystem pre-check + progress) |
| `GitRepository.cs` | **Modified** — add the mutating verb factories |
| `IGitClient.cs` / `GitClient.cs` | **Modified** — add `Init` and the two `Clone` overloads; take `IFileSystemProvider` |

**Tests — `GitIntegration.Test/`**

| File | Responsibility |
|---|---|
| `Fakes/FakeFileSystemProvider.cs` | Wraps `MockFileSystem` as an `IFileSystemProvider` |
| `Builders/GitAddBuilderTests.cs` … one per verb | argv assertions and result handling |
| `GitClientMutatingTests.cs` | `Init` / `Clone` behaviour over the scripted runner and the fake filesystem |
| `Integration/TemporaryRepository.cs` | Per-test temp repo helper with a Windows-safe recursive delete |
| `Integration/GitRoundTripTests.cs` | Tier-3 tests against a real git binary, self-skipping when absent |

**Modified:** `Directory.Packages.props` and `GitIntegration.Test/GitIntegration.Test.csproj` gain `Testably.Abstractions.Testing`.

Each verb's interface and builder share one file, matching Phase 3.

---
## Task 1: Foundations — result models, exception, progress seam, and the filesystem fake

Everything later tasks consume, in one task because none of it is independently rejectable.

**Files:**
- Create: `GitIntegration/Models/GitCompleted.cs`
- Create: `GitIntegration/Models/GitInitResult.cs`
- Modify: `GitIntegration/Execution/GitExceptions.cs` (append `GitNothingToCommitException`)
- Modify: `GitIntegration/Builders/GitCommandBuilder.cs` (add the `Progress` seam)
- Modify: `Directory.Packages.props`
- Modify: `GitIntegration.Test/GitIntegration.Test.csproj`
- Create: `GitIntegration.Test/Fakes/FakeFileSystemProvider.cs`
- Test: `GitIntegration.Test/Builders/GitCommandBuilderProgressTests.cs`
- Test: `GitIntegration.Test/Fakes/FakeFileSystemProviderTests.cs`

**Interfaces:**
- Consumes: `GitCommandException`, `GitRepository`, `GitProcessRequest.Progress`, `GitCommandBuilder<TResult>` (all existing).
- Produces:
  - `public sealed record GitCompleted` with `required IReadOnlyList<string> Arguments`.
  - `public sealed record GitInitResult` with `required GitRepository Repository` and `required bool AlreadyExisted`.
  - `public sealed class GitNothingToCommitException : GitCommandException` with the four constructors its siblings have.
  - `protected IProgress<string>? Progress { get; set; }` on `GitCommandBuilder<TResult>`, passed into every `GitProcessRequest` the base builds.
  - `internal sealed class FakeFileSystemProvider(MockFileSystem inner) : IFileSystemProvider`.

- [ ] **Step 1: Add the test-only package**

In `Directory.Packages.props`, add inside the existing `<ItemGroup>`:

```xml
<PackageVersion Include="Testably.Abstractions.Testing" Version="7.0.2" />
```

In `GitIntegration.Test/GitIntegration.Test.csproj`, add to the `<ItemGroup>` that already carries `Microsoft.Extensions.DependencyInjection`:

```xml
<!-- Phase 4's Init and Clone take an IFileSystemProvider so their destination checks can be
     tested without touching disk. MockFileSystem is the in-memory IFileSystem this wraps.
     Note the vendor: ktsu.Essentials builds on Testably.Abstractions, not the similarly named
     TestableIO.System.IO.Abstractions, and Testably's testing package is versioned lower than
     its abstractions package. Both facts look like mistakes and are not. -->
<PackageReference Include="Testably.Abstractions.Testing" />
```

Run `dotnet restore` and confirm it succeeds with no downgrade warnings.

- [ ] **Step 2: Write the failing tests**

`GitIntegration.Test/Fakes/FakeFileSystemProviderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

using Testably.Abstractions.Testing;

[TestClass]
public class FakeFileSystemProviderTests
{
	private static string DestinationPath =>
		OperatingSystem.IsWindows() ? @"C:\dest" : "/dest";

	[TestMethod]
	public void ReportsAMissingDirectoryAsAbsent()
	{
		MockFileSystem mock = new();
		FakeFileSystemProvider fileSystem = new(mock);

		Assert.IsFalse(fileSystem.Directory.Exists(DestinationPath));
	}

	[TestMethod]
	public void DistinguishesAnEmptyDirectoryFromANonEmptyOne()
	{
		// These are the only two questions Clone asks the filesystem, so they are the two the fake
		// has to answer correctly.
		MockFileSystem mock = new();
		FakeFileSystemProvider fileSystem = new(mock);

		_ = fileSystem.Directory.CreateDirectory(DestinationPath);
		Assert.IsTrue(fileSystem.Directory.Exists(DestinationPath));
		Assert.AreEqual(0, fileSystem.Directory.GetFileSystemEntries(DestinationPath).Length);

		fileSystem.File.WriteAllText(fileSystem.Path.Combine(DestinationPath, "f.txt"), "x");
		Assert.AreEqual(1, fileSystem.Directory.GetFileSystemEntries(DestinationPath).Length);
	}
}
```

`GitIntegration.Test/Builders/GitCommandBuilderProgressTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitCommandBuilderProgressTests
{
	/// <summary>A builder that exposes the protected progress seam so it can be set from a test.</summary>
	private sealed class ProgressBuilder(IGitProcessRunner runner) : GitCommandBuilder<string>(runner, repositoryPath: null)
	{
		internal void SetProgress(IProgress<string>? progress) => Progress = progress;

		protected override void AppendVerbArguments(ICollection<string> arguments) => arguments.Add("clone");

		protected override string ParseResult(GitProcessResult result) => result.StandardOutput;
	}

	[TestMethod]
	public async Task ForwardsTheProgressSinkIntoTheRequestAsync()
	{
		// RecordingGitProcessRunner replays its canned output through request.Progress, exactly as
		// the real runner streams chunks as git produces them. Before this seam existed no builder
		// could observe a long-running command's output until it exited.
		List<string> reported = [];
		RecordingGitProcessRunner runner = new() { StandardOutput = "Cloning into 'x'..." };
		ProgressBuilder builder = new(runner);
		builder.SetProgress(new Progress<string>(reported.Add));

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task LeavesTheRequestProgressNullWhenNoSinkIsSetAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "done" };
		ProgressBuilder builder = new(runner);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task ForwardsTheProgressSinkFromTryExecuteTooAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "done" };
		ProgressBuilder builder = new(runner);
		builder.SetProgress(new Progress<string>(_ => { }));

		_ = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~FakeFileSystemProviderTests|FullyQualifiedName~GitCommandBuilderProgressTests"`

Expected: compilation failure — `FakeFileSystemProvider` does not exist and `GitCommandBuilder<TResult>` has no `Progress`.

- [ ] **Step 4: Write `GitCompleted`**

`GitIntegration/Models/GitCompleted.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// The result of a git command whose only outcome is that it succeeded.
/// </summary>
/// <remarks>
/// Several mutating verbs — <c>add</c>, <c>checkout</c>, branch creation and deletion, and the
/// remote commands — write nothing to standard output that is worth reading, and report failure
/// through the exit code alone. C# has no generic <c>void</c>, so
/// <see cref="IGitCommandBuilder{TResult}"/> needs a type to close over; this is it. The argument
/// vector is carried because it is the one piece of information a caller might still want after a
/// successful run, for logging or for reproducing the command by hand.
/// </remarks>
public sealed record GitCompleted
{
	/// <summary>Gets the argument vector git was run with.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }
}
```

- [ ] **Step 5: Write `GitInitResult`**

`GitIntegration/Models/GitInitResult.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The outcome of initialising a repository.
/// </summary>
public sealed record GitInitResult
{
	/// <summary>Gets the repository, ready to run further commands against.</summary>
	public required GitRepository Repository { get; init; }

	/// <summary>
	/// Gets a value indicating whether a repository was already present at the target path.
	/// </summary>
	/// <remarks>
	/// <c>git init</c> is idempotent: run against an existing repository it reinitialises and exits
	/// zero, announcing the difference only in prose that this library does not parse. It also
	/// silently ignores <c>--initial-branch</c> on that path, so a caller that asked for a
	/// particular initial branch and got <see langword="true"/> here did not get the branch it
	/// asked for. The value comes from a <c>rev-parse</c> probe taken before <c>init</c> runs.
	/// </remarks>
	public required bool AlreadyExisted { get; init; }
}
```

- [ ] **Step 6: Append `GitNothingToCommitException`**

Add to `GitIntegration/Execution/GitExceptions.cs`, after `GitRepositoryNotFoundException`:

```csharp
/// <summary>
/// <c>git commit</c> was run with nothing staged.
/// </summary>
/// <remarks>
/// Given its own type because it is the one <c>commit</c> failure that is an ordinary program state
/// rather than a fault — a caller that commits on a timer, or after a no-op edit, hits it routinely
/// and wants to carry on. The alternative is matching on exit code 1, which <c>commit</c> shares
/// with other failures, or on git's English prose, which every caller would then have to duplicate.
/// </remarks>
public sealed class GitNothingToCommitException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	public GitNothingToCommitException() { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitNothingToCommitException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitNothingToCommitException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitNothingToCommitException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }
}
```

- [ ] **Step 7: Add the `Progress` seam to the builder base**

In `GitIntegration/Builders/GitCommandBuilder.cs`, add this property after the existing `RepositoryPath` property:

```csharp
	/// <summary>
	/// Gets or sets an optional sink for git's incremental output.
	/// </summary>
	/// <remarks>
	/// Forwarded into every <see cref="GitProcessRequest"/> this builder issues. Only the
	/// long-running verbs have any use for it — <c>clone</c> writes its whole progress stream to
	/// standard error, and Phase 5's <c>fetch</c> and <c>push</c> will do the same — so most
	/// builders leave it null and the request carries no sink at all. The sink may be invoked
	/// concurrently by the standard-output and standard-error readers and must be thread-safe.
	/// </remarks>
	protected IProgress<string>? Progress { get; set; }
```

Then change **both** `ExecuteAsync` and `TryExecuteAsync` so the request they build carries it. Each currently reads:

```csharp
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments() },
			cancellationToken).ConfigureAwait(false);
```

and becomes:

```csharp
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);
```

`using System;` is already present in that file.

- [ ] **Step 8: Write the filesystem fake**

`GitIntegration.Test/Fakes/FakeFileSystemProvider.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.IO.Abstractions;

using ktsu.Essentials;

using Testably.Abstractions.Testing;

/// <summary>
/// Presents an in-memory <see cref="MockFileSystem"/> as an <see cref="IFileSystemProvider"/>.
/// </summary>
/// <remarks>
/// <see cref="IFileSystemProvider"/> is a marker interface over
/// <see cref="System.IO.Abstractions.IFileSystem"/> and adds no members of its own, so this is pure
/// delegation. It exists because <see cref="MockFileSystem"/> implements the base interface but not
/// the marker, and the library's constructors ask for the marker.
/// </remarks>
/// <param name="inner">The in-memory filesystem to delegate to.</param>
internal sealed class FakeFileSystemProvider(MockFileSystem inner) : IFileSystemProvider
{
	public IDirectory Directory => inner.Directory;

	public IDirectoryInfoFactory DirectoryInfo => inner.DirectoryInfo;

	public IDriveInfoFactory DriveInfo => inner.DriveInfo;

	public IFile File => inner.File;

	public IFileInfoFactory FileInfo => inner.FileInfo;

	public IFileStreamFactory FileStream => inner.FileStream;

	public IFileSystemWatcherFactory FileSystemWatcher => inner.FileSystemWatcher;

	public IFileVersionInfoFactory FileVersionInfo => inner.FileVersionInfo;

	public IPath Path => inner.Path;
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FakeFileSystemProviderTests|FullyQualifiedName~GitCommandBuilderProgressTests"`

Expected: PASS, 5 tests.

- [ ] **Step 10: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, 199 pre-existing plus 5 new passing.

- [ ] **Step 11: Commit**

```bash
git add Directory.Packages.props GitIntegration.Test/GitIntegration.Test.csproj GitIntegration/Models GitIntegration/Execution/GitExceptions.cs GitIntegration/Builders/GitCommandBuilder.cs GitIntegration.Test/Fakes GitIntegration.Test/Builders/GitCommandBuilderProgressTests.cs
git commit -m "[minor] Add mutating-verb foundations and the builder progress seam"
```

---

## Task 2: `Add`

The simplest mutating verb, and the one that establishes the shape every other `GitCompleted` verb copies.

**Files:**
- Create: `GitIntegration/Builders/GitAddBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitAddBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` (Task 1); `GitCommandBuilder<TResult>.AppendOperands`, `TestPaths.Root`, `RecordingGitProcessRunner`.
- Produces:
  - `public interface IGitAddBuilder : IGitCommandBuilder<GitCompleted>` with `ForPath(RelativeFilePath) → IGitAddBuilder`, `All() → IGitAddBuilder`, `UpdateTrackedOnly() → IGitAddBuilder`.
  - `internal sealed class GitAddBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitAddBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitAddBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"add",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitAddBuilder all = new(runner, TestPaths.Root);
		_ = all.All();
		CollectionAssert.Contains(all.BuildArguments().ToArray(), "--all");

		GitAddBuilder update = new(runner, TestPaths.Root);
		_ = update.UpdateTrackedOnly();
		CollectionAssert.Contains(update.BuildArguments().ToArray(), "--update");
	}

	[TestMethod]
	public void PutsPathsBehindTheEndOfOptionsMarker()
	{
		// A pathspec is caller-supplied, so a value beginning with a dash would otherwise be read by
		// git as a flag.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[marker + 1]);
	}

	[TestMethod]
	public void AccumulatesPathsAcrossCalls()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("a.txt".As<RelativeFilePath>()).ForPath("b.txt".As<RelativeFilePath>());

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "a.txt".As<RelativeFilePath>().WeakString);
		CollectionAssert.Contains(arguments, "b.txt".As<RelativeFilePath>().WeakString);
	}

	[TestMethod]
	public void EmitsNoEndOfOptionsMarkerWhenNoPathIsGiven()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);
		_ = builder.All();

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		IGitAddBuilder chained = builder.All().UpdateTrackedOnly();

		Assert.AreSame(builder, chained);
	}

	[TestMethod]
	public void RejectsANullPath()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}

	[TestMethod]
	public async Task ExecuteReportsTheArgumentVectorOnSuccessAsync()
	{
		// git add writes nothing useful to standard output, so the result carries the vector rather
		// than a parse of output that does not exist.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);
		_ = builder.All();

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteThrowsWhenAPathspecMatchesNothingAsync()
	{
		// Captured from git 2.50: a pathspec matching no file exits 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: pathspec 'no/such/file.txt' did not match any files\n",
		};
		GitAddBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitAddBuilderTests"`

Expected: compilation failure — `GitAddBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitAddBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Stages changes for the next commit.
/// </summary>
public interface IGitAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Stages this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitAddBuilder ForPath(RelativeFilePath path);

	/// <summary>Stages every change in the working tree, including new and deleted files.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitAddBuilder All();

	/// <summary>
	/// Stages changes to files git already tracks, leaving untracked files alone.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitAddBuilder UpdateTrackedOnly();
}

/// <summary>
/// Builds <c>git add</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitAddBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitAddBuilder
{
	private readonly List<RelativeFilePath> _paths = [];
	private bool _all;
	private bool _updateTrackedOnly;

	/// <inheritdoc />
	public IGitAddBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	public IGitAddBuilder All()
	{
		_all = true;
		return this;
	}

	/// <inheritdoc />
	public IGitAddBuilder UpdateTrackedOnly()
	{
		_updateTrackedOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("add");

		if (_all)
		{
			arguments.Add("--all");
		}

		if (_updateTrackedOnly)
		{
			arguments.Add("--update");
		}

		if (_paths.Count > 0)
		{
			// Pathspecs are relative to the -C directory, which GitClient always resolves to the
			// working-tree root, so a RelativeFilePath maps onto one with no translation.
			string[] operands = new string[_paths.Count];

			for (int index = 0; index < _paths.Count; index++)
			{
				operands[index] = _paths[index].WeakString;
			}

			AppendOperands(arguments, operands);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitAddBuilderTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitAddBuilder.cs GitIntegration.Test/Builders/GitAddBuilderTests.cs
git commit -m "[minor] Add the add verb builder"
```

---
## Task 3: Branch creation and deletion

Two builders in one task: they are the same shape, operate on the same object, and a reviewer judging one is judging both.

**Files:**
- Create: `GitIntegration/Builders/GitBranchCreateBuilder.cs`
- Create: `GitIntegration/Builders/GitBranchDeleteBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitBranchWriteBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` (Task 1); `GitBranchName`, `GitRefName`, `AppendOperands`.
- Produces:
  - `public interface IGitBranchCreateBuilder : IGitCommandBuilder<GitCompleted>` with `StartingAt(GitRefName) → IGitBranchCreateBuilder` and `Force() → IGitBranchCreateBuilder`.
  - `internal sealed class GitBranchCreateBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitBranchName name)`.
  - `public interface IGitBranchDeleteBuilder : IGitCommandBuilder<GitCompleted>` with `Force() → IGitBranchDeleteBuilder`.
  - `internal sealed class GitBranchDeleteBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitBranchName name)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitBranchWriteBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitBranchWriteBuilderTests
{
	private static GitBranchName Feature => "feature/x".As<GitBranchName>();

	[TestMethod]
	public void BuildsTheBranchCreateVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"branch",
			"--end-of-options",
			"feature/x",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void PutsTheStartPointAfterTheBranchName()
	{
		// git branch <name> [<start-point>] is positional: reversing them creates a branch with the
		// wrong name pointing at the wrong place, and git reports no error.
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.StartingAt("main".As<GitRefName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("feature/x", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void MapsForceOnCreateToTheResetFlag()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.Force();

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "--force");
	}

	[TestMethod]
	public void BuildsTheBranchDeleteVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"branch",
			"--delete",
			"--end-of-options",
			"feature/x",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsForceOnDeleteToTheForceFlag()
	{
		// --delete --force is the long form of -D, which deletes a branch that is not fully merged.
		RecordingGitProcessRunner runner = new();
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.Force();

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--delete");
		CollectionAssert.Contains(arguments, "--force");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();

		GitBranchCreateBuilder create = new(runner, TestPaths.Root, Feature);
		Assert.AreSame(create, create.Force().StartingAt("main".As<GitRefName>()));

		GitBranchDeleteBuilder delete = new(runner, TestPaths.Root, Feature);
		Assert.AreSame(delete, delete.Force());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitBranchCreateBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitBranchDeleteBuilder(runner, TestPaths.Root, null!));

		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.StartingAt(null!));
	}

	[TestMethod]
	public async Task CreateThrowsWhenTheBranchAlreadyExistsAsync()
	{
		// Captured from git 2.50: creating a duplicate branch exits 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: a branch named 'feature/x' already exists\n",
		};
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task DeleteReportsAnUnmergedBranchAsAFailureAsync()
	{
		// Captured from git 2.50: deleting an unmerged branch exits 1, not 128 — a caller probing
		// for this must not assume every git failure is 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardError = "error: the branch 'feature/x' is not fully merged\n",
		};
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchWriteBuilderTests"`

Expected: compilation failure — neither builder exists.

- [ ] **Step 3: Write the create builder**

`GitIntegration/Builders/GitBranchCreateBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates a branch.
/// </summary>
public interface IGitBranchCreateBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Points the new branch at this revision instead of at HEAD.
	/// </summary>
	/// <param name="startPoint">The revision the branch should start from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="startPoint"/> is <see langword="null"/>.</exception>
	public IGitBranchCreateBuilder StartingAt(GitRefName startPoint);

	/// <summary>
	/// Resets the branch to the start point if it already exists, instead of failing.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchCreateBuilder Force();
}

/// <summary>
/// Builds <c>git branch &lt;name&gt; [&lt;start-point&gt;]</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The branch to create.</param>
internal sealed class GitBranchCreateBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitBranchName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitBranchCreateBuilder
{
	private readonly GitBranchName _name = Ensure.NotNull(name);
	private GitRefName? _startPoint;
	private bool _force;

	/// <inheritdoc />
	public IGitBranchCreateBuilder StartingAt(GitRefName startPoint)
	{
		_startPoint = Ensure.NotNull(startPoint);
		return this;
	}

	/// <inheritdoc />
	public IGitBranchCreateBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("branch");

		if (_force)
		{
			arguments.Add("--force");
		}

		// Order is load-bearing: git branch takes <name> then an optional <start-point>
		// positionally, and swapping them creates a differently-named branch without complaint.
		if (_startPoint is null)
		{
			AppendOperands(arguments, _name.WeakString);
		}
		else
		{
			AppendOperands(arguments, _name.WeakString, _startPoint.WeakString);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 4: Write the delete builder**

`GitIntegration/Builders/GitBranchDeleteBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Deletes a branch.
/// </summary>
public interface IGitBranchDeleteBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Deletes the branch even when it has commits no other branch contains.
	/// </summary>
	/// <remarks>
	/// Without this, git refuses to delete an unmerged branch and exits with code 1. With it, the
	/// commits on that branch become unreachable and are eventually garbage collected.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchDeleteBuilder Force();
}

/// <summary>
/// Builds <c>git branch --delete &lt;name&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The branch to delete.</param>
internal sealed class GitBranchDeleteBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitBranchName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitBranchDeleteBuilder
{
	private readonly GitBranchName _name = Ensure.NotNull(name);
	private bool _force;

	/// <inheritdoc />
	public IGitBranchDeleteBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("branch");

		// Long forms throughout: --delete --force is -D, and spelling it out keeps the vector
		// readable when it is copied out of a GitCommandException and rerun by hand.
		arguments.Add("--delete");

		if (_force)
		{
			arguments.Add("--force");
		}

		AppendOperands(arguments, _name.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchWriteBuilderTests"`

Expected: PASS, 9 tests.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Builders/GitBranchCreateBuilder.cs GitIntegration/Builders/GitBranchDeleteBuilder.cs GitIntegration.Test/Builders/GitBranchWriteBuilderTests.cs
git commit -m "[minor] Add the branch create and delete verbs"
```

---

## Task 4: `Checkout`

**Files:**
- Create: `GitIntegration/Builders/GitCheckoutBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitCheckoutBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` (Task 1); `GitRefName`, `AppendOperands`.
- Produces:
  - `public interface IGitCheckoutBuilder : IGitCommandBuilder<GitCompleted>` with `CreatingBranch() → IGitCheckoutBuilder`, `Force() → IGitCheckoutBuilder`, `Detach() → IGitCheckoutBuilder`.
  - `internal sealed class GitCheckoutBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitRefName target)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitCheckoutBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitCheckoutBuilderTests
{
	private static GitRefName Main => "main".As<GitRefName>();

	[TestMethod]
	public void BuildsTheDefaultCheckoutVector()
	{
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"checkout",
			"--end-of-options",
			"main",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitCheckoutBuilder creating = new(runner, TestPaths.Root, Main);
		_ = creating.CreatingBranch();
		CollectionAssert.Contains(creating.BuildArguments().ToArray(), "-b");

		GitCheckoutBuilder forced = new(runner, TestPaths.Root, Main);
		_ = forced.Force();
		CollectionAssert.Contains(forced.BuildArguments().ToArray(), "--force");

		GitCheckoutBuilder detached = new(runner, TestPaths.Root, Main);
		_ = detached.Detach();
		CollectionAssert.Contains(detached.BuildArguments().ToArray(), "--detach");
	}

	[TestMethod]
	public void KeepsFlagsBeforeTheEndOfOptionsMarker()
	{
		// Anything after --end-of-options is an operand, so a flag emitted there would be handed to
		// git as a ref name.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		_ = builder.CreatingBranch().Force();

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "-b") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--force") < marker);
		Assert.AreEqual("main", arguments[marker + 1]);
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		Assert.AreSame(builder, builder.CreatingBranch().Force().Detach());
	}

	[TestMethod]
	public void RejectsANullTarget()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCheckoutBuilder(runner, TestPaths.Root, null!));
	}

	[TestMethod]
	public async Task ExecuteSucceedsEvenThoughGitReportsOnStandardErrorAsync()
	{
		// git checkout writes "Switched to branch 'x'" to standard error and exits 0. A builder that
		// treated non-empty stderr as failure would reject every successful checkout.
		RecordingGitProcessRunner runner = new() { StandardError = "Switched to branch 'main'\n" };
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteThrowsForAnUnknownRefAsync()
	{
		// Captured from git 2.50: an unresolvable checkout target exits 1, not 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardError = "error: pathspec 'no-such' did not match any file(s) known to git\n",
		};
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, "no-such".As<GitRefName>());

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitCheckoutBuilderTests"`

Expected: compilation failure — `GitCheckoutBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitCheckoutBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Switches the working tree to a different branch, tag, or commit.
/// </summary>
/// <remarks>
/// <c>checkout</c> rather than the newer <c>switch</c>, because the target is a
/// <see cref="GitRefName"/> — which may be a branch, a tag, or an object id — and <c>switch</c>
/// handles only branches.
/// </remarks>
public interface IGitCheckoutBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Creates the target as a new branch at the current HEAD and switches to it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder CreatingBranch();

	/// <summary>
	/// Switches even when it would discard uncommitted changes in the working tree.
	/// </summary>
	/// <remarks>Local modifications to files that differ between the two trees are lost.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Force();

	/// <summary>Checks the target out as a detached HEAD rather than switching to a branch.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Detach();
}

/// <summary>
/// Builds <c>git checkout</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="target">The branch, tag, or commit to switch to.</param>
internal sealed class GitCheckoutBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName target)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitCheckoutBuilder
{
	private readonly GitRefName _target = Ensure.NotNull(target);
	private bool _creatingBranch;
	private bool _force;
	private bool _detach;

	/// <inheritdoc />
	public IGitCheckoutBuilder CreatingBranch()
	{
		_creatingBranch = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Detach()
	{
		_detach = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("checkout");

		// -b has no long form, unlike every other flag this library emits.
		if (_creatingBranch)
		{
			arguments.Add("-b");
		}

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_detach)
		{
			arguments.Add("--detach");
		}

		// Every flag must precede the marker: anything after it is an operand, so a flag emitted
		// there would reach git as a ref name.
		AppendOperands(arguments, _target.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitCheckoutBuilderTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitCheckoutBuilder.cs GitIntegration.Test/Builders/GitCheckoutBuilderTests.cs
git commit -m "[minor] Add the checkout verb builder"
```

---

## Task 5: The three remote-write verbs

`remote add`, `remote remove`, and `remote set-url` in one task. Each is a handful of lines with no options worth separating, and they are only meaningful as a set.

**Files:**
- Create: `GitIntegration/Builders/GitRemoteAddBuilder.cs`
- Create: `GitIntegration/Builders/GitRemoteRemoveBuilder.cs`
- Create: `GitIntegration/Builders/GitRemoteSetUrlBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitRemoteWriteBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` (Task 1); `GitRemoteName`, `GitRepositoryRemotePath`, `AppendOperands`.
- Produces:
  - `public interface IGitRemoteAddBuilder : IGitCommandBuilder<GitCompleted>` with `WithFetch() → IGitRemoteAddBuilder`.
  - `public interface IGitRemoteRemoveBuilder : IGitCommandBuilder<GitCompleted>` (no members).
  - `public interface IGitRemoteSetUrlBuilder : IGitCommandBuilder<GitCompleted>` with `ForPushOnly() → IGitRemoteSetUrlBuilder`.
  - Three `internal sealed` builders, each taking `(IGitProcessRunner, AbsoluteDirectoryPath, GitRemoteName)` plus a `GitRepositoryRemotePath` for add and set-url.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitRemoteWriteBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteWriteBuilderTests
{
	private static GitRemoteName Origin => "origin".As<GitRemoteName>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static string[] Prefix =>
	[
		"-C", TestPaths.Root.WeakString,
		"--no-pager",
		"-c", "core.quotepath=false",
		"-c", "color.ui=false",
		"remote",
	];

	[TestMethod]
	public void BuildsTheRemoteAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] expectedArguments =
		[
			.. Prefix,
			"add",
			"--end-of-options",
			"origin",
			"https://example.com/repo.git",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheRemoteRemoveVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteRemoveBuilder builder = new(runner, TestPaths.Root, Origin);

		string[] expectedArguments =
		[
			.. Prefix,
			"remove",
			"--end-of-options",
			"origin",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheRemoteSetUrlVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteSetUrlBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] expectedArguments =
		[
			.. Prefix,
			"set-url",
			"--end-of-options",
			"origin",
			"https://example.com/repo.git",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void SetUrlForPushOnlyEmitsThePushFlagBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteSetUrlBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		_ = builder.ForPushOnly();

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--push") < marker);
	}

	[TestMethod]
	public void RemoteAddWithFetchEmitsTheFetchFlag()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		_ = builder.WithFetch();

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "-f");
	}

	[TestMethod]
	public void NameComesBeforeUrl()
	{
		// git remote add <name> <url> is positional, and reversing them produces a remote named
		// after the URL pointing at a path named after the remote.
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("https://example.com/repo.git", arguments[marker + 2]);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteAddBuilder(runner, TestPaths.Root, null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteAddBuilder(runner, TestPaths.Root, Origin, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteRemoveBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteSetUrlBuilder(runner, TestPaths.Root, null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteSetUrlBuilder(runner, TestPaths.Root, Origin, null!));
	}

	[TestMethod]
	public async Task RemoteAddReportsADuplicateAsExitCodeThreeAsync()
	{
		// Captured from git 2.50. The remote commands use exit codes 2 and 3, unlike the 1 and 128
		// seen elsewhere, which is why nothing in this library keys off a particular code.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 3,
			StandardError = "error: remote origin already exists.\n",
		};
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(3, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task RemoteRemoveReportsAMissingRemoteAsExitCodeTwoAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 2,
			StandardError = "error: No such remote: 'origin'\n",
		};
		GitRemoteRemoveBuilder builder = new(runner, TestPaths.Root, Origin);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(2, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteWriteBuilderTests"`

Expected: compilation failure — none of the three builders exist.

- [ ] **Step 3: Write `GitRemoteAddBuilder`**

`GitIntegration/Builders/GitRemoteAddBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Adds a remote.
/// </summary>
public interface IGitRemoteAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Fetches from the remote immediately after adding it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitRemoteAddBuilder WithFetch();
}

/// <summary>
/// Builds <c>git remote add &lt;name&gt; &lt;url&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to add.</param>
/// <param name="url">The URL the remote points at.</param>
internal sealed class GitRemoteAddBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name,
	GitRepositoryRemotePath url)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteAddBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);
	private readonly GitRepositoryRemotePath _url = Ensure.NotNull(url);
	private bool _withFetch;

	/// <inheritdoc />
	public IGitRemoteAddBuilder WithFetch()
	{
		_withFetch = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("add");

		// -f has no long form on this subcommand.
		if (_withFetch)
		{
			arguments.Add("-f");
		}

		// Both operands are caller-supplied, and the URL especially so: a remote path beginning
		// with a dash is the option-injection case NotAnOptionAttribute and this marker both guard.
		AppendOperands(arguments, _name.WeakString, _url.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 4: Write `GitRemoteRemoveBuilder`**

`GitIntegration/Builders/GitRemoteRemoveBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a remote and every remote-tracking branch belonging to it.
/// </summary>
public interface IGitRemoteRemoveBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git remote remove &lt;name&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to remove.</param>
internal sealed class GitRemoteRemoveBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteRemoveBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("remove");

		AppendOperands(arguments, _name.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 5: Write `GitRemoteSetUrlBuilder`**

`GitIntegration/Builders/GitRemoteSetUrlBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Changes the URL a remote points at.
/// </summary>
public interface IGitRemoteSetUrlBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Sets only the push URL, leaving the fetch URL as it was.
	/// </summary>
	/// <remarks>
	/// This is what makes <see cref="GitRemote.FetchUrl"/> and <see cref="GitRemote.PushUrl"/>
	/// differ — a repository that fetches over HTTPS but pushes over SSH, for instance.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitRemoteSetUrlBuilder ForPushOnly();
}

/// <summary>
/// Builds <c>git remote set-url &lt;name&gt; &lt;url&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to change.</param>
/// <param name="url">The URL to set.</param>
internal sealed class GitRemoteSetUrlBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name,
	GitRepositoryRemotePath url)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteSetUrlBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);
	private readonly GitRepositoryRemotePath _url = Ensure.NotNull(url);
	private bool _forPushOnly;

	/// <inheritdoc />
	public IGitRemoteSetUrlBuilder ForPushOnly()
	{
		_forPushOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("set-url");

		if (_forPushOnly)
		{
			arguments.Add("--push");
		}

		AppendOperands(arguments, _name.WeakString, _url.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteWriteBuilderTests"`

Expected: PASS, 9 tests.

- [ ] **Step 7: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 8: Commit**

```bash
git add GitIntegration/Builders/GitRemoteAddBuilder.cs GitIntegration/Builders/GitRemoteRemoveBuilder.cs GitIntegration/Builders/GitRemoteSetUrlBuilder.cs GitIntegration.Test/Builders/GitRemoteWriteBuilderTests.cs
git commit -m "[minor] Add the remote add, remove, and set-url verbs"
```

---
## Task 6: `Commit`

The hardest verb in the phase. It needs two invocations, and it is the only one whose interesting failure is reported on **standard output** — which the base class's failure classifier does not read.

**Files:**
- Create: `GitIntegration/Builders/GitCommitBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitCommitBuilderTests.cs`

**Interfaces:**
- Consumes: `GitNothingToCommitException` (Task 1); `GitCommit`, `GitLogBuilder`, `GitLogParser`, `GitCommitMessage`, `GitAuthorName`, `GitAuthorEmail` (all Phase 3 or earlier); `ScriptedGitProcessRunner` (existing test fake, multi-response).
- Produces:
  - `public interface IGitCommitBuilder : IGitCommandBuilder<GitCommit>` with `WithBody(string) → IGitCommitBuilder`, `AllowEmpty() → IGitCommitBuilder`, `StageTrackedFiles() → IGitCommitBuilder`, `WithAuthor(GitAuthorName, GitAuthorEmail) → IGitCommitBuilder`.
  - `internal sealed class GitCommitBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitCommitMessage message)`.

### Why two invocations, and how they are wired

`git commit` prints `[main (root-commit) 6b93c10] first commit` — a human summary with an abbreviated sha and no author, date, or tree. There is no porcelain form. So the builder commits, then reads the new commit back with the same pinned `log -1` format Phase 3 already uses.

The readback **reuses `GitLogBuilder` for its argument vector only**. That keeps the format string in exactly one place (`GitOutputFormats.LogFormat`) rather than duplicating it here, while letting this builder run the vector through the same runner and hand the output to its own `ParseResult`. Nothing is duplicated and nothing is dead code.

### The `CreateException` override reads standard output

`GitCommandBuilder<TResult>.CreateException` inspects `result.StandardError`. Git reports "nothing to commit" on **standard output**, leaving stderr empty, so the base classifier can never see it. The override checks stdout for **both** phrases — `nothing to commit` (clean tree) and `nothing added to commit` (untracked files present). Neither contains the other, so matching one alone misses half the cases.

**Amend is deliberately not offered.** It rewrites history, and the spec defers history-rewriting operations. Adding it later is a one-flag change.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitCommitBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitCommitBuilderTests
{
	private const string Nul = "\u0000";
	private const string Us = "\u001f";

	private const string Sha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string Tree = "f3758b7757b1f9bfe8c8e05fc5ac51bf3650c7d5";
	private const string Parent = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";

	/// <summary>One record in the pinned log format, as the readback invocation returns it.</summary>
	private const string ReadBack =
		Sha + Us + Tree + Us + Parent + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"subject here" + Us + "body text" + Nul;

	private static GitCommitMessage Message => "subject here".As<GitCommitMessage>();

	[TestMethod]
	public void BuildsTheDefaultCommitVector()
	{
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"commit",
			"--message", "subject here",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheBodyAsASecondMessage()
	{
		// git joins repeated --message values with a blank line, which is exactly the subject/body
		// convention, so the body needs no manual newline handling.
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = builder.WithBody("body text");

		string[] arguments = builder.BuildArguments().ToArray();
		int subject = Array.IndexOf(arguments, "subject here");

		Assert.AreEqual("--message", arguments[subject + 1]);
		Assert.AreEqual("body text", arguments[subject + 2]);
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitCommitBuilder empty = new(runner, TestPaths.Root, Message);
		_ = empty.AllowEmpty();
		CollectionAssert.Contains(empty.BuildArguments().ToArray(), "--allow-empty");

		GitCommitBuilder staged = new(runner, TestPaths.Root, Message);
		_ = staged.StageTrackedFiles();
		CollectionAssert.Contains(staged.BuildArguments().ToArray(), "--all");
	}

	[TestMethod]
	public void FormatsTheAuthorOverrideAsGitExpects()
	{
		// --author takes a single "Name <email>" string. Splitting it into two arguments makes git
		// treat the second as a pathspec.
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = builder.WithAuthor("Other Name".As<GitAuthorName>(), "other@example.com".As<GitAuthorEmail>());

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "--author=Other Name <other@example.com>");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		Assert.AreSame(builder, builder.WithBody("b").AllowEmpty().StageTrackedFiles());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCommitBuilder(runner, TestPaths.Root, null!));

		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBody(null!));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = builder.WithAuthor(null!, "e@example.com".As<GitAuthorEmail>()));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = builder.WithAuthor("N".As<GitAuthorName>(), null!));
	}

	[TestMethod]
	public async Task ExecuteRunsCommitThenReadsTheCommitBackAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n 1 file changed, 1 insertion(+)\n")
			.Then(standardOutput: ReadBack);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitCommit commit = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Sha.As<GitCommitSha>(), commit.Sha);
		Assert.AreEqual(Tree.As<GitCommitSha>(), commit.TreeSha);
		Assert.AreEqual("subject here", commit.Subject);
		Assert.AreEqual("body text", commit.Body);

		// Two invocations, in order: the commit, then the readback.
		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "commit");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "log");
	}

	[TestMethod]
	public async Task TheReadBackUsesThePinnedLogFormatAndAsksForOneCommitAsync()
	{
		// Asserted literally so a change to the shared format constant fails here rather than
		// silently returning a differently-shaped commit.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n")
			.Then(standardOutput: ReadBack);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] readBack = [.. runner.Invocations[1]];
		CollectionAssert.Contains(
			readBack,
			"--format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b");
		CollectionAssert.Contains(readBack, "--max-count=1");
		CollectionAssert.Contains(readBack, "-z");
	}

	[TestMethod]
	public async Task ThrowsNothingToCommitWhenTheTreeIsCleanAsync()
	{
		// Captured from git 2.50: the message is on STANDARD OUTPUT with stderr empty, and the exit
		// code is 1. The base class's classifier only reads stderr, which is why this builder
		// overrides CreateException.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "On branch main\nnothing to commit, working tree clean\n", exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitNothingToCommitException exception = await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public async Task ThrowsNothingToCommitWhenOnlyUntrackedFilesArePresentAsync()
	{
		// The second of git's two phrases. It does not contain the first, so a classifier matching
		// only "nothing to commit" would miss this case entirely.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(
				standardOutput:
					"On branch main\nUntracked files:\n\tnew.txt\n\n" +
					"nothing added to commit but untracked files present (use \"git add\" to track)\n",
				exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnOrdinaryCommitFailureStaysAGenericCommandExceptionAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: unable to write new index file\n", exitCode: 128);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsNothingToCommitAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "nothing to commit, working tree clean\n", exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitResult<GitCommit> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);

		// The readback must not run when the commit itself failed.
		Assert.AreEqual(1, runner.Invocations.Count);
	}

	[TestMethod]
	public async Task ReportsAnEmptyReadBackAsAParseFailureAsync()
	{
		// git said the commit succeeded but log returned nothing. That is a parse problem, not a
		// command problem, and must not masquerade as GitCommandException.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n")
			.Then(standardOutput: string.Empty);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

**Before writing this file, read the "Known tooling hazard" note in `constraints.md`** — the fixture embeds NUL and the unit separator, and a file-writing tool has repeatedly turned those escapes into raw control bytes. Run the byte check afterwards.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitCommitBuilderTests"`

Expected: compilation failure — `GitCommitBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitCommitBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Records the staged changes as a new commit.
/// </summary>
public interface IGitCommitBuilder : IGitCommandBuilder<GitCommit>
{
	/// <summary>
	/// Adds a body below the subject, separated from it by a blank line.
	/// </summary>
	/// <param name="body">The body text.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
	public IGitCommitBuilder WithBody(string body);

	/// <summary>
	/// Records a commit even when nothing is staged.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCommitBuilder AllowEmpty();

	/// <summary>
	/// Stages every modified and deleted tracked file first, leaving untracked files alone.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCommitBuilder StageTrackedFiles();

	/// <summary>
	/// Records a different author than the committer.
	/// </summary>
	/// <remarks>
	/// This changes the author only. The committer still comes from git's configuration, which is
	/// how git distinguishes who wrote a change from who applied it.
	/// </remarks>
	/// <param name="name">The author's name.</param>
	/// <param name="email">The author's email address.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="email"/> is <see langword="null"/>.
	/// </exception>
	public IGitCommitBuilder WithAuthor(GitAuthorName name, GitAuthorEmail email);
}

/// <summary>
/// Builds <c>git commit</c>, then reads the resulting commit back.
/// </summary>
/// <remarks>
/// One of two verbs in this library that needs two invocations. <c>git commit</c> prints a human
/// summary — <c>[main (root-commit) 6b93c10] first commit</c> — carrying an abbreviated object id
/// and nothing else, and offers no machine-readable alternative. So the commit is followed by a
/// <c>log -1</c> using the same pinned format every other commit in this library is parsed from.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="repositoryPath">The repository to scope the commands to.</param>
/// <param name="message">The commit subject.</param>
internal sealed class GitCommitBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitCommitMessage message)
	: GitCommandBuilder<GitCommit>(runner, repositoryPath), IGitCommitBuilder
{
	// Held separately from the base's nullable RepositoryPath because the readback constructs a
	// GitLogBuilder, which requires a non-null path. Commit is always repository-scoped.
	private readonly AbsoluteDirectoryPath _repositoryPath = Ensure.NotNull(repositoryPath);
	private readonly GitCommitMessage _message = Ensure.NotNull(message);
	private string? _body;
	private string? _author;
	private bool _allowEmpty;
	private bool _stageTrackedFiles;

	/// <inheritdoc />
	public IGitCommitBuilder WithBody(string body)
	{
		_body = Ensure.NotNull(body);
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder AllowEmpty()
	{
		_allowEmpty = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder StageTrackedFiles()
	{
		_stageTrackedFiles = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder WithAuthor(GitAuthorName name, GitAuthorEmail email)
	{
		// git parses a single "Name <email>" string here; passing two arguments would make it read
		// the second as a pathspec.
		_author = $"{Ensure.NotNull(name).WeakString} <{Ensure.NotNull(email).WeakString}>";
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("commit");

		if (_stageTrackedFiles)
		{
			arguments.Add("--all");
		}

		if (_allowEmpty)
		{
			arguments.Add("--allow-empty");
		}

		if (_author is not null)
		{
			arguments.Add("--author=" + _author);
		}

		// --message rather than -m, and repeated for the body: git joins repeated values with a
		// blank line between them, which is precisely the subject-then-body convention.
		arguments.Add("--message");
		arguments.Add(_message.WeakString);

		if (_body is not null)
		{
			arguments.Add("--message");
			arguments.Add(_body);
		}
	}

	/// <summary>
	/// Turns the readback invocation's output into the committed <see cref="GitCommit"/>.
	/// </summary>
	/// <remarks>
	/// Called with the output of the <c>log -1</c> readback, not of the commit itself — the base
	/// class never invokes it here, because <see cref="ExecuteAsync"/> is overridden.
	/// </remarks>
	/// <param name="result">The readback invocation's outcome.</param>
	/// <returns>The commit that was just recorded.</returns>
	/// <exception cref="GitParseException">The readback returned no commit.</exception>
	protected override GitCommit ParseResult(GitProcessResult result)
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(Ensure.NotNull(result).StandardOutput);

		return commits.Count > 0
			? commits[0]
			: throw new GitParseException(
				"git reported a successful commit but reading it back returned no commit.");
	}

	/// <inheritdoc />
	public override async Task<GitCommit> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return await ReadBackAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCommit>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		// The readback is skipped entirely when the commit failed — there is nothing to read back,
		// and running it would report the previous commit as though it were this one.
		return result.Success
			? GitResult<GitCommit>.FromValue(await ReadBackAsync(cancellationToken).ConfigureAwait(false))
			: GitResult<GitCommit>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	/// <summary>
	/// Classifies a failed commit, recognising the one failure that is an ordinary program state.
	/// </summary>
	/// <remarks>
	/// Overridden because the base class inspects standard error and git reports "nothing to
	/// commit" on standard <em>output</em>, leaving standard error empty. Both of git's phrasings
	/// are matched: the tree may be clean, or it may hold only untracked files, and neither message
	/// contains the other. The match depends on the <c>LC_ALL=C</c> that
	/// <c>RunCommandGitProcessRunner</c> forces on every invocation.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		if (result.StandardOutput.Contains("nothing to commit", StringComparison.Ordinal) ||
			result.StandardOutput.Contains("nothing added to commit", StringComparison.Ordinal))
		{
			return new GitNothingToCommitException(
				$"There is nothing staged to commit: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError);
		}

		return base.CreateException(result);
	}

	private async Task<GitCommit> ReadBackAsync(CancellationToken cancellationToken)
	{
		// GitLogBuilder is used for its argument vector only, so the pinned format string stays in
		// exactly one place. Running it here rather than calling its ExecuteAsync keeps the "no
		// commit came back" failure attributable to the commit, not to a stray log call.
		GitLogBuilder log = new(Runner, _repositoryPath);
		_ = log.Take(1);

		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = log.BuildArguments() },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw base.CreateException(result);
		}

		return ParseResult(result);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitCommitBuilderTests"`

Expected: PASS, 13 tests.

- [ ] **Step 5: Verify the fixture bytes**

Run the byte check from `constraints.md` against `GitIntegration.Test/Builders/GitCommitBuilderTests.cs`. Only newline (0x0A) and tab (0x09) may appear.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Builders/GitCommitBuilder.cs GitIntegration.Test/Builders/GitCommitBuilderTests.cs
git commit -m "[minor] Add the commit verb with its log readback"
```

---
## Task 7: `Init`

The second two-invocation verb, and the first consumer of `IFileSystemProvider`.

**Files:**
- Create: `GitIntegration/Builders/GitInitBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitInitBuilderTests.cs`

**Interfaces:**
- Consumes: `GitInitResult` (Task 1); `GitTextBuilder` (Phase 3, internal); `GitBranchName`; `ScriptedGitProcessRunner`.
- Produces:
  - `public interface IGitInitBuilder : IGitCommandBuilder<GitInitResult>` with `Bare() → IGitInitBuilder` and `WithInitialBranch(GitBranchName) → IGitInitBuilder`.
  - `internal sealed class GitInitBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath targetPath)`.

### Why the probe comes first

`git init` against an existing repository reinitialises it, exits 0, and says so only in prose — and **silently ignores `--initial-branch` on that path**. A caller who asked for `main` and got an existing repository on `master` has no way to tell from the exit code. So the builder runs `rev-parse --is-inside-work-tree` against the target first and reports the answer as `GitInitResult.AlreadyExisted`.

The probe is expected to fail when the directory is not a repository — or does not exist at all — so it uses `TryExecuteAsync` and treats any non-zero exit as "no repository here".

**`init` is not repository-scoped in the usual sense.** The target may not be a repository yet, so `-C` cannot point at it: git would fail with `cannot change to '<path>'` before doing anything. The path is therefore passed as an **operand** to `init`, and the builder passes `repositoryPath: null` to the base so no `-C` is emitted.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitInitBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitInitBuilderTests
{
	private static AbsoluteDirectoryPath Target =>
		(OperatingSystem.IsWindows() ? @"C:\dev\new-repo" : "/dev/new-repo").As<AbsoluteDirectoryPath>();

	[TestMethod]
	public void BuildsTheInitVectorWithoutRepositoryScoping()
	{
		// No -C: the target is not a repository yet, so git would fail trying to change into it
		// before doing any work. The path is an operand instead.
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"init",
			"--end-of-options",
			Target.WeakString,
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlagsBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		_ = builder.Bare().WithInitialBranch("main".As<GitBranchName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--bare") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--initial-branch=main") < marker);
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		Assert.AreSame(builder, builder.Bare().WithInitialBranch("main".As<GitBranchName>()));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitInitBuilder(runner, null!));

		GitInitBuilder builder = new(runner, Target);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithInitialBranch(null!));
	}

	[TestMethod]
	public async Task ProbesBeforeInitialisingAndReportsAFreshRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository (or any of the parent directories): .git\n", exitCode: 128)
			.Then(standardOutput: "Initialized empty Git repository in /dev/new-repo/.git/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.AlreadyExisted);
		Assert.AreEqual(Target, result.Repository.LocalPath);
		Assert.IsNotNull(result.Repository.ProcessRunner);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--is-inside-work-tree");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "init");
	}

	[TestMethod]
	public async Task ReportsAnExistingRepositoryAsAlreadyExistingAsync()
	{
		// git init on an existing repository exits 0 and only says "Reinitialized" in prose, so the
		// probe is the sole machine-readable signal.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "true\n")
			.Then(standardOutput: "Reinitialized existing Git repository in /dev/new-repo/.git/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.AlreadyExisted);
	}

	[TestMethod]
	public async Task StillRunsInitWhenTheRepositoryAlreadyExistsAsync()
	{
		// The probe reports, it does not gate: git init is idempotent and running it is harmless.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "true\n")
			.Then(standardOutput: "Reinitialized existing Git repository\n");
		GitInitBuilder builder = new(runner, Target);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "init");
	}

	[TestMethod]
	public async Task ThrowsWhenInitItselfFailsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardError: "fatal: cannot mkdir /dev/new-repo: Permission denied\n", exitCode: 128);
		GitInitBuilder builder = new(runner, Target);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsAnInitFailureAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardError: "fatal: cannot mkdir: Permission denied\n", exitCode: 128);
		GitInitBuilder builder = new(runner, Target);

		GitResult<GitInitResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitInitBuilderTests"`

Expected: compilation failure — `GitInitBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitInitBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates a repository.
/// </summary>
public interface IGitInitBuilder : IGitCommandBuilder<GitInitResult>
{
	/// <summary>Creates a repository with no working tree.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitInitBuilder Bare();

	/// <summary>
	/// Names the branch the repository starts on instead of taking git's configured default.
	/// </summary>
	/// <remarks>
	/// Ignored by git when the repository already exists, which is why
	/// <see cref="GitInitResult.AlreadyExisted"/> is worth checking after asking for one.
	/// </remarks>
	/// <param name="name">The initial branch name.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitInitBuilder WithInitialBranch(GitBranchName name);
}

/// <summary>
/// Builds <c>git init</c>, preceded by a probe for an existing repository.
/// </summary>
/// <remarks>
/// The target is passed as an operand rather than through <c>-C</c>, because <c>-C</c> requires the
/// directory to exist and <c>init</c> is frequently the thing that creates it.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="targetPath">Where the repository should be.</param>
internal sealed class GitInitBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath targetPath)
	: GitCommandBuilder<GitInitResult>(runner, repositoryPath: null), IGitInitBuilder
{
	private readonly AbsoluteDirectoryPath _targetPath = Ensure.NotNull(targetPath);
	private GitBranchName? _initialBranch;
	private bool _bare;
	private bool _alreadyExisted;

	/// <inheritdoc />
	public IGitInitBuilder Bare()
	{
		_bare = true;
		return this;
	}

	/// <inheritdoc />
	public IGitInitBuilder WithInitialBranch(GitBranchName name)
	{
		_initialBranch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("init");

		if (_bare)
		{
			arguments.Add("--bare");
		}

		if (_initialBranch is not null)
		{
			arguments.Add("--initial-branch=" + _initialBranch.WeakString);
		}

		AppendOperands(arguments, _targetPath.WeakString);
	}

	/// <summary>
	/// Builds the result, taking the already-existed answer from the probe run just before.
	/// </summary>
	/// <remarks>
	/// The probe's answer arrives through a field rather than through <paramref name="result"/>,
	/// because it is not in <c>init</c>'s output — it cannot be, which is the whole reason the probe
	/// exists. Holding it in a field is safe: a builder is single-use and not thread-safe by
	/// contract, and both entry points set it immediately before delegating to the base.
	/// </remarks>
	/// <param name="result">The invocation outcome, which carries nothing this result needs.</param>
	/// <returns>The initialised repository and whether it was already there.</returns>
	protected override GitInitResult ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return new GitInitResult
		{
			Repository = new GitRepository
			{
				LocalPath = _targetPath,
				ProcessRunner = Runner,
			},
			AlreadyExisted = _alreadyExisted,
		};
	}

	/// <inheritdoc />
	public override async Task<GitInitResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		// Probe first, then let the base run init and call ParseResult exactly as it does for every
		// other verb. Reimplementing the run-and-classify flow here would duplicate the base's
		// failure handling for no gain.
		_alreadyExisted = await ProbeAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitInitResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		_alreadyExisted = await ProbeAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
	{
		// TryExecuteAsync, because failure is the expected answer: the directory may hold no
		// repository, or may not exist at all, and both exit 128 and both mean "not yet".
		GitResult<string> probe = await new GitTextBuilder(Runner, _targetPath, "rev-parse", "--is-inside-work-tree")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return probe.Success && string.Equals(probe.Value, "true", StringComparison.Ordinal);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitInitBuilderTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitInitBuilder.cs GitIntegration.Test/Builders/GitInitBuilderTests.cs
git commit -m "[minor] Add the init verb with its existing-repository probe"
```

---

## Task 8: `Clone`

The only verb that takes a filesystem dependency, and the only one where progress reporting matters.

**Files:**
- Create: `GitIntegration/Builders/GitCloneBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitCloneBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` is **not** used here — clone returns `GitRepository`. Consumes `FakeFileSystemProvider` (Task 1), the `Progress` seam (Task 1), `GitRepositoryRemotePath`, `GitBranchName`.
- Produces:
  - `public interface IGitCloneBuilder : IGitCommandBuilder<GitRepository>` with `WithBranch(GitBranchName) → IGitCloneBuilder`, `WithDepth(int) → IGitCloneBuilder`, `Bare() → IGitCloneBuilder`, `ReportingProgress(IProgress<string>) → IGitCloneBuilder`.
  - `internal sealed class GitCloneBuilder(IGitProcessRunner runner, IFileSystemProvider fileSystem, GitRepositoryRemotePath source, AbsoluteDirectoryPath destination)`.

### The destination check is advisory, and says so

Git already rejects a non-empty destination with exit 128 and leaves no directory behind, so the pre-check exists to fail **before** a potentially long network clone rather than after it. It is inherently racy — a directory can appear between the check and the clone — so git's own refusal remains the authority. The check throws `GitCommandException` with exit code -1 and an empty argument vector, because no git command ran.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitCloneBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Testably.Abstractions.Testing;

[TestClass]
public class GitCloneBuilderTests
{
	private static GitRepositoryRemotePath Source =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static AbsoluteDirectoryPath Destination =>
		(OperatingSystem.IsWindows() ? @"C:\dev\clone" : "/dev/clone").As<AbsoluteDirectoryPath>();

	private static FakeFileSystemProvider EmptyFileSystem() => new(new MockFileSystem());

	[TestMethod]
	public void BuildsTheDefaultCloneVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"clone",
			"--end-of-options",
			"https://example.com/repo.git",
			Destination.WeakString,
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void SourceComesBeforeDestination()
	{
		// git clone <source> <destination> is positional, and reversing them tries to clone the
		// destination path into a directory named after the URL.
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("https://example.com/repo.git", arguments[marker + 1]);
		Assert.AreEqual(Destination.WeakString, arguments[marker + 2]);
	}

	[TestMethod]
	public void MapsTheOptionFlagsBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		_ = builder.WithBranch("main".As<GitBranchName>()).WithDepth(1).Bare();

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--branch") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "main") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--depth=1") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--bare") < marker);
	}

	[TestMethod]
	public void RejectsANonPositiveDepth()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(-1));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		FakeFileSystemProvider fileSystem = EmptyFileSystem();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, null!, Source, Destination));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, fileSystem, null!, Destination));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, fileSystem, Source, null!));

		GitCloneBuilder builder = new(runner, fileSystem, Source, Destination);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ClonesIntoAMissingDestinationAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardError = "Cloning into '/dev/clone'...\ndone.\n",
		};
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		GitRepository repository = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Destination, repository.LocalPath);
		Assert.AreEqual(Source, repository.RemotePath);
		Assert.IsNotNull(repository.ProcessRunner);
	}

	[TestMethod]
	public async Task ClonesIntoAnExistingButEmptyDestinationAsync()
	{
		// git accepts an existing empty directory, so the pre-check must too.
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitRepository repository = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Destination, repository.LocalPath);
	}

	[TestMethod]
	public async Task RefusesANonEmptyDestinationBeforeRunningGitAsync()
	{
		// The whole point of the pre-check: a doomed clone should not pay its network cost first.
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);
		mock.File.WriteAllText(mock.Path.Combine(Destination.WeakString, "existing.txt"), "x");

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, Destination.WeakString);
		Assert.IsNull(runner.LastRequest);
	}

	[TestMethod]
	public async Task TryExecuteReportsANonEmptyDestinationAsAResultAsync()
	{
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);
		mock.File.WriteAllText(mock.Path.Combine(Destination.WeakString, "existing.txt"), "x");

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitResult<GitRepository> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.IsNull(runner.LastRequest);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheRequestAsync()
	{
		// git clone writes its entire progress stream to standard error, so a caller that wants to
		// watch a long clone needs the sink wired through to the request. The assertion is on the
		// request rather than on what the sink received: Progress<T> marshals its callback through the
		// synchronization context, so a received-chunks assertion would race the report.
		RecordingGitProcessRunner runner = new() { StandardError = "Receiving objects: 100%\n" };
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task ThrowsWhenGitRefusesTheCloneAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: repository 'https://example.com/repo.git' does not exist\n",
		};
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitCloneBuilderTests"`

Expected: compilation failure — `GitCloneBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitCloneBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Essentials;
using ktsu.Semantics.Paths;

/// <summary>
/// Copies a remote repository into a local working copy.
/// </summary>
public interface IGitCloneBuilder : IGitCommandBuilder<GitRepository>
{
	/// <summary>Checks this branch out instead of the remote's default.</summary>
	/// <param name="name">The branch to check out.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitCloneBuilder WithBranch(GitBranchName name);

	/// <summary>
	/// Fetches only this many commits of history, producing a shallow clone.
	/// </summary>
	/// <param name="depth">How many commits of history to fetch. Must be positive.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
	public IGitCloneBuilder WithDepth(int depth);

	/// <summary>Creates a repository with no working tree.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCloneBuilder Bare();

	/// <summary>
	/// Reports git's progress output as it arrives, rather than only when the clone finishes.
	/// </summary>
	/// <remarks>
	/// Clone writes its entire progress stream to standard error. The sink may be invoked
	/// concurrently by the standard-output and standard-error readers and must be thread-safe.
	/// </remarks>
	/// <param name="progress">The sink to report to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitCloneBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git clone</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="fileSystem">Checks the destination before the clone starts.</param>
/// <param name="source">The repository to clone.</param>
/// <param name="destination">Where the working copy should go.</param>
internal sealed class GitCloneBuilder(
	IGitProcessRunner runner,
	IFileSystemProvider fileSystem,
	GitRepositoryRemotePath source,
	AbsoluteDirectoryPath destination)
	: GitCommandBuilder<GitRepository>(runner, repositoryPath: null), IGitCloneBuilder
{
	private readonly IFileSystemProvider _fileSystem = Ensure.NotNull(fileSystem);
	private readonly GitRepositoryRemotePath _source = Ensure.NotNull(source);
	private readonly AbsoluteDirectoryPath _destination = Ensure.NotNull(destination);
	private GitBranchName? _branch;
	private int? _depth;
	private bool _bare;

	/// <inheritdoc />
	public IGitCloneBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder WithDepth(int depth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
		_depth = depth;
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder Bare()
	{
		_bare = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("clone");

		if (_branch is not null)
		{
			// --branch takes its value as a separate argument, unlike --depth.
			arguments.Add("--branch");
			arguments.Add(_branch.WeakString);
		}

		if (_depth is int depth)
		{
			arguments.Add("--depth=" + depth.ToString(CultureInfo.InvariantCulture));
		}

		if (_bare)
		{
			arguments.Add("--bare");
		}

		// Both operands are caller-supplied. The source especially: an unvalidated remote path of
		// --upload-pack=... reaching git clone is arbitrary code execution, which is what
		// NotAnOptionAttribute and this marker together close off.
		AppendOperands(arguments, _source.WeakString, _destination.WeakString);
	}

	/// <inheritdoc />
	protected override GitRepository ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		// Nothing to parse: git clone writes only progress prose, all of it to standard error.
		return new GitRepository
		{
			LocalPath = _destination,
			RemotePath = _source,
			ProcessRunner = Runner,
		};
	}

	/// <inheritdoc />
	public override Task<GitRepository> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDestinationOccupied();
		return base.ExecuteAsync(cancellationToken);
	}

	/// <inheritdoc />
	public override Task<GitResult<GitRepository>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		if (IsDestinationOccupied())
		{
			return Task.FromResult(GitResult<GitRepository>.FromError(new GitCommandError
			{
				ExitCode = -1,
				Arguments = [],
				StandardError = DestinationOccupiedMessage,
			}));
		}

		return base.TryExecuteAsync(cancellationToken);
	}

	private string DestinationOccupiedMessage =>
		$"The clone destination '{_destination.WeakString}' already exists and is not empty.";

	/// <summary>
	/// Decides whether the destination already holds anything.
	/// </summary>
	/// <remarks>
	/// Advisory only. Git enforces the same rule itself and exits 128 when the destination is
	/// non-empty, leaving no directory behind; this check exists so a clone that cannot possibly
	/// succeed fails before paying its network cost. It is inherently racy — a directory can appear
	/// between this check and the clone — so git's refusal, not this, is the authority.
	/// </remarks>
	private bool IsDestinationOccupied() =>
		_fileSystem.Directory.Exists(_destination.WeakString) &&
		_fileSystem.Directory.GetFileSystemEntries(_destination.WeakString).Length > 0;

	private void ThrowIfDestinationOccupied()
	{
		if (IsDestinationOccupied())
		{
			// Exit code -1 and an empty vector, because no git command ran. GitCommandException
			// documents -1 as its "no data" default for exactly this reason.
			throw new GitCommandException(DestinationOccupiedMessage, -1, [], string.Empty);
		}
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitCloneBuilderTests"`

Expected: PASS, 11 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitCloneBuilder.cs GitIntegration.Test/Builders/GitCloneBuilderTests.cs
git commit -m "[minor] Add the clone verb with its destination pre-check"
```

---
## Task 9: Wiring — `GitRepository`, `IGitClient`, `GitClient`, and DI

**Files:**
- Modify: `GitIntegration/GitRepository.cs`
- Modify: `GitIntegration/IGitClient.cs`
- Modify: `GitIntegration/GitClient.cs`
- Test: `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`
- Test: `GitIntegration.Test/GitClientMutatingTests.cs`
- Test: `GitIntegration.Test/ServiceCollectionExtensionsTests.cs` (add one method)

**Interfaces:**
- Consumes: every builder from Tasks 2–8.
- Produces:
  - On `GitRepository`: `Add()`, `Commit(GitCommitMessage)`, `CreateBranch(GitBranchName)`, `DeleteBranch(GitBranchName)`, `Checkout(GitRefName)`, `AddRemote(GitRemoteName, GitRepositoryRemotePath)`, `RemoveRemote(GitRemoteName)`, `SetRemoteUrl(GitRemoteName, GitRepositoryRemotePath)`.
  - On `IGitClient`: `Init(AbsoluteDirectoryPath) → IGitInitBuilder`, `Clone(GitRepositoryRemotePath, AbsoluteDirectoryPath) → IGitCloneBuilder`, `Clone(GitRepository) → IGitCloneBuilder`.
  - On `GitClient`: a second constructor `GitClient(IGitProcessRunner, IFileSystemProvider)`.

### Keeping the 2.1.0 constructor working

`GitClient` shipped in 2.1.0 with a public `GitClient(IGitProcessRunner)`. Clone needs an `IFileSystemProvider`, but **adding a required parameter to a public constructor is a source-breaking change**, and this phase is a minor version bump. So the existing signature stays and delegates to the new one, defaulting to the real filesystem:

```csharp
public GitClient(IGitProcessRunner runner) : this(runner, new NativeFileSystemProvider())
```

`NativeFileSystemProvider` is public with a parameterless constructor, and the library already references its package — verified, not assumed. DI resolves the two-argument constructor because `AddGitIntegration` already calls `AddNativeFileSystemProvider()`.

### `Clone(GitRepository)`

The spec's seam between the two layers: a hosting provider produces a `GitRepository` carrying `RemotePath` and an intended `LocalPath`, and this turns it into a working copy. It throws `ArgumentException` when `RemotePath` is null, because a repository with no remote path cannot be cloned and failing at the call is clearer than failing inside git.

- [ ] **Step 1: Write the failing repository tests**

`GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryMutatingVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	private static GitRemoteName Origin => "origin".As<GitRemoteName>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	[TestMethod]
	public void EveryMutatingVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Add().BuildArguments()],
			[.. repository.Commit("m".As<GitCommitMessage>()).BuildArguments()],
			[.. repository.CreateBranch("b".As<GitBranchName>()).BuildArguments()],
			[.. repository.DeleteBranch("b".As<GitBranchName>()).BuildArguments()],
			[.. repository.Checkout("main".As<GitRefName>()).BuildArguments()],
			[.. repository.AddRemote(Origin, Url).BuildArguments()],
			[.. repository.RemoveRemote(Origin).BuildArguments()],
			[.. repository.SetRemoteUrl(Origin, Url).BuildArguments()],
		];

		foreach (string[] vector in vectors)
		{
			Assert.AreEqual("-C", vector[0]);
			Assert.AreEqual(TestPaths.Root.WeakString, vector[1]);
		}
	}

	[TestMethod]
	public void EachCallReturnsAFreshBuilder()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Add(), repository.Add());
		Assert.AreNotSame(repository.Checkout("main".As<GitRefName>()), repository.Checkout("main".As<GitRefName>()));
	}

	[TestMethod]
	public void EveryMutatingVerbRequiresAProcessRunner()
	{
		// A repository carrying hosting metadata only describes something that may not exist on
		// disk yet. Deleting the guard from any one of these must fail a test.
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Add());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Commit("m".As<GitCommitMessage>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.CreateBranch("b".As<GitBranchName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.DeleteBranch("b".As<GitBranchName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Checkout("main".As<GitRefName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.AddRemote(Origin, Url));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.RemoveRemote(Origin));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.SetRemoteUrl(Origin, Url));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Commit(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.CreateBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.DeleteBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Checkout(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.AddRemote(null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.AddRemote(Origin, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.RemoveRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.SetRemoteUrl(null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.SetRemoteUrl(Origin, null!));
	}
}
```

`GitIntegration.Test/GitClientMutatingTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Testably.Abstractions.Testing;

[TestClass]
public class GitClientMutatingTests
{
	private static AbsoluteDirectoryPath Target =>
		(OperatingSystem.IsWindows() ? @"C:\dev\new-repo" : "/dev/new-repo").As<AbsoluteDirectoryPath>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static GitClient ClientOn(IGitProcessRunner runner) =>
		new(runner, new FakeFileSystemProvider(new MockFileSystem()));

	[TestMethod]
	public void InitBuildsAVectorTargetingTheGivenPath()
	{
		RecordingGitProcessRunner runner = new();

		string[] arguments = [.. ClientOn(runner).Init(Target).BuildArguments()];

		CollectionAssert.Contains(arguments, "init");
		CollectionAssert.Contains(arguments, Target.WeakString);
		CollectionAssert.DoesNotContain(arguments, "-C");
	}

	[TestMethod]
	public void CloneBuildsAVectorCarryingSourceAndDestination()
	{
		RecordingGitProcessRunner runner = new();

		string[] arguments = [.. ClientOn(runner).Clone(Url, Target).BuildArguments()];

		CollectionAssert.Contains(arguments, "clone");
		CollectionAssert.Contains(arguments, Url.WeakString);
		CollectionAssert.Contains(arguments, Target.WeakString);
	}

	[TestMethod]
	public void CloneFromARepositoryUsesItsRemotePathAndLocalPath()
	{
		// The seam between the two layers: a hosting provider yields a repository with metadata and
		// an intended local path, and this turns it into a working copy.
		RecordingGitProcessRunner runner = new();
		GitRepository metadataOnly = new() { LocalPath = Target, RemotePath = Url };

		string[] arguments = [.. ClientOn(runner).Clone(metadataOnly).BuildArguments()];

		CollectionAssert.Contains(arguments, Url.WeakString);
		CollectionAssert.Contains(arguments, Target.WeakString);
	}

	[TestMethod]
	public void CloneFromARepositoryWithNoRemotePathIsRejectedAtTheCall()
	{
		// Failing here beats failing inside git with a confusing message about an empty argument.
		RecordingGitProcessRunner runner = new();
		GitRepository noRemote = new() { LocalPath = Target };

		Assert.ThrowsExactly<ArgumentException>(() => _ = ClientOn(runner).Clone(noRemote));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitClient client = ClientOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Init(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(null!, Target));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(Url, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitClient(runner, null!));
	}

	[TestMethod]
	public void TheSingleArgumentConstructorStillWorks()
	{
		// Shipped public in 2.1.0. Adding a required parameter would be source-breaking, so the old
		// signature stays and defaults to the real filesystem.
		RecordingGitProcessRunner runner = new();

		GitClient client = new(runner);

		Assert.IsNotNull(client.Init(Target));
	}

	[TestMethod]
	public async Task InitThroughTheClientReportsAFreshRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardOutput: "Initialized empty Git repository\n");

		GitInitResult result = await ClientOn(runner)
			.Init(Target)
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.AlreadyExisted);
		Assert.AreEqual(Target, result.Repository.LocalPath);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

Add to `GitIntegration.Test/ServiceCollectionExtensionsTests.cs`:

```csharp
	[TestMethod]
	public void TheRegisteredClientCanBuildACloneBuilder()
	{
		// Clone needs an IFileSystemProvider, which AddGitIntegration has registered since Phase 2
		// but nothing consumed until now. This proves the two-argument constructor resolves.
		ServiceCollection services = new();
		_ = services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();
		IGitClient client = provider.GetRequiredService<IGitClient>();

		string destination = OperatingSystem.IsWindows() ? @"C:\dev\clone" : "/dev/clone";

		Assert.IsNotNull(client.Clone(
			"https://example.com/repo.git".As<GitRepositoryRemotePath>(),
			destination.As<AbsoluteDirectoryPath>()));
	}
```

That test file needs `using ktsu.Semantics.Paths;` and `using ktsu.Semantics.Strings;` if not already present, plus `using System;` for `OperatingSystem`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryMutatingVerbTests|FullyQualifiedName~GitClientMutatingTests"`

Expected: compilation failure — none of the new members exist.

- [ ] **Step 3: Add the mutating verb factories to `GitRepository`**

Insert into `GitIntegration/GitRepository.cs`, after the existing `Remotes()` factory and before the private `RequireRunner()`. Do not disturb `OpenWebClient`, `IsBrowsableUri`, or any read-only factory.

```csharp
	/// <summary>Stages changes for the next commit.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitAddBuilder Add() => new GitAddBuilder(RequireRunner(), LocalPath);

	/// <summary>Records the staged changes as a new commit.</summary>
	/// <param name="message">The commit subject.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitCommitBuilder Commit(GitCommitMessage message)
	{
		// Argument validation precedes the state check so a null argument is reported as such,
		// rather than as a missing runner.
		Ensure.NotNull(message);
		return new GitCommitBuilder(RequireRunner(), LocalPath, message);
	}

	/// <summary>Creates a branch.</summary>
	/// <param name="name">The branch to create.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchCreateBuilder CreateBranch(GitBranchName name)
	{
		Ensure.NotNull(name);
		return new GitBranchCreateBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Deletes a branch.</summary>
	/// <param name="name">The branch to delete.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchDeleteBuilder DeleteBranch(GitBranchName name)
	{
		Ensure.NotNull(name);
		return new GitBranchDeleteBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Switches the working tree to a different branch, tag, or commit.</summary>
	/// <param name="target">The branch, tag, or commit to switch to.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitCheckoutBuilder Checkout(GitRefName target)
	{
		Ensure.NotNull(target);
		return new GitCheckoutBuilder(RequireRunner(), LocalPath, target);
	}

	/// <summary>Adds a remote.</summary>
	/// <param name="name">The remote to add.</param>
	/// <param name="url">The URL the remote points at.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="url"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteAddBuilder AddRemote(GitRemoteName name, GitRepositoryRemotePath url)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(url);
		return new GitRemoteAddBuilder(RequireRunner(), LocalPath, name, url);
	}

	/// <summary>Removes a remote and every remote-tracking branch belonging to it.</summary>
	/// <param name="name">The remote to remove.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteRemoveBuilder RemoveRemote(GitRemoteName name)
	{
		Ensure.NotNull(name);
		return new GitRemoteRemoveBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Changes the URL a remote points at.</summary>
	/// <param name="name">The remote to change.</param>
	/// <param name="url">The URL to set.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="url"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteSetUrlBuilder SetRemoteUrl(GitRemoteName name, GitRepositoryRemotePath url)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(url);
		return new GitRemoteSetUrlBuilder(RequireRunner(), LocalPath, name, url);
	}
```

- [ ] **Step 4: Add `Init` and `Clone` to `IGitClient`**

Append these members to the `IGitClient` interface in `GitIntegration/IGitClient.cs`, and remove the "Phase 4 adds Init and Clone to this interface" remark now that it has:

```csharp
	/// <summary>
	/// Creates a repository at a path.
	/// </summary>
	/// <remarks>
	/// Safe to run against a path that already holds a repository: git reinitialises it, and the
	/// result reports <see cref="GitInitResult.AlreadyExisted"/> so a caller can tell.
	/// </remarks>
	/// <param name="path">Where the repository should be.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitInitBuilder Init(AbsoluteDirectoryPath path);

	/// <summary>
	/// Clones a repository into a local working copy.
	/// </summary>
	/// <param name="source">The repository to clone.</param>
	/// <param name="destination">Where the working copy should go.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="source"/> or <paramref name="destination"/> is <see langword="null"/>.
	/// </exception>
	public IGitCloneBuilder Clone(GitRepositoryRemotePath source, AbsoluteDirectoryPath destination);

	/// <summary>
	/// Clones the repository a hosting provider described.
	/// </summary>
	/// <remarks>
	/// This is the one seam between the library's two layers: a provider produces a
	/// <see cref="GitRepository"/> carrying remote metadata and an intended local path, and this
	/// turns it into a working copy.
	/// </remarks>
	/// <param name="repository">The repository to clone, carrying a non-null
	/// <see cref="GitRepository.RemotePath"/>.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="repository"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="repository"/> has no <see cref="GitRepository.RemotePath"/>.
	/// </exception>
	public IGitCloneBuilder Clone(GitRepository repository);
```

`GitIntegration/IGitClient.cs` already has `using ktsu.Semantics.Paths;`.

- [ ] **Step 5: Implement them on `GitClient`**

In `GitIntegration/GitClient.cs`, replace the primary constructor with an explicit pair, and add the three members.

Change the class declaration from the primary-constructor form to:

```csharp
public sealed class GitClient : IGitClient
{
	private readonly IGitProcessRunner _runner;
	private readonly IFileSystemProvider _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="GitClient"/> class over the real filesystem.
	/// </summary>
	/// <remarks>
	/// Kept because this signature shipped in 2.1.0, before <c>Clone</c> needed a filesystem.
	/// Adding a required parameter to it would be a source-breaking change.
	/// </remarks>
	/// <param name="runner">Runs every command this client issues.</param>
	public GitClient(IGitProcessRunner runner)
		: this(runner, new NativeFileSystemProvider())
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="GitClient"/> class.
	/// </summary>
	/// <param name="runner">Runs every command this client issues.</param>
	/// <param name="fileSystem">Checks a clone destination before the clone starts.</param>
	public GitClient(IGitProcessRunner runner, IFileSystemProvider fileSystem)
	{
		_runner = Ensure.NotNull(runner);
		_fileSystem = Ensure.NotNull(fileSystem);
	}
```

**Read the existing file before editing.** `GitClient` currently uses a primary constructor and declares its field as `private readonly IGitProcessRunner _runner = Ensure.NotNull(runner);` — an initializer that must be **removed**, because the field is now assigned in the constructor body. Leaving the initializer in place is a compile error once the primary constructor's `runner` parameter is gone. Everything below the constructors is unchanged: the existing methods already read `_runner`.

Add these using directives inside the namespace: `using ktsu.Essentials;` (for `IFileSystemProvider`) and `using ktsu.Essentials.FileSystemProviders.Native;` (for `NativeFileSystemProvider`).

Then add the three members:

```csharp
	/// <inheritdoc />
	public IGitInitBuilder Init(AbsoluteDirectoryPath path) =>
		new GitInitBuilder(_runner, Ensure.NotNull(path));

	/// <inheritdoc />
	public IGitCloneBuilder Clone(GitRepositoryRemotePath source, AbsoluteDirectoryPath destination) =>
		new GitCloneBuilder(_runner, _fileSystem, Ensure.NotNull(source), Ensure.NotNull(destination));

	/// <inheritdoc />
	public IGitCloneBuilder Clone(GitRepository repository)
	{
		Ensure.NotNull(repository);

		// Reported at the call rather than left to fail inside git, where an empty source argument
		// produces a message about the destination instead.
		GitRepositoryRemotePath source = repository.RemotePath
			?? throw new ArgumentException(
				"The repository has no RemotePath, so there is nothing to clone from.",
				nameof(repository));

		return Clone(source, repository.LocalPath);
	}
```

Also update the class-level `<remarks>` on `GitClient`: the sentence saying Phase 4's `Init` and `Clone` "do need" a filesystem should now read in the present tense, since they exist.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryMutatingVerbTests|FullyQualifiedName~GitClientMutatingTests|FullyQualifiedName~ServiceCollectionExtensionsTests"`

Expected: PASS, 12 new plus the existing DI tests.

- [ ] **Step 7: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 8: Commit**

```bash
git add GitIntegration/GitRepository.cs GitIntegration/IGitClient.cs GitIntegration/GitClient.cs GitIntegration.Test/GitRepositoryMutatingVerbTests.cs GitIntegration.Test/GitClientMutatingTests.cs GitIntegration.Test/ServiceCollectionExtensionsTests.cs
git commit -m "[minor] Wire the mutating verbs into GitRepository and IGitClient"
```

---

## Task 10: Tier-3 integration tests against a real git binary

The spec places these in Phase 4 because this is the first phase whose verbs can build a repository from nothing. They are the only tests in the suite that run git.

**Files:**
- Create: `GitIntegration.Test/Integration/TemporaryRepository.cs`
- Create: `GitIntegration.Test/Integration/GitRoundTripTests.cs`

**Interfaces:**
- Consumes: every builder from Tasks 2–9, plus Phase 3's read-only verbs.
- Produces: no library types — this task adds tests only.

### Three things these tests must survive

1. **No git binary.** CI, or a contributor's machine, may not have git. The tests self-skip with `Assert.Inconclusive` rather than fail.
2. **No global git identity.** A machine with no `user.name` cannot commit. Every commit-producing test passes the identity explicitly, and the helper writes it into the repository's own config so it never depends on — or disturbs — the host's global configuration.
3. **Read-only files on Windows.** Git marks objects under `.git/objects` read-only, and `Directory.Delete(recursive: true)` throws `UnauthorizedAccessException` on them. The helper clears the attribute before deleting, and never lets a cleanup failure fail a test that otherwise passed.

- [ ] **Step 1: Write the temporary-repository helper**

`GitIntegration.Test/Integration/TemporaryRepository.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// A throwaway directory on the real filesystem, removed when the test finishes.
/// </summary>
internal sealed class TemporaryRepository : IDisposable
{
	private readonly string _root;

	public TemporaryRepository()
	{
		// A GUID rather than a test name, so parallel runs of the same test cannot collide.
		_root = Path.Combine(Path.GetTempPath(), "ktsu-git-it-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(_root);
	}

	/// <summary>Gets the directory as the library's path type.</summary>
	public AbsoluteDirectoryPath Root => _root.As<AbsoluteDirectoryPath>();

	/// <summary>Gets the directory as a plain string, for direct file operations.</summary>
	public string RootPath => _root;

	/// <summary>Writes a file inside the repository, creating any directories it needs.</summary>
	/// <param name="relativePath">The path relative to the repository root.</param>
	/// <param name="contents">What to write.</param>
	public void WriteFile(string relativePath, string contents)
	{
		string full = Path.Combine(_root, relativePath);
		string? directory = Path.GetDirectoryName(full);

		if (!string.IsNullOrEmpty(directory))
		{
			_ = Directory.CreateDirectory(directory);
		}

		File.WriteAllText(full, contents);
	}

	public void Dispose()
	{
		try
		{
			DeleteRecursively(_root);
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing a passing test over. Windows in
			// particular can hold a git pack file open briefly after the process exits.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static void DeleteRecursively(string path)
	{
		if (!Directory.Exists(path))
		{
			return;
		}

		// git marks everything under .git/objects read-only, and Directory.Delete refuses those on
		// Windows. Clearing the attribute first is what makes cleanup reliable there.
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(path, recursive: true);
	}
}
```

- [ ] **Step 2: Write the integration tests**

`GitIntegration.Test/Integration/GitRoundTripTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Essentials.FileSystemProviders.Native;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises the verbs against a real git binary, in a throwaway repository per test.
/// </summary>
/// <remarks>
/// Marked as integration tests and skipped when git is not on PATH, so a machine or a CI job
/// without git still reports a green suite rather than a wall of failures.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitRoundTripTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	private static GitClient CreateClient() =>
		new(new RunCommandGitProcessRunner(new GitOptions()), new NativeFileSystemProvider());

	/// <summary>
	/// Skips the calling test when no usable git binary is present.
	/// </summary>
	private static async Task RequireGitAsync(CancellationToken cancellationToken)
	{
		try
		{
			_ = await CreateClient().GetVersionAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (GitExecutableNotFoundException)
		{
			Assert.Inconclusive("git is not on PATH, so the integration tests were skipped.");
		}
	}

	/// <summary>
	/// Initialises a repository with a deterministic identity and initial branch.
	/// </summary>
	/// <remarks>
	/// The identity is written into the repository's own config rather than taken from the host,
	/// so the tests neither depend on a configured user nor disturb one. The initial branch is
	/// named explicitly for the same reason: <c>init.defaultBranch</c> varies by machine.
	/// </remarks>
	private static async Task<GitRepository> InitialiseAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitClient client = CreateClient();

		GitInitResult init = await client
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(init.AlreadyExisted);

		GitRepository repository = init.Repository;

		_ = await new GitTextBuilder(
			repository.ProcessRunner!, repository.LocalPath, "config", "user.name", AuthorName.WeakString)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await new GitTextBuilder(
			repository.ProcessRunner!, repository.LocalPath, "config", "user.email", AuthorEmail.WeakString)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	[TestMethod]
	public async Task InitCreatesARepositoryAndReportsItAsFreshAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await repository.IsClonedAsync(cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task InitReportsAnExistingRepositoryAsAlreadyExistingAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		_ = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		GitInitResult second = await CreateClient()
			.Init(temporary.Root)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(second.AlreadyExisted);
	}

	[TestMethod]
	public async Task AddAndCommitProduceAReadableCommitAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitCommit commit = await repository
			.Commit("first commit".As<GitCommitMessage>())
			.WithBody("A body line.")
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// The readback is the whole reason Commit runs twice — this asserts it returned real data,
		// not the abbreviated summary git prints.
		Assert.AreEqual("first commit", commit.Subject);
		Assert.AreEqual("A body line.", commit.Body);
		Assert.AreEqual(AuthorName, commit.Author.Name);
		Assert.AreEqual(AuthorEmail, commit.Author.Email);
		Assert.AreEqual(0, commit.ParentShas.Count);
		Assert.AreEqual(40, commit.Sha.WeakString.Length);
	}

	[TestMethod]
	public async Task CommittingWithNothingStagedThrowsTheDedicatedExceptionAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// Nothing has changed since, so git exits 1 and says so on standard output.
		await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await repository.Commit("c2".As<GitCommitMessage>())
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StatusReflectsStagedAndUntrackedWorkAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitStatus clean = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsTrue(clean.IsClean);
		Assert.AreEqual("main".As<GitBranchName>(), clean.Branch);

		temporary.WriteFile("untracked.txt", "two\n");
		GitStatus dirty = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsFalse(dirty.IsClean);
	}

	[TestMethod]
	public async Task BranchCreateCheckoutAndDeleteRoundTripAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitBranchName feature = "feature/x".As<GitBranchName>();
		_ = await repository.CreateBranch(feature).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitBranch> afterCreate =
			await repository.Branches().LocalOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(2, afterCreate.Count);

		_ = await repository.Checkout("feature/x".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitStatus onFeature = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(feature, onFeature.Branch);

		_ = await repository.Checkout("main".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.DeleteBranch(feature).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitBranch> afterDelete =
			await repository.Branches().LocalOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, afterDelete.Count);
	}

	[TestMethod]
	public async Task RemoteAddSetUrlAndRemoveRoundTripAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		GitRemoteName origin = "origin".As<GitRemoteName>();
		GitRepositoryRemotePath first = "https://example.com/one.git".As<GitRepositoryRemotePath>();
		GitRepositoryRemotePath second = "https://example.com/two.git".As<GitRepositoryRemotePath>();

		_ = await repository.AddRemote(origin, first).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> added =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, added.Count);
		Assert.AreEqual(first, added[0].FetchUrl);

		_ = await repository.SetRemoteUrl(origin, second).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> changed =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(second, changed[0].FetchUrl);

		_ = await repository.RemoveRemote(origin).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> removed =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(0, removed.Count);
	}

	[TestMethod]
	public async Task CloneReproducesTheSourceHistoryAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		GitRepository origin = await InitialiseAsync(source, cancellationToken).ConfigureAwait(false);

		source.WriteFile("a.txt", "one\n");
		_ = await origin.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit committed = await origin
			.Commit("cloned commit".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository destinationRoot = new();
		AbsoluteDirectoryPath destination =
			System.IO.Path.Combine(destinationRoot.RootPath, "copy").As<AbsoluteDirectoryPath>();

		GitRepository clone = await CreateClient()
			.Clone(source.RootPath.As<GitRepositoryRemotePath>(), destination)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history =
			await clone.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task CloneRefusesANonEmptyDestinationAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		_ = await InitialiseAsync(source, cancellationToken).ConfigureAwait(false);

		using TemporaryRepository occupied = new();
		occupied.WriteFile("in-the-way.txt", "x");

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await CreateClient()
				.Clone(source.RootPath.As<GitRepositoryRemotePath>(), occupied.Root)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DiffReportsAStagedRenameAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("before.txt", "line1\nline2\nline3\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A pure rename: the content must be identical or git scores it below the rename threshold
		// and reports a delete plus an add instead.
		System.IO.File.Move(
			System.IO.Path.Combine(temporary.RootPath, "before.txt"),
			System.IO.Path.Combine(temporary.RootPath, "after.txt"));
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> changes = await repository.Diff()
			.Staged()
			.DetectRenames()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, changes.Count);
		Assert.AreEqual(GitChangeKind.Renamed, changes[0].Kind);
		Assert.AreEqual("before.txt".As<RelativeFilePath>(), changes[0].OriginalPath);
		Assert.AreEqual("after.txt".As<RelativeFilePath>(), changes[0].Path);
		Assert.AreEqual(100, changes[0].SimilarityPercent);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test --filter "TestCategory=Integration"`

Expected: PASS, 10 tests. If git is not on PATH they report Inconclusive rather than failing — that is the intended behaviour, not a problem to fix.

- [ ] **Step 4: Confirm the whole suite is still green**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, every test passing.

- [ ] **Step 5: Confirm no temp directories leaked**

Run: `ls "$TMPDIR" 2>/dev/null | grep ktsu-git-it- || echo "none leaked"` (or the Windows equivalent against `%TEMP%`).

Expected: `none leaked`. A handful surviving is tolerable — `Dispose` swallows cleanup failures deliberately — but a large number means the helper is not working.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration.Test/Integration
git commit -m "[minor] Add tier-3 integration tests against a real git binary"
```

---

## What this phase deliberately does not do

- **No `commit --amend`.** It rewrites history, which the spec defers along with the other history-editing operations. One flag to add later.
- **No `add --force`.** Staging an ignored file is unusual enough to wait for a caller who needs it.
- **No `switch` or `restore`.** `checkout` covers both, and the target type is a `GitRefName` that may be a tag or an object id, which `switch` does not accept.
- **No submodule support on clone.** Submodules are a spec non-goal.
- **No `fetch`, `pull`, or `push`, and no hosting providers.** Phase 5.
- **The clone destination check remains advisory.** Git's own refusal is the authority; the pre-check exists only to fail before a long network operation.

## Deferred items now due

Two duplications were deferred from Phase 3 on the grounds that Phase 4 would bring a third caller. Re-examine both once Task 9 lands, and fold whichever still earns it into the final review's fix wave rather than a task of its own:

1. **The pathspec tail.** `GitLogBuilder` and `GitDiffBuilder` both end with `arguments.Add("--")` and a loop over their paths. `GitAddBuilder` uses `AppendOperands` instead, because `add` takes pathspecs as plain operands rather than after a `--` separator — so the third caller may not materialise after all. Check before factoring.
2. **`GitRepository.IsClonedAsync` versus `GitClient.IsRepositoryCoreAsync`.** `GitInitBuilder.ProbeAsync` is now a third copy of the same `rev-parse --is-inside-work-tree` plus `string.Equals(value, "true")` shape. Three callers is the threshold that was set; a `GitProbes.IsWorkTreeAsync(IGitProcessRunner, AbsoluteDirectoryPath, CancellationToken)` internal helper would collapse all three.

## Self-review

**Spec coverage.** Every verb the spec's Phase 4 bullet names has a task: `Init` (7), `Clone` (8), `Add` (2), `Commit` (6), `CreateBranch`/`DeleteBranch` (3), `Checkout` (4), and remote add/remove/set-url (5). The `IGitClient` members the spec declares — `Init`, both `Clone` overloads — land in Task 9, and the `GitRepository` members it declares land there too. Tier-3 integration tests land in Task 10, as the spec requires. The spec's `IFileSystemProvider` requirement is met for `Clone`; for `Init` the equivalent check is a `rev-parse` probe, which answers the same question more reliably than a filesystem test would and is argued in Task 7.

**Three decisions taken with the user, recorded here so an executor does not relitigate them:** `commit` with nothing staged gets a dedicated exception type; `init` probes with `rev-parse` first so it can report `AlreadyExisted`; and `clone` uses `IFileSystemProvider` for a destination pre-check.

**Type consistency.** `GitCompleted` is the result of `Add`, `Checkout`, both branch verbs, and all three remote verbs. `Commit` returns `GitCommit`, `Init` returns `GitInitResult`, `Clone` returns `GitRepository`. Every builder constructor is `(IGitProcessRunner, AbsoluteDirectoryPath, …)` except `GitInitBuilder` — which takes a target path and passes `repositoryPath: null` to the base — and `GitCloneBuilder`, which additionally takes an `IFileSystemProvider` and also passes `null`. Both exceptions are argued at their tasks.

**One compatibility risk, handled.** `GitClient`'s public constructor shipped in 2.1.0 and is preserved by overload rather than changed. Task 9's `TheSingleArgumentConstructorStillWorks` pins it.






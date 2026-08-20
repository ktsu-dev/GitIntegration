# GitIntegration v2 — Phase 5a: Remote Sync Verbs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the three verbs that talk to a remote — `fetch`, `pull`, and `push` — with typed, parsed results for the two that offer a machine-readable format.

**Scope:** The first half of the spec's Phase 5. The spec bundles remote sync with the hosting layer, but the two are independent subsystems meeting at a single seam (`IGitClient.Clone(GitRepository)`) that already exists. **Phase 5b — `IGitHostingProvider`, the `GitProvider` async refactor, GitHub enumeration, and Azure DevOps — gets its own plan.** Each half is releasable alone.

**Architecture:** Same shape as Phases 3–4: a builder per verb over `GitCommandBuilder<TResult>`, delegating to a pure parser. Two things are new. `push` is the first verb whose *failure* carries the information the caller wants — a rejected push exits 1 while printing a complete porcelain record — so `ExecuteAsync` and `TryExecuteAsync` deliberately diverge. And `fetch --porcelain` requires git ≥ 2.41, so `GitFetchBuilder` probes the version once and degrades honestly below that rather than parsing human output.

**Tech Stack:** .NET 10 / .NET 9, `ktsu.Sdk`, `ktsu.Semantics.Strings` 3.0.1, `ktsu.Semantics.Paths` 3.0.1, `ktsu.RunCommand` 1.5.0, `ktsu.Essentials` 2.0.0, MSTest via `MSTest.Sdk`. **No new packages.**

**Spec:** `docs/superpowers/specs/2026-08-19-gitintegration-v2-design.md`

**Prior plans:** Phases 1–2, 3, and 4 under `docs/superpowers/plans/`. This builds directly on Phase 3's `GitVersion.AtLeast` and Phase 4's `GitCompleted`.

> **Prerequisite — read before starting Task 8.** Task 8 reuses `GitRoundTripTests.IsGitRequired`
> and `GitRoundTripTests.RequiredEnvironmentVariable`, which are added by the cross-platform CI
> work on `feature/cross-platform-ci` (PR #77) and are **not** on `main` at the time this plan was
> written. Before Task 8, confirm they exist:
>
> ```bash
> git grep -n "IsGitRequired" -- GitIntegration.Test/Integration/GitRoundTripTests.cs
> ```
>
> If that returns nothing, merge `main` into this branch once PR #77 has landed. Do **not** work
> around it by writing a second copy of the skip helper — two helpers that must agree about when a
> missing git is fatal is exactly the kind of divergence that makes a CI guard silently inert.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Tabs** for indentation; **LF** line endings (`.gitattributes` sets `* text=auto eol=lf`).
- **File-scoped namespaces**, `using` directives **inside** the namespace. `ktsu.GitIntegration` for the library, `ktsu.GitIntegration.Test` for tests, regardless of folder.
- **Every file starts with** `// Copyright (c) 2023-2026 ktsu-dev contributors`, then a blank line.
- **Nullable reference types on; warnings are errors.**
- **Zero `[SuppressMessage]` attributes.** Four shipped phases have none. Fix the code, not the analyzer. Complaints seen so far and all fixable: CA1002, CA1062, CA1716, CA1859, CA1861, CA2007, IDE0002, IDE0005, IDE0032, IDE0290, IDE0300, IDE0305, MSTEST0032, MSTEST0065.
- **No `this.` qualifiers**; explicit accessibility everywhere, including `public` on interface members.
- **XML docs on every public member** — the SDK errors on a missing one.
- **`Ensure.NotNull(x)`** in the library (`Polyfill`, `PrivateAssets="all"`); **`ArgumentNullException.ThrowIfNull`** in tests, where `Ensure` is invisible.
- **`.ConfigureAwait(false)` on every await**, library and tests.
- **Validate arguments before state.** A method that null-checks an argument and also requires object state checks the argument first.
- **MSTest**, semantic assertions, async test methods end in `Async`, `TestContext.CancellationTokenSource.Token` for cancellation. Discard the result when asserting a value-returning call throws: `Assert.ThrowsExactly<T>(() => _ = x.M(null!))`.
- **Commit tags:** `[minor]` for features. Recognised: `[major]`, `[minor]`, `[patch]`, `[pre]`. **`[fix]` is not recognised.** No `Co-Authored-By` lines.
- **Do not edit** `VERSION.md`, `CHANGELOG.md`, `LATEST_CHANGELOG.md`, `LICENSE.md`.
- **Build:** `dotnet build`. **Test:** `dotnet test`. **Never `dotnet test --nologo`** — it runs zero tests and exits 5.
- **A library `PackageReference` added for analyzer KTSU0006 needs both `PrivateAssets="all"` and a `VersionOverride`** pinned to the lowest version any consumer could resolve. This phase should need no new package at all; if you think you do, stop and report it as a blocker.

### Carried design invariants

- **`git -C <path>`** scopes every repository command, never a process working directory.
- **`GIT_TERMINAL_PROMPT=0` and `LC_ALL=C`** on every invocation. Both matter more here than anywhere else: the C locale is what makes the message-matching in this phase dependable, and the terminal-prompt suppression is what stops an unauthenticated remote hanging.
- **Caller-supplied operands go through `AppendOperands`**, which emits `--end-of-options`.
- **Builders are mutable, single-use, not thread-safe**, return `this` typed as the interface, and `GitRepository.Verb()` returns a fresh one each call.
- **Builder classes `internal sealed`; interfaces public.**

---

## Findings from probing the installed git

Captured from `git version 2.50.1.windows.1` with `LC_ALL=C`, against a local bare repository acting as a remote. **These are the formats the parsers are built on — do not re-derive them.**

### `push --porcelain` — TAB-separated, flag-prefixed, terminated by `Done`

```
To /path/to/origin.git
*	refs/heads/main:refs/heads/main	[new branch]
Done
```

The record line is `<flag>\t<local-ref>:<remote-ref>\t<summary>`. Captured flags, one per outcome:

| Flag | Example record | Meaning |
|---|---|---|
| `*` | `*\trefs/heads/main:refs/heads/main\t[new branch]` | ref created |
| ` ` (space) | ` \trefs/heads/main:refs/heads/main\t66afe49..7c85857` | fast-forward |
| `=` | `=\trefs/heads/main:refs/heads/main\t[up to date]` | nothing to do |
| `!` | `!\trefs/heads/main:refs/heads/main\t[rejected] (fetch first)` | rejected |
| `-` | `-\t:refs/heads/throwaway\t[deleted]` | deleted — **note the empty local ref before the colon** |

Two lines are not records and must be skipped: the leading `To <url>`, and the trailing `Done`. A `-u` push also prints `branch 'main' set up to track 'origin/main'.` on stdout, which is likewise not a record.

**The critical finding: a rejected push exits 1 and still prints the full record.** Verified. This is the first verb in the library whose failure output is the thing the caller actually wants.

`--dry-run` and `--force` produce records in exactly the same shape; `--force-with-lease` is accepted.

### `fetch --porcelain` — space-separated, explicit shas, stdout

```
  7c85857fc85452150652c289fc94f1f912b96c40 6702dd1957dedc4e0f54245bda65f3ddc5f37e64 refs/remotes/origin/main
```

Format is `<flag><space><old-sha> <new-sha> <local-ref>`. Character 0 is the flag — a space for a fast-forward, which is why the line appears to start with two spaces. Standard error is empty. **When nothing changed, there is no output at all** and the exit code is 0.

Plain `fetch` without `--porcelain` writes its `From …` / `* branch …` progress to **standard error**, and nothing to standard output.

### `pull` conflicts report on standard OUTPUT and exit 128

```
$ git pull origin main
--- stdout ---
Auto-merging c.txt
CONFLICT (content): Merge conflict in c.txt
Automatic merge failed; fix conflicts and then commit the result.
--- stderr ---
From /path/to/origin
 * branch            main       -> FETCH_HEAD
--- exit 128 ---
```

Same trap as `commit`'s "nothing to commit": `GitCommandBuilder<TResult>.CreateException` reads only `StandardError`, so the classifier must read `StandardOutput`.

Afterwards the repository is in a describable state — `status --porcelain=v2` reports a `u UU` unmerged record, which Phase 3's parser already maps to `GitFileState.Unmerged` on both sides. So a caller who catches a conflict can use `Status()` to find out what conflicted, without this library needing any conflict-resolution machinery the spec lists as a non-goal.

### `GIT_TERMINAL_PROMPT=0` works

A push to an unreachable HTTPS remote fails immediately with exit 128 and `fatal: unable to access …` rather than blocking on a credential prompt. The Phase 2 environment overlay does its job, and these verbs need no timeout-based escape.

### Other exit codes

`fetch` from a nonexistent remote exits **128**. A conflicting `pull` exits **128**. A rejected `push` exits **1**.

---

## Two decisions this plan makes, and why

### 1. `fetch` degrades rather than parsing human output below git 2.41

The spec's table says `fetch --porcelain` (git ≥ 2.41), "else stderr". But the same spec states, twice and more fundamentally, that **human-facing output is never parsed** — it is the rule the entire parsing strategy rests on. The two instructions conflict, and the stronger, more general one should win.

So below 2.41 `GitFetchBuilder` runs a plain `fetch`, which still *does the work*, and returns a `GitFetchResult` whose `Updates` is empty and whose `DetailAvailable` is `false`. Only the itemised report is unavailable, and a caller who needs it can compare `Branches()` before and after. The alternative — a second, locale-fragile parser for prose that git has changed before — buys itemisation on old git at the cost of a class of silent misparse.

Git 2.41 shipped in May 2023, so this is a real population (Ubuntu 22.04 ships 2.34), which is why the degradation is explicit and inspectable rather than a silent empty list.

### 2. `Push` makes `ExecuteAsync` and `TryExecuteAsync` mean different things

A rejected push exits 1 *and* prints a complete record. That breaks the library's usual assumption that a non-zero exit means "nothing useful came back".

- **`ExecuteAsync` stays strict.** Any rejected ref throws `GitPushRejectedException`, which **carries the fully parsed `GitPushResult`** so nothing is lost. A genuine failure — no such remote, network, auth — throws `GitCommandException` as everywhere else.
- **`TryExecuteAsync` returns the parsed result**, rejections included, and the caller inspects `GitPushResult.HasRejections`. That is what "try" should mean when git ran and told us precisely what happened.

Both are documented on the interface. This is deliberate and is the one place in the library where the two entry points differ in more than exception-vs-result.

---

## File Structure

**Library — `GitIntegration/`**

| File | Responsibility |
|---|---|
| `Models/GitRefUpdate.cs` | `GitRefUpdate` + `GitRefUpdateKind` |
| `Models/GitFetchResult.cs` | `GitFetchResult` |
| `Models/GitPushResult.cs` | `GitPushResult` |
| `Execution/GitExceptions.cs` | **Modified** — add `GitPushRejectedException`, `GitPullConflictException` |
| `Parsing/GitPushParser.cs` | `push --porcelain` → `GitPushResult` |
| `Parsing/GitFetchParser.cs` | `fetch --porcelain` → `GitFetchResult` |
| `Builders/GitPushBuilder.cs` | `IGitPushBuilder` + `GitPushBuilder` |
| `Builders/GitFetchBuilder.cs` | `IGitFetchBuilder` + `GitFetchBuilder` (version probe) |
| `Builders/GitPullBuilder.cs` | `IGitPullBuilder` + `GitPullBuilder` (stdout classifier) |
| `GitRepository.cs` | **Modified** — add `Fetch()`, `Pull()`, `Push()` |

**Tests — `GitIntegration.Test/`**

| File | Responsibility |
|---|---|
| `Parsing/GitPushParserTests.cs`, `Parsing/GitFetchParserTests.cs` | fixtures captured above |
| `Builders/GitPushBuilderTests.cs`, `Builders/GitFetchBuilderTests.cs`, `Builders/GitPullBuilderTests.cs` | argv and behaviour |
| `GitRepositoryRemoteVerbTests.cs` | the three factories |
| `Integration/GitRemoteSyncTests.cs` | round trip against a local bare remote |

---
## Task 1: Models and exceptions

**Files:**
- Create: `GitIntegration/Models/GitRefUpdate.cs`
- Create: `GitIntegration/Models/GitFetchResult.cs`
- Create: `GitIntegration/Models/GitPushResult.cs`
- Modify: `GitIntegration/Execution/GitExceptions.cs`
- Test: `GitIntegration.Test/Models/GitRemoteResultTests.cs`

**Interfaces:**
- Consumes: `GitCommitSha`, `GitRefName` (existing); `GitCommandException` (existing).
- Produces:
  - `GitRefUpdateKind { FastForward, Forced, Removed, Created, Rejected, UpToDate, TagUpdate, Unknown }`.
  - `GitRefUpdate` — `required GitRefUpdateKind Kind`, `required GitRefName Reference`, `GitRefName? Source`, `GitCommitSha? OldSha`, `GitCommitSha? NewSha`, `string Summary = ""`, derived `bool IsRejected`.
  - `GitFetchResult` — `required IReadOnlyList<GitRefUpdate> Updates`, `required bool DetailAvailable`, derived `bool IsUpToDate`.
  - `GitPushResult` — `required IReadOnlyList<GitRefUpdate> Updates`, derived `bool HasRejections`.
  - `GitPushRejectedException : GitCommandException` with a `GitPushResult? Result` property and five constructors.
  - `GitPullConflictException : GitCommandException` with four constructors.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Models/GitRemoteResultTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteResultTests
{
	private static GitRefUpdate Update(GitRefUpdateKind kind) => new()
	{
		Kind = kind,
		Reference = "refs/heads/main".As<GitRefName>(),
		Summary = "[test]",
	};

	[TestMethod]
	public void AFetchWithNoUpdatesIsUpToDate()
	{
		// git prints nothing at all when a fetch changed nothing, so an empty list is the
		// ordinary success case rather than a sign that parsing failed.
		GitFetchResult result = new() { Updates = [], DetailAvailable = true };

		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public void AFetchWithUpdatesIsNotUpToDate()
	{
		GitFetchResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.FastForward)],
			DetailAvailable = true,
		};

		Assert.IsFalse(result.IsUpToDate);
	}

	[TestMethod]
	public void AFetchWithoutDetailIsNotReportedAsUpToDate()
	{
		// The dangerous confusion this guards against: on git older than 2.41 the update list is
		// empty because it could not be gathered, not because nothing happened. Reporting that as
		// "up to date" would be a silent lie.
		GitFetchResult result = new() { Updates = [], DetailAvailable = false };

		Assert.IsFalse(result.IsUpToDate);
	}

	[TestMethod]
	public void APushWithARejectedRefReportsRejections()
	{
		GitPushResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.FastForward), Update(GitRefUpdateKind.Rejected)],
		};

		Assert.IsTrue(result.HasRejections);
	}

	[TestMethod]
	public void APushWithNoRejectedRefsReportsNone()
	{
		GitPushResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.Created), Update(GitRefUpdateKind.UpToDate)],
		};

		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public void OnlyARejectedUpdateIsRejected()
	{
		Assert.IsTrue(Update(GitRefUpdateKind.Rejected).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.FastForward).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.UpToDate).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.Removed).IsRejected);
	}

	[TestMethod]
	public void ARejectedPushExceptionCarriesTheParsedResult()
	{
		// The whole point of the type: a rejected push exits non-zero while printing exactly what
		// the caller wanted to know, so the detail must survive the throw.
		GitPushResult result = new() { Updates = [Update(GitRefUpdateKind.Rejected)] };

		GitPushRejectedException exception = new("rejected", 1, [], string.Empty, result);

		Assert.AreSame(result, exception.Result);
		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public void ARejectedPushExceptionHasNoResultWhenConstructedWithoutOne()
	{
		Assert.IsNull(new GitPushRejectedException("rejected").Result);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteResultTests"`

Expected: compilation failure — none of these types exist.

- [ ] **Step 3: Write `GitRefUpdate`**

`GitIntegration/Models/GitRefUpdate.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// What happened to one reference during a fetch or a push.
/// </summary>
/// <remarks>
/// The flag characters git uses are shared between the two verbs but not identical in meaning:
/// <c>-</c> is a deletion when pushing and a prune when fetching, and <c>t</c> appears only when
/// fetching. The names here are neutral between the two.
/// </remarks>
public enum GitRefUpdateKind
{
	/// <summary>The reference moved forward without rewriting history. Git's flag is a space.</summary>
	FastForward,

	/// <summary>The reference was moved in a way that discarded commits. Git's flag is <c>+</c>.</summary>
	Forced,

	/// <summary>The reference was deleted on push, or pruned on fetch. Git's flag is <c>-</c>.</summary>
	Removed,

	/// <summary>The reference did not exist before. Git's flag is <c>*</c>.</summary>
	Created,

	/// <summary>Git refused the update. Its flag is <c>!</c>.</summary>
	Rejected,

	/// <summary>There was nothing to do. Git's flag is <c>=</c>.</summary>
	UpToDate,

	/// <summary>A tag was updated. Git's flag is <c>t</c>, and only fetch emits it.</summary>
	TagUpdate,

	/// <summary>Git used a flag this library does not recognise.</summary>
	Unknown,
}

/// <summary>
/// One reference changed by a fetch or a push.
/// </summary>
public sealed record GitRefUpdate
{
	/// <summary>Gets what happened to the reference.</summary>
	public required GitRefUpdateKind Kind { get; init; }

	/// <summary>
	/// Gets the reference that was updated: the remote reference when pushing, the local
	/// remote-tracking reference when fetching.
	/// </summary>
	public required GitRefName Reference { get; init; }

	/// <summary>
	/// Gets the local reference that was pushed, or <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Populated only by push, and null there too for a deletion — git writes an empty local side,
	/// as in <c>:refs/heads/gone</c>, because nothing is being sent.
	/// </remarks>
	public GitRefName? Source { get; init; }

	/// <summary>Gets the object id the reference pointed at before, when git reported one.</summary>
	/// <remarks>
	/// Fetch reports full object ids directly. Push reports them only inside
	/// <see cref="Summary"/> as an abbreviated range such as <c>66afe49..7c85857</c>; those are
	/// parsed out where present, so they may be shorter than a full identifier.
	/// </remarks>
	public GitCommitSha? OldSha { get; init; }

	/// <summary>Gets the object id the reference points at now, when git reported one.</summary>
	public GitCommitSha? NewSha { get; init; }

	/// <summary>
	/// Gets git's own summary text, verbatim — <c>[new branch]</c>, <c>[up to date]</c>,
	/// <c>[rejected] (fetch first)</c>, or a commit range. Empty for fetch, whose porcelain format
	/// carries no summary field.
	/// </summary>
	public string Summary { get; init; } = string.Empty;

	/// <summary>Gets a value indicating whether git refused this update.</summary>
	public bool IsRejected => Kind == GitRefUpdateKind.Rejected;
}
```

- [ ] **Step 4: Write `GitFetchResult`**

`GitIntegration/Models/GitFetchResult.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// What a fetch brought in.
/// </summary>
public sealed record GitFetchResult
{
	/// <summary>Gets the references the fetch changed, in the order git listed them.</summary>
	public required IReadOnlyList<GitRefUpdate> Updates { get; init; }

	/// <summary>
	/// Gets a value indicating whether git was able to report which references changed.
	/// </summary>
	/// <remarks>
	/// <c>fetch --porcelain</c> arrived in git 2.41. Below that the fetch still runs and still
	/// succeeds, but no machine-readable account of it is available, and this library does not
	/// parse the human-facing alternative. So <see cref="Updates"/> is empty for two entirely
	/// different reasons, and this flag is what separates them.
	/// </remarks>
	public required bool DetailAvailable { get; init; }

	/// <summary>
	/// Gets a value indicating whether the fetch changed nothing.
	/// </summary>
	/// <remarks>
	/// False when <see cref="DetailAvailable"/> is false, whatever <see cref="Updates"/> holds:
	/// an empty list that could not be gathered is not evidence that nothing happened, and
	/// reporting it as "up to date" would be a silent lie.
	/// </remarks>
	public bool IsUpToDate => DetailAvailable && Updates.Count == 0;
}
```

- [ ] **Step 5: Write `GitPushResult`**

`GitIntegration/Models/GitPushResult.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What a push did to each reference it touched.
/// </summary>
public sealed record GitPushResult
{
	/// <summary>Gets the references the push touched, in the order git listed them.</summary>
	public required IReadOnlyList<GitRefUpdate> Updates { get; init; }

	/// <summary>
	/// Gets a value indicating whether git refused any of the updates.
	/// </summary>
	/// <remarks>
	/// A rejected push exits non-zero while still reporting a complete account of every reference,
	/// so this can be true on a result the caller obtained without an exception — see
	/// <c>IGitPushBuilder</c>, where <c>ExecuteAsync</c> and <c>TryExecuteAsync</c> deliberately
	/// treat rejection differently.
	/// </remarks>
	public bool HasRejections => Updates.Any(static update => update.IsRejected);
}
```

- [ ] **Step 6: Append the two exceptions**

Add to `GitIntegration/Execution/GitExceptions.cs`, after `GitNothingToCommitException`:

```csharp
/// <summary>
/// Git refused at least one of the references a push tried to update.
/// </summary>
/// <remarks>
/// Carries the parsed <see cref="Result"/> because this is the one failure in the library whose
/// output is exactly what the caller wanted: a rejected push exits non-zero and still prints a
/// complete porcelain record naming every reference and why each was refused. Throwing that detail
/// away would leave a caller with an exit code and some prose.
/// </remarks>
public sealed class GitPushRejectedException : GitCommandException
{
	/// <summary>Gets the parsed push result, or <see langword="null"/> when none was available.</summary>
	public GitPushResult? Result { get; }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	public GitPushRejectedException() { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitPushRejectedException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitPushRejectedException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitPushRejectedException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	/// <param name="result">The parsed account of what happened to each reference.</param>
	public GitPushRejectedException(
		string message,
		int exitCode,
		IReadOnlyList<string> arguments,
		string standardError,
		GitPushResult result)
		: base(message, exitCode, arguments, standardError) => Result = result;
}

/// <summary>
/// A pull merged cleanly enough to start but left conflicts in the working tree.
/// </summary>
/// <remarks>
/// <para>
/// The repository is now mid-merge, which no other verb in this library produces. Resolving it is
/// a non-goal of this design, but the state is inspectable: <c>status</c> reports each conflicted
/// path as an unmerged entry, so a caller can find out exactly what needs attention through
/// <c>GitRepository.Status()</c>.
/// </para>
/// <para>
/// Given its own type because git reports the conflict on standard <em>output</em> while exiting
/// 128, so without a dedicated classification it would be indistinguishable from any other pull
/// failure.
/// </para>
/// </remarks>
public sealed class GitPullConflictException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	public GitPullConflictException() { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitPullConflictException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitPullConflictException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitPullConflictException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteResultTests"`

Expected: PASS, 8 tests.

- [ ] **Step 8: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all pre-existing tests plus 8 new ones passing.

- [ ] **Step 9: Commit**

```bash
git add GitIntegration/Models/GitRefUpdate.cs GitIntegration/Models/GitFetchResult.cs GitIntegration/Models/GitPushResult.cs GitIntegration/Execution/GitExceptions.cs GitIntegration.Test/Models/GitRemoteResultTests.cs
git commit -m "[minor] Add remote sync result models and their exceptions"
```

---

## Task 2: `GitPushParser`

**Files:**
- Create: `GitIntegration/Parsing/GitPushParser.cs`
- Test: `GitIntegration.Test/Parsing/GitPushParserTests.cs`

**Interfaces:**
- Consumes: `GitPushResult`, `GitRefUpdate`, `GitRefUpdateKind` (Task 1); `GitParseValues`, `GitParseException` (Phase 3).
- Produces: `internal static class GitPushParser` — `internal static GitPushResult Parse(string output)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitPushParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPushParserTests
{
	private const string Tab = "\t";
	private const string To = "To C:/dev/origin.git\n";
	private const string Done = "Done\n";

	private static string Record(string flag, string refs, string summary) =>
		flag + Tab + refs + Tab + summary + "\n";

	[TestMethod]
	public void ReadsANewBranch()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("*", "refs/heads/main:refs/heads/main", "[new branch]") + Done);

		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[0].Kind);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Reference);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Source);
		Assert.AreEqual("[new branch]", result.Updates[0].Summary);
		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public void ReadsAFastForwardAndItsShaRange()
	{
		// A space is a real flag value, not padding, so the record cannot be trimmed before
		// splitting or the flag disappears.
		GitPushResult result = GitPushParser.Parse(
			To + Record(" ", "refs/heads/main:refs/heads/main", "66afe49..7c85857") + Done);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.FastForward, update.Kind);
		Assert.AreEqual("66afe49".As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual("7c85857".As<GitCommitSha>(), update.NewSha);
	}

	[TestMethod]
	public void ReadsAnUpToDateRefWithNoShaRange()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("=", "refs/heads/main:refs/heads/main", "[up to date]") + Done);

		Assert.AreEqual(GitRefUpdateKind.UpToDate, result.Updates[0].Kind);
		Assert.IsNull(result.Updates[0].OldSha);
		Assert.IsNull(result.Updates[0].NewSha);
	}

	[TestMethod]
	public void ReadsARejectionAndReportsItOnTheResult()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("!", "refs/heads/main:refs/heads/main", "[rejected] (fetch first)") + Done);

		Assert.AreEqual(GitRefUpdateKind.Rejected, result.Updates[0].Kind);
		Assert.IsTrue(result.Updates[0].IsRejected);
		Assert.IsTrue(result.HasRejections);
		Assert.AreEqual("[rejected] (fetch first)", result.Updates[0].Summary);
	}

	[TestMethod]
	public void ReadsADeletionWhoseLocalSideIsEmpty()
	{
		// git writes ":refs/heads/gone" for a delete — nothing is being sent, so the local side is
		// blank. Splitting on the colon and requiring both halves would throw here.
		GitPushResult result = GitPushParser.Parse(
			To + Record("-", ":refs/heads/gone", "[deleted]") + Done);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.Removed, update.Kind);
		Assert.AreEqual("refs/heads/gone".As<GitRefName>(), update.Reference);
		Assert.IsNull(update.Source);
	}

	[TestMethod]
	public void ReadsSeveralRecordsInOrder()
	{
		GitPushResult result = GitPushParser.Parse(
			To +
			Record("*", "refs/heads/a:refs/heads/a", "[new branch]") +
			Record("!", "refs/heads/b:refs/heads/b", "[rejected] (non-fast-forward)") +
			Done);

		Assert.AreEqual(2, result.Updates.Count);
		Assert.AreEqual("refs/heads/a".As<GitRefName>(), result.Updates[0].Reference);
		Assert.AreEqual("refs/heads/b".As<GitRefName>(), result.Updates[1].Reference);
		Assert.IsTrue(result.HasRejections);
	}

	[TestMethod]
	public void SkipsTheNonRecordLinesGitPrints()
	{
		// The "To <url>" header, the trailing "Done", and the tracking notice a -u push emits are
		// all on standard output alongside the records.
		GitPushResult result = GitPushParser.Parse(
			To +
			Record("*", "refs/heads/main:refs/heads/main", "[new branch]") +
			"branch 'main' set up to track 'origin/main'.\n" +
			Done);

		Assert.AreEqual(1, result.Updates.Count);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		GitPushResult result = GitPushParser.Parse(
			"To C:/dev/origin.git\r\n*\trefs/heads/main:refs/heads/main\t[new branch]\r\nDone\r\n");

		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual("[new branch]", result.Updates[0].Summary);
	}

	[TestMethod]
	public void ReportsAnUnknownFlagWithoutThrowing()
	{
		// Unlike the status codes, git's push flags are not a closed set this library can rely on
		// never growing, and failing an entire push report over one character would be worse than
		// naming the reference with an unknown kind.
		GitPushResult result = GitPushParser.Parse(
			To + Record("?", "refs/heads/main:refs/heads/main", "[something new]") + Done);

		Assert.AreEqual(GitRefUpdateKind.Unknown, result.Updates[0].Kind);
	}

	[TestMethod]
	public void ReturnsNoUpdatesForEmptyOutput() =>
		Assert.AreEqual(0, GitPushParser.Parse(string.Empty).Updates.Count);

	[TestMethod]
	public void RejectsARecordWithTooFewFields() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitPushParser.Parse(To + "*\trefs/heads/main:refs/heads/main\n" + Done));

	[TestMethod]
	public void RejectsARecordWhoseRefsFieldHasNoColon() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitPushParser.Parse(To + Record("*", "refs/heads/main", "[new branch]") + Done));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitPushParserTests"`

Expected: compilation failure — `GitPushParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitPushParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git push --porcelain</c>.
/// </summary>
/// <remarks>
/// Each record is <c>&lt;flag&gt;TAB&lt;local-ref&gt;:&lt;remote-ref&gt;TAB&lt;summary&gt;</c>.
/// Records are surrounded by lines that are not records — a leading <c>To &lt;url&gt;</c>, a
/// trailing <c>Done</c>, and a tracking notice when the push set an upstream — so the parser
/// recognises records by shape rather than by position.
/// </remarks>
internal static class GitPushParser
{
	private const int FieldCount = 3;

	/// <summary>
	/// Parses porcelain push output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed account of every reference the push touched.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitPushResult Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitRefUpdate> updates = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			// A record always carries two tabs. Nothing else git prints on this stream does, which
			// is what lets the header, the trailer, and the tracking notice be skipped by shape.
			if (record.Length == 0 || !record.Contains('\t', StringComparison.Ordinal))
			{
				continue;
			}

			updates.Add(ReadUpdate(record));
		}

		return new GitPushResult { Updates = updates };
	}

	private static GitRefUpdate ReadUpdate(string record)
	{
		string[] fields = record.Split('\t');

		if (fields.Length < FieldCount || fields[0].Length != 1)
		{
			throw new GitParseException($"Malformed push record: '{record}'.");
		}

		int colon = fields[1].IndexOf(':');

		if (colon < 0)
		{
			throw new GitParseException($"A push record's reference field has no colon: '{record}'.");
		}

		string source = fields[1][..colon];
		string destination = fields[1][(colon + 1)..];
		string summary = fields[2];
		(GitCommitSha? oldSha, GitCommitSha? newSha) = ReadShaRange(summary);

		return new GitRefUpdate
		{
			Kind = ToKind(fields[0][0]),
			Reference = GitParseValues.ToSemantic<GitRefName>(destination, "pushed reference"),

			// A deletion writes an empty local side — ":refs/heads/gone" — because nothing is being
			// sent, so an empty source is a normal record rather than a malformed one.
			Source = source.Length == 0
				? null
				: GitParseValues.ToSemantic<GitRefName>(source, "pushed source reference"),
			OldSha = oldSha,
			NewSha = newSha,
			Summary = summary,
		};
	}

	/// <summary>
	/// Pulls the object ids out of a summary that carries a commit range.
	/// </summary>
	/// <remarks>
	/// Push reports object ids only inside the summary, abbreviated, as <c>66afe49..7c85857</c>.
	/// Every other summary git writes there is bracketed prose, so anything without the separator
	/// simply has no range to report.
	/// </remarks>
	private static (GitCommitSha? OldSha, GitCommitSha? NewSha) ReadShaRange(string summary)
	{
		int separator = summary.IndexOf("..", StringComparison.Ordinal);

		if (separator <= 0)
		{
			return (null, null);
		}

		string before = summary[..separator];
		string after = summary[(separator + 2)..];

		return GitCommitSha.TryCreate(before, out GitCommitSha? oldSha) &&
			GitCommitSha.TryCreate(after, out GitCommitSha? newSha)
				? (oldSha, newSha)
				: (null, null);
	}

	private static GitRefUpdateKind ToKind(char flag) => flag switch
	{
		' ' => GitRefUpdateKind.FastForward,
		'+' => GitRefUpdateKind.Forced,
		'-' => GitRefUpdateKind.Removed,
		'*' => GitRefUpdateKind.Created,
		'!' => GitRefUpdateKind.Rejected,
		'=' => GitRefUpdateKind.UpToDate,
		't' => GitRefUpdateKind.TagUpdate,

		// Deliberately tolerant, unlike the status parser: git's push flags are not a closed set
		// this library can rely on never growing, and failing a whole push report over one
		// unrecognised character would be worse than naming the reference with an unknown kind.
		_ => GitRefUpdateKind.Unknown,
	};
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitPushParserTests"`

Expected: PASS, 12 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Parsing/GitPushParser.cs GitIntegration.Test/Parsing/GitPushParserTests.cs
git commit -m "[minor] Add the porcelain push parser"
```

---
## Task 3: `GitFetchParser`

**Files:**
- Create: `GitIntegration/Parsing/GitFetchParser.cs`
- Test: `GitIntegration.Test/Parsing/GitFetchParserTests.cs`

**Interfaces:**
- Consumes: `GitFetchResult`, `GitRefUpdate`, `GitRefUpdateKind` (Task 1); `GitParseValues`, `GitParseException`.
- Produces: `internal static class GitFetchParser` — `internal static GitFetchResult Parse(string output)`, always returning `DetailAvailable = true`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitFetchParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitFetchParserTests
{
	private const string OldSha = "7c85857fc85452150652c289fc94f1f912b96c40";
	private const string NewSha = "6702dd1957dedc4e0f54245bda65f3ddc5f37e64";

	private static string Record(string flag, string oldSha, string newSha, string reference) =>
		flag + " " + oldSha + " " + newSha + " " + reference + "\n";

	[TestMethod]
	public void ReadsAFastForward()
	{
		// The flag for a fast-forward is a space, so the captured line begins with two spaces —
		// the flag, then the separator. Trimming the line before splitting loses the flag.
		GitFetchResult result = GitFetchParser.Parse(
			Record(" ", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.AreEqual(1, result.Updates.Count);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.FastForward, update.Kind);
		Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), update.Reference);
		Assert.AreEqual(OldSha.As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual(NewSha.As<GitCommitSha>(), update.NewSha);
		Assert.IsNull(update.Source);
		Assert.AreEqual(string.Empty, update.Summary);
	}

	[TestMethod]
	public void MapsEveryFlagGitDocuments()
	{
		string output =
			Record(" ", OldSha, NewSha, "refs/remotes/origin/ff") +
			Record("+", OldSha, NewSha, "refs/remotes/origin/forced") +
			Record("-", OldSha, NewSha, "refs/remotes/origin/pruned") +
			Record("*", OldSha, NewSha, "refs/remotes/origin/created") +
			Record("!", OldSha, NewSha, "refs/remotes/origin/rejected") +
			Record("=", OldSha, NewSha, "refs/remotes/origin/same") +
			Record("t", OldSha, NewSha, "refs/tags/v1");

		GitFetchResult result = GitFetchParser.Parse(output);

		Assert.AreEqual(GitRefUpdateKind.FastForward, result.Updates[0].Kind);
		Assert.AreEqual(GitRefUpdateKind.Forced, result.Updates[1].Kind);
		Assert.AreEqual(GitRefUpdateKind.Removed, result.Updates[2].Kind);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[3].Kind);
		Assert.AreEqual(GitRefUpdateKind.Rejected, result.Updates[4].Kind);
		Assert.AreEqual(GitRefUpdateKind.UpToDate, result.Updates[5].Kind);
		Assert.AreEqual(GitRefUpdateKind.TagUpdate, result.Updates[6].Kind);
	}

	[TestMethod]
	public void ReportsDetailAsAvailable()
	{
		// This parser only ever runs when git supported --porcelain, so anything it produces is a
		// real account. The false case is set by the builder, not here.
		GitFetchResult result = GitFetchParser.Parse(
			Record(" ", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.IsTrue(result.DetailAvailable);
	}

	[TestMethod]
	public void TreatsNoOutputAsUpToDate()
	{
		// git prints nothing at all when a fetch changed nothing, and exits 0.
		GitFetchResult result = GitFetchParser.Parse(string.Empty);

		Assert.AreEqual(0, result.Updates.Count);
		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		// Built through Record so the flag and its separator are both present. Writing the leading
		// space by hand would shift every field one character left: the object ids would silently
		// parse one digit short while the assertion on Reference still passed.
		GitFetchResult result = GitFetchParser.Parse(
			Record(" ", OldSha, NewSha, "refs/remotes/origin/main").TrimEnd('\n') + "\r\n");

		Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), result.Updates[0].Reference);
		Assert.AreEqual(OldSha.As<GitCommitSha>(), result.Updates[0].OldSha);
		Assert.AreEqual(NewSha.As<GitCommitSha>(), result.Updates[0].NewSha);
	}

	[TestMethod]
	public void ReportsAnUnknownFlagWithoutThrowing()
	{
		GitFetchResult result = GitFetchParser.Parse(
			Record("?", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.AreEqual(GitRefUpdateKind.Unknown, result.Updates[0].Kind);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields() =>
		Assert.ThrowsExactly<GitParseException>(() => GitFetchParser.Parse(" " + OldSha + " " + NewSha + "\n"));

	[TestMethod]
	public void RejectsARecordWhoseObjectIdIsNotValid() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitFetchParser.Parse(Record(" ", "not-a-sha", NewSha, "refs/remotes/origin/main")));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitFetchParserTests"`

Expected: compilation failure — `GitFetchParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitFetchParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git fetch --porcelain</c>.
/// </summary>
/// <remarks>
/// Each record is <c>&lt;flag&gt;&lt;space&gt;&lt;old-id&gt; &lt;new-id&gt; &lt;local-ref&gt;</c>.
/// Unlike push, the object ids are full and explicit and there is no summary field. Available only
/// on git 2.41 and above; <see cref="GitFetchBuilder"/> decides whether this parser runs at all.
/// </remarks>
internal static class GitFetchParser
{
	private const int FieldCount = 3;

	/// <summary>
	/// Parses porcelain fetch output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed account of every reference the fetch changed.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitFetchResult Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitRefUpdate> updates = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			// Nothing at all is printed when the fetch changed nothing, so empty lines are the
			// ordinary case rather than a malformed record.
			if (record.Length == 0)
			{
				continue;
			}

			updates.Add(ReadUpdate(record));
		}

		return new GitFetchResult { Updates = updates, DetailAvailable = true };
	}

	private static GitRefUpdate ReadUpdate(string record)
	{
		// Character zero is the flag and character one is the separator, so the fields begin at
		// index two. A fast-forward's flag is a space, which is why the record cannot be trimmed
		// or split on whitespace generally — the flag would vanish into the delimiter.
		if (record.Length < 2)
		{
			throw new GitParseException($"Malformed fetch record: '{record}'.");
		}

		char flag = record[0];
		string[] fields = record[2..].Split(' ', FieldCount);

		if (fields.Length < FieldCount)
		{
			throw new GitParseException($"Malformed fetch record: '{record}'.");
		}

		return new GitRefUpdate
		{
			Kind = ToKind(flag),
			Reference = GitParseValues.ToSemantic<GitRefName>(fields[2], "fetched reference"),
			OldSha = GitParseValues.ToSemantic<GitCommitSha>(fields[0], "fetched old object id"),
			NewSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "fetched new object id"),
		};
	}

	private static GitRefUpdateKind ToKind(char flag) => flag switch
	{
		' ' => GitRefUpdateKind.FastForward,
		'+' => GitRefUpdateKind.Forced,
		'-' => GitRefUpdateKind.Removed,
		'*' => GitRefUpdateKind.Created,
		'!' => GitRefUpdateKind.Rejected,
		'=' => GitRefUpdateKind.UpToDate,
		't' => GitRefUpdateKind.TagUpdate,
		_ => GitRefUpdateKind.Unknown,
	};
}
```

Note that `Source` and `Summary` are deliberately left at their defaults: fetch reports neither.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitFetchParserTests"`

Expected: PASS, 8 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Parsing/GitFetchParser.cs GitIntegration.Test/Parsing/GitFetchParserTests.cs
git commit -m "[minor] Add the porcelain fetch parser"
```

---

## Task 4: `GitPushBuilder`

The verb whose two entry points deliberately differ.

**Files:**
- Create: `GitIntegration/Builders/GitPushBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitPushBuilderTests.cs`

**Interfaces:**
- Consumes: `GitPushResult`, `GitPushRejectedException` (Task 1); `GitPushParser` (Task 2); `GitRemoteName`, `GitBranchName`, `AppendOperands`, the `Progress` seam (Phase 4 Task 1).
- Produces:
  - `public interface IGitPushBuilder : IGitCommandBuilder<GitPushResult>` with `ToRemote(GitRemoteName)`, `WithBranch(GitBranchName)`, `SettingUpstream()`, `Force()`, `ForceWithLease()`, `DeletingRemoteBranch()`, `DryRun()`, `ReportingProgress(IProgress<string>)`.
  - `internal sealed class GitPushBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitPushBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPushBuilderTests
{
	private const string RejectedOutput =
		"To C:/dev/origin.git\n" +
		"!\trefs/heads/main:refs/heads/main\t[rejected] (fetch first)\n" +
		"Done\n";

	private const string AcceptedOutput =
		"To C:/dev/origin.git\n" +
		"*\trefs/heads/main:refs/heads/main\t[new branch]\n" +
		"Done\n";

	[TestMethod]
	public void BuildsTheDefaultPushVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"push",
			"--porcelain",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.SettingUpstream().Force().DryRun().DeletingRemoteBranch();

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--set-upstream");
		CollectionAssert.Contains(arguments, "--force");
		CollectionAssert.Contains(arguments, "--dry-run");
		CollectionAssert.Contains(arguments, "--delete");
	}

	[TestMethod]
	public void ForceWithLeaseReplacesAPlainForce()
	{
		// --force and --force-with-lease both overwrite, but the lease refuses when the remote
		// moved since it was last fetched. Emitting both would let the blunter one win silently.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Force().ForceWithLease();

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--force-with-lease");
		CollectionAssert.DoesNotContain(arguments, "--force");
	}

	[TestMethod]
	public void PutsTheRemoteAndBranchBehindTheEndOfOptionsMarkerInOrder()
	{
		// git push <remote> <refspec> is positional; reversed, git looks for a remote named after
		// the branch.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ToRemote("origin".As<GitRemoteName>()).WithBranch("main".As<GitBranchName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void EmitsNoMarkerWhenNeitherRemoteNorBranchIsGiven()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void RejectsABranchWithoutARemote()
	{
		// git push <refspec> with no remote pushes to the configured upstream and reads the first
		// operand as the remote, so a branch alone would be silently misread.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithBranch("main".As<GitBranchName>());

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ToRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ExecuteReturnsTheParsedResultOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = AcceptedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitPushResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, result.Updates.Count);
		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public async Task ExecuteThrowsRejectedAndCarriesTheParsedResultAsync()
	{
		// The heart of this verb: git exits 1 and still prints a full account, so the exception
		// has to carry it or the caller learns nothing they could act on.
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardOutput = RejectedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitPushRejectedException exception = await Assert.ThrowsExactlyAsync<GitPushRejectedException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsNotNull(exception.Result);
		Assert.IsTrue(exception.Result.HasRejections);
		Assert.AreEqual("[rejected] (fetch first)", exception.Result.Updates[0].Summary);
		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReturnsTheResultRatherThanAnErrorForARejectionAsync()
	{
		// The deliberate divergence: git ran and told us exactly what happened, so "try" reports it
		// as a value the caller inspects rather than as an opaque failure.
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardOutput = RejectedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitPushResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsNotNull(result.Value);
		Assert.IsTrue(result.Value.HasRejections);
	}

	[TestMethod]
	public async Task AGenuineFailureStillThrowsACommandExceptionAsync()
	{
		// No porcelain records at all means git never got as far as talking about references —
		// a bad remote, a network failure, an auth failure. That is an ordinary command failure.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReportsAGenuineFailureAsAnErrorAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitPushResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheRequestAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = AcceptedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitPushBuilderTests"`

Expected: compilation failure — `GitPushBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitPushBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Sends local commits to a remote.
/// </summary>
/// <remarks>
/// The one verb in this library whose two entry points differ in more than exception-versus-result,
/// because a rejected push exits non-zero while still printing a complete account of every
/// reference. <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/> stays strict and throws
/// <see cref="GitPushRejectedException"/>, which carries that account so nothing is lost.
/// <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/> returns the account as a value and
/// leaves the caller to check <see cref="GitPushResult.HasRejections"/> — git ran and said exactly
/// what happened, which is what "try" should surface.
/// </remarks>
public interface IGitPushBuilder : IGitCommandBuilder<GitPushResult>
{
	/// <summary>Pushes to this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to push to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder ToRemote(GitRemoteName name);

	/// <summary>
	/// Pushes this branch. Requires a remote, which git reads as the first positional operand.
	/// </summary>
	/// <remarks>
	/// The requirement is checked when the argument vector is built, not here: a caller may set the
	/// branch before the remote, so only the finished configuration knows whether the pair is
	/// complete. <c>BuildArguments</c> therefore throws <see cref="InvalidOperationException"/> for
	/// a branch with no remote — a configuration error, not I/O, so its purity is intact.
	/// </remarks>
	/// <param name="name">The branch to push.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder WithBranch(GitBranchName name);

	/// <summary>Records the pushed branch as the local branch's upstream.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder SettingUpstream();

	/// <summary>Overwrites the remote branch even when that discards commits.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder Force();

	/// <summary>
	/// Overwrites the remote branch, but only if it has not moved since it was last fetched.
	/// </summary>
	/// <remarks>Replaces <see cref="Force"/> when both are requested, being the safer of the two.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder ForceWithLease();

	/// <summary>Deletes the named branch on the remote rather than updating it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder DeletingRemoteBranch();

	/// <summary>Reports what would happen without changing anything on the remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder DryRun();

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git push --porcelain</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPushBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitPushResult>(runner, repositoryPath), IGitPushBuilder
{
	private GitRemoteName? _remote;
	private GitBranchName? _branch;
	private bool _setUpstream;
	private bool _force;
	private bool _forceWithLease;
	private bool _delete;
	private bool _dryRun;

	/// <inheritdoc />
	public IGitPushBuilder ToRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder SettingUpstream()
	{
		_setUpstream = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder ForceWithLease()
	{
		_forceWithLease = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder DeletingRemoteBranch()
	{
		_delete = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder DryRun()
	{
		_dryRun = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("push");

		// Unconditional: it is what makes the outcome machine-readable, and it is the only reason
		// a rejected push can be reported as data rather than as prose.
		arguments.Add("--porcelain");

		if (_setUpstream)
		{
			arguments.Add("--set-upstream");
		}

		// The lease wins when both were asked for: it refuses if the remote moved since it was last
		// fetched, so letting the blunter flag override it would quietly remove that protection.
		if (_forceWithLease)
		{
			arguments.Add("--force-with-lease");
		}
		else if (_force)
		{
			arguments.Add("--force");
		}

		if (_delete)
		{
			arguments.Add("--delete");
		}

		if (_dryRun)
		{
			arguments.Add("--dry-run");
		}

		AppendRefspec(arguments);
	}

	private void AppendRefspec(ICollection<string> arguments)
	{
		if (_remote is null)
		{
			// git push <refspec> with no remote reads the first operand as the remote, so a branch
			// on its own would be silently misinterpreted rather than rejected.
			if (_branch is not null)
			{
				throw new InvalidOperationException(
					"A branch was given without a remote. git reads the first operand as the remote name, " +
					"so call ToRemote as well.");
			}

			return;
		}

		if (_branch is null)
		{
			AppendOperands(arguments, _remote.WeakString);
			return;
		}

		AppendOperands(arguments, _remote.WeakString, _branch.WeakString);
	}

	/// <inheritdoc />
	protected override GitPushResult ParseResult(GitProcessResult result) =>
		GitPushParser.Parse(Ensure.NotNull(result).StandardOutput);

	/// <inheritdoc />
	public override async Task<GitPushResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await RunAsync(cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return ParseResult(result);
		}

		// A non-zero exit means one of two very different things. If porcelain records came back,
		// git got far enough to talk about references and refused some of them, and the caller
		// wants that detail. If none came back, git never reached the remote at all.
		GitPushResult parsed = ParseResult(result);

		throw parsed.Updates.Count > 0
			? new GitPushRejectedException(
				$"git refused at least one reference: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError,
				parsed)
			: CreateException(result);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitPushResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await RunAsync(cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return GitResult<GitPushResult>.FromValue(ParseResult(result));
		}

		GitPushResult parsed = ParseResult(result);

		// Deliberately a success carrying rejections rather than an error: git ran and reported
		// precisely what happened to every reference, so there is a value to return. Callers
		// distinguish the two with GitPushResult.HasRejections.
		return parsed.Updates.Count > 0
			? GitResult<GitPushResult>.FromValue(parsed)
			: GitResult<GitPushResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	private Task<GitProcessResult> RunAsync(CancellationToken cancellationToken) =>
		Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitPushBuilderTests"`

Expected: PASS, 13 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitPushBuilder.cs GitIntegration.Test/Builders/GitPushBuilderTests.cs
git commit -m "[minor] Add the push verb builder"
```

---
## Task 5: `GitFetchBuilder`

The first verb whose argument vector depends on a runtime probe of the installed git.

**Files:**
- Create: `GitIntegration/Builders/GitFetchBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitFetchBuilderTests.cs`

**Interfaces:**
- Consumes: `GitFetchResult` (Task 1); `GitFetchParser` (Task 3); `GitVersionBuilder` and `GitVersion.AtLeast` (Phase 3); `GitRemoteName`, `AppendOperands`, the `Progress` seam.
- Produces:
  - `public interface IGitFetchBuilder : IGitCommandBuilder<GitFetchResult>` with `FromRemote(GitRemoteName)`, `AllRemotes()`, `Prune()`, `WithTags()`, `WithDepth(int)`, `ReportingProgress(IProgress<string>)`.
  - `internal sealed class GitFetchBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

### How the version probe works

`--porcelain` on fetch arrived in git 2.41. `BuildArguments()` is documented as pure with no I/O, so it **cannot** run a probe — instead it emits the vector for whichever mode was already decided, defaulting to porcelain. `ExecuteAsync` and `TryExecuteAsync` probe first with `GitVersionBuilder`, set the mode, then delegate to the base. This is the same probe-then-delegate shape `GitInitBuilder` uses, and the mutable field is safe for the same reason: builders are single-use and not thread-safe by contract.

Below 2.41 the fetch still runs; only the itemised report is unavailable, and `GitFetchResult.DetailAvailable` says so. **Do not add a stderr parser for older git** — the design forbids parsing human output, and that rule is more load-bearing than the itemisation it would buy.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitFetchBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitFetchBuilderTests
{
	private const string OldSha = "7c85857fc85452150652c289fc94f1f912b96c40";
	private const string NewSha = "6702dd1957dedc4e0f54245bda65f3ddc5f37e64";

	private const string PorcelainOutput =
		" " + OldSha + " " + NewSha + " refs/remotes/origin/main\n";

	private static ScriptedGitProcessRunner RunnerOn(string version, string fetchOutput) =>
		new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version " + version + "\n")
			.Then(standardOutput: fetchOutput);

	[TestMethod]
	public void BuildsThePorcelainVectorByDefault()
	{
		// BuildArguments is documented as pure with no I/O, so it cannot probe. It emits the
		// porcelain form until an execution path tells it otherwise.
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"fetch",
			"--porcelain",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.AllRemotes().Prune().WithTags().WithDepth(1);

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--all");
		CollectionAssert.Contains(arguments, "--prune");
		CollectionAssert.Contains(arguments, "--tags");
		CollectionAssert.Contains(arguments, "--depth=1");
	}

	[TestMethod]
	public void PutsTheRemoteBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FromRemote("origin".As<GitRemoteName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("origin", arguments[marker + 1]);
	}

	[TestMethod]
	public void RejectsANonPositiveDepth()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(-1));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.FromRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ProbesTheVersionThenFetchesWithPorcelainOnModernGitAsync()
	{
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1.windows.1", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.DetailAvailable);
		Assert.AreEqual(1, result.Updates.Count);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--version");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task OmitsPorcelainAndReportsNoDetailOnOlderGitAsync()
	{
		// 2.40 predates fetch --porcelain. The fetch must still happen; only the itemised report
		// is unavailable, and DetailAvailable is what tells the caller which of the two empty-list
		// meanings applies.
		ScriptedGitProcessRunner runner = RunnerOn("2.40.1", string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.DetailAvailable);
		Assert.AreEqual(0, result.Updates.Count);
		Assert.IsFalse(result.IsUpToDate);

		CollectionAssert.DoesNotContain(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task TreatsExactlyTwoFortyOneAsSupportedAsync()
	{
		// The documented floor, asserted exactly: an off-by-one here silently disables porcelain
		// for every user on the first version that supports it.
		ScriptedGitProcessRunner runner = RunnerOn("2.41.0", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.DetailAvailable);
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task ReportsAnUpToDateFetchOnModernGitAsync()
	{
		// git prints nothing when a fetch changed nothing, which is a genuine "up to date" — as
		// distinct from the empty list an old git produces.
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1", string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public async Task ThrowsWhenTheFetchItselfFailsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardError: "fatal: 'nosuch' does not appear to be a git repository\n", exitCode: 128);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReportsAFetchFailureAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardError: "fatal: could not read from remote\n", exitCode: 128);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheFetchRequestAsync()
	{
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, runner.Invocations.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitFetchBuilderTests"`

Expected: compilation failure — `GitFetchBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitFetchBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Downloads objects and refs from a remote without touching the working tree.
/// </summary>
public interface IGitFetchBuilder : IGitCommandBuilder<GitFetchResult>
{
	/// <summary>Fetches from this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to fetch from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitFetchBuilder FromRemote(GitRemoteName name);

	/// <summary>Fetches from every configured remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder AllRemotes();

	/// <summary>Deletes remote-tracking branches whose counterparts no longer exist on the remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder Prune();

	/// <summary>Fetches tags as well as branches.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder WithTags();

	/// <summary>Limits history to this many commits per branch.</summary>
	/// <param name="depth">How many commits of history to fetch. Must be positive.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
	public IGitFetchBuilder WithDepth(int depth);

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitFetchBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git fetch</c>, with <c>--porcelain</c> where the installed git supports it.
/// </summary>
/// <remarks>
/// <c>fetch --porcelain</c> arrived in git 2.41. Below that this builder still fetches, but returns
/// a result whose <see cref="GitFetchResult.DetailAvailable"/> is false: the work happened and only
/// the itemised account is missing. It deliberately does not fall back to parsing git's human
/// output, which the design forbids for every other verb and which git has changed before.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="repositoryPath">The repository to scope the commands to.</param>
internal sealed class GitFetchBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitFetchResult>(runner, repositoryPath), IGitFetchBuilder
{
	/// <summary>The first git release whose <c>fetch</c> understands <c>--porcelain</c>.</summary>
	private const int PorcelainMajor = 2;
	private const int PorcelainMinor = 41;

	private GitRemoteName? _remote;
	private int? _depth;
	private bool _allRemotes;
	private bool _prune;
	private bool _tags;

	// Defaults true so BuildArguments — which is pure and cannot probe — emits the modern form.
	// The execution paths set it from an actual version probe before the vector is built.
	private bool _porcelainSupported = true;

	/// <inheritdoc />
	public IGitFetchBuilder FromRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder AllRemotes()
	{
		_allRemotes = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder Prune()
	{
		_prune = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder WithTags()
	{
		_tags = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder WithDepth(int depth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
		_depth = depth;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("fetch");

		if (_porcelainSupported)
		{
			arguments.Add("--porcelain");
		}

		if (_allRemotes)
		{
			arguments.Add("--all");
		}

		if (_prune)
		{
			arguments.Add("--prune");
		}

		if (_tags)
		{
			arguments.Add("--tags");
		}

		if (_depth is int depth)
		{
			arguments.Add("--depth=" + depth.ToString(CultureInfo.InvariantCulture));
		}

		if (_remote is not null)
		{
			AppendOperands(arguments, _remote.WeakString);
		}
	}

	/// <inheritdoc />
	protected override GitFetchResult ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		// Without --porcelain there is no machine-readable account to parse, and this library does
		// not read the human alternative. The fetch still happened; only the itemisation is absent,
		// which DetailAvailable records so an empty list is not mistaken for "nothing changed".
		return _porcelainSupported
			? GitFetchParser.Parse(result.StandardOutput)
			: new GitFetchResult { Updates = [], DetailAvailable = false };
	}

	/// <inheritdoc />
	public override async Task<GitFetchResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitFetchResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Asks the installed git what version it is, so the vector can be built to suit.
	/// </summary>
	/// <remarks>
	/// A separate invocation rather than something <see cref="BuildArguments"/> could do, because
	/// that method is documented as a pure computation with no I/O — it is what makes the emitted
	/// command inspectable in a test without running anything.
	/// </remarks>
	private async Task ProbeVersionAsync(CancellationToken cancellationToken)
	{
		GitVersion version = await new GitVersionBuilder(Runner)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_porcelainSupported = version.AtLeast(PorcelainMajor, PorcelainMinor);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitFetchBuilderTests"`

Expected: PASS, 12 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitFetchBuilder.cs GitIntegration.Test/Builders/GitFetchBuilderTests.cs
git commit -m "[minor] Add the fetch verb builder with its version probe"
```

---

## Task 6: `GitPullBuilder`

**Files:**
- Create: `GitIntegration/Builders/GitPullBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitPullBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCompleted` (Phase 4), `GitPullConflictException` (Task 1); `GitRemoteName`, `GitBranchName`, `AppendOperands`, the `Progress` seam.
- Produces:
  - `public interface IGitPullBuilder : IGitCommandBuilder<GitCompleted>` with `FromRemote(GitRemoteName)`, `WithBranch(GitBranchName)`, `FastForwardOnly()`, `Rebase()`, `Prune()`, `ReportingProgress(IProgress<string>)`.
  - `internal sealed class GitPullBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

### Why `GitCompleted` and not a richer result

`pull` is a fetch followed by a merge, and everything it prints is human prose — no porcelain form exists. Rather than invent a parser for it, the verb reports success or failure and leaves the caller to ask `Status()` and `Log()` what actually changed, which are already precise. A conflict is the one outcome worth its own type, because the repository is left mid-merge.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitPullBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPullBuilderTests
{
	private const string ConflictOutput =
		"Auto-merging c.txt\n" +
		"CONFLICT (content): Merge conflict in c.txt\n" +
		"Automatic merge failed; fix conflicts and then commit the result.\n";

	[TestMethod]
	public void BuildsTheDefaultPullVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"pull",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitPullBuilder ffOnly = new(runner, TestPaths.Root);
		_ = ffOnly.FastForwardOnly();
		CollectionAssert.Contains(ffOnly.BuildArguments().ToArray(), "--ff-only");

		GitPullBuilder rebase = new(runner, TestPaths.Root);
		_ = rebase.Rebase();
		CollectionAssert.Contains(rebase.BuildArguments().ToArray(), "--rebase");

		GitPullBuilder prune = new(runner, TestPaths.Root);
		_ = prune.Prune();
		CollectionAssert.Contains(prune.BuildArguments().ToArray(), "--prune");
	}

	[TestMethod]
	public void RejectsAskingForBothFastForwardOnlyAndRebase()
	{
		// git accepts both and lets one win silently. Since they mean opposite things about
		// history — refuse to merge, versus rewrite to avoid merging — a caller who asked for both
		// has a bug, and reporting it beats guessing which they meant.
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FastForwardOnly().Rebase();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void PutsTheRemoteAndBranchBehindTheMarkerInOrder()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FromRemote("origin".As<GitRemoteName>()).WithBranch("main".As<GitBranchName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void RejectsABranchWithoutARemote()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithBranch("main".As<GitBranchName>());

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.FromRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ExecuteSucceedsOnACleanPullAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput = "Updating 4bafe6a..0631bf6\nFast-forward\n c.txt | 1 +\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ThrowsConflictWhenTheMergeLeavesConflictsAsync()
	{
		// Captured from git 2.50: the conflict is reported on STANDARD OUTPUT with exit 128, so the
		// base classifier — which reads standard error — cannot see it.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = ConflictOutput,
			StandardError = "From C:/dev/origin\n * branch main -> FETCH_HEAD\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitPullConflictException exception = await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
		StringAssert.Contains(exception.Message, "c.txt");
	}

	[TestMethod]
	public async Task RecognisesARebaseConflictTooAsync()
	{
		// A rebase reports its conflicts with different prose but the same "CONFLICT" marker, and
		// leaves the repository mid-rebase rather than mid-merge. Both are conflicts to a caller.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardOutput = "CONFLICT (content): Merge conflict in c.txt\n",
			StandardError = "error: could not apply 0631bf6... mine\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnOrdinaryPullFailureStaysAGenericCommandExceptionAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsAConflictWithItsDiagnosticTextAsync()
	{
		// The conflict text is on standard output, so an error built only from standard error would
		// carry the fetch progress and nothing about the conflict.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = ConflictOutput,
			StandardError = string.Empty,
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		StringAssert.Contains(result.Error?.StandardError ?? string.Empty, "CONFLICT");
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitPullBuilderTests"`

Expected: compilation failure — `GitPullBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitPullBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Fetches from a remote and integrates the result into the current branch.
/// </summary>
/// <remarks>
/// Returns only success or failure. Everything <c>pull</c> prints is human prose with no porcelain
/// alternative, so rather than invent a parser for it this verb leaves the caller to ask
/// <c>Status()</c> and <c>Log()</c> what changed — both of which are precise. The one outcome worth
/// its own type is a conflict, because it leaves the repository mid-merge.
/// </remarks>
public interface IGitPullBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Pulls from this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to pull from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder FromRemote(GitRemoteName name);

	/// <summary>Pulls this branch. Requires a remote, which git reads as the first operand.</summary>
	/// <remarks>
	/// Checked when the argument vector is built rather than here, because a caller may set the
	/// branch before the remote and only the finished configuration knows whether the pair is
	/// complete. The same applies to asking for both <see cref="FastForwardOnly"/> and
	/// <see cref="Rebase"/>. Both raise <see cref="InvalidOperationException"/> from
	/// <c>BuildArguments</c>, which is a configuration error rather than I/O.
	/// </remarks>
	/// <param name="name">The branch to pull.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder WithBranch(GitBranchName name);

	/// <summary>
	/// Refuses to pull at all when the result would need a merge commit.
	/// </summary>
	/// <remarks>Cannot be combined with <see cref="Rebase"/>; the two mean opposite things.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder FastForwardOnly();

	/// <summary>
	/// Replays local commits on top of the fetched ones instead of merging.
	/// </summary>
	/// <remarks>Cannot be combined with <see cref="FastForwardOnly"/>.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Rebase();

	/// <summary>Deletes remote-tracking branches whose counterparts are gone, as part of the fetch.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Prune();

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git pull</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPullBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitPullBuilder
{
	private GitRemoteName? _remote;
	private GitBranchName? _branch;
	private bool _fastForwardOnly;
	private bool _rebase;
	private bool _prune;

	/// <inheritdoc />
	public IGitPullBuilder FromRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder FastForwardOnly()
	{
		_fastForwardOnly = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Rebase()
	{
		_rebase = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Prune()
	{
		_prune = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		// git accepts both and lets one quietly win. They mean opposite things about history —
		// refuse to merge, versus rewrite so no merge is needed — so a caller who asked for both
		// has a bug worth reporting rather than a preference worth guessing.
		if (_fastForwardOnly && _rebase)
		{
			throw new InvalidOperationException(
				"FastForwardOnly and Rebase cannot both be requested: one refuses to create a merge, " +
				"the other rewrites history to avoid needing one.");
		}

		arguments.Add("pull");

		if (_fastForwardOnly)
		{
			arguments.Add("--ff-only");
		}

		if (_rebase)
		{
			arguments.Add("--rebase");
		}

		if (_prune)
		{
			arguments.Add("--prune");
		}

		if (_remote is null)
		{
			if (_branch is not null)
			{
				throw new InvalidOperationException(
					"A branch was given without a remote. git reads the first operand as the remote name, " +
					"so call FromRemote as well.");
			}

			return;
		}

		if (_branch is null)
		{
			AppendOperands(arguments, _remote.WeakString);
			return;
		}

		AppendOperands(arguments, _remote.WeakString, _branch.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };

	/// <summary>
	/// Classifies a failed pull, recognising a conflict as its own outcome.
	/// </summary>
	/// <remarks>
	/// Overridden because the base class inspects standard error while git announces a conflict on
	/// standard <em>output</em> — the same trap <c>commit</c> sets with "nothing to commit". Both
	/// a merge conflict and a rebase conflict carry the word CONFLICT, so one match covers both,
	/// and the <c>LC_ALL=C</c> that every invocation runs under is what makes it dependable.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return result.StandardOutput.Contains("CONFLICT", StringComparison.Ordinal)
			? new GitPullConflictException(
				"The pull left conflicts in the working tree. Use Status() to see which paths " +
				$"are unmerged: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError)
			: base.CreateException(result);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCompleted>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return GitResult<GitCompleted>.FromValue(ParseResult(result));
		}

		// Pull is the second verb whose diagnostic lands on standard output, so an error built only
		// from standard error would carry the fetch progress and say nothing about the conflict.
		return GitResult<GitCompleted>.FromError(new GitCommandError
		{
			ExitCode = result.ExitCode,
			Arguments = result.Arguments,
			// Both streams, newline-separated so they stay readable: the fetch progress lands on
			// standard error while the conflict itself lands on standard output, and a caller
			// diagnosing a failed pull wants each of them.
			StandardError = string.IsNullOrWhiteSpace(result.StandardError)
				? result.StandardOutput
				: result.StandardError.TrimEnd('
') + "
" + result.StandardOutput,
		});
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitPullBuilderTests"`

Expected: PASS, 11 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitPullBuilder.cs GitIntegration.Test/Builders/GitPullBuilderTests.cs
git commit -m "[minor] Add the pull verb builder"
```

---
## Task 7: Wiring into `GitRepository`

**Files:**
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/GitRepositoryRemoteVerbTests.cs`

**Interfaces:**
- Consumes: `IGitFetchBuilder`/`GitFetchBuilder` (Task 5), `IGitPullBuilder`/`GitPullBuilder` (Task 6), `IGitPushBuilder`/`GitPushBuilder` (Task 4).
- Produces: `GitRepository.Fetch()`, `GitRepository.Pull()`, `GitRepository.Push()`, each returning a fresh builder and each routed through the existing private `RequireRunner()`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/GitRepositoryRemoteVerbTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

[TestClass]
public class GitRepositoryRemoteVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	[TestMethod]
	public void EveryRemoteVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Fetch().BuildArguments()],
			[.. repository.Pull().BuildArguments()],
			[.. repository.Push().BuildArguments()],
		];

		foreach (string[] vector in vectors)
		{
			Assert.AreEqual("-C", vector[0]);
			Assert.AreEqual(TestPaths.Root.WeakString, vector[1]);
		}
	}

	[TestMethod]
	public void EachRemoteVerbEmitsItsOwnCommand()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		CollectionAssert.Contains(repository.Fetch().BuildArguments().ToArray(), "fetch");
		CollectionAssert.Contains(repository.Pull().BuildArguments().ToArray(), "pull");
		CollectionAssert.Contains(repository.Push().BuildArguments().ToArray(), "push");
	}

	[TestMethod]
	public void EachCallReturnsAFreshBuilder()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Fetch(), repository.Fetch());
		Assert.AreNotSame(repository.Pull(), repository.Pull());
		Assert.AreNotSame(repository.Push(), repository.Push());
	}

	[TestMethod]
	public void EveryRemoteVerbRequiresAProcessRunner()
	{
		// A repository carrying hosting metadata only describes something that may not exist on
		// disk. Deleting the guard from any one of these must fail this test.
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Fetch());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Pull());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Push());
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryRemoteVerbTests"`

Expected: compilation failure — none of the three factories exist.

- [ ] **Step 3: Add the three factories**

Read `GitIntegration/GitRepository.cs` in full first. Insert these after the existing `SetRemoteUrl` factory and before the private `RequireRunner()`. Everything already in the file — the read-only factories, the Phase 4 mutating factories, `IsClonedAsync`, `OpenWebClient`, `IsBrowsableUri` — stays untouched.

```csharp
	/// <summary>Downloads objects and refs from a remote without touching the working tree.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitFetchBuilder Fetch() => new GitFetchBuilder(RequireRunner(), LocalPath);

	/// <summary>Fetches from a remote and integrates the result into the current branch.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitPullBuilder Pull() => new GitPullBuilder(RequireRunner(), LocalPath);

	/// <summary>Sends local commits to a remote.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitPushBuilder Push() => new GitPushBuilder(RequireRunner(), LocalPath);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryRemoteVerbTests"`

Expected: PASS, 4 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/GitRepository.cs GitIntegration.Test/GitRepositoryRemoteVerbTests.cs
git commit -m "[minor] Wire the remote sync verbs into GitRepository"
```

---

## Task 8: Integration tests against a local remote

**Files:**
- Create: `GitIntegration.Test/Integration/GitRemoteSyncTests.cs`
- Test: the same file

**Interfaces:**
- Consumes: everything from Tasks 1–7, plus Phase 4's `TemporaryRepository` helper and Phase 4's `GitRoundTripTests` conventions.
- Produces: no library types.

### What makes these different from Phase 4's

They need **two** repositories and a bare one in between. A bare repository can act as a remote over a plain filesystem path, so these tests exercise the real fetch/pull/push machinery with no network and no credentials.

Reuse the conventions Phase 4 established and do not re-derive them:

- `RequireGitAsync` self-skips when git is absent, unless `KTSU_GIT_INTEGRATION_TESTS_REQUIRED` is set — which CI sets, so a runner without git fails rather than reporting a green suite. Copy the same helper shape.
- Identity (`user.name`, `user.email`) and `commit.gpgsign false` are written into **each repository's own config**, never globally, so the tests neither depend on nor disturb the host.
- The initial branch is named explicitly, because `init.defaultBranch` varies by machine.
- `TemporaryRepository` handles GUID-named temp roots and the Windows read-only cleanup.

- [ ] **Step 1: Write the tests**

`GitIntegration.Test/Integration/GitRemoteSyncTests.cs`:

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
/// Exercises fetch, pull, and push against a real git binary and a real remote.
/// </summary>
/// <remarks>
/// The remote is a bare repository on the local filesystem, which git treats exactly like any other
/// remote. That gives real push negotiation and real rejection behaviour with no network and no
/// credentials — the two things that would make these tests flaky or unrunnable in CI.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitRemoteSyncTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();
	private static readonly GitRemoteName Origin = "origin".As<GitRemoteName>();
	private static readonly GitBranchName Main = "main".As<GitBranchName>();

	private static GitClient CreateClient() =>
		new(new RunCommandGitProcessRunner(new GitOptions()), new NativeFileSystemProvider());

	private static async Task RequireGitAsync(CancellationToken cancellationToken)
	{
		try
		{
			_ = await CreateClient().GetVersionAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (GitExecutableNotFoundException) when (
			!GitRoundTripTests.IsGitRequired(
				Environment.GetEnvironmentVariable(GitRoundTripTests.RequiredEnvironmentVariable)))
		{
			Assert.Inconclusive("git is not on PATH, so the integration tests were skipped.");
		}
	}

	/// <summary>Creates a bare repository that can stand in for a remote.</summary>
	private static async Task<AbsoluteDirectoryPath> CreateBareRemoteAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await CreateClient()
			.Init(temporary.Root)
			.Bare()
			.WithInitialBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(init.AlreadyExisted);

		return init.Repository.LocalPath;
	}

	/// <summary>
	/// Creates a working repository with a deterministic identity, wired to a remote.
	/// </summary>
	private static async Task<GitRepository> CreateWorkingCopyAsync(
		TemporaryRepository temporary,
		AbsoluteDirectoryPath remote,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await CreateClient()
			.Init(temporary.Root)
			.WithInitialBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository repository = init.Repository;
		IGitProcessRunner runner = repository.ProcessRunner!;

		// Written into this repository's own config, never globally: the tests must not depend on
		// the host having an identity, nor disturb the one it has. Signing is disabled for the same
		// reason — a developer with commit.gpgsign set globally would otherwise fail every commit.
		foreach ((string key, string value) in new[]
		{
			("user.name", AuthorName.WeakString),
			("user.email", AuthorEmail.WeakString),
			("commit.gpgsign", "false"),
		})
		{
			_ = await new GitTextBuilder(runner, repository.LocalPath, "config", key, value)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		}

		_ = await repository
			.AddRemote(Origin, remote.WeakString.As<GitRepositoryRemotePath>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	private static async Task<GitCommit> CommitFileAsync(
		GitRepository repository,
		TemporaryRepository temporary,
		string name,
		string contents,
		string message,
		CancellationToken cancellationToken)
	{
		temporary.WriteFile(name, contents);

		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return await repository
			.Commit(message.As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task PushCreatesTheBranchOnTheRemoteAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository workingDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);
		GitRepository repository = await CreateWorkingCopyAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(repository, workingDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);

		GitPushResult result = await repository.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.SettingUpstream()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(result.HasRejections);
		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[0].Kind);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Reference);
	}

	[TestMethod]
	public async Task PushingTwiceReportsUpToDateAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository workingDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);
		GitRepository repository = await CreateWorkingCopyAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(repository, workingDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await repository.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitPushResult second = await repository.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(GitRefUpdateKind.UpToDate, second.Updates[0].Kind);
	}

	[TestMethod]
	public async Task ARejectedPushThrowsAndCarriesTheDetailAsync()
	{
		// The behaviour the whole push design exists for: git exits non-zero and still reports
		// exactly which reference it refused and why.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A second clone advances the remote, so the first repository's next push is behind.
		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "b.txt", "two\n", "c2", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "three\n", "c3", cancellationToken).ConfigureAwait(false);

		GitPushRejectedException exception = await Assert.ThrowsExactlyAsync<GitPushRejectedException>(
			async () => await first.Push().ToRemote(Origin).WithBranch(Main)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsNotNull(exception.Result);
		Assert.IsTrue(exception.Result.HasRejections);
		StringAssert.Contains(exception.Result.Updates[0].Summary, "rejected");
	}

	[TestMethod]
	public async Task TryPushReturnsTheRejectionAsAValueAsync()
	{
		// The deliberate divergence between the two entry points, exercised against real git.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "b.txt", "two\n", "c2", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "three\n", "c3", cancellationToken).ConfigureAwait(false);

		GitResult<GitPushResult> result = await first.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsNotNull(result.Value);
		Assert.IsTrue(result.Value.HasRejections);
	}

	[TestMethod]
	public async Task FetchReportsTheUpdatedRemoteTrackingBranchAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);

		GitFetchResult fetched = await second.Fetch()
			.FromRemote(Origin)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// The itemised account is only available on git 2.41 and above. Below that the fetch still
		// worked, so assert the reference landed either way and the detail only when it is offered.
		if (fetched.DetailAvailable)
		{
			Assert.AreEqual(1, fetched.Updates.Count);
			Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), fetched.Updates[0].Reference);
		}

		IReadOnlyList<GitBranch> branches =
			await second.Branches().RemoteOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, branches.Count);
		Assert.IsTrue(branches[0].IsRemote);
	}

	[TestMethod]
	public async Task FetchingTwiceReportsNothingTheSecondTimeAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Fetch().FromRemote(Origin).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitFetchResult again = await second.Fetch()
			.FromRemote(Origin)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, again.Updates.Count);

		if (again.DetailAvailable)
		{
			Assert.IsTrue(again.IsUpToDate);
		}
	}

	[TestMethod]
	public async Task PullBringsTheOtherRepositorysCommitAcrossAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		GitCommit committed = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);

		_ = await second.Pull()
			.FromRemote(Origin)
			.WithBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history =
			await second.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task AConflictingPullThrowsAndLeavesAnUnmergedPathAsync()
	{
		// The one pull outcome with its own type, and the reason it has one: the repository is left
		// mid-merge, and Status() is how a caller finds out what needs attention.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "line1\nline2\n", "base", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "c.txt", "line1\nTHEIRS\n", "theirs", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "line1\nMINE\n", "mine", cancellationToken).ConfigureAwait(false);

		await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await first.Pull().FromRemote(Origin).WithBranch(Main)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);

		// The repository is mid-merge, and that state is inspectable through the read-only verbs
		// rather than needing any conflict machinery in this library.
		GitStatus status = await first.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(status.IsClean);
		Assert.IsTrue(status.Entries.Any(entry => entry.IndexState == GitFileState.Unmerged));
	}

	public TestContext TestContext { get; set; } = null!;
}
```

The file needs `using System.Linq;` for `Any`.

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test --filter "TestCategory=Integration"`

Expected: PASS, 18 tests — Phase 4's 10 plus these 8. If they report Inconclusive, git is not on PATH; that is the designed behaviour, not something to fix.

- [ ] **Step 3: Run them with git treated as required**

Run: `KTSU_GIT_INTEGRATION_TESTS_REQUIRED=1 dotnet test --filter "TestCategory=Integration"`

Expected: the same 18 passing. This is the mode CI uses, where a missing git fails instead of skipping.

- [ ] **Step 4: Confirm the whole suite is green**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, every test passing.

- [ ] **Step 5: Confirm no temporary directories leaked**

These tests create three temporary repositories each, so a leak shows up faster here than anywhere else. Check the system temp directory for `ktsu-git-it-*` entries. A handful surviving is tolerable — `Dispose` swallows cleanup failures deliberately — but a growing pile means the helper is not coping with the bare repositories.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration.Test/Integration/GitRemoteSyncTests.cs
git commit -m "[minor] Add integration tests for the remote sync verbs"
```

---

## What this plan deliberately does not do

- **No stderr parser for `fetch` on git below 2.41.** Argued above: the design's rule against parsing human output outweighs the itemisation it would buy, and `DetailAvailable` makes the gap visible rather than silent.
- **No conflict resolution.** The spec lists `merge`, `rebase`, and the rest of the interactive commands as non-goals. `GitPullConflictException` reports the state; `Status()` describes it; resolving it is the caller's business.
- **No `push --mirror`, `--tags`, or refspec syntax beyond `<remote> <branch>`.** A caller needing a raw refspec is better served by a future explicit option than by this plan guessing at one.
- **No credential handling.** `GIT_TERMINAL_PROMPT=0` means an unauthenticated remote fails fast rather than hanging; supplying credentials is the hosting layer's concern and is out of scope for both halves of Phase 5.
- **Nothing from Phase 5b.** `IGitHostingProvider`, the `GitProvider` async refactor, GitHub enumeration, and Azure DevOps over raw `HttpClient` get their own plan.

## Self-review

**Spec coverage.** The spec's Phase 5 bullet names `Fetch`, `Pull`, `Push` — Tasks 4, 5, 6, wired in Task 7 and exercised in Task 8. The output-parsing table's `Push | push --porcelain | GitPushResult` and `Fetch | fetch --porcelain (git ≥ 2.41), else stderr | GitFetchResult` rows are both implemented, the second with an argued divergence recorded above. `GitRefUpdate`, named in the spec's result-model section as following the same shape, is Task 1. The hosting half of the bullet is deliberately deferred to Phase 5b.

**One divergence from the spec, argued at length:** fetch degrades rather than parsing stderr below 2.41.

**One place where the library's own contract bends, deliberately:** `Push` makes `ExecuteAsync` and `TryExecuteAsync` mean different things, because it is the only verb whose failure output is the payload. Documented on the interface and pinned by tests in Tasks 4 and 8.

**Type consistency.** `GitRefUpdate` is produced by both parsers and consumed by both results; `GitRefUpdateKind` has one mapping table per parser because the two verbs assign different meanings to `-`, which is noted on the enum. Every builder constructor is `(IGitProcessRunner, AbsoluteDirectoryPath)`. `GitCompleted` is reused from Phase 4 for `Pull` rather than inventing a parallel unit type. `GitPushResult.HasRejections`, `GitFetchResult.DetailAvailable`, and `GitFetchResult.IsUpToDate` are referenced identically in the tasks that define them and the tasks that use them.





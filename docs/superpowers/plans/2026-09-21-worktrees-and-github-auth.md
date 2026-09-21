# Worktree Verbs, GitHub Owner Kinds, and Device Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `git worktree` verbs, GitHub repository enumeration that can see an organisation's private repositories, and a GitHub OAuth device flow, so a desktop launcher can build a tree of every repository its user can reach and manage branches as one worktree each.

**Architecture:** Three independent additions. The worktree verbs follow the library's existing builder-plus-parser pattern exactly: a `GitWorktree` record, a `GitWorktreeParser` over `git worktree list --porcelain`, four builders, and four factory methods on `GitRepository`. The GitHub change is an owner-kind property that routes `GetRepositoriesAsync` to whichever endpoint can answer for that kind of owner, defaulting to today's route. The device flow is a standalone type in `Hosting/` that obtains a `HostingCredential` and stores nothing.

**Tech Stack:** .NET 10 and .NET 9 multi-target, C#, MSTest via MSTest.Sdk (Microsoft Testing Platform), Octokit 14.0.0, ktsu.Semantics, ktsu.CredentialCache, ktsu.RunCommand.

**Spec:** `docs/superpowers/specs/2026-09-21-worktrees-and-github-auth-design.md`

## Global Constraints

- **Version: `[minor]` — 3.2.0.** Additive only. No existing member changes shape or behaviour.
- **Never run `dotnet test --nologo`.** Under Microsoft Testing Platform it silently runs zero tests and exits 5. Always plain `dotnet test`.
- **Copyright header on every new file:** `// Copyright (c) 2023-2026 ktsu-dev contributors`
- **Namespace:** `ktsu.GitIntegration` for library files, `ktsu.GitIntegration.Test` for test files. File-scoped, `using` directives inside the namespace, matching every existing file.
- **Tabs, not spaces.** The repository indents with tabs.
- **British spelling in prose and documentation comments** (`behaviour`, `organisation`, `normalise`), matching the existing codebase.
- **Every public member needs an XML doc comment.** The build treats missing documentation as an error.
- **`Ensure.NotNull` in the library, `ArgumentNullException.ThrowIfNull` in tests.** The library takes Polyfill with `PrivateAssets="all"`, so `Ensure` is not visible to the test project.
- **Caller-supplied operands go after `--end-of-options`,** via `GitCommandBuilder<T>.AppendOperands`.
- **Target frameworks:** library `net10.0;net9.0`, tests `net10.0` only. Do not use an API unavailable on net9.0 in library code.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `GitIntegration/Models/GitWorktree.cs` | The inert record describing one worktree |
| `GitIntegration/Parsing/GitWorktreeParser.cs` | Reads `worktree list --porcelain` into records |
| `GitIntegration/Builders/GitWorktreeListBuilder.cs` | `IGitWorktreeListBuilder` and its implementation |
| `GitIntegration/Builders/GitWorktreeAddBuilder.cs` | `IGitWorktreeAddBuilder` and its implementation |
| `GitIntegration/Builders/GitWorktreeWriteBuilders.cs` | Remove and prune, two small builders that change together |
| `GitIntegration/Hosting/GitHubDeviceFlow.cs` | `GitHubDeviceCode` and `GitHubDeviceFlow` |
| `GitIntegration.Test/Parsing/GitWorktreeParserTests.cs` | Parser fixtures |
| `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs` | Argument-vector assertions for all four builders |
| `GitIntegration.Test/Hosting/GitHubDeviceFlowTests.cs` | Device flow against a fake transport |
| `GitIntegration.Test/Fixtures/github-org-repositories.json` | Org listing fixture with a private repository |

**Modified:**

| File | Change |
|---|---|
| `GitIntegration/GitRepository.cs` | Four verb factory methods |
| `GitIntegration/Parsing/GitParseValues.cs` | `ToAbsoluteDirectoryPath` |
| `GitIntegration/Models/GitEnums.cs` | `GitHubOwnerKind` |
| `GitIntegration/SemanticTypes/GitProviderTypes.cs` | `GitHubOAuthClientId` |
| `GitIntegration/GitHubProvider.cs` | `OwnerKind`, enumeration routing, `X-GitHub-SSO` in the message |
| `GitIntegration.Test/Hosting/GitHubProviderTests.cs` | Owner-kind and single sign-on tests |
| `GitIntegration.Test/GitRepositoryVerbTests.cs` | `Worktrees()` factory guard tests |
| `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs` | Add, remove, prune factory guard tests |
| `README.md`, `CHANGELOG.md` | Documentation |

---

## Task 1: The worktree model and parser

**Files:**
- Create: `GitIntegration/Models/GitWorktree.cs`
- Create: `GitIntegration/Parsing/GitWorktreeParser.cs`
- Modify: `GitIntegration/Parsing/GitParseValues.cs`
- Test: `GitIntegration.Test/Parsing/GitWorktreeParserTests.cs`

**Interfaces:**
- Consumes: `GitParseValues.ToSemantic<T>(string value, string description)`, `GitParseException`, `GitCommitSha`, `GitBranchName`, all existing.
- Produces: `GitWorktree` (record with `Path`, `Head`, `Branch`, `IsMain`, `IsBare`, `IsDetached`, `IsLocked`, `LockReason`, `IsPrunable`, `PrunableReason`); `GitWorktreeParser.Parse(string output)` returning `IReadOnlyList<GitWorktree>`; `GitParseValues.ToAbsoluteDirectoryPath(string value)` returning `AbsoluteDirectoryPath`.

### Background: the format being parsed

`git worktree list --porcelain` emits one record per worktree, records separated by a blank line, and a trailing blank line after the last record. Each record is `attribute` or `attribute value` lines:

```
worktree /home/u/project
HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f
branch refs/heads/main

worktree /home/u/project-feature
HEAD 2b8e4d0f6a8c0e2a4c6e8b0d2f4a6c8e0b2d4f6a
branch refs/heads/feature
locked contains uncommitted experiment

worktree /home/u/project-review
HEAD 9c1a7f2e4b6d8a0c2e4f6a8b0d2c4e6f8a0b2d4c
detached
prunable gitdir file points to non-existent location

worktree /home/u/project-bare
bare

```

`HEAD` and `branch` are absent for a bare worktree. `branch` is absent and `detached` present for a detached one. `locked` and `prunable` each appear alone or with a reason.

- [ ] **Step 1: Write the failing parser tests**

Create `GitIntegration.Test/Parsing/GitWorktreeParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitWorktreeParserTests
{
	private const string MainOnly =
		"worktree /home/u/project\n" +
		"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
		"branch refs/heads/main\n" +
		"\n";

	private const string ThreeWorktrees =
		"worktree /home/u/project\n" +
		"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
		"branch refs/heads/main\n" +
		"\n" +
		"worktree /home/u/project-feature\n" +
		"HEAD 2b8e4d0f6a8c0e2a4c6e8b0d2f4a6c8e0b2d4f6a\n" +
		"branch refs/heads/feature\n" +
		"locked contains uncommitted experiment\n" +
		"\n" +
		"worktree /home/u/project-review\n" +
		"HEAD 9c1a7f2e4b6d8a0c2e4f6a8b0d2c4e6f8a0b2d4c\n" +
		"detached\n" +
		"prunable gitdir file points to non-existent location\n" +
		"\n";

	[TestMethod]
	public void ReadsTheOnlyWorktreeAsTheMainOne()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly);

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual("/home/u/project", worktrees[0].Path.WeakString);
		Assert.AreEqual("7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f".As<GitCommitSha>(), worktrees[0].Head);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
		Assert.IsTrue(worktrees[0].IsMain);
		Assert.IsFalse(worktrees[0].IsBare);
		Assert.IsFalse(worktrees[0].IsDetached);
		Assert.IsFalse(worktrees[0].IsLocked);
		Assert.IsFalse(worktrees[0].IsPrunable);
	}

	[TestMethod]
	public void StripsTheRefsHeadsPrefixFromTheBranch()
	{
		// The hosting half of this library already hands back bare branch names. A caller should not
		// have to know which half produced a name to know its shape.
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly);

		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}

	[TestMethod]
	public void MarksOnlyTheFirstRecordAsMain()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.AreEqual(3, worktrees.Count);
		Assert.IsTrue(worktrees[0].IsMain);
		Assert.IsFalse(worktrees[1].IsMain);
		Assert.IsFalse(worktrees[2].IsMain);
	}

	[TestMethod]
	public void ReadsALockReasonWhenGitGivesOne()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[1].IsLocked);
		Assert.AreEqual("contains uncommitted experiment", worktrees[1].LockReason);
	}

	[TestMethod]
	public void ReportsALockWithNoReasonAsLockedWithoutOne()
	{
		// "locked" alone and "locked <reason>" are different facts, and a caller showing the reason
		// must be able to tell "no reason recorded" from "not locked".
		string output =
			"worktree /home/u/project\n" +
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"locked\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.IsTrue(worktrees[0].IsLocked);
		Assert.IsNull(worktrees[0].LockReason);
	}

	[TestMethod]
	public void ReadsADetachedWorktreeWithNoBranch()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[2].IsDetached);
		Assert.IsNull(worktrees[2].Branch);
		Assert.AreEqual("9c1a7f2e4b6d8a0c2e4f6a8b0d2c4e6f8a0b2d4c".As<GitCommitSha>(), worktrees[2].Head);
	}

	[TestMethod]
	public void ReadsAPrunableReason()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[2].IsPrunable);
		Assert.AreEqual("gitdir file points to non-existent location", worktrees[2].PrunableReason);
	}

	[TestMethod]
	public void ReadsABareWorktreeWithNeitherHeadNorBranch()
	{
		string output =
			"worktree /home/u/project-bare\n" +
			"bare\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.AreEqual(1, worktrees.Count);
		Assert.IsTrue(worktrees[0].IsBare);
		Assert.IsNull(worktrees[0].Head);
		Assert.IsNull(worktrees[0].Branch);
	}

	[TestMethod]
	public void DoesNotInventARecordFromTheTrailingBlankLine()
	{
		// --porcelain emits a blank line after the last record as well as between records. A naive
		// split on the separator turns that into a phantom entry.
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.AreEqual(3, worktrees.Count);
	}

	[TestMethod]
	public void ReadsRecordsSeparatedByCarriageReturnLineFeeds()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly.Replace("\n", "\r\n", StringComparison.Ordinal));

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}

	[TestMethod]
	public void ReadsNothingFromEmptyOutput()
	{
		Assert.AreEqual(0, GitWorktreeParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsARecordWithNoWorktreePath()
	{
		// Every attribute is optional except the one naming what the record describes.
		string output =
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"\n";

		_ = Assert.ThrowsExactly<GitParseException>(() => GitWorktreeParser.Parse(output));
	}

	[TestMethod]
	public void IgnoresAnAttributeItDoesNotRecognise()
	{
		// Git may add attributes. An unknown one must not fail a listing that is otherwise readable.
		string output =
			"worktree /home/u/project\n" +
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"somethingnew value\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeParserTests"`
Expected: compile failure, `GitWorktree` and `GitWorktreeParser` do not exist.

- [ ] **Step 3: Write the model**

Create `GitIntegration/Models/GitWorktree.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;

/// <summary>
/// One working tree of a repository: where it is, and what it has checked out.
/// </summary>
/// <remarks>
/// Inert data, like <see cref="GitBranch"/> and <see cref="GitSubmodule"/>. It carries no
/// <see cref="GitRepository"/> and no process runner. A caller that wants to run a verb inside a
/// worktree passes <see cref="Path"/> to <see cref="IGitClient.OpenAsync"/>, which is the same
/// journey it would make from any other path.
/// </remarks>
public sealed record GitWorktree
{
	/// <summary>Gets the worktree's own directory.</summary>
	public required AbsoluteDirectoryPath Path { get; init; }

	/// <summary>
	/// Gets the commit checked out here, or <see langword="null"/> when git reported none.
	/// </summary>
	/// <remarks>
	/// Absent for a bare worktree, which has no checkout to report. Present for a detached one,
	/// where it is the only thing identifying what is checked out.
	/// </remarks>
	public GitCommitSha? Head { get; init; }

	/// <summary>
	/// Gets the branch checked out here, or <see langword="null"/> when there is none.
	/// </summary>
	/// <remarks>
	/// Absent for a bare or detached worktree. Reported as a bare name with git's
	/// <c>refs/heads/</c> prefix stripped, so that a branch name from this half of the library has
	/// the same shape as one from the hosting half.
	/// </remarks>
	public GitBranchName? Branch { get; init; }

	/// <summary>
	/// Gets a value indicating whether this is the repository's main working tree.
	/// </summary>
	/// <remarks>
	/// <b>Positional.</b> Git's porcelain has no attribute for this: the main working tree is simply
	/// the first record git emits, always. The parser sets this on the first record and on no other.
	/// It exists because a caller managing worktrees needs to refuse to remove the one that owns the
	/// repository, and answering that once here beats every caller reimplementing it.
	/// </remarks>
	public bool IsMain { get; init; }

	/// <summary>Gets a value indicating whether this worktree has no working directory.</summary>
	public bool IsBare { get; init; }

	/// <summary>Gets a value indicating whether this worktree has a commit checked out rather than a branch.</summary>
	public bool IsDetached { get; init; }

	/// <summary>Gets a value indicating whether this worktree is locked against pruning.</summary>
	public bool IsLocked { get; init; }

	/// <summary>
	/// Gets the reason this worktree is locked, or <see langword="null"/> when git recorded none.
	/// </summary>
	/// <remarks>
	/// Separately nullable from <see cref="IsLocked"/>, because git emits <c>locked</c> both alone
	/// and with a reason, and "locked for a reason nobody recorded" is a different fact from "not
	/// locked".
	/// </remarks>
	public string? LockReason { get; init; }

	/// <summary>Gets a value indicating whether git considers this worktree's record removable.</summary>
	public bool IsPrunable { get; init; }

	/// <summary>
	/// Gets the reason this worktree is prunable, or <see langword="null"/> when git recorded none.
	/// </summary>
	public string? PrunableReason { get; init; }
}
```

- [ ] **Step 4: Add the absolute path conversion**

Append to `GitIntegration/Parsing/GitParseValues.cs`, inside the class, after `ToRelativeDirectoryPath`:

```csharp
	/// <summary>
	/// Converts a raw path field into an absolute directory path.
	/// </summary>
	/// <remarks>
	/// The absolute counterpart to <see cref="ToRelativeDirectoryPath"/>, for the fields git reports
	/// as whole paths rather than as paths within a repository — a worktree's own directory is the
	/// first of them. An empty field is a malformed record, and a path this type refuses is one git
	/// produced and this library cannot represent, which is a parse failure rather than a value to
	/// pass along unchecked.
	/// </remarks>
	/// <param name="value">The raw path as git printed it.</param>
	/// <returns>The converted path.</returns>
	/// <exception cref="GitParseException">
	/// <paramref name="value"/> is empty or cannot be represented as an absolute directory path.
	/// </exception>
	internal static AbsoluteDirectoryPath ToAbsoluteDirectoryPath(string value)
	{
		if (!string.IsNullOrEmpty(value) &&
			AbsoluteDirectoryPath.TryCreate(value, out AbsoluteDirectoryPath? path) &&
			path is not null)
		{
			return path;
		}

		throw new GitParseException(
			$"git reported a path that cannot be represented as an absolute directory path: '{value}'.");
	}
```

- [ ] **Step 5: Write the parser**

Create `GitIntegration/Parsing/GitWorktreeParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// The porcelain format is one record per worktree, records separated by a blank line and a blank
/// line after the last. Each line is an attribute name, optionally followed by a space and a value.
/// Only <c>worktree</c> is guaranteed present: a bare worktree reports neither <c>HEAD</c> nor
/// <c>branch</c>, and a detached one reports <c>HEAD</c> and <c>detached</c> but no <c>branch</c>.
/// </remarks>
internal static class GitWorktreeParser
{
	private const string RefsHeadsPrefix = "refs/heads/";

	/// <summary>
	/// Parses a porcelain worktree listing.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The worktrees, in the order git listed them, the main one first.</returns>
	/// <exception cref="GitParseException">A record named no worktree, or a field failed validation.</exception>
	internal static IReadOnlyList<GitWorktree> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitWorktree> worktrees = [];
		List<string> record = [];

		foreach (string line in output.Split('\n'))
		{
			string entry = line.TrimEnd('\r');

			if (entry.Length != 0)
			{
				record.Add(entry);
				continue;
			}

			// A blank line closes a record. Closing only a non-empty one is what keeps the trailing
			// blank line git always emits from producing a phantom final entry.
			if (record.Count != 0)
			{
				worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0));
				record.Clear();
			}
		}

		// Output not ending in a blank line still closes its last record, so a listing stays readable
		// if git ever stops emitting the trailing separator.
		if (record.Count != 0)
		{
			worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0));
		}

		return worktrees;
	}

	/// <summary>
	/// Turns one record's attribute lines into a worktree.
	/// </summary>
	/// <param name="record">The record's lines, in git's order.</param>
	/// <param name="isMain">Whether this is the first record git emitted.</param>
	/// <returns>The parsed worktree.</returns>
	/// <exception cref="GitParseException">The record named no worktree, or a field failed validation.</exception>
	private static GitWorktree ParseRecord(IReadOnlyList<string> record, bool isMain)
	{
		string? path = null;
		GitCommitSha? head = null;
		GitBranchName? branch = null;
		bool isBare = false;
		bool isDetached = false;
		bool isLocked = false;
		bool isPrunable = false;
		string? lockReason = null;
		string? prunableReason = null;

		foreach (string line in record)
		{
			int separator = line.IndexOf(' ', StringComparison.Ordinal);
			string attribute = separator < 0 ? line : line[..separator];
			string value = separator < 0 ? string.Empty : line[(separator + 1)..];

			switch (attribute)
			{
				case "worktree":
					path = value;
					break;
				case "HEAD":
					head = GitParseValues.ToSemantic<GitCommitSha>(value, "commit id");
					break;
				case "branch":
					branch = GitParseValues.ToSemantic<GitBranchName>(StripRefsHeadsPrefix(value), "branch name");
					break;
				case "bare":
					isBare = true;
					break;
				case "detached":
					isDetached = true;
					break;
				case "locked":
					isLocked = true;
					lockReason = value.Length == 0 ? null : value;
					break;
				case "prunable":
					isPrunable = true;
					prunableReason = value.Length == 0 ? null : value;
					break;
				default:
					// Unknown attributes are skipped rather than rejected. Git may add one, and a
					// listing that is otherwise readable should not fail over a field nobody reads.
					break;
			}
		}

		return path is null
			? throw new GitParseException("git reported a worktree record naming no worktree path.")
			: new GitWorktree
			{
				Path = GitParseValues.ToAbsoluteDirectoryPath(path),
				Head = head,
				Branch = branch,
				IsMain = isMain,
				IsBare = isBare,
				IsDetached = isDetached,
				IsLocked = isLocked,
				LockReason = lockReason,
				IsPrunable = isPrunable,
				PrunableReason = prunableReason,
			};
	}

	/// <summary>
	/// Strips a leading <c>refs/heads/</c>, leaving the value untouched when the prefix is absent.
	/// </summary>
	/// <param name="reference">The reference as git printed it.</param>
	/// <returns>The bare branch name.</returns>
	private static string StripRefsHeadsPrefix(string reference) =>
		reference.StartsWith(RefsHeadsPrefix, StringComparison.Ordinal)
			? reference[RefsHeadsPrefix.Length..]
			: reference;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeParserTests"`
Expected: PASS, 13 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add GitIntegration/Models/GitWorktree.cs GitIntegration/Parsing/GitWorktreeParser.cs GitIntegration/Parsing/GitParseValues.cs GitIntegration.Test/Parsing/GitWorktreeParserTests.cs
git commit -m "feat: read git worktree list --porcelain into a typed model"
```

---

## Task 2: The worktree listing verb

**Files:**
- Create: `GitIntegration/Builders/GitWorktreeListBuilder.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`
- Test: `GitIntegration.Test/GitRepositoryVerbTests.cs`

**Interfaces:**
- Consumes: `GitWorktree`, `GitWorktreeParser.Parse` from Task 1. `GitCommandBuilder<TResult>(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)` with abstract `AppendVerbArguments(ICollection<string>)` and `ParseResult(GitProcessResult)`. `RecordingGitProcessRunner` and `TestPaths.Root` from the test project.
- Produces: `IGitWorktreeListBuilder : IGitCommandBuilder<IReadOnlyList<GitWorktree>>`; `GitRepository.Worktrees()` returning it.

- [ ] **Step 1: Write the failing tests**

Create `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;

[TestClass]
public sealed class GitWorktreeBuilderTests
{
	private static readonly string[] GlobalArguments =
	[
		"-C", TestPaths.Root.WeakString,
		"--no-pager",
		"-c", "core.quotepath=false",
		"-c", "color.ui=false",
	];

	private static string[] Expect(params string[] verbArguments) =>
		[.. GlobalArguments, .. verbArguments];

	[TestMethod]
	public void BuildsTheWorktreeListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		CollectionAssert.AreEqual(Expect("worktree", "list", "--porcelain"), arguments.ToArray());
	}
}
```

Append to `GitIntegration.Test/GitRepositoryVerbTests.cs`, inside the existing class:

```csharp
	[TestMethod]
	public void WorktreesRequiresAProcessRunner()
	{
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		_ = Assert.ThrowsExactly<InvalidOperationException>(() => repository.Worktrees());
	}

	[TestMethod]
	public void WorktreesRequiresALocalPath()
	{
		GitRepository repository = new() { ProcessRunner = new RecordingGitProcessRunner() };

		_ = Assert.ThrowsExactly<InvalidOperationException>(() => repository.Worktrees());
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeBuilderTests|FullyQualifiedName~GitRepositoryVerbTests"`
Expected: compile failure, `GitWorktreeListBuilder` and `Worktrees` do not exist.

- [ ] **Step 3: Write the builder**

Create `GitIntegration/Builders/GitWorktreeListBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the repository's working trees.
/// </summary>
/// <remarks>
/// Reports the main working tree first, which is the order git emits and the only thing identifying
/// it — see <see cref="GitWorktree.IsMain"/>.
/// </remarks>
public interface IGitWorktreeListBuilder : IGitCommandBuilder<IReadOnlyList<GitWorktree>>
{
}

/// <summary>
/// Builds <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// No options. The porcelain listing already reports every attribute this library models, and git's
/// remaining options on this verb either change the format (<c>-v</c>, which is the human-facing
/// form) or add a field this library reads from the porcelain output anyway (<c>--expire</c>, which
/// only affects which entries are annotated prunable).
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitWorktreeListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitWorktree>>(runner, repositoryPath), IGitWorktreeListBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("list");
		arguments.Add("--porcelain");
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitWorktree> ParseResult(GitProcessResult result) =>
		GitWorktreeParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 4: Add the factory**

In `GitIntegration/GitRepository.cs`, after the `Tags()` factory:

```csharp
	/// <summary>Lists the repository's working trees.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitWorktreeListBuilder Worktrees() => new GitWorktreeListBuilder(RequireRunner(), RequireLocalPath());
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeBuilderTests|FullyQualifiedName~GitRepositoryVerbTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitWorktreeListBuilder.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs GitIntegration.Test/GitRepositoryVerbTests.cs
git commit -m "feat: add the worktree listing verb"
```

---

## Task 3: The worktree add verb

**Files:**
- Create: `GitIntegration/Builders/GitWorktreeAddBuilder.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`
- Test: `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`

**Interfaces:**
- Consumes: `GitCommandBuilder<GitCompleted>`, `AppendOperands(ICollection<string>, params string[])`, `GitCompleted`, `GitBranchName`, `GitRefName`, `AbsoluteDirectoryPath`.
- Produces: `IGitWorktreeAddBuilder` with `CheckingOut(GitBranchName)`, `CreatingBranch(GitBranchName)`, `CreatingOrResettingBranch(GitBranchName)`, `Detached()`, `From(GitRefName)`, `Force()`, `WithoutCheckout()`, each returning `IGitWorktreeAddBuilder`; `GitRepository.AddWorktree(AbsoluteDirectoryPath path)` returning it.

### Background: the syntax being built

`git worktree add [-f] [--detach] [--no-checkout] [-b <new-branch> | -B <new-branch>] <path> [<commit-ish>]`

The four mode settings divide across two places. `CreatingBranch` emits `-b`, `CreatingOrResettingBranch` emits `-B`, `Detached` emits `--detach`. `CheckingOut` emits no option and instead writes the `<commit-ish>` operand.

`From` writes that **same** operand. So `CheckingOut` and `From` are one field under two names, and the later call wins. They are typed differently because that is the useful distinction: `CheckingOut` takes a `GitBranchName` and reads as the ordinary case, `From` takes a `GitRefName` and admits a tag or a raw revision.

- [ ] **Step 1: Write the failing tests**

Append to `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`, inside the class:

```csharp
	[TestMethod]
	public void BuildsTheMinimalWorktreeAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void PutsACheckedOutBranchInTheCommitIshOperand()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CheckingOut("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--end-of-options", TestPaths.Worktree.WeakString, "feature"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheCreateBranchOptionBeforeThePath()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-b", "feature", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheResettingCreateBranchOption()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingOrResettingBranch("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-B", "feature", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void CombinesBranchCreationWithAStartPoint()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>()).From("origin/main".As<GitRefName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-b", "feature", "--end-of-options", TestPaths.Worktree.WeakString, "origin/main"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void CombinesDetachmentWithACommitIsh()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Detached().From("v1.2.0".As<GitRefName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--detach", "--end-of-options", TestPaths.Worktree.WeakString, "v1.2.0"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void TheLastModeSelectionWins()
	{
		// The four modes are one field, resolved rather than accumulated, matching how
		// IGitBranchListBuilder's LocalOnly and RemoteOnly already replace each other. A vector
		// carrying both -b and --detach is one git rejects.
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>()).Detached();

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "--detach");
		CollectionAssert.DoesNotContain(arguments, "-b");
		CollectionAssert.DoesNotContain(arguments, "feature");
	}

	[TestMethod]
	public void TheLastCommitIshSelectionWins()
	{
		// CheckingOut and From write the same operand slot, so the same resolution applies.
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CheckingOut("feature".As<GitBranchName>()).From("v1.2.0".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "v1.2.0");
		CollectionAssert.DoesNotContain(arguments, "feature");
	}

	[TestMethod]
	public void EmitsForceAndNoCheckout()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Force().WithoutCheckout();

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--force", "--no-checkout", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task ReportsCompletionOnSuccess()
	{
		RecordingGitProcessRunner runner = new() { StandardError = "Preparing worktree (new branch 'feature')\n" };
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(completed);
	}

	public TestContext TestContext { get; set; } = null!;
```

Add to the file's `using` directives: `using System.Threading.Tasks;` and `using ktsu.Semantics.Strings;`.

Append to `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`, inside the existing class:

```csharp
	[TestMethod]
	public void AddWorktreeRejectsANullPath()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			ProcessRunner = new RecordingGitProcessRunner(),
		};

		_ = Assert.ThrowsExactly<ArgumentNullException>(() => repository.AddWorktree(null!));
	}

	[TestMethod]
	public void AddWorktreeRequiresAProcessRunner()
	{
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		_ = Assert.ThrowsExactly<InvalidOperationException>(() => repository.AddWorktree(TestPaths.Worktree));
	}
```

- [ ] **Step 2: Add the shared test path**

`TestPaths` lives at the bottom of `GitIntegration.Test/GitRepositoryMetadataTests.cs`, outside the test class:

```csharp
/// <summary>Paths that exist on every platform the tests run on.</summary>
internal static class TestPaths
{
	public static AbsoluteDirectoryPath Root { get; } =
		(OperatingSystem.IsWindows() ? @"C:\" : "/").As<AbsoluteDirectoryPath>();
}
```

Add `Worktree` alongside `Root`, following the same platform switch:

```csharp
	/// <summary>An absolute directory distinct from <see cref="Root"/>, used as a worktree destination.</summary>
	public static AbsoluteDirectoryPath Worktree { get; } =
		(OperatingSystem.IsWindows() ? @"C:\project-feature" : "/project-feature").As<AbsoluteDirectoryPath>();
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeBuilderTests"`
Expected: compile failure, `GitWorktreeAddBuilder` does not exist.

- [ ] **Step 4: Write the builder**

Create `GitIntegration/Builders/GitWorktreeAddBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates an additional working tree.
/// </summary>
/// <remarks>
/// <see cref="CheckingOut"/>, <see cref="CreatingBranch"/>, <see cref="CreatingOrResettingBranch"/>
/// and <see cref="Detached"/> are four settings of one mode, and a later call replaces an earlier
/// one, as <c>IGitBranchListBuilder</c>'s <c>LocalOnly</c> and <c>RemoteOnly</c> already do. A vector
/// carrying two of them at once is one git rejects.
/// </remarks>
public interface IGitWorktreeAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Checks out an existing branch in the new worktree. Replaces any previous mode.</summary>
	/// <param name="branch">The branch to check out.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CheckingOut(GitBranchName branch);

	/// <summary>Creates a branch and checks it out in the new worktree. Replaces any previous mode.</summary>
	/// <remarks>Fails when the branch already exists; see <see cref="CreatingOrResettingBranch"/>.</remarks>
	/// <param name="branch">The branch to create.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CreatingBranch(GitBranchName branch);

	/// <summary>
	/// Creates a branch, resetting it when it already exists, and checks it out. Replaces any
	/// previous mode.
	/// </summary>
	/// <param name="branch">The branch to create or reset.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CreatingOrResettingBranch(GitBranchName branch);

	/// <summary>Checks out a commit rather than a branch. Replaces any previous mode.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder Detached();

	/// <summary>
	/// Sets the commit-ish the new worktree starts from: the start point for the branch-creating
	/// modes, and the commit to detach at for <see cref="Detached"/>.
	/// </summary>
	/// <remarks>
	/// Writes the same operand as <see cref="CheckingOut"/>, so the later of the two calls wins.
	/// Distinct from it only in taking a <see cref="GitRefName"/>, which admits a tag or a raw
	/// revision rather than a branch alone.
	/// </remarks>
	/// <param name="commitish">The revision to start from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder From(GitRefName commitish);

	/// <summary>Creates the worktree even when git would otherwise refuse.</summary>
	/// <remarks>
	/// Git refuses when the branch is already checked out in another worktree, and when the
	/// destination is a missing-but-registered worktree directory.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder Force();

	/// <summary>Registers the worktree without populating its working directory.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder WithoutCheckout();
}

/// <summary>
/// Builds <c>git worktree add</c>.
/// </summary>
/// <remarks>
/// <c>--guess-remote</c> is deliberately not exposed. Plain <c>git worktree add &lt;path&gt;
/// &lt;branch&gt;</c> already creates a local tracking branch when the name matches exactly one
/// remote, which covers creating a worktree for a branch that exists only on the remote.
/// <c>--guess-remote</c> extends that to the case where no branch is named at all, which this
/// builder always names.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">Where the new worktree goes.</param>
internal sealed class GitWorktreeAddBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	AbsoluteDirectoryPath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreeAddBuilder
{
	private enum AddMode
	{
		Plain,
		CreateBranch,
		CreateOrResetBranch,
		Detach,
	}

	private readonly AbsoluteDirectoryPath _path = Ensure.NotNull(path);

	private AddMode _mode = AddMode.Plain;
	private string? _branch;
	private string? _commitish;
	private bool _force;
	private bool _withoutCheckout;

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CheckingOut(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.Plain;
		_branch = null;
		_commitish = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CreatingBranch(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.CreateBranch;
		_branch = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CreatingOrResettingBranch(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.CreateOrResetBranch;
		_branch = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder Detached()
	{
		_mode = AddMode.Detach;
		_branch = null;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder From(GitRefName commitish)
	{
		Ensure.NotNull(commitish);

		_commitish = commitish.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder WithoutCheckout()
	{
		_withoutCheckout = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("add");

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_withoutCheckout)
		{
			arguments.Add("--no-checkout");
		}

		switch (_mode)
		{
			case AddMode.CreateBranch:
				arguments.Add("-b");
				arguments.Add(_branch!);
				break;
			case AddMode.CreateOrResetBranch:
				arguments.Add("-B");
				arguments.Add(_branch!);
				break;
			case AddMode.Detach:
				arguments.Add("--detach");
				break;
			case AddMode.Plain:
			default:
				break;
		}

		// The path and the commit-ish are both caller-supplied operands and share one end-of-options
		// marker, which git reads as applying to everything after it.
		if (_commitish is null)
		{
			AppendOperands(arguments, _path.WeakString);
		}
		else
		{
			AppendOperands(arguments, _path.WeakString, _commitish);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) => new();
}
```

Note: the `-b <branch>` value is a library-routed caller value that cannot precede `--end-of-options`, because git requires the option and its value together before the operands. `GitBranchName`'s own validation is what stops a dash-leading value reaching it; check whether `GitBranchName` carries `NotAnOptionAttribute` (grep: `grep -n "GitBranchName" GitIntegration/SemanticTypes/GitRefTypes.cs`) and, if it does not, note it in the pull request rather than changing the type in this task.

- [ ] **Step 5: Add the factory**

In `GitIntegration/GitRepository.cs`, after `Worktrees()`:

```csharp
	/// <summary>Creates an additional working tree.</summary>
	/// <param name="path">Where the new worktree goes.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitWorktreeAddBuilder AddWorktree(AbsoluteDirectoryPath path)
	{
		// Argument validation precedes the state check, matching every other verb taking an operand.
		Ensure.NotNull(path);
		return new GitWorktreeAddBuilder(RequireRunner(), RequireLocalPath(), path);
	}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeBuilderTests|FullyQualifiedName~GitRepositoryMutatingVerbTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Builders/GitWorktreeAddBuilder.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs GitIntegration.Test/GitRepositoryMutatingVerbTests.cs
git commit -m "feat: add the worktree creation verb"
```

---

## Task 4: The worktree remove and prune verbs

**Files:**
- Create: `GitIntegration/Builders/GitWorktreeWriteBuilders.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`
- Test: `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`

**Interfaces:**
- Consumes: everything Task 3 consumes.
- Produces: `IGitWorktreeRemoveBuilder` with `Force()`; `IGitWorktreePruneBuilder` with no members; `GitRepository.RemoveWorktree(AbsoluteDirectoryPath path)` and `GitRepository.PruneWorktrees()`.

Both live in one file because they are two small builders over the same verb that change together, matching `GitRemoteWriteBuilders`-style grouping already in the test project.

- [ ] **Step 1: Write the failing tests**

Append to `GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs`, inside the class:

```csharp
	[TestMethod]
	public void BuildsTheWorktreeRemoveVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeRemoveBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		CollectionAssert.AreEqual(
			Expect("worktree", "remove", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsForceOnRemoveBeforeThePath()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeRemoveBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Force();

		CollectionAssert.AreEqual(
			Expect("worktree", "remove", "--force", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheWorktreePruneVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreePruneBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.AreEqual(
			Expect("worktree", "prune"),
			builder.BuildArguments().ToArray());
	}
```

Append to `GitIntegration.Test/GitRepositoryMutatingVerbTests.cs`, inside the class:

```csharp
	[TestMethod]
	public void RemoveWorktreeRejectsANullPath()
	{
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			ProcessRunner = new RecordingGitProcessRunner(),
		};

		_ = Assert.ThrowsExactly<ArgumentNullException>(() => repository.RemoveWorktree(null!));
	}

	[TestMethod]
	public void PruneWorktreesRequiresAProcessRunner()
	{
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		_ = Assert.ThrowsExactly<InvalidOperationException>(() => repository.PruneWorktrees());
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitWorktreeBuilderTests"`
Expected: compile failure, the two builders do not exist.

- [ ] **Step 3: Write the builders**

Create `GitIntegration/Builders/GitWorktreeWriteBuilders.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a working tree and the administrative record of it.
/// </summary>
public interface IGitWorktreeRemoveBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Removes the worktree even when its working directory is not clean.</summary>
	/// <remarks>
	/// Git refuses to remove a worktree holding modified tracked files or untracked files, since
	/// doing so discards them. This says the caller knows.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeRemoveBuilder Force();
}

/// <summary>
/// Removes administrative records for working trees whose directories have gone.
/// </summary>
/// <remarks>
/// No options. <c>--dry-run</c> is deliberately not exposed: it would change this verb's result
/// from <see cref="GitCompleted"/> into a listing, and a caller that wants to know what would be
/// removed reads <see cref="GitWorktree.IsPrunable"/> from a listing it can already obtain.
/// </remarks>
public interface IGitWorktreePruneBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git worktree remove</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">The worktree to remove.</param>
internal sealed class GitWorktreeRemoveBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	AbsoluteDirectoryPath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreeRemoveBuilder
{
	private readonly AbsoluteDirectoryPath _path = Ensure.NotNull(path);

	private bool _force;

	/// <inheritdoc />
	public IGitWorktreeRemoveBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("remove");

		if (_force)
		{
			arguments.Add("--force");
		}

		AppendOperands(arguments, _path.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) => new();
}

/// <summary>
/// Builds <c>git worktree prune</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitWorktreePruneBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreePruneBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("prune");
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) => new();
}
```

- [ ] **Step 4: Add the factories**

In `GitIntegration/GitRepository.cs`, after `AddWorktree`:

```csharp
	/// <summary>Removes a working tree and the administrative record of it.</summary>
	/// <param name="path">The worktree to remove.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitWorktreeRemoveBuilder RemoveWorktree(AbsoluteDirectoryPath path)
	{
		Ensure.NotNull(path);
		return new GitWorktreeRemoveBuilder(RequireRunner(), RequireLocalPath(), path);
	}

	/// <summary>Removes administrative records for working trees whose directories have gone.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitWorktreePruneBuilder PruneWorktrees() =>
		new GitWorktreePruneBuilder(RequireRunner(), RequireLocalPath());
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitWorktreeWriteBuilders.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitWorktreeBuilderTests.cs GitIntegration.Test/GitRepositoryMutatingVerbTests.cs
git commit -m "feat: add the worktree removal and prune verbs"
```

---

## Task 5: GitHub owner kinds

**Files:**
- Modify: `GitIntegration/Models/GitEnums.cs`
- Modify: `GitIntegration/GitHubProvider.cs`
- Create: `GitIntegration.Test/Fixtures/github-org-repositories.json`
- Test: `GitIntegration.Test/Hosting/GitHubProviderTests.cs`

**Interfaces:**
- Consumes: `GitProvider.Owner` (`GitProviderOwner`), `GitHubProvider.CreateClient()` returning `(GitHubClient, IDisposable)`, `GitHubProvider.ToGitRepository(Octokit.Repository)`, `GitHubProvider.Translate(Octokit.ApiException)`, `FakeHttpMessageHandler` with `Respond(HttpStatusCode, string, params (string, string)[])` and `Requests`.
- Produces: `GitHubOwnerKind` enum with `User`, `Organization`, `AuthenticatedUser`; `GitHubProvider.OwnerKind` init property defaulting to `GitHubOwnerKind.User`.

**Important:** an existing test, `GitHubProviderTests`, asserts `"/users/contoso/repos"` as the route. The default must keep that test passing untouched. If it fails, the default is wrong, not the test.

- [ ] **Step 1: Create the organisation fixture**

Copy `GitIntegration.Test/Fixtures/github-repositories.json` to `github-org-repositories.json`, then edit the copy so that it contains exactly two entries: the first with `"name": "org-public-repo"`, `"private": false`, and an owner block whose `"login"` is `"contoso"`; the second with `"name": "org-private-repo"`, `"private": true`, and the same owner login. Update each entry's `full_name`, `html_url`, and `clone_url` to match its name under `contoso`. Leave every other key exactly as captured.

Confirm the file is copied to the test output: check `GitIntegration.Test/GitIntegration.Test.csproj` for how the existing fixtures are included (grep: `grep -n "Fixtures" GitIntegration.Test/GitIntegration.Test.csproj`). If they are listed individually rather than by wildcard, add the new file.

- [ ] **Step 2: Write the failing tests**

Append to `GitIntegration.Test/Hosting/GitHubProviderTests.cs`, inside the class:

```csharp
	[TestMethod]
	public async Task EnumeratesAnOrganisationThroughTheOrgsRoute()
	{
		// The route is a documented part of this provider's contract, not an Octokit detail:
		// GET /orgs/{org}/repos is the only one of the three that reports an organisation's private
		// repositories to a token that can see them.
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, Fixture("github-org-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			OwnerKind = GitHubOwnerKind.Organization,
			Handler = handler,
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/orgs/contoso/repos", handler.Requests[0].Uri.AbsolutePath);
		Assert.AreEqual(2, repositories.Count);
		Assert.AreEqual("org-public-repo".As<GitRepositoryName>(), repositories[0].Name);
		Assert.AreEqual("org-private-repo".As<GitRepositoryName>(), repositories[1].Name);
	}

	[TestMethod]
	public async Task EnumeratesTheAuthenticatedAccountThroughTheUserRoute()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, Fixture("github-org-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			OwnerKind = GitHubOwnerKind.AuthenticatedUser,
			Handler = handler,
		};

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/user/repos", handler.Requests[0].Uri.AbsolutePath);
		StringAssert.Contains(handler.Requests[0].Uri.Query, "affiliation=owner%2Corganization_member");
	}

	[TestMethod]
	public async Task DropsRepositoriesBelongingToAnotherOwnerWhenEnumeratingTheAuthenticatedAccount()
	{
		// GET /user/repos takes no owner parameter, so routing to it unfiltered would silently ignore
		// a configured Owner. The filter is what keeps this method's contract "Owner's repositories".
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, Fixture("github-org-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "someone-else".As<GitProviderOwner>(),
			OwnerKind = GitHubOwnerKind.AuthenticatedUser,
			Handler = handler,
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, repositories.Count);
	}

	[TestMethod]
	public async Task MatchesTheOwnerWithoutRegardToCase()
	{
		// GitHub treats logins as case-insensitive. An ordinal comparison would drop a caller's
		// repositories over a capital letter the caller did not choose.
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, Fixture("github-org-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "CONTOSO".As<GitProviderOwner>(),
			OwnerKind = GitHubOwnerKind.AuthenticatedUser,
			Handler = handler,
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, repositories.Count);
	}

	[TestMethod]
	public async Task DefaultsToTheUserRouteItAlwaysUsed()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/users/contoso/repos", handler.Requests[0].Uri.AbsolutePath);
	}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitHubProviderTests"`
Expected: compile failure, `GitHubOwnerKind` and `OwnerKind` do not exist.

- [ ] **Step 4: Add the enum**

Append to `GitIntegration/Models/GitEnums.cs`:

```csharp
/// <summary>
/// What kind of account a <see cref="GitHubProvider"/>'s owner is, which decides the endpoint its
/// repository enumeration can use.
/// </summary>
/// <remarks>
/// GitHub answers "which repositories does this owner have" at three different routes with three
/// different coverages, and no single one of them serves every caller. Stating the kind is what lets
/// this provider pick correctly without probing, and without a contract that depends on what GitHub
/// answers to a speculative request.
/// </remarks>
public enum GitHubOwnerKind
{
	/// <summary>
	/// A user account other than the token's own. Enumerates that user's <b>public</b> repositories
	/// only, whatever credential is supplied.
	/// </summary>
	User,

	/// <summary>
	/// An organisation. Enumerates the organisation's repositories, including private ones the
	/// credential can see.
	/// </summary>
	Organization,

	/// <summary>
	/// The account the credential belongs to. Enumerates every repository that account owns or can
	/// reach through an organisation membership, narrowed to
	/// <see cref="GitProvider.Owner"/>.
	/// </summary>
	AuthenticatedUser,
}
```

- [ ] **Step 5: Add the property and the routing**

In `GitIntegration/GitHubProvider.cs`, add the property after `Name`:

```csharp
	/// <summary>
	/// Gets what kind of account <see cref="GitProvider.Owner"/> is.
	/// </summary>
	/// <remarks>
	/// <see cref="GitHubOwnerKind.User"/> by default, which is the route and the coverage this
	/// provider has always had. A caller needing an organisation's private repositories sets
	/// <see cref="GitHubOwnerKind.Organization"/>; one needing its own sets
	/// <see cref="GitHubOwnerKind.AuthenticatedUser"/>.
	/// </remarks>
	public GitHubOwnerKind OwnerKind { get; init; } = GitHubOwnerKind.User;
```

Replace the body of `GetRepositoriesAsync`, keeping its existing signature, and **rewrite its `<remarks>`** so the documentation no longer claims the method is public-only unconditionally:

```csharp
	/// <inheritdoc/>
	/// <remarks>
	/// Adds to the interface's remarks rather than restating them.
	/// <see cref="IGitHostingProvider.GetRepositoriesAsync"/> says only that implementations differ
	/// in coverage and points here for this host's specifics, so the two texts have one job each and
	/// neither is a copy of the other. An edit that moves this explanation must leave that pointer
	/// aimed somewhere real.
	/// <para>
	/// The route, and therefore the coverage, is decided by <see cref="OwnerKind"/>.
	/// <see cref="GitHubOwnerKind.User"/> — the default — calls <c>GET /users/{login}/repos</c>,
	/// which returns <see cref="GitProvider.Owner"/>'s <b>public</b> repositories only; supplying a
	/// token does not widen it, because that endpoint does not honour authentication to reveal
	/// private repositories. <see cref="GitHubOwnerKind.Organization"/> calls
	/// <c>GET /orgs/{org}/repos</c>, which does report private repositories the credential can see.
	/// <see cref="GitHubOwnerKind.AuthenticatedUser"/> calls <c>GET /user/repos</c> with
	/// <c>affiliation=owner,organization_member</c>.
	/// </para>
	/// <para>
	/// <c>GET /user/repos</c> takes no owner parameter — it always describes the token's own
	/// reachable repositories — so that route's results are filtered to
	/// <see cref="GitProvider.Owner"/> here, case-insensitively, since GitHub treats logins that way.
	/// Without the filter, selecting that kind would silently ignore a configured owner, which is the
	/// objection that kept this method on the user route in the first place. Filtering rather than
	/// validating the token's login against <see cref="GitProvider.Owner"/> costs no extra request
	/// and still serves an organisation the token is merely a member of.
	/// </para>
	/// </remarks>
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable createdTransport) = CreateClient();
		using IDisposable transport = createdTransport;

		try
		{
			IReadOnlyList<Repository> repositories = OwnerKind switch
			{
				GitHubOwnerKind.Organization =>
					await client.Repository.GetAllForOrg(Owner.WeakString).ConfigureAwait(false),
				GitHubOwnerKind.AuthenticatedUser =>
					FilterToOwner(await client.Repository.GetAllForCurrent(AuthenticatedUserRequest).ConfigureAwait(false)),
				_ => await client.Repository.GetAllForUser(Owner.WeakString).ConfigureAwait(false),
			};

			return [.. repositories.Select(ToGitRepository)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <summary>
	/// The request <see cref="GitHubOwnerKind.AuthenticatedUser"/> enumerates with.
	/// </summary>
	/// <remarks>
	/// The affiliation is stated explicitly rather than left to GitHub's default, for the reason the
	/// pull request listing states its own filter: this provider's coverage is defined by this
	/// library, not by restating whichever default a vendor happens to ship today.
	/// </remarks>
	private static RepositoryRequest AuthenticatedUserRequest => new()
	{
		Affiliation = RepositoryAffiliation.OwnerAndOrganizationMember,
	};

	/// <summary>
	/// Drops repositories belonging to anyone but <see cref="GitProvider.Owner"/>.
	/// </summary>
	/// <remarks>
	/// Only <see cref="GitHubOwnerKind.AuthenticatedUser"/> needs this: the other two routes carry
	/// the owner in the request path and cannot answer for anybody else. Compared with
	/// <see cref="StringComparison.OrdinalIgnoreCase"/> because GitHub logins are case-insensitive.
	/// </remarks>
	/// <param name="repositories">Everything the route reported.</param>
	/// <returns>The subset this provider's owner has.</returns>
	private IReadOnlyList<Repository> FilterToOwner(IReadOnlyList<Repository> repositories) =>
		[.. repositories.Where(repository =>
			string.Equals(repository.Owner?.Login, Owner.WeakString, StringComparison.OrdinalIgnoreCase))];
```

Verify `RepositoryAffiliation.OwnerAndOrganizationMember` is the exact Octokit 14.0.0 member name before building:

```bash
grep -o 'name="F:Octokit.RepositoryAffiliation[^"]*"' ~/.nuget/packages/octokit/14.0.0/lib/netstandard2.0/Octokit.xml
```

If the member is named differently, use the name that grep reports and keep the query-string assertion in the test aligned with whatever Octokit then sends.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitHubProviderTests"`
Expected: PASS, including every pre-existing test in that class unchanged.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Models/GitEnums.cs GitIntegration/GitHubProvider.cs GitIntegration.Test/Hosting/GitHubProviderTests.cs GitIntegration.Test/Fixtures/github-org-repositories.json GitIntegration.Test/GitIntegration.Test.csproj
git commit -m "feat: route GitHub repository enumeration by owner kind"
```

---

## Task 6: The single sign-on authorisation URL

**Files:**
- Modify: `GitIntegration/GitHubProvider.cs`
- Test: `GitIntegration.Test/Hosting/GitHubProviderTests.cs`

**Interfaces:**
- Consumes: `GitHubProvider.Translate(Octokit.ApiException)`, `GitHubProvider.TryGetRetryAfterSeconds(Octokit.ApiException)` as the pattern for reading a header, `GitHostingAuthenticationException`.
- Produces: no new public surface. `Translate` returns the same exception types; only an authentication failure's message changes when the header is present.

### Background

A token that is valid but not authorised for an organisation's SAML single sign-on gets `403` with an `X-GitHub-SSO` header. Its value looks like `required; url=https://github.com/orgs/contoso/sso?authorization_request=ABC123`. Without surfacing that URL, "bad token" and "good token one click from working" are the same exception with the same text.

- [ ] **Step 1: Write the failing tests**

Append to `GitIntegration.Test/Hosting/GitHubProviderTests.cs`, inside the class:

```csharp
	[TestMethod]
	public async Task ReportsTheSingleSignOnAuthorisationUrlOnAForbiddenResponse()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.Forbidden,
			"{\"message\":\"Resource protected by organization SAML enforcement.\"}",
			("Content-Type", "application/json"),
			("X-GitHub-SSO", "required; url=https://github.com/orgs/contoso/sso?authorization_request=ABC123"));
		GitHubProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			OwnerKind = GitHubOwnerKind.Organization,
			Handler = handler,
		};

		GitHostingAuthenticationException exception =
			await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
				async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "https://github.com/orgs/contoso/sso?authorization_request=ABC123");
	}

	[TestMethod]
	public async Task StillReportsAForbiddenResponseCarryingNoSingleSignOnHeader()
	{
		// The URL is an addition to the message, never a requirement for classifying the failure.
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.Forbidden,
			"{\"message\":\"Bad credentials\"}",
			("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		_ = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitHubProviderTests"`
Expected: the first test FAILS, the message contains no URL. The second passes already.

- [ ] **Step 3: Add the header reader**

`Translate` is a switch expression at `GitIntegration/GitHubProvider.cs:424`, and `TryGetRetryAfterSeconds` sits just below it. The header reader follows that method's pattern exactly: headers are scanned case-insensitively rather than looked up by key, because HTTP header names are case-insensitive while Octokit's header dictionary compares ordinally.

Add to `GitIntegration/GitHubProvider.cs`, next to `TryGetRetryAfterSeconds`:

```csharp
	/// <summary>
	/// Reads the authorisation URL from a failed response's single sign-on header, if it carries one.
	/// </summary>
	/// <remarks>
	/// Scanned case-insensitively rather than looked up by key, for the reason
	/// <see cref="TryGetRetryAfterSeconds"/> gives: HTTP header names are case-insensitive and
	/// Octokit's header dictionary compares them ordinally, so a keyed lookup would work only by
	/// matching whatever casing Octokit happens to canonicalise this header to.
	/// <para>
	/// The header's value is a parameter list, <c>required; url=&lt;uri&gt;</c>. Only the URL is read,
	/// and anything else in the list is left alone: the caller's whole use for this is a link to open.
	/// </para>
	/// </remarks>
	/// <param name="exception">The failed response.</param>
	/// <returns>The authorisation URL, or <see langword="null"/> when the header is absent or carries none.</returns>
	private static string? TryGetSingleSignOnUrl(ApiException exception)
	{
		const string headerName = "X-GitHub-SSO";
		const string urlParameter = "url=";

		if (exception.HttpResponse?.Headers is not IReadOnlyDictionary<string, string> headers)
		{
			return null;
		}

		foreach (KeyValuePair<string, string> header in headers.Where(
			candidate => candidate.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase)))
		{
			foreach (string parameter in header.Value.Split(';'))
			{
				string trimmed = parameter.Trim();

				if (trimmed.StartsWith(urlParameter, StringComparison.OrdinalIgnoreCase))
				{
					string url = trimmed[urlParameter.Length..];
					return url.Length == 0 ? null : url;
				}
			}
		}

		return null;
	}
```

- [ ] **Step 4: Wire it into Translate**

`Translate`'s arms each pass `exception.Message`, so the amended message is computed once before the switch and used by the two authentication arms only. Add below the existing `responseBody` line:

```csharp
		// A token that is valid but unauthorised for an organisation's single sign-on arrives as a
		// plain 403, indistinguishable in status and body from a bad credential. The header is the
		// only thing carrying the URL that resolves it, and that URL is the whole remedy — without
		// it, the two failures a caller most needs to tell apart read identically.
		string? singleSignOnUrl = TryGetSingleSignOnUrl(exception);
		string authenticationMessage = singleSignOnUrl is null
			? exception.Message
			: $"{exception.Message} This organisation requires single sign-on authorisation for " +
			  $"this credential. Authorise it at: {singleSignOnUrl}";
```

Then change only these two arms, leaving every other arm and the arm ordering untouched. The ordering of the rate-limit, authentication and not-found arms is load-bearing and documented as such in the method's remarks:

```csharp
			AuthorizationException => new GitHostingAuthenticationException(authenticationMessage, Name, exception.StatusCode, responseBody, exception),
			ForbiddenException => new GitHostingAuthenticationException(authenticationMessage, Name, exception.StatusCode, responseBody, exception),
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitHubProviderTests"`
Expected: PASS.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/GitHubProvider.cs GitIntegration.Test/Hosting/GitHubProviderTests.cs
git commit -m "feat: report the single sign-on authorisation url on a forbidden response"
```

---

## Task 7: The GitHub device flow

**Files:**
- Modify: `GitIntegration/SemanticTypes/GitProviderTypes.cs`
- Create: `GitIntegration/Hosting/GitHubDeviceFlow.cs`
- Test: `GitIntegration.Test/Hosting/GitHubDeviceFlowTests.cs`

**Interfaces:**
- Consumes: `HostingCredential.FromToken(string)`, `GitHostingAuthenticationException`, `GitHostingRequestException`, `GitProvider.CreateDefaultHandler()` as the pattern for a shared transport, `FakeHttpMessageHandler`.
- Produces: `GitHubOAuthClientId` semantic string; `GitHubDeviceCode` record with `DeviceCode`, `UserCode`, `VerificationUri`, `ExpiresIn`, `Interval`; `GitHubDeviceFlow(GitHubOAuthClientId clientId, IReadOnlyList<string> scopes)` with `RequestDeviceCodeAsync` and `WaitForTokenAsync`.

### Correction to the spec

The spec's `GitHubDeviceCode` omitted the **device code** itself. `Octokit.IOauthClient.CreateAccessTokenForDeviceFlow(string clientId, OauthDeviceFlowResponse response, CancellationToken)` needs that value to resume, and it is distinct from the user code: the user code is the short string a human types, the device code is the opaque one the polling request carries. `GitHubDeviceCode` therefore carries both. Correct the spec's record in the same commit.

### Confirmed Octokit 14.0.0 surface

- `IOauthClient.InitiateDeviceFlow(OauthDeviceFlowRequest, CancellationToken)` returns `Task<OauthDeviceFlowResponse>`.
- `OauthDeviceFlowRequest(string clientId)` with a `Scopes` collection.
- `OauthDeviceFlowResponse` carries `DeviceCode`, `UserCode`, `VerificationUri`, `ExpiresIn`, `Interval`.
- `IOauthClient.CreateAccessTokenForDeviceFlow(string, OauthDeviceFlowResponse, CancellationToken)` returns `Task<OauthToken>`, and its documentation states it "will poll the access token endpoint, until the device and user codes expire or the user has successfully authorized the app".
- `OauthToken` carries `AccessToken`, `Error`, `ErrorDescription`.
- `GitHubClient.Oauth` exposes the client.

`ExpiresIn` and `Interval` are integer seconds and are converted to `TimeSpan` at this library's boundary.

- [ ] **Step 1: Write the failing tests**

Create `GitIntegration.Test/Hosting/GitHubDeviceFlowTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Net;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitHubDeviceFlowTests
{
	public TestContext TestContext { get; set; } = null!;

	private const string DeviceCodeBody =
		"{\"device_code\":\"dev-abc\",\"user_code\":\"WXYZ-1234\"," +
		"\"verification_uri\":\"https://github.com/login/device\"," +
		"\"expires_in\":900,\"interval\":5}";

	private static GitHubDeviceFlow CreateFlow(FakeHttpMessageHandler handler) =>
		new("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), ["repo", "read:org"]) { Handler = handler };

	[TestMethod]
	public async Task RequestsADeviceCodeAndReportsWhatTheUserNeeds()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));

		GitHubDeviceCode code = await CreateFlow(handler)
			.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("WXYZ-1234", code.UserCode);
		Assert.AreEqual("dev-abc", code.DeviceCode);
		Assert.AreEqual(new Uri("https://github.com/login/device"), code.VerificationUri);
		Assert.AreEqual(TimeSpan.FromSeconds(900), code.ExpiresIn);
		Assert.AreEqual(TimeSpan.FromSeconds(5), code.Interval);
	}

	[TestMethod]
	public async Task SendsTheRequestedScopes()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));

		_ = await CreateFlow(handler)
			.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		StringAssert.Contains(handler.Requests[0].Body, "repo");
		StringAssert.Contains(handler.Requests[0].Body, "read:org");
	}

	[TestMethod]
	public async Task ReturnsAHostNativeTokenOnSuccess()
	{
		// FromToken, not FromBearerToken: a GitHub OAuth token travels under Octokit's Token scheme.
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"access_token\":\"gho_realtoken\",\"token_type\":\"bearer\",\"scope\":\"repo,read:org\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		HostingCredential credential = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HostingCredentialKind.Token, credential.Kind);
		Assert.AreEqual("gho_realtoken", credential.Token);
	}

	[TestMethod]
	public async Task ReportsARefusedAuthorisationAsAnAuthenticationFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"error\":\"access_denied\",\"error_description\":\"The user denied the request.\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		GitHostingAuthenticationException exception =
			await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "access_denied");
	}

	[TestMethod]
	public async Task ReportsAnExpiredCodeAsAnAuthenticationFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"error\":\"expired_token\",\"error_description\":\"The device code has expired.\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		_ = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsAnUnusableClientIdentifierAsARequestFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.NotFound,
			"{\"error\":\"Not Found\"}",
			("Content-Type", "application/json"));

		_ = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await CreateFlow(handler)
				.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsANullClientIdentifier() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(() => new GitHubDeviceFlow(null!, ["repo"]));

	[TestMethod]
	public void RejectsNullScopes() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(
			() => new GitHubDeviceFlow("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), null!));
}
```

Check whether `FakeHttpMessageHandler`'s `RecordedRequest` exposes a `Body` property (grep: `grep -n "record RecordedRequest\|public.*Body" GitIntegration.Test/Fakes/FakeHttpMessageHandler.cs`). If it does not, add one capturing the request content as a string, and cover it with a test in `FakeHttpMessageHandlerTests` in this same task.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitHubDeviceFlowTests"`
Expected: compile failure, `GitHubDeviceFlow` does not exist.

- [ ] **Step 3: Add the semantic type**

Append to `GitIntegration/SemanticTypes/GitProviderTypes.cs`:

```csharp
/// <summary>
/// The client identifier GitHub issues when an OAuth App is registered.
/// </summary>
/// <remarks>
/// Not a secret. The device flow has no client secret precisely because a desktop binary cannot keep
/// one, so a consuming application may hold this in ordinary configuration. It is a value this
/// library takes rather than one it ships: the identifier belongs to whoever registered the app.
/// </remarks>
[HasNonWhitespaceContent]
public sealed record GitHubOAuthClientId : SemanticString<GitHubOAuthClientId> { }
```

- [ ] **Step 4: Write the flow**

Create `GitIntegration/Hosting/GitHubDeviceFlow.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Octokit;
using Octokit.Internal;

/// <summary>
/// What GitHub issues when a device flow begins: the code a human types, and the code the polling
/// request carries.
/// </summary>
/// <remarks>
/// <see cref="UserCode"/> and <see cref="DeviceCode"/> are different values for different audiences.
/// The user code is short and is displayed; the device code is opaque and is what
/// <see cref="GitHubDeviceFlow.WaitForTokenAsync"/> sends. Showing the device code to a user, or
/// sending the user code in its place, both fail in ways that look like a broken sign-in.
/// </remarks>
public sealed record GitHubDeviceCode
{
	/// <summary>Gets the code the user enters at <see cref="VerificationUri"/>.</summary>
	public required string UserCode { get; init; }

	/// <summary>Gets the code the token request carries. Not shown to the user.</summary>
	public required string DeviceCode { get; init; }

	/// <summary>Gets the page the user opens to enter <see cref="UserCode"/>.</summary>
	public required Uri VerificationUri { get; init; }

	/// <summary>Gets how long this code remains usable.</summary>
	/// <remarks>Surfaced because a caller showing a countdown needs it and cannot derive it.</remarks>
	public required TimeSpan ExpiresIn { get; init; }

	/// <summary>Gets the minimum wait GitHub requires between token requests.</summary>
	public required TimeSpan Interval { get; init; }
}

/// <summary>
/// Obtains a GitHub credential through the OAuth device flow.
/// </summary>
/// <remarks>
/// <para>
/// Two calls rather than one. Device flow has an inherent pause in the middle — GitHub issues a
/// short code, the user types it into a browser, and only then does polling succeed — and that pause
/// is minutes long with the code on screen throughout. A single method taking a "here's the code"
/// callback would invoke it from whatever thread an HTTP continuation resumed on, leaving every
/// graphical caller to marshal the code back to a user interface thread from inside a callback it
/// does not control. Splitting the call puts the seam where the pause already is.
/// </para>
/// <para>
/// Separate from <see cref="GitHubProvider"/>, whose every other method is one request and one
/// response. Nothing here resolves or applies a credential; this type produces one and
/// <see cref="GitProvider"/> consumes one, through the credential cache or
/// <see cref="GitProvider.CredentialSource"/>.
/// </para>
/// <para>
/// Stores nothing. <see cref="WaitForTokenAsync"/> returns the credential and the caller decides
/// where it lives:
/// </para>
/// <code>
/// GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(cancellationToken);
/// // show code.UserCode and open code.VerificationUri
/// HostingCredential credential = await flow.WaitForTokenAsync(code, cancellationToken);
/// CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = credential.Token! });
/// </code>
/// </remarks>
/// <param name="clientId">The OAuth App's client identifier.</param>
/// <param name="scopes">The scopes to request, such as <c>repo</c> and <c>read:org</c>.</param>
public sealed class GitHubDeviceFlow(GitHubOAuthClientId clientId, IReadOnlyList<string> scopes)
{
	/// <summary>
	/// The transport every flow shares when no <see cref="Handler"/> was injected.
	/// </summary>
	/// <remarks>
	/// Its own instance rather than <see cref="GitHubProvider"/>'s, matching that type's reasoning:
	/// one handler for the process rather than one per call, and
	/// <see cref="GitProvider.CreateDefaultHandler"/> is what keeps the settings from drifting apart.
	/// </remarks>
	private static readonly SocketsHttpHandler SharedHandler = GitProvider.CreateDefaultHandler();

	private readonly GitHubOAuthClientId _clientId = Ensure.NotNull(clientId);
	private readonly IReadOnlyList<string> _scopes = Ensure.NotNull(scopes);

	/// <summary>
	/// Gets or initializes the transport this flow issues requests through, or <see langword="null"/>
	/// to use the shared one.
	/// </summary>
	/// <remarks>
	/// Internal rather than public, so no transport type appears in this library's public API, and
	/// the test project injects a fake through <c>InternalsVisibleTo</c> — the same seam
	/// <see cref="GitProvider.Handler"/> provides.
	/// </remarks>
	internal HttpMessageHandler? Handler { get; init; }

	/// <summary>
	/// Asks GitHub to begin a device flow.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The codes and timings the flow's second half needs.</returns>
	/// <exception cref="GitHostingException">GitHub refused or could not be reached.</exception>
	public async Task<GitHubDeviceCode> RequestDeviceCodeAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		GitHubClient client = CreateClient();

		OauthDeviceFlowRequest request = new(_clientId.WeakString);

		foreach (string scope in _scopes)
		{
			request.Scopes.Add(scope);
		}

		try
		{
			OauthDeviceFlowResponse response =
				await client.Oauth.InitiateDeviceFlow(request, cancellationToken).ConfigureAwait(false);

			return new GitHubDeviceCode
			{
				UserCode = response.UserCode,
				DeviceCode = response.DeviceCode,
				VerificationUri = new Uri(response.VerificationUri),
				// GitHub reports both as integer seconds; the conversion happens once, here.
				ExpiresIn = TimeSpan.FromSeconds(response.ExpiresIn),
				Interval = TimeSpan.FromSeconds(response.Interval),
			};
		}
		catch (ApiException exception)
		{
			throw new GitHostingRequestException(
				$"GitHub refused to begin a device flow: {exception.Message}", exception);
		}
	}

	/// <summary>
	/// Waits for the user to authorise the flow, then returns the credential GitHub issues.
	/// </summary>
	/// <remarks>
	/// Polls until the user authorises, the code expires, or <paramref name="cancellationToken"/> is
	/// cancelled, so this may block for as long as <see cref="GitHubDeviceCode.ExpiresIn"/>. Octokit
	/// handles the <c>authorization_pending</c> and <c>slow_down</c> responses internally, at the
	/// interval GitHub asked for.
	/// </remarks>
	/// <param name="code">What <see cref="RequestDeviceCodeAsync"/> returned.</param>
	/// <param name="cancellationToken">Abandons the wait.</param>
	/// <returns>A host-native token credential.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
	/// <exception cref="GitHostingAuthenticationException">The user refused, or the code expired.</exception>
	/// <exception cref="GitHostingRequestException">GitHub refused the request or could not be reached.</exception>
	public async Task<HostingCredential> WaitForTokenAsync(GitHubDeviceCode code, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(code);
		cancellationToken.ThrowIfCancellationRequested();

		GitHubClient client = CreateClient();

		// Octokit resumes from its own response type rather than from ours, so the fields it needs
		// are handed back in the shape it expects. Only DeviceCode and Interval are read on this
		// path; the rest are set so the value is not half-populated if Octokit's use ever widens.
		OauthDeviceFlowResponse response = new()
		{
			DeviceCode = code.DeviceCode,
			UserCode = code.UserCode,
			VerificationUri = code.VerificationUri.ToString(),
			ExpiresIn = (int)code.ExpiresIn.TotalSeconds,
			Interval = (int)code.Interval.TotalSeconds,
		};

		OauthToken token;

		try
		{
			token = await client.Oauth
				.CreateAccessTokenForDeviceFlow(_clientId.WeakString, response, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (ApiException exception)
		{
			throw new GitHostingRequestException(
				$"GitHub refused the device flow token request: {exception.Message}", exception);
		}

		// GitHub reports a refusal and an expiry as a 200 carrying an error field rather than as a
		// failure status, so this is checked before the token is read rather than caught above.
		if (!string.IsNullOrEmpty(token.Error))
		{
			throw new GitHostingAuthenticationException(
				$"GitHub did not issue a token: {token.Error}. {token.ErrorDescription}".TrimEnd());
		}

		return string.IsNullOrEmpty(token.AccessToken)
			? throw new GitHostingRequestException("GitHub reported neither a token nor an error.")
			// FromToken, not FromBearerToken: a GitHub OAuth token travels under Octokit's Token
			// scheme, which is what FromToken means. FromBearerToken is for an Entra ID access token
			// against Azure DevOps.
			: HostingCredential.FromToken(token.AccessToken);
	}

	/// <summary>
	/// Creates an Octokit client wired to this flow's transport.
	/// </summary>
	/// <remarks>
	/// Unauthenticated by construction: obtaining a credential is what this type is for, so it has
	/// none to send. The handler is wrapped so that disposing the client never disposes a transport
	/// this type does not own — <see cref="SharedHandler"/> outlives every call, and an injected one
	/// belongs to whoever supplied it.
	/// </remarks>
	/// <returns>The client.</returns>
	private GitHubClient CreateClient() =>
		new(new ProductHeaderValue("ktsu-GitIntegration"),
			new HttpClientAdapter(() => new GitHubProvider.NonOwningHandler(Handler ?? SharedHandler)));
}
```

Three things to verify while implementing, because they are read from the package's shape rather than from its source:

1. `GitProvider.CreateDefaultHandler` and `GitHubProvider.NonOwningHandler` are both currently `private` or `private protected`. Widen each to `internal` (or `private protected` to `internal`) so this type can reach them, and note the widening in the commit message. Do not make either public.
2. `OauthDeviceFlowResponse`'s properties may have no public setters. If they do not, construct it through whichever constructor Octokit exposes (`grep -o 'name="M:Octokit.OauthDeviceFlowResponse[^"]*"' ~/.nuget/packages/octokit/14.0.0/lib/netstandard2.0/Octokit.xml`). If none is usable, replace this type's two Octokit calls with direct posts to `https://github.com/login/device/code` and `https://github.com/login/oauth/access_token` with `Accept: application/json`, polling at `Interval` and widening by five seconds on each `slow_down`, and keep every test in Step 1 unchanged.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitHubDeviceFlowTests"`
Expected: PASS.

- [ ] **Step 6: Correct the spec's record**

In `docs/superpowers/specs/2026-09-21-worktrees-and-github-auth-design.md`, add `DeviceCode` to the `GitHubDeviceCode` listing in the Device flow section, with a sentence saying it is the opaque code the polling request carries, distinct from the user code.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add GitIntegration/Hosting/GitHubDeviceFlow.cs GitIntegration/SemanticTypes/GitProviderTypes.cs GitIntegration/GitProvider.cs GitIntegration/GitHubProvider.cs GitIntegration.Test/Hosting/GitHubDeviceFlowTests.cs GitIntegration.Test/Fakes/FakeHttpMessageHandler.cs docs/superpowers/specs/2026-09-21-worktrees-and-github-auth-design.md
git commit -m "feat: obtain a GitHub credential through the OAuth device flow"
```

---

## Task 8: Documentation

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `VERSION.md`

**Interfaces:**
- Consumes: every public member added in Tasks 1 through 7.
- Produces: nothing code depends on.

- [ ] **Step 1: Check how the version and changelog are maintained**

Run: `head -30 CHANGELOG.md && cat VERSION.md && grep -rn "version" .github/workflows/*.yml 2>/dev/null | head`

The ktsu.Sdk build generates some metadata automatically. If `CHANGELOG.md` and `VERSION.md` carry a `[bot]` provenance in `git log`, they are generated: leave both alone and say so in the pull request instead of editing them.

- [ ] **Step 2: Update the features list**

In `README.md`, extend the existing **Features** bullets:

- Add `Worktrees()`, `AddWorktree(...)`, `RemoveWorktree(...)` and `PruneWorktrees()` to the **Fluent Verb Builders** bullet's verb lists, read-only and mutating respectively.
- Add `GitWorktree` to the **Strongly-Typed Results** bullet.
- Extend the **Hosting Provider Abstraction** bullet to say that `GitHubProvider` routes enumeration by `OwnerKind`, and that only `Organization` and `AuthenticatedUser` report private repositories.
- Add a new bullet: **Interactive GitHub Sign-In** — `GitHubDeviceFlow` obtains a credential through GitHub's OAuth device flow, in two calls so a caller can display the user code while the wait runs.

- [ ] **Step 3: Add usage examples**

In `README.md`, after the existing **Listing Commits and Diffs** example, add:

````markdown
### One Worktree per Branch

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

IReadOnlyList<GitWorktree> worktrees = await repository.Worktrees().ExecuteAsync();

GitBranchName branch = "feature/search".As<GitBranchName>();

if (!worktrees.Any(worktree => worktree.Branch == branch))
{
    AbsoluteDirectoryPath destination = "/repos/project-feature-search".As<AbsoluteDirectoryPath>();

    await repository.AddWorktree(destination)
        .CreatingBranch(branch)
        .From("origin/main".As<GitRefName>())
        .ExecuteAsync();
}
```

The main working tree reports `IsMain`, which is how a caller refuses to remove the one that owns the
repository. It is positional — git emits it first — rather than an attribute git labels.

### Signing In to GitHub

```csharp
using ktsu.CredentialCache;
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitHubDeviceFlow flow = new("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), ["repo", "read:org"]);

GitHubDeviceCode code = await flow.RequestDeviceCodeAsync();
Console.WriteLine($"Open {code.VerificationUri} and enter {code.UserCode}");

HostingCredential credential = await flow.WaitForTokenAsync(code);
CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = credential.Token! });
```

The two calls are split so the user code can stay on screen for the minutes the wait may take. The
flow stores nothing: where the credential lives is the caller's decision.

### Enumerating an Organisation's Private Repositories

```csharp
GitHubProvider provider = new()
{
    Owner = "contoso".As<GitProviderOwner>(),
    OwnerKind = GitHubOwnerKind.Organization,
    PersonaGUID = persona,
};

IReadOnlyList<GitRepository> repositories = await provider.GetRepositoriesAsync();
```

`OwnerKind` defaults to `GitHubOwnerKind.User`, which enumerates public repositories only — the route
this provider has always used. `Organization` and `AuthenticatedUser` report private repositories the
credential can see. A token that is valid but not authorised for an organisation's single sign-on
raises `GitHostingAuthenticationException` carrying the URL to authorise it at.
````

- [ ] **Step 4: Verify the documentation builds**

Run: `dotnet build`
Expected: no warnings about missing or malformed XML documentation.

- [ ] **Step 5: Run the full suite one last time**

Run: `dotnet test`
Expected: PASS, every test.

- [ ] **Step 6: Commit**

```bash
git add README.md
git commit -m "docs: document worktree verbs, owner kinds, and device flow"
```

---

## Open item, carried out of this plan

**The OAuth App does not exist yet.** Every test here runs against a fake transport, so no task is blocked. But nothing has been confirmed against GitHub itself, and it cannot be until someone with organisation ownership registers an OAuth App with device flow enabled and approves it for SAML single sign-on, scopes `repo` and `read:org`.

Record this in the pull request description as an unchecked item rather than claiming the device flow is verified end to end.

# Patch verbs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give this library the three git verbs a caller needs to show a diff and stage part of a file: reading a patch into hunks, applying one to the index, and unstaging.

**Architecture:** A model that keeps git's own hunk bytes alongside a parsed form, a parser that fills it, and three builders in the same shape as every other verb here. Assembling chosen hunks into an applyable patch is pure logic in the model, so it is tested without running git.

**Tech Stack:** C#, net10.0 and net9.0, MSTest 4.3.3 through MSTest.Sdk, `ktsu.Semantics.Paths` and `ktsu.Semantics.Strings`.

**Spec:** `docs/superpowers/specs/2026-09-24-patch-verbs-design.md`

## Global Constraints

Every task's requirements implicitly include this section.

- Copyright header on every new file, exactly: `// Copyright (c) 2023-2026 ktsu-dev contributors`
- Namespaces: `ktsu.GitIntegration` for library files, `ktsu.GitIntegration.Test` for tests. File-scoped, with every `using` **inside** the namespace declaration.
- Indent with tabs, never spaces.
- US spelling in identifiers, comments and user-facing strings.
- **In the library**, null guards are `Ensure.NotNull(x)` from Polyfill.
- **In the test project**, they are `ArgumentNullException.ThrowIfNull(x)`. Polyfill is referenced with `PrivateAssets="all"`, so `Ensure` is not visible there. This is the reverse of the library rule and the existing fakes carry a comment saying so.
- Compare sequences with `Assert.AreSequenceEqual`, never `CollectionAssert.AreEqual` or `AreEquivalent`. Existing tests still use `CollectionAssert`; new ones do not.
- Warnings are errors. The build fails on an unused `using` and on formatting.
- A public builder is an `IGitXBuilder` interface plus an `internal sealed class` implementing it, constructed with `(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)` and deriving from `GitCommandBuilder<TResult>`.
- `GitCommandBuilder<TResult>` provides `BuildArguments()` for tests, `ExecuteAsync`, `TryExecuteAsync`, and the overridable `GetDiagnostic` and `CreateException`. Subclasses implement `AppendVerbArguments(ICollection<string>)` and `ParseResult(GitProcessResult)`.
- `AppendOperands(arguments, params string[])` emits `--end-of-options` before its operands. Use it for anything caller-supplied.
- Every built vector begins `-C <root> --no-pager -c core.quotepath=false -c color.ui=false`, added by the base class. Task tests that assert a whole vector must include that prefix.
- Commit messages carry a version tag: `[major]`, `[minor]`, `[patch]`. This work is `[minor]`, since it adds surface without breaking any. **Do not add `Co-Authored-By` lines.**
- Do not edit `VERSION.md`, `CHANGELOG.md` or `LICENSE.md`.
- Build with `dotnet build`. `dotnet test` reports zero tests on macOS with this MSTest.Sdk setup: run `./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test` directly, optionally with `--filter "FullyQualifiedName~<ClassName>"`.
- Baseline before any change: **668 tests passing, 0 warnings.**
- Integration tests live in `GitIntegration.Test/Integration/`, call `await IntegrationGitFixture.RequireGitAsync(cancellationToken)` first so they skip where git is absent, and build repositories with `using TemporaryRepository repository = new();`.

## Review Focus

Five inputs the spec implies that no task's happy path exercises, most likely to bite first.

1. **An empty or whitespace patch handed to `Apply`.** Git reports an unhelpful error about a corrupt patch. A caller passing the result of selecting no hunks should get something better. Test in Task 4.
2. **`PatchFor` with no hunks.** Produces a header with no body, which is the input above. It should refuse rather than build it. Test in Task 1.
3. **The temporary file when apply throws.** A failing patch must not leave files in the temp directory, and the failure that matters is git's, not the cleanup's. Test in Task 4.
4. **A file both renamed and modified.** Carries rename headers *and* hunks, so code that treats rename as "no hunks" drops real changes. Test in Task 2.
5. **A patch generated, then the working tree moved.** `Checked()` must report failure and leave the index untouched, which is the guarantee a caller's refusal depends on. Test in Task 4.

---

## File Structure

| File | Responsibility |
|---|---|
| `GitIntegration/Models/GitPatch.cs` | **Create.** `GitPatch`, `GitFilePatch`, `GitHunk`, `GitPatchLine`, and `PatchFor` |
| `GitIntegration/Models/GitEnums.cs` | **Modify.** Add `GitPatchLineKind` |
| `GitIntegration/Parsing/GitPatchParser.cs` | **Create.** Unified diff text to `GitPatch` |
| `GitIntegration/Builders/GitPatchBuilder.cs` | **Create.** `IGitPatchBuilder` and its implementation |
| `GitIntegration/Builders/GitApplyBuilder.cs` | **Create.** `IGitApplyBuilder`, including the temporary file |
| `GitIntegration/Builders/GitRestoreBuilder.cs` | **Create.** `IGitRestoreBuilder`, including the version fallback |
| `GitIntegration/GitRepository.cs` | **Modify.** Add `Patch()`, `Apply()`, `Unstage()` |

---

## Task 1: The patch model and assembling a patch

**Files:**
- Create: `GitIntegration/Models/GitPatch.cs`
- Modify: `GitIntegration/Models/GitEnums.cs`
- Test: `GitIntegration.Test/Models/GitPatchTests.cs`

**Interfaces:**
- Consumes: `GitChangeKind` from `GitEnums.cs`.
- Produces:
  - `enum GitPatchLineKind { Context, Added, Removed }`
  - `sealed record GitPatchLine { GitPatchLineKind Kind; string Text; int? OldNumber; int? NewNumber; }`
  - `sealed record GitHunk { int OldStart; int OldCount; int NewStart; int NewCount; string Heading; IReadOnlyList<GitPatchLine> Lines; string Text; }`
  - `sealed record GitFilePatch { RelativeFilePath Path; RelativeFilePath? OriginalPath; GitChangeKind Kind; bool IsBinary; bool IsConflicted; string Header; IReadOnlyList<GitHunk> Hunks; string PatchFor(IEnumerable<GitHunk> hunks); }`
  - `sealed record GitPatch { IReadOnlyList<GitFilePatch> Files; }`

This task runs no git and needs no parser. `PatchFor` is the one piece of pure logic here and everything later depends on it being right.

- [ ] **Step 1: Write the failing tests**

Create `GitIntegration.Test/Models/GitPatchTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitPatchTests
{
	private const string Header =
		"diff --git a/f.txt b/f.txt\nindex 92dfa21..81f8caa 100644\n--- a/f.txt\n+++ b/f.txt\n";

	private static GitHunk HunkOne => new()
	{
		OldStart = 1,
		OldCount = 3,
		NewStart = 1,
		NewCount = 3,
		Heading = string.Empty,
		Lines = [],
		Text = "@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n",
	};

	private static GitHunk HunkTwo => new()
	{
		OldStart = 8,
		OldCount = 3,
		NewStart = 8,
		NewCount = 3,
		Heading = string.Empty,
		Lines = [],
		Text = "@@ -8,3 +8,3 @@\n h\n-i\n+I\n j\n",
	};

	private static GitFilePatch FileWithTwoHunks => new()
	{
		Path = "f.txt".As<RelativeFilePath>(),
		OriginalPath = null,
		Kind = GitChangeKind.Modified,
		IsBinary = false,
		IsConflicted = false,
		Header = Header,
		Hunks = [HunkOne, HunkTwo],
	};

	[TestMethod]
	public void PatchForOneHunkCarriesTheHeaderAndThatHunkOnly()
	{
		string patch = FileWithTwoHunks.PatchFor([HunkOne]);

		Assert.AreEqual(Header + HunkOne.Text, patch);
	}

	[TestMethod]
	public void PatchForEveryHunkKeepsThemInFileOrder()
	{
		string patch = FileWithTwoHunks.PatchFor([HunkTwo, HunkOne]);

		Assert.AreEqual(
			Header + HunkOne.Text + HunkTwo.Text,
			patch,
			"Hunks are emitted in the order the file holds them, not the order the caller asked for, because git reads a patch top to bottom.");
	}

	[TestMethod]
	public void PatchForNoHunksRefuses() =>
		Assert.ThrowsExactly<ArgumentException>(
			() => _ = FileWithTwoHunks.PatchFor([]),
			"A header with no body is a patch git rejects as corrupt, and the caller learns nothing from that message.");

	[TestMethod]
	public void PatchForNullRefuses() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = FileWithTwoHunks.PatchFor(null!));

	[TestMethod]
	public void PatchPreservesAHunkVerbatimIncludingTheNoNewlineMarker()
	{
		GitHunk hunk = new()
		{
			OldStart = 1,
			OldCount = 1,
			NewStart = 1,
			NewCount = 1,
			Heading = string.Empty,
			Lines = [],
			Text = "@@ -1 +1 @@\n-a\n+b\n\\ No newline at end of file\n",
		};

		GitFilePatch file = FileWithTwoHunks with { Hunks = [hunk] };

		StringAssert.Contains(
			file.PatchFor([hunk]),
			"\\ No newline at end of file",
			StringComparison.Ordinal,
			"Regenerating a hunk from its parsed lines loses this, and apply then rejects the patch.");
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build`
Expected: build failure, `GitPatch`, `GitFilePatch`, `GitHunk` and `GitPatchLineKind` do not exist.

- [ ] **Step 3: Add the enum**

In `GitIntegration/Models/GitEnums.cs`, following the shape of the enums already there:

```csharp
/// <summary>What one line of a patch does.</summary>
public enum GitPatchLineKind
{
	/// <summary>Present on both sides, shown for context.</summary>
	Context,

	/// <summary>Present only after the change.</summary>
	Added,

	/// <summary>Present only before the change.</summary>
	Removed,
}
```

- [ ] **Step 4: Create the model**

Create `GitIntegration/Models/GitPatch.cs` with the four records. `PatchFor` is the only method:

```csharp
	/// <summary>
	/// Assembles the header and the given hunks into text <c>git apply</c> accepts.
	/// </summary>
	/// <remarks>
	/// Hunks are emitted in the order this file holds them rather than the order they were given,
	/// because git reads a patch top to bottom and rejects one whose hunks run backwards.
	/// <para>
	/// The hunks are not checked for belonging to this file. The comparison would cost a pass per
	/// call to catch a mistake no reasonable caller makes, and a hunk from elsewhere fails at apply
	/// with git's own message.
	/// </para>
	/// </remarks>
	/// <param name="hunks">The hunks to include.</param>
	/// <returns>The patch text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="hunks"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="hunks"/> is empty.</exception>
	public string PatchFor(IEnumerable<GitHunk> hunks)
	{
		Ensure.NotNull(hunks);

		HashSet<GitHunk> wanted = [.. hunks];

		if (wanted.Count == 0)
		{
			throw new ArgumentException(
				"A patch needs at least one hunk. A header with no body is rejected as corrupt, and "
				+ "git's message for it explains nothing.",
				nameof(hunks));
		}

		StringBuilder builder = new(Header);

		foreach (GitHunk hunk in Hunks.Where(wanted.Contains))
		{
			_ = builder.Append(hunk.Text);
		}

		return builder.ToString();
	}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test --filter "FullyQualifiedName~GitPatchTests"`
Expected: 5 passing.

- [ ] **Step 6: Run the full suite**

Run: `dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test`
Expected: 673 passing (668 baseline plus 5), 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Models/GitPatch.cs GitIntegration/Models/GitEnums.cs GitIntegration.Test/Models/GitPatchTests.cs
git commit -m "[minor] Add the patch model and hunk assembly

A file patch keeps git's own hunk text beside the parsed lines, because a
hunk regenerated from its lines loses the no-newline marker and apply then
rejects it. PatchFor selects hunks into a patch, emitting them in file
order since git reads one top to bottom, and refuses an empty selection
rather than producing a header git calls corrupt."
```

---

## Task 2: The parser

**Files:**
- Create: `GitIntegration/Parsing/GitPatchParser.cs`
- Create: `GitIntegration.Test/Fixtures/patch-two-hunks.txt`, `patch-no-newline.txt`, `patch-rename-modified.txt`, `patch-binary.txt`, `patch-conflict.txt`, `patch-crlf.txt`
- Test: `GitIntegration.Test/Parsing/GitPatchParserTests.cs`

**Interfaces:**
- Consumes: the model from Task 1.
- Produces: `internal static class GitPatchParser` with `public static GitPatch Parse(string output)`.

**Capturing the fixtures.** Generate each with a real git in a scratch directory and save the exact bytes. Do not hand-write them, and do not tidy them: the point of a fixture is that it is what git emits. Capture with `git -c color.ui=false diff --no-ext-diff --no-textconv -U3`. For the conflict fixture, create a merge conflict and diff the unmerged path. For the CRLF fixture, write a file with CRLF endings and modify one line.

- [ ] **Step 1: Capture the fixtures**

Build each case in a temporary repository and write the captured output into `GitIntegration.Test/Fixtures/`. Record the git version used in a comment at the top of the test class, matching how `docs/superpowers/plans/2026-08-20-gitintegration-v2-phase3-read-only-verbs.md` records its fixtures. Set every fixture file to `Content` with `CopyToOutputDirectory` in `GitIntegration.Test.csproj` if the existing fixtures need that, matching whatever the JSON fixtures already do.

- [ ] **Step 2: Write the failing tests**

Create `GitIntegration.Test/Parsing/GitPatchParserTests.cs`. One test per fixture, asserting the shape rather than every byte:

```csharp
	[TestMethod]
	public void ParsesTwoHunksWithTheirLineNumbers()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-two-hunks.txt"));

		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual("f.txt", file.Path.WeakString);
		Assert.AreEqual(GitChangeKind.Modified, file.Kind);
		Assert.AreEqual(2, file.Hunks.Count);

		GitHunk first = file.Hunks[0];
		Assert.AreEqual(1, first.OldStart);
		Assert.IsTrue(first.Text.StartsWith("@@", StringComparison.Ordinal));
		Assert.IsTrue(
			first.Lines.Any(line => line.Kind == GitPatchLineKind.Added),
			"A modification has at least one added line.");
	}

	[TestMethod]
	public void KeepsTheNoNewlineMarkerInsideTheHunkText()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-no-newline.txt"));

		StringAssert.Contains(
			patch.Files.Single().Hunks.Single().Text,
			"\\ No newline at end of file",
			StringComparison.Ordinal);
	}

	[TestMethod]
	public void ReadsARenamedFileThatAlsoChanged()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-rename-modified.txt"));

		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual(GitChangeKind.Renamed, file.Kind);
		Assert.IsNotNull(file.OriginalPath);
		Assert.AreNotEqual(file.OriginalPath!.WeakString, file.Path.WeakString);
		Assert.IsTrue(
			file.Hunks.Count > 0,
			"A rename can carry content changes too, and treating rename as hunkless drops them.");
	}

	[TestMethod]
	public void FlagsABinaryFileAndGivesItNoHunks()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-binary.txt")).Files.Single();

		Assert.IsTrue(file.IsBinary);
		Assert.AreEqual(0, file.Hunks.Count);
	}

	[TestMethod]
	public void FlagsAConflictedFileAndGivesItNoHunks()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-conflict.txt")).Files.Single();

		Assert.IsTrue(
			file.IsConflicted,
			"Combined format is not an applyable patch, so it must not be parsed into ordinary hunks.");
		Assert.AreEqual(0, file.Hunks.Count);
	}

	[TestMethod]
	public void ParsesCarriageReturnContentWithoutStrippingIt()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-crlf.txt")).Files.Single();

		StringAssert.Contains(
			file.Hunks.Single().Text,
			"\r",
			StringComparison.Ordinal,
			"A patch is byte-sensitive, so content line endings survive parsing.");
	}

	[TestMethod]
	public void ParsesAnEmptyDiffAsNoFiles() =>
		Assert.AreEqual(0, GitPatchParser.Parse(string.Empty).Files.Count);
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build`
Expected: build failure, `GitPatchParser` does not exist.

- [ ] **Step 4: Implement the parser**

Walk the output line by line. A line starting `diff --git ` begins a file. Header lines are everything before the first `@@` or, for a file with no hunks, everything to the next `diff --git `. `similarity index`, `rename from` and `rename to` set `Kind` and `OriginalPath`. `new file mode` and `deleted file mode` set `Kind`. A line starting `Binary files ` or `GIT binary patch` sets `IsBinary`. A line starting `@@@` sets `IsConflicted` and stops hunk collection for that file. A line starting `@@ ` begins a hunk: parse `@@ -old,count +new,count @@ heading`, where a missing count means 1. Inside a hunk, ` ` is `Context`, `+` is `Added`, `-` is `Removed`, and `\` is part of the text but not a line. Line numbers advance per kind. Accumulate each hunk's raw text verbatim as you go.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test --filter "FullyQualifiedName~GitPatchParserTests"`
Expected: 7 passing.

- [ ] **Step 6: Run the full suite and commit**

```bash
dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test
git add GitIntegration/Parsing/GitPatchParser.cs GitIntegration.Test/Parsing/GitPatchParserTests.cs GitIntegration.Test/Fixtures/
git commit -m "[minor] Parse a unified diff into files, hunks and lines

Each hunk keeps the bytes git emitted alongside the parsed lines. Combined
format from an unmerged path is recognized and left unparsed rather than
turned into ordinary hunks, which would produce patches git rejects with
nothing explaining why, and a rename that also changed content keeps its
hunks."
```

---

## Task 3: Reading a patch

**Files:**
- Create: `GitIntegration/Builders/GitPatchBuilder.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitPatchBuilderTests.cs`

**Interfaces:**
- Consumes: `GitPatchParser.Parse` from Task 2.
- Produces:
  - `public interface IGitPatchBuilder : IGitCommandBuilder<GitPatch>` with `Staged()`, `WithContext(int lines)`, `ForPath(RelativeFilePath path)`, `Against(GitRefName revision)`, `Between(GitRefName a, GitRefName b)`, `DetectRenames()`
  - `GitRepository.Patch()` returning `IGitPatchBuilder`

- [ ] **Step 1: Write the failing tests**

```csharp
	[TestMethod]
	public void BuildsTheDefaultPatchVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"diff",
			"--no-ext-diff",
			"--no-textconv",
			"--no-color",
		];

		Assert.AreSequenceEqual(expectedArguments, builder.BuildArguments());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitPatchBuilder staged = new(runner, TestPaths.Root);
		_ = staged.Staged();
		Assert.IsTrue(staged.BuildArguments().Contains("--cached"));

		GitPatchBuilder context = new(runner, TestPaths.Root);
		_ = context.WithContext(7);
		Assert.IsTrue(context.BuildArguments().Contains("-U7"));

		GitPatchBuilder renames = new(runner, TestPaths.Root);
		_ = renames.DetectRenames();
		Assert.IsTrue(renames.BuildArguments().Contains("--find-renames"));
	}

	[TestMethod]
	public void NeverEmitsTheNulSeparator()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		Assert.IsFalse(
			builder.BuildArguments().Contains("-z"),
			"Patch format is line-based, and -z changes only the name-status framing this builder does not use.");
	}

	[TestMethod]
	public void RefusesANegativeContextCount()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		_ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithContext(-1));
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build`
Expected: build failure, `GitPatchBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

`AppendVerbArguments` adds `diff`, then `--no-ext-diff`, `--no-textconv`, `--no-color` unconditionally, then the options in a fixed order, then revisions and paths through `AppendOperands`. `ParseResult` returns `GitPatchParser.Parse(result.StandardOutput)`.

Document the three unconditional flags where they are added:

```csharp
		// Correctness, not tidiness. A repository with a gitattributes diff driver emits
		// human-readable output in place of a patch, which no caller can apply, and --no-color
		// guards a config setting color.diff to always, which would put escape sequences into text
		// that goes back to apply. color.ui on the base vector does not cover that: git lets
		// color.diff take precedence over it.
```

- [ ] **Step 4: Add the entry point**

In `GitRepository.cs`, beside `Diff()`:

```csharp
	/// <summary>Reads a patch, with the hunks and lines a caller needs to show or stage a change.</summary>
	/// <returns>The builder.</returns>
	public IGitPatchBuilder Patch() => new GitPatchBuilder(RequireRunner(), RequireLocalPath());
```

- [ ] **Step 5: Run the tests to verify they pass, then the full suite**

Run: `dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test`
Expected: everything passing, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitPatchBuilder.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitPatchBuilderTests.cs
git commit -m "[minor] Add Patch, which reads a diff as hunks rather than counts

Diff answers which files changed and by how much. Patch answers what
changed, which is what a caller drawing a diff or staging a hunk needs. It
always passes --no-ext-diff and --no-textconv, because a repository with a
gitattributes diff driver otherwise emits something no caller can apply."
```

---

## Task 4: Applying a patch

**Files:**
- Create: `GitIntegration/Builders/GitApplyBuilder.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitApplyBuilderTests.cs`
- Test: `GitIntegration.Test/Integration/GitPatchRoundTripTests.cs`

**Interfaces:**
- Consumes: the model from Task 1, `Patch()` from Task 3.
- Produces:
  - `public interface IGitApplyBuilder : IGitCommandBuilder<GitCompleted>` with `ToIndex()`, `Reversed()`, `Checked()`
  - `GitRepository.Apply(string patchText)` returning `IGitApplyBuilder`

- [ ] **Step 1: Write the failing unit tests**

```csharp
	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitApplyBuilder index = new(runner, TestPaths.Root, "patch");
		_ = index.ToIndex();
		Assert.IsTrue(index.BuildArguments().Contains("--cached"));

		GitApplyBuilder reversed = new(runner, TestPaths.Root, "patch");
		_ = reversed.Reversed();
		Assert.IsTrue(reversed.BuildArguments().Contains("--reverse"));

		GitApplyBuilder checkOnly = new(runner, TestPaths.Root, "patch");
		_ = checkOnly.Checked();
		Assert.IsTrue(checkOnly.BuildArguments().Contains("--check"));
	}

	[TestMethod]
	public void AlwaysSuppressesWhitespaceWarnings()
	{
		RecordingGitProcessRunner runner = new();
		GitApplyBuilder builder = new(runner, TestPaths.Root, "patch");

		Assert.IsTrue(
			builder.BuildArguments().Contains("--whitespace=nowarn"),
			"Staging content already on disk is not the moment to enforce a whitespace policy the user configured for authoring.");
	}

	[TestMethod]
	public void RefusesEmptyPatchText()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

		_ = Assert.ThrowsExactly<ArgumentException>(() => _ = repository.Apply("   "));
	}

	[TestMethod]
	public async Task DeletesTheTemporaryFileEvenWhenGitFailsAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "error: corrupt patch" };
		GitApplyBuilder builder = new(runner, TestPaths.Root, "not a patch\n");

		_ = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync().ConfigureAwait(false)).ConfigureAwait(false);

		string path = runner.LastArguments!.Last();

		Assert.IsFalse(
			File.Exists(path),
			"A failing patch must not leave files behind, and the failure the caller sees is git's, not the cleanup's.");
	}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build`
Expected: build failure, `GitApplyBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

The constructor takes the patch text. `Apply` on `GitRepository` validates it is neither null nor whitespace and throws `ArgumentException` otherwise, because an empty patch reaches git as a corrupt-patch error that explains nothing.

`AppendVerbArguments` adds `apply`, `--whitespace=nowarn`, the option flags, then the temporary file's path through `AppendOperands`. The file is written when the vector is built and deleted after the process returns, in a `finally`, by overriding `ExecuteAsync` and `TryExecuteAsync` to wrap the base call.

Write the file as UTF-8 with no byte order mark, preserving the text exactly:

```csharp
		File.WriteAllText(path, _patchText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
```

- [ ] **Step 4: Write the round-trip integration test**

Create `GitIntegration.Test/Integration/GitPatchRoundTripTests.cs`, following the shape of the tests already in that directory:

```csharp
	[TestMethod]
	public async Task StagingOneHunkLeavesTheOtherUnstagedAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// seed a ten-line file, commit it, then change the second and the tenth line
		// so the diff has two separate hunks

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);

		GitPatch patch = await opened.Patch().WithContext(1).ExecuteAsync().ConfigureAwait(false);
		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual(2, file.Hunks.Count, "The fixture must produce two hunks, or this proves nothing.");

		_ = await opened.Apply(file.PatchFor([file.Hunks[0]])).ToIndex().ExecuteAsync().ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> staged = await opened.Diff().Staged().WithLineCounts()
			.ExecuteAsync().ConfigureAwait(false);
		IReadOnlyList<GitDiffEntry> unstaged = await opened.Diff().WithLineCounts()
			.ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(1, staged.Single().Insertions);
		Assert.AreEqual(1, unstaged.Single().Insertions);
	}

	[TestMethod]
	public async Task CheckedReportsFailureWhenTheWorkingTreeMovedAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// seed and commit a file, change it, read the patch, then overwrite the file
		// with unrelated content so the patch no longer applies

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);
		GitFilePatch file = (await opened.Patch().ExecuteAsync().ConfigureAwait(false)).Files.Single();
		string text = file.PatchFor(file.Hunks);

		repository.WriteFile("f.txt", "something else entirely\n");

		GitResult<GitCompleted> result = await opened.Apply(text).ToIndex().Checked()
			.TryExecuteAsync().ConfigureAwait(false);

		Assert.IsFalse(result.Success, "A caller's refusal depends on this reporting failure rather than throwing.");

		IReadOnlyList<GitDiffEntry> staged = await opened.Diff().Staged().ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(0, staged.Count, "--check must change nothing.");
	}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build && ./GitIntegration.Test/bin/Debug/net10.0/ktsu.GitIntegration.Test --filter "FullyQualifiedName~GitApply"`
Expected: unit tests and both round-trip tests passing. If git is absent they skip rather than fail.

- [ ] **Step 6: Run the full suite and commit**

```bash
git add GitIntegration/Builders/GitApplyBuilder.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitApplyBuilderTests.cs GitIntegration.Test/Integration/GitPatchRoundTripTests.cs
git commit -m "[minor] Add Apply, which puts a patch into the index

Staging a hunk is Apply(text).ToIndex(), and unstaging one is the same with
Reversed. Checked asks whether a patch would still apply without changing
anything, which is what lets a caller refuse cleanly when the working tree
moved underneath it.

Git reads a patch from standard input or a file, and the process request
carries no standard input, so the patch goes to a temporary file that is
deleted on every path. The round-trip test is what proves the model and the
parser kept every byte."
```

---

## Task 5: Unstaging

**Files:**
- Create: `GitIntegration/Builders/GitRestoreBuilder.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Test: `GitIntegration.Test/Builders/GitRestoreBuilderTests.cs`

**Interfaces:**
- Consumes: the version probe pattern in `GitFetchBuilder`.
- Produces:
  - `public interface IGitRestoreBuilder : IGitCommandBuilder<GitCompleted>`
  - `GitRepository.Unstage(RelativeFilePath path)` returning `IGitRestoreBuilder`

- [ ] **Step 1: Write the failing tests**

```csharp
	[TestMethod]
	public void BuildsTheRestoreVectorOnAModernGit()
	{
		RecordingGitProcessRunner runner = new();
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		IReadOnlyList<string> arguments = builder.BuildArguments();

		Assert.IsTrue(arguments.Contains("restore"));
		Assert.IsTrue(arguments.Contains("--staged"));
		Assert.IsTrue(arguments.Contains("f.txt"));
	}

	[TestMethod]
	public void RefusesANullPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

		_ = Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Unstage(null!));
	}
```

Read `GitFetchBuilder`'s `ProbeVersionAsync` and `PorcelainSupportedByVersion` before writing the fallback test. If the probe can be driven from a `RecordingGitProcessRunner` by seeding `StandardOutput` with a version string, add a test asserting the vector becomes `reset HEAD -- <path>` below 2.23. If it cannot be driven without a second git installation, do not write a test that asserts against a mock of our own probe: say so in the report and leave the fallback covered by inspection.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build`
Expected: build failure, `GitRestoreBuilder` does not exist.

- [ ] **Step 3: Implement the builder with its fallback**

Follow `GitFetchBuilder`: probe the version in `ExecuteAsync` and `TryExecuteAsync` before the vector is built, and choose `restore --staged -- <path>` at 2.23 or above and `reset HEAD -- <path>` below it.

- [ ] **Step 4: Add the entry point**

```csharp
	/// <summary>Removes a path's staged changes, leaving the working tree alone.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The builder.</returns>
	public IGitRestoreBuilder Unstage(RelativeFilePath path) =>
		new GitRestoreBuilder(RequireRunner(), RequireLocalPath(), Ensure.NotNull(path));
```

- [ ] **Step 5: Run the full suite and commit**

```bash
git add GitIntegration/Builders/GitRestoreBuilder.cs GitIntegration/GitRepository.cs GitIntegration.Test/Builders/GitRestoreBuilderTests.cs
git commit -m "[minor] Add Unstage for the whole-file case

git restore arrived in 2.23, so this probes the installed version and falls
back to reset below it, the same shape Fetch already uses for porcelain.
Unstaging a hunk is Apply reversed. This is the file-level verb, and the
only one available for a binary file, which has no hunks."
```

---

## Self-review notes

**Spec coverage.** Every section maps to a task: the model and `PatchFor` to Task 1, the parser and the four cases a patch cannot express to Task 2, the reader and its three unconditional flags to Task 3, apply with its temporary file and `--check` to Task 4, and unstaging with its version fallback to Task 5. The testing section's fixtures are Task 2 and its round trip is Task 4.

**Review Focus coverage.** Empty patch text is Task 4 Step 1, `PatchFor` with no hunks is Task 1 Step 1, the temporary file on failure is Task 4 Step 1, a renamed and modified file is Task 2 Step 2, and a moved working tree is Task 4 Step 4.

**One thing deliberately left open.** Task 5 does not promise a test for the version fallback, because whether the probe can be driven without a second git installation is not knowable from reading. The task says what to do in each case rather than pretending the answer.

**Fixtures are captured, not written.** Task 2 Step 1 exists as its own step because a hand-written fixture would pass the parser tests and fail the round trip, which is the one failure this plan is shaped to prevent.

# GitIntegration v2 — Phase 3: Read-Only Verbs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the read-only half of the local layer — result models, pure output parsers, and verb builders for `status`, `log`, `diff`, `for-each-ref`, `remote -v`, `rev-parse`, and `--version` — plus `IGitClient`/`GitClient` and the local verb methods on `GitRepository`.

**Scope:** Phase 3 of the five phases in the spec. Phases 1–2 (Semantics migration, execution core, builder base, DI) are merged and released as 2.0.0. Phase 4 (mutating verbs) and Phase 5 (remote sync, hosting providers) follow and get their own plans.

**Architecture:** Each verb is three pieces that are tested separately. A **parser** is an `internal static class` with a pure `string → model` method: no I/O, no `IGitProcessRunner`, no filesystem. A **builder** derives from the existing `GitCommandBuilder<TResult>`, contributes only its verb's arguments through `AppendVerbArguments`, and delegates `ParseResult` to the parser. A **model** is a `sealed record` of `ktsu.Semantics` types. `GitClient` composes builders into the four client-level operations; `GitRepository` exposes one builder factory per verb.

**Tech Stack:** .NET 10 / .NET 9, `ktsu.Sdk` 2.25.0, `ktsu.Semantics.Strings` 3.0.1, `ktsu.Semantics.Paths` 3.0.1, `ktsu.RunCommand` 1.5.0, `ktsu.Essentials` 2.0.0, MSTest via `MSTest.Sdk`.

**Spec:** `docs/superpowers/specs/2026-08-19-gitintegration-v2-design.md`

**Prior plan:** `docs/superpowers/plans/2026-08-19-gitintegration-v2-phase1-2-foundation.md`

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Indentation is tabs**, not spaces, in all `.cs` files. **Line endings are LF** for `.cs`: this repo's `.gitattributes` sets `* text=auto eol=lf`, which overrides the CRLF default in the global CLAUDE.md.
- **File-scoped namespaces.** `using` directives go **inside** the namespace, after the namespace line. Namespace is `ktsu.GitIntegration` for the library and `ktsu.GitIntegration.Test` for tests, regardless of folder.
- **Every file starts with** `// Copyright (c) 2023-2026 ktsu-dev contributors` as the first line, followed by a blank line.
- **Nullable reference types enabled; warnings are errors.** The build fails on any warning, including `CS8603`.
- **Zero `[SuppressMessage]` attributes.** The repository currently has none and this phase must not add any. Every analyzer complaint encountered so far (CA1002, CA1062, CA1861, CA2007, IDE0300, MSTEST0032, MSTEST0065) turned out to be genuinely fixable. Fix the code, not the analyzer.
- **No `this.` qualifiers.** Always specify accessibility modifiers explicitly, including `public` on interface members (the repo's style — see `IGitCommandBuilder<TResult>`).
- **XML doc comments are required on every public member.** The SDK treats missing docs as an error. Internal members do not require them but the existing code documents them anyway; match that.
- **Use `Ensure.NotNull(x)`** (from `Polyfill`, supplied by `ktsu.Sdk`) for parameter validation in the **library**. `Polyfill` is referenced with `PrivateAssets="all"`, so `Ensure` is **not visible to the test project** — tests use `ArgumentNullException.ThrowIfNull`.
- **Every `await` in the library gets `.ConfigureAwait(false)`** (CA2007). Test code does too, matching the existing tests.
- **Tests use MSTest** with semantic assertions — `Assert.AreEqual`, `Assert.IsNotNull`, `Assert.IsNull`, `Assert.ThrowsExactly`, `Assert.ThrowsExactlyAsync`, `CollectionAssert.AreEqual`. Never `Assert.IsTrue(a == b)`.
- **Async test methods must be named with an `Async` suffix** (MSTEST0032/0065). Test classes that need a token use `public TestContext TestContext { get; set; } = null!;` and pass `TestContext.CancellationTokenSource.Token`.
- **Commit message tags:** use `[minor]` on feature commits — this phase is additive. Never add `Co-Authored-By` lines.
- **Do not edit** `VERSION.md`, `CHANGELOG.md`, `LICENSE.md` — they are generated.
- **`ktsu.Sdk` regenerates `.gitignore` on every `dotnet build`.** Manual edits are transient; use `.git/info/exclude` for local ignores.
- **Build command:** `dotnet build`. **Test command:** `dotnet test` — **never `dotnet test --nologo`**, which silently runs zero tests under Microsoft.Testing.Platform and exits 5.
- **Run a specific test:** `dotnet test --filter "FullyQualifiedName~GitStatusParserTests"`.
- **Target frameworks** stay `net10.0;net9.0` for the library, `net10.0` for the test project. No new package references are needed by this phase.

### Design invariants carried over from Phases 1–2

These are decided; do not revisit them while implementing.

- **Every repository-scoped command uses `git -C <path>`**, never a process working directory, even though `ktsu.RunCommand` 1.5.0 now supports `CommandOptions.WorkingDirectory`. `-C` appears in the argument vector, so a failing command can be copied out of a `GitCommandException` and rerun verbatim. `GitCommandBuilder<TResult>` already does this — pass a non-null `repositoryPath` and it happens.
- **Every invocation runs with `GIT_TERMINAL_PROMPT=0` and `LC_ALL=C`** (`RunCommandGitProcessRunner.EnvironmentOverlay`). The forced C locale is what makes these parsers safe to write against English, machine-stable output.
- **Parse only machine-readable formats, never human output.**
- **Caller-supplied operands go through `GitCommandBuilder<TResult>.AppendOperands`**, which emits `--end-of-options` first. Library-chosen literals (`refs/heads`, `--porcelain=v2`) are not caller-supplied and do not need it.
- **Builders are mutable, single-use, not thread-safe**, and return `this` from each configuration method. `GitRepository.Verb()` returns a fresh builder each call.
- **The API is asynchronous only.** No synchronous `Execute()` overloads.

---

## Findings from probing the installed git

Everything below was captured from `git version 2.50.1.windows.1` on Windows with `LC_ALL=C`, and from `ktsu.Semantics.Paths` 3.0.1. The spec said the format strings would be "confirmed against the installed git during implementation" — this is that confirmation. **Do not re-derive these; they are the fixtures.**

### 1. `status --porcelain=v2 --branch -z` puts a rename's original path in a *separate* NUL field

```
# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f\0
# branch.head main\0
2 RM N... 100644 100644 100644 5626abf0…e171 5626abf0…e171 R100 renamed.txt\0a.txt\0
1 A. N... 000000 100644 100644 0000000000…0000 54f9d6da…30af staged.txt\0
? untracked.txt\0
```

The `2` record's own text ends at the new path. The **original path is the next NUL-terminated field**, not part of the record. A parser that treats each NUL-delimited chunk as one record produces a garbage entry for every rename. The parser must consume the following field when it sees a `2`.

With an upstream configured and one unpushed commit:

```
# branch.oid 9429d206…5738\0# branch.head main\0# branch.upstream origin/main\0# branch.ab +1 -0\0
```

Detached HEAD emits `# branch.head (detached)` and no `branch.upstream`/`branch.ab` headers.

Record layouts (fields are space-separated, path is last and may contain spaces):

| Prefix | Layout | Split count | Path index |
|---|---|---|---|
| `1` | `1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>` | 9 | 8 |
| `2` | `2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>` + next field is `<origPath>` | 10 | 9 |
| `u` | `u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>` | 11 | 10 |
| `?` | `? <path>` | 2 | 1 |
| `!` | `! <path>` | 2 | 1 |

### 2. `log -z` records are NUL-**terminated**, so the last split element is empty

```
$ git log -z -1 --format=%H%x1f%s%x1f%b | xxd
9429d2063d91f1097de51a196cb8203b06335738 1f "Second commit" 1f 00
```

Every parser that splits on NUL must skip empty chunks rather than assume the count.

`%b` carries the trailing newline git appends to the body. `%P` is space-separated and **empty for a root commit**. `%aI`/`%cI` are strict ISO-8601 with offset: `2026-08-20T00:05:20+10:00`.

### 3. `diff --name-status -z` is a token stream, not fixed-width records

```
$ git diff --name-status -z --find-renames --cached | xxd
44 00 61 2e 74 78 74 00  D \0 a.txt \0
52 31 30 30 00 62 2e …   R100 \0 b.txt \0 b-renamed.txt \0
41 00 63 6f 70 79 …      A \0 copy.txt \0
```

A `D`/`A`/`M` record is two tokens; an `R`/`C` record is **three** — status, source path, destination path. Pairwise splitting is wrong. The status token carries the similarity score inline (`R100`).

### 4. `for-each-ref --format=%(refname:short)…` cannot distinguish local from remote — the format must be extended

The spec pins:

```
%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)
```

Against a real clone that produces:

```
main<US>9429d206…<US>origin/main<US>*
origin<US>9429d206…<US><US>            ← this is refs/remotes/origin/HEAD
origin/feature/x<US>9429d206…<US><US>
origin/main<US>9429d206…<US><US>
```

Two defects. First, `GitBranch.IsRemote` is unknowable from a short name: a local branch may legitimately be called `origin/main`. Second, `refs/remotes/origin/HEAD` — a symbolic ref naming the remote's default branch, present in every clone — shortens to the bare remote name `origin` and would be reported as a branch called "origin".

**Decision: prepend `%(refname)` to the format.** The full ref name settles both questions in one invocation. This is a refinement the spec explicitly sanctions ("Both format strings are pinned by parser fixtures and confirmed against the installed git during implementation"), and the alternative — two `for-each-ref` invocations — costs an extra process for no gain.

```
%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)
```

Also note `%(HEAD)` yields `*` for the current branch and **a single space**, not an empty string, otherwise. Records are `\n`-separated; git forbids control characters in ref names, so line splitting is safe here even without `-z`.

### 5. `remote -v` is TAB-separated with a `(fetch)`/`(push)` suffix

```
origin\tC:/…/fixture-upstream (fetch)
origin\tC:/…/fixture-upstream (push)
```

A local remote path may contain spaces, so the parser must anchor on the `" (fetch)"` / `" (push)"` **suffix**, not on the last space.

### 6. `--version` carries a platform suffix

```
git version 2.50.1.windows.1
```

`GitVersion` must parse leading numeric components and keep the rest verbatim. It cannot assume exactly three dot-separated numbers.

### 7. `rev-parse` does the upward walk, so Phase 3 needs no filesystem access

```
$ git -C '<repo>/dir with spaces' rev-parse --show-toplevel
C:/…/fixture-repo                              (exit 0, forward slashes on every platform)

$ git -C '<repo>' rev-parse --is-inside-work-tree
true                                           (exit 0)

$ git -C '<not-a-repo>' rev-parse --is-inside-work-tree
fatal: not a git repository (or any of the parent directories): .git   (exit 128)

$ git -C '<missing-dir>' rev-parse --show-toplevel
fatal: cannot change to '<missing-dir>': No such file or directory     (exit 128)

$ git -C '<repo>' remote get-url origin
C:/…/fixture-upstream                          (exit 0)

$ git -C '<repo>' remote get-url nosuch
error: No such remote 'nosuch'                 (exit 2)
```

The spec describes `DiscoverAsync` as "walks up from `startingPath`", and routes every filesystem touchpoint through `ktsu.Essentials.IFileSystemProvider`. **`git rev-parse --show-toplevel` performs exactly that upward walk inside git**, so Phase 3 implements discovery through the runner and takes no filesystem dependency at all. `IFileSystemProvider` is still registered by `AddGitIntegration` (Phase 2 did that) and Phase 4 will use it for `Init`/`Clone` destination checks, where there genuinely is no repository to ask.

Consequence: `GitClient` needs **no** `IFileSystemProvider` constructor parameter, and its tests need no in-memory filesystem — only the recording runner.

### 8. `--end-of-options` is accepted by `rev-parse`, `log`, and `diff`

Confirmed by invocation. `git rev-parse --verify --end-of-options HEAD`, `git log … --end-of-options HEAD -- <pathspec>`, and `git diff --name-status -z --end-of-options HEAD` all exit 0. Pathspecs still go after `--`, which is its own end-of-options marker for that position.

### 9. `RelativeFilePath` canonicalises separators and **rejects control characters on Windows**

Measured against `ktsu.Semantics.Paths` 3.0.1:

| Input | `TryCreate` | Canonical value |
|---|---|---|
| `docs/plan.md` | true | `docs\plan.md` |
| `a file with spaces.txt` | true | unchanged |
| `ünïcødé/file.txt` | true | `ünïcødé\file.txt` |
| `star*.txt`, `colon:name.txt`, `quote"name.txt` | true | unchanged |
| `src/` | true | `src` (trailing separator stripped) |
| `""` (empty) | **true** | `""` |
| `weird<LF>name.txt` | **false** | — |

Three consequences that shape the parsers and their tests:

- **Forward slashes become `\` on Windows.** Tests must build expected values by running the *same* string through `.As<RelativeFilePath>()`, never by hard-coding `docs\plan.md`, or they fail on Linux.
- **The empty string is accepted.** A malformed record with a blank path field would otherwise yield a silently empty path, so the parsers must reject empty explicitly.
- **A path containing a newline cannot be represented.** Git permits it, and `-z` exists partly so such paths survive transport intact. `RelativeFilePath` still refuses it on Windows (control characters are invalid path characters there) while accepting it on Linux — a platform split that must not become a silent one.

  **Decision: throw `GitParseException` naming the offending raw path.** The spec pins `RelativeFilePath` as the model's path type, so the alternatives are to throw or to silently drop the entry; dropping would make `GitStatus.IsClean` lie. This is a documented limitation, covered by a test, and the honest failure mode. `-z` still earns its place regardless: without it git quote-escapes paths and every parser would need an unescaper.

### 10. `SemanticString<T>.TryCreate` is callable generically; `T.TryCreate` is not

`TryCreate` is a plain static method on `SemanticString<TDerived>`, not a `static abstract` interface member, so `T.TryCreate(…)` inside a generic method fails with `CS0704: Cannot do non-virtual member lookup in 'T'`. Calling it on the constructed base type works:

```csharp
public static T To<T>(string value) where T : SemanticString<T>, new()
	=> SemanticString<T>.TryCreate(value, out T? result) && result is not null ? result : throw …;
```

The `result is not null` clause is load-bearing — without it the compiler emits `CS8603 Possible null reference return`, which is an error in this repo.

---

## File Structure

New folders `Models/` and `Parsing/` join the existing `Builders/`, `Execution/`, and `SemanticTypes/`. Namespace stays `ktsu.GitIntegration` everywhere; the folders are organisational only, matching how `Execution/` and `SemanticTypes/` already work.

**Library — `GitIntegration/`**

| File | Responsibility |
|---|---|
| `Models/GitEnums.cs` | `GitFileState`, `GitChangeKind`, `GitUntrackedFilesMode` |
| `Models/GitStatus.cs` | `GitStatus`, `GitStatusEntry` |
| `Models/GitCommit.cs` | `GitCommit`, `GitSignature` |
| `Models/GitBranch.cs` | `GitBranch` |
| `Models/GitRemote.cs` | `GitRemote` |
| `Models/GitDiffEntry.cs` | `GitDiffEntry` |
| `Models/GitVersion.cs` | `GitVersion` |
| `Parsing/GitOutputFormats.cs` | The pinned format strings and separator constants, shared by builder and parser so they cannot drift |
| `Parsing/GitParseValues.cs` | `ToSemantic<T>`, `ToRelativeFilePath` — the shared conversions that turn raw git text into Semantics types or throw `GitParseException` |
| `Parsing/GitStatusParser.cs` | `status --porcelain=v2 --branch -z` → `GitStatus` |
| `Parsing/GitLogParser.cs` | `log -z --format=…` → `IReadOnlyList<GitCommit>` |
| `Parsing/GitDiffParser.cs` | `diff --name-status -z` → `IReadOnlyList<GitDiffEntry>` |
| `Parsing/GitBranchParser.cs` | `for-each-ref --format=…` → `IReadOnlyList<GitBranch>` |
| `Parsing/GitRemoteParser.cs` | `remote -v` → `IReadOnlyList<GitRemote>` |
| `Parsing/GitVersionParser.cs` | `--version` → `GitVersion` |
| `Builders/GitStatusBuilder.cs` | `IGitStatusBuilder` + `GitStatusBuilder` |
| `Builders/GitLogBuilder.cs` | `IGitLogBuilder` + `GitLogBuilder` |
| `Builders/GitDiffBuilder.cs` | `IGitDiffBuilder` + `GitDiffBuilder` |
| `Builders/GitBranchListBuilder.cs` | `IGitBranchListBuilder` + `GitBranchListBuilder` |
| `Builders/GitRemoteListBuilder.cs` | `IGitRemoteListBuilder` + `GitRemoteListBuilder` |
| `Builders/GitRevParseBuilder.cs` | `IGitRevParseBuilder` + `GitRevParseBuilder` |
| `Builders/GitVersionBuilder.cs` | `IGitVersionBuilder` + `GitVersionBuilder` |
| `Builders/GitTextBuilder.cs` | Internal builder returning trimmed stdout, used by `GitClient` for `rev-parse --show-toplevel`, `rev-parse --is-inside-work-tree`, and `remote get-url origin` |
| `IGitClient.cs` | `IGitClient` |
| `GitClient.cs` | `GitClient` |
| `Execution/GitExceptions.cs` | **Modified** — add `GitParseException` |
| `GitRepository.cs` | **Modified** — add `ProcessRunner`, `IsClonedAsync`, and the six read-only verb factories |
| `ServiceCollectionExtensions.cs` | **Modified** — register `GitClient` / `IGitClient` |

Each verb's interface and builder share one file because they change together and neither is meaningful alone. This differs from `IGitCommandBuilder.cs` / `GitCommandBuilder.cs`, which are split because the base contract is consumed independently of the base implementation.

**Builder classes are `internal sealed`; their interfaces are public.** `GitRepository` returns the interface, so the concrete type is never named by a consumer, and keeping it internal keeps the public API to what the spec actually specifies. `[assembly: InternalsVisibleTo("ktsu.GitIntegration.Test")]` is already in `GitIntegration/AssemblyInfo.cs`, so tests reach them directly.

**Tests — `GitIntegration.Test/`**

| File | Responsibility |
|---|---|
| `Fakes/ScriptedGitProcessRunner.cs` | Multi-invocation fake: queued responses, recorded argument vectors. `RecordingGitProcessRunner` only replays one canned result, which is not enough for `GitClient`'s two-command discovery. |
| `Parsing/GitStatusParserTests.cs` | + inline fixtures |
| `Parsing/GitLogParserTests.cs` | + inline fixtures |
| `Parsing/GitDiffParserTests.cs` | + inline fixtures |
| `Parsing/GitBranchParserTests.cs` | + inline fixtures |
| `Parsing/GitRemoteParserTests.cs` | + inline fixtures |
| `Parsing/GitVersionParserTests.cs` | + inline fixtures |
| `Builders/GitStatusBuilderTests.cs` | argv assertions |
| `Builders/GitLogBuilderTests.cs` | argv assertions |
| `Builders/GitDiffBuilderTests.cs` | argv assertions |
| `Builders/GitBranchListBuilderTests.cs` | argv assertions |
| `Builders/GitRemoteListBuilderTests.cs` | argv assertions |
| `Builders/GitRevParseBuilderTests.cs` | argv assertions |
| `Builders/GitVersionBuilderTests.cs` | argv assertions |
| `GitClientTests.cs` | client behaviour over the scripted runner |
| `GitRepositoryVerbTests.cs` | verb factories, `ProcessRunner` guard, `IsClonedAsync` |

**Fixtures are inline `const string` fields, not files on disk.** Git's machine formats embed NUL and `0x1F`, which make a fixture file binary: the repo's `* text=auto eol=lf` would skip it for EOL normalisation, `git diff` would render it unreadable, and a reviewer could not see what changed. Inline literals using `\u0000` and `\u001f` are reviewable in a diff and impossible to mis-encode.

**Write NUL as `\u0000`, never `\0`.** In a fixture like `"…renamed.txt\0a.txt"` the escape is unambiguous, but `"\0" + "1 A. N…"` written as `"\01 A. N…"` reads as an octal escape to anyone skimming it. `\u0000` never does.

---
## Task 1: Result models, enums, and parsing primitives

Nothing in this task talks to git. It establishes the vocabulary every later task consumes, plus the two shared conversions and the parse-failure exception. It is one task rather than eight because none of these pieces is independently rejectable — a reviewer either accepts the model vocabulary or does not.

**Files:**
- Create: `GitIntegration/Models/GitEnums.cs`
- Create: `GitIntegration/Models/GitStatus.cs`
- Create: `GitIntegration/Models/GitCommit.cs`
- Create: `GitIntegration/Models/GitBranch.cs`
- Create: `GitIntegration/Models/GitRemote.cs`
- Create: `GitIntegration/Models/GitDiffEntry.cs`
- Create: `GitIntegration/Models/GitVersion.cs`
- Create: `GitIntegration/Parsing/GitOutputFormats.cs`
- Create: `GitIntegration/Parsing/GitParseValues.cs`
- Modify: `GitIntegration/Execution/GitExceptions.cs` (append `GitParseException`)
- Test: `GitIntegration.Test/Parsing/GitParseValuesTests.cs`
- Test: `GitIntegration.Test/Models/GitVersionTests.cs`

**Interfaces:**
- Consumes: `GitException` (existing, `GitIntegration/Execution/GitExceptions.cs`); `GitBranchName`, `GitRemoteName`, `GitCommitSha`, `GitAuthorName`, `GitAuthorEmail`, `GitRepositoryRemotePath` (existing, `GitIntegration/SemanticTypes/`).
- Produces:
  - `GitFileState`, `GitChangeKind`, `GitUntrackedFilesMode` enums.
  - `GitStatus`, `GitStatusEntry`, `GitCommit`, `GitSignature`, `GitBranch`, `GitRemote`, `GitDiffEntry`, `GitVersion` records.
  - `GitVersion.AtLeast(int major, int minor) → bool`.
  - `GitParseException : GitException`, with `()`, `(string)`, `(string, Exception)` constructors.
  - `internal static class GitOutputFormats` — `const char UnitSeparator`, `const string LogFormat`, `const string ForEachRefFormat`.
  - `internal static class GitParseValues` — `ToSemantic<TSemantic>(string value, string description) → TSemantic` and `ToRelativeFilePath(string value) → RelativeFilePath`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitParseValuesTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitParseValuesTests
{
	[TestMethod]
	public void ToSemanticAcceptsAValidValue()
	{
		GitCommitSha sha = GitParseValues.ToSemantic<GitCommitSha>("ABCDEF12", "commit id");

		// GitCommitSha canonicalises to lowercase, so the conversion must go through Create,
		// not through a cast that would bypass MakeCanonical.
		Assert.AreEqual("abcdef12".As<GitCommitSha>(), sha);
	}

	[TestMethod]
	public void ToSemanticRejectsAValueThatFailsValidation()
	{
		GitParseException exception = Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToSemantic<GitCommitSha>("zzzz", "commit id"));

		// The message has to name both what was expected and what git actually said, because a
		// parse failure is only ever diagnosed from the message.
		StringAssert.Contains(exception.Message, "commit id");
		StringAssert.Contains(exception.Message, "zzzz");
	}

	[TestMethod]
	public void ToSemanticRejectsAnEmptyValue()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToSemantic<GitCommitSha>(string.Empty, "commit id"));
	}

	[TestMethod]
	public void ToRelativeFilePathCanonicalisesSeparators()
	{
		RelativeFilePath path = GitParseValues.ToRelativeFilePath("docs/plan.md");

		// Built through As<T> rather than hard-coded, because RelativeFilePath rewrites '/' to the
		// host separator: a literal "docs\\plan.md" would pass on Windows and fail on Linux.
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>(), path);
	}

	[TestMethod]
	public void ToRelativeFilePathKeepsSpacesAndNonAsciiCharacters()
	{
		RelativeFilePath path = GitParseValues.ToRelativeFilePath("dir with spaces/ünïcødé.txt");

		Assert.AreEqual("dir with spaces/ünïcødé.txt".As<RelativeFilePath>(), path);
	}

	[TestMethod]
	public void ToRelativeFilePathRejectsAnEmptyPath()
	{
		// RelativeFilePath.TryCreate accepts the empty string, so the guard has to be explicit or a
		// malformed record yields a silently blank path.
		Assert.ThrowsExactly<GitParseException>(() => GitParseValues.ToRelativeFilePath(string.Empty));
	}

	[TestMethod]
	public void ToRelativeFilePathReportsAPathItCannotRepresent()
	{
		// Git permits a newline in a path and -z transports it intact, but RelativeFilePath refuses
		// control characters on Windows. Failing loudly beats dropping the entry, which would make
		// GitStatus.IsClean lie. On Linux the value is representable and no exception is thrown, so
		// the assertion is conditional on the platform.
		string path = "weird" + (char)10 + "name.txt";

		if (RelativeFilePath.TryCreate(path, out RelativeFilePath? supported) && supported is not null)
		{
			Assert.AreEqual(supported, GitParseValues.ToRelativeFilePath(path));
			return;
		}

		GitParseException exception = Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToRelativeFilePath(path));

		StringAssert.Contains(exception.Message, "name.txt");
	}
}
```

`GitIntegration.Test/Models/GitVersionTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitVersionTests
{
	private static GitVersion Version(int major, int minor, int patch) =>
		new() { Major = major, Minor = minor, Patch = patch, Raw = $"{major}.{minor}.{patch}" };

	[TestMethod]
	public void AtLeastAcceptsTheExactVersion() =>
		Assert.IsTrue(Version(2, 41, 0).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastAcceptsAHigherMinor() =>
		Assert.IsTrue(Version(2, 50, 1).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastAcceptsAHigherMajor() =>
		Assert.IsTrue(Version(3, 0, 0).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastRejectsALowerMinor() =>
		Assert.IsFalse(Version(2, 40, 9).AtLeast(2, 41));

	[TestMethod]
	public void AtLeastRejectsALowerMajor() =>
		Assert.IsFalse(Version(1, 99, 0).AtLeast(2, 41));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitParseValuesTests|FullyQualifiedName~GitVersionTests"`

Expected: compilation failure — `GitParseValues`, `GitParseException`, and `GitVersion` do not exist.

- [ ] **Step 3: Add `GitParseException`**

Append to `GitIntegration/Execution/GitExceptions.cs`, after `GitRepositoryNotFoundException`:

```csharp
/// <summary>
/// Git ran successfully but produced output this library could not interpret.
/// </summary>
/// <remarks>
/// Distinct from <see cref="GitCommandException"/>, which means git itself reported a failure.
/// This means the invocation succeeded and the output did not match the machine-readable format
/// the parser was written against — a git version emitting a shape we do not know, or a value git
/// permits that the corresponding <c>ktsu.Semantics</c> type refuses, such as a path containing a
/// newline on Windows.
/// </remarks>
public sealed class GitParseException : GitException
{
	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	public GitParseException() { }

	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitParseException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitParseException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Write the enums**

`GitIntegration/Models/GitEnums.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The state of one file on one side of the index, as reported by <c>status</c>.
/// </summary>
public enum GitFileState
{
	/// <summary>The file is unchanged on this side.</summary>
	Unmodified,

	/// <summary>The file's contents changed.</summary>
	Modified,

	/// <summary>The file is newly tracked.</summary>
	Added,

	/// <summary>The file was removed.</summary>
	Deleted,

	/// <summary>The file was moved from another path.</summary>
	Renamed,

	/// <summary>The file was copied from another path.</summary>
	Copied,

	/// <summary>The file is not tracked and is not ignored.</summary>
	Untracked,

	/// <summary>The file matches an ignore rule.</summary>
	Ignored,

	/// <summary>The file has conflicting changes from an unfinished merge.</summary>
	Unmerged,

	/// <summary>The file changed kind, for instance from a regular file to a symbolic link.</summary>
	TypeChanged,
}

/// <summary>
/// The kind of change <c>diff --name-status</c> reported for one path.
/// </summary>
public enum GitChangeKind
{
	/// <summary>The path was added.</summary>
	Added,

	/// <summary>The path was copied from another path.</summary>
	Copied,

	/// <summary>The path was deleted.</summary>
	Deleted,

	/// <summary>The path's contents changed.</summary>
	Modified,

	/// <summary>The path was moved from another path.</summary>
	Renamed,

	/// <summary>The path changed kind, for instance from a regular file to a symbolic link.</summary>
	TypeChanged,

	/// <summary>The path has conflicting changes from an unfinished merge.</summary>
	Unmerged,

	/// <summary>Git reported a status letter this library does not recognise.</summary>
	Unknown,
}

/// <summary>
/// How much untracked detail <c>status</c> should report.
/// </summary>
public enum GitUntrackedFilesMode
{
	/// <summary>Report no untracked files at all.</summary>
	No,

	/// <summary>Report untracked files, collapsing a wholly untracked directory to one entry.</summary>
	Normal,

	/// <summary>Report every untracked file individually, including inside untracked directories.</summary>
	All,
}
```

- [ ] **Step 5: Write the records**

`GitIntegration/Models/GitStatus.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// The working tree and index state of a repository.
/// </summary>
public sealed record GitStatus
{
	/// <summary>
	/// Gets the checked-out branch, or <see langword="null"/> when HEAD is detached.
	/// </summary>
	public GitBranchName? Branch { get; init; }

	/// <summary>
	/// Gets the upstream the current branch tracks, or <see langword="null"/> when it tracks none.
	/// </summary>
	public GitBranchName? Upstream { get; init; }

	/// <summary>Gets how many commits the current branch has that its upstream does not.</summary>
	public int Ahead { get; init; }

	/// <summary>Gets how many commits the upstream has that the current branch does not.</summary>
	public int Behind { get; init; }

	/// <summary>Gets a value indicating whether HEAD points at a commit rather than a branch.</summary>
	public bool IsDetached { get; init; }

	/// <summary>Gets every path git reported as differing from a clean checkout.</summary>
	public required IReadOnlyList<GitStatusEntry> Entries { get; init; }

	/// <summary>
	/// Gets a value indicating whether git reported no differing paths.
	/// </summary>
	/// <remarks>
	/// This reflects what the invocation asked for. A status built with
	/// <c>--untracked-files=no</c> reports clean while untracked files exist, because it was told
	/// not to look for them.
	/// </remarks>
	public bool IsClean => Entries.Count == 0;
}

/// <summary>
/// One path git reported as differing from a clean checkout.
/// </summary>
public sealed record GitStatusEntry
{
	/// <summary>Gets the difference between HEAD and the index.</summary>
	public required GitFileState IndexState { get; init; }

	/// <summary>Gets the difference between the index and the working tree.</summary>
	public required GitFileState WorkTreeState { get; init; }

	/// <summary>Gets the path, relative to the repository root.</summary>
	public required RelativeFilePath Path { get; init; }

	/// <summary>
	/// Gets the path this file came from for a rename or a copy, or <see langword="null"/>
	/// otherwise.
	/// </summary>
	public RelativeFilePath? OriginalPath { get; init; }
}
```

`GitIntegration/Models/GitCommit.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// One commit.
/// </summary>
public sealed record GitCommit
{
	/// <summary>Gets the commit's own object id.</summary>
	public required GitCommitSha Sha { get; init; }

	/// <summary>Gets the object id of the tree this commit points at.</summary>
	public required GitCommitSha TreeSha { get; init; }

	/// <summary>
	/// Gets the parents, in git's own order: empty for a root commit, one for an ordinary commit,
	/// and two or more for a merge.
	/// </summary>
	public required IReadOnlyList<GitCommitSha> ParentShas { get; init; }

	/// <summary>Gets who wrote the change, and when.</summary>
	public required GitSignature Author { get; init; }

	/// <summary>Gets who committed the change, and when. Differs from the author after a rebase or a cherry-pick.</summary>
	public required GitSignature Committer { get; init; }

	/// <summary>Gets the first line of the commit message.</summary>
	public required string Subject { get; init; }

	/// <summary>Gets the remainder of the commit message, empty when there is none.</summary>
	public string Body { get; init; } = string.Empty;
}

/// <summary>
/// A name, an address, and a timestamp, as recorded on a commit.
/// </summary>
public sealed record GitSignature
{
	/// <summary>Gets the recorded name.</summary>
	public required GitAuthorName Name { get; init; }

	/// <summary>Gets the recorded email address.</summary>
	public required GitAuthorEmail Email { get; init; }

	/// <summary>
	/// Gets the recorded time, with the offset the commit was made in.
	/// </summary>
	/// <remarks>
	/// Parsed from git's strict ISO-8601 output, so the original offset is preserved rather than
	/// normalised to UTC — the local time a commit was made in is information a caller may want.
	/// </remarks>
	public required DateTimeOffset Timestamp { get; init; }
}
```

`GitIntegration/Models/GitBranch.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// One branch reference.
/// </summary>
public sealed record GitBranch
{
	/// <summary>
	/// Gets the short name, such as <c>main</c> for a local branch or <c>origin/main</c> for a
	/// remote-tracking one.
	/// </summary>
	public required GitBranchName Name { get; init; }

	/// <summary>Gets the object id the branch points at.</summary>
	public required GitCommitSha Sha { get; init; }

	/// <summary>Gets the upstream this branch tracks, or <see langword="null"/> when it tracks none.</summary>
	public GitBranchName? Upstream { get; init; }

	/// <summary>Gets a value indicating whether this is the checked-out branch.</summary>
	public bool IsCurrent { get; init; }

	/// <summary>
	/// Gets a value indicating whether this is a remote-tracking branch under <c>refs/remotes</c>.
	/// </summary>
	/// <remarks>
	/// Determined from the full reference name, not from the short name: a local branch may
	/// legitimately be called <c>origin/main</c>, which is indistinguishable from a remote-tracking
	/// branch once shortened.
	/// </remarks>
	public bool IsRemote { get; init; }
}
```

`GitIntegration/Models/GitRemote.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// One configured remote.
/// </summary>
public sealed record GitRemote
{
	/// <summary>Gets the remote's name, such as <c>origin</c>.</summary>
	public required GitRemoteName Name { get; init; }

	/// <summary>Gets the URL git fetches from.</summary>
	public required GitRepositoryRemotePath FetchUrl { get; init; }

	/// <summary>
	/// Gets the URL git pushes to. Equal to <see cref="FetchUrl"/> unless <c>remote.*.pushurl</c>
	/// is configured.
	/// </summary>
	public required GitRepositoryRemotePath PushUrl { get; init; }
}
```

`GitIntegration/Models/GitDiffEntry.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;

/// <summary>
/// One path changed between two states of the repository.
/// </summary>
public sealed record GitDiffEntry
{
	/// <summary>Gets what happened to the path.</summary>
	public required GitChangeKind Kind { get; init; }

	/// <summary>Gets the path as it exists after the change, relative to the repository root.</summary>
	public required RelativeFilePath Path { get; init; }

	/// <summary>
	/// Gets the path this file came from for a rename or a copy, or <see langword="null"/>
	/// otherwise.
	/// </summary>
	public RelativeFilePath? OriginalPath { get; init; }

	/// <summary>
	/// Gets git's similarity score for a rename or a copy, from 0 to 100, or <see langword="null"/>
	/// when git reported none.
	/// </summary>
	public int? SimilarityPercent { get; init; }
}
```

`GitIntegration/Models/GitVersion.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The version of the git binary being invoked.
/// </summary>
/// <remarks>
/// Git appends a platform suffix on some builds — <c>2.50.1.windows.1</c> — so the numeric
/// components are exposed separately from <see cref="Raw"/>, which keeps whatever git printed.
/// </remarks>
public sealed record GitVersion
{
	/// <summary>Gets the major version.</summary>
	public required int Major { get; init; }

	/// <summary>Gets the minor version, or zero when git did not report one.</summary>
	public required int Minor { get; init; }

	/// <summary>Gets the patch version, or zero when git did not report one.</summary>
	public required int Patch { get; init; }

	/// <summary>Gets the version exactly as git printed it, without the <c>git version </c> prefix.</summary>
	public required string Raw { get; init; }

	/// <summary>
	/// Decides whether this version is at least the given major and minor version.
	/// </summary>
	/// <remarks>
	/// Feature gates in git are documented against a major and minor pair — <c>fetch --porcelain</c>
	/// arrived in 2.41 — so the patch component is deliberately not part of the comparison.
	/// </remarks>
	/// <param name="major">The required major version.</param>
	/// <param name="minor">The required minor version.</param>
	/// <returns><see langword="true"/> when this version is at least that version.</returns>
	public bool AtLeast(int major, int minor) => Major != major ? Major > major : Minor >= minor;
}
```

- [ ] **Step 6: Write the parsing primitives**

`GitIntegration/Parsing/GitOutputFormats.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The machine-readable output formats this library asks git for.
/// </summary>
/// <remarks>
/// Shared between the builder that requests a format and the parser that reads it, so the two
/// cannot drift apart. A builder test asserts the exact string reaches the argument vector, and a
/// parser test asserts the shape it produces, which together pin the format from both ends.
/// </remarks>
internal static class GitOutputFormats
{
	/// <summary>
	/// The ASCII unit separator, used between fields within one record.
	/// </summary>
	/// <remarks>
	/// Chosen because git forbids it in a reference name and no filesystem permits it in a path,
	/// so it can never appear inside a field and be mistaken for a separator.
	/// </remarks>
	internal const char UnitSeparator = '\u001f';

	/// <summary>
	/// The <c>log</c> format: sha, tree, parents, author name/email/date, committer
	/// name/email/date, subject, body.
	/// </summary>
	/// <remarks>
	/// Used with <c>-z</c>, which NUL-terminates each commit, so a multi-line body cannot be
	/// mistaken for the start of a new record. <c>%x1f</c> is git's escape for a literal byte.
	/// </remarks>
	internal const string LogFormat = "%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b";

	/// <summary>
	/// The <c>for-each-ref</c> format: full reference name, short name, object id, upstream, and
	/// the current-branch marker.
	/// </summary>
	/// <remarks>
	/// The full reference name leads, and is the reason this format differs from the one sketched
	/// in the design document. A short name cannot say whether a branch is local or
	/// remote-tracking — a local branch may be called <c>origin/main</c> — and
	/// <c>refs/remotes/origin/HEAD</c>, present in every clone, shortens to the bare remote name
	/// and would otherwise be reported as a branch called <c>origin</c>. <c>%1f</c> is
	/// <c>for-each-ref</c>'s own hex escape, which differs in spelling from <c>log</c>'s
	/// <c>%x1f</c> but means the same byte.
	/// </remarks>
	internal const string ForEachRefFormat =
		"%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)";
}
```

`GitIntegration/Parsing/GitParseValues.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Turns raw git output into <c>ktsu.Semantics</c> values, or explains why it could not.
/// </summary>
internal static class GitParseValues
{
	/// <summary>
	/// Converts a raw field into a semantic string, throwing when it fails that type's validation.
	/// </summary>
	/// <typeparam name="TSemantic">The semantic string type to produce.</typeparam>
	/// <param name="value">The raw field as git printed it.</param>
	/// <param name="description">What the field is, used in the failure message.</param>
	/// <returns>The converted value.</returns>
	/// <exception cref="GitParseException">
	/// <paramref name="value"/> is empty or fails the type's validation.
	/// </exception>
	internal static TSemantic ToSemantic<TSemantic>(string value, string description)
		where TSemantic : SemanticString<TSemantic>, new()
	{
		// Called on the constructed base type rather than as TSemantic.TryCreate: TryCreate is a
		// plain static method, not a static abstract interface member, so invoking it through the
		// type parameter is CS0704. The explicit null test is what keeps the return from being
		// CS8603, which this repository treats as an error.
		if (!string.IsNullOrEmpty(value) &&
			SemanticString<TSemantic>.TryCreate(value, out TSemantic? result) &&
			result is not null)
		{
			return result;
		}

		throw new GitParseException($"git reported a {description} that is not valid: '{value}'.");
	}

	/// <summary>
	/// Converts a raw path field into a repository-relative path.
	/// </summary>
	/// <remarks>
	/// Two failures are folded together here. An empty field is a malformed record — and
	/// <see cref="RelativeFilePath"/> accepts the empty string, so nothing else would catch it. A
	/// path containing a control character is one git permits and <see cref="RelativeFilePath"/>
	/// refuses on Windows; reporting it beats dropping the entry, which would make
	/// <see cref="GitStatus.IsClean"/> claim a dirty tree is clean.
	/// </remarks>
	/// <param name="value">The raw path as git printed it.</param>
	/// <returns>The converted path.</returns>
	/// <exception cref="GitParseException">
	/// <paramref name="value"/> is empty or cannot be represented as a relative file path.
	/// </exception>
	internal static RelativeFilePath ToRelativeFilePath(string value)
	{
		if (!string.IsNullOrEmpty(value) &&
			RelativeFilePath.TryCreate(value, out RelativeFilePath? path) &&
			path is not null)
		{
			return path;
		}

		throw new GitParseException(
			$"git reported a path that cannot be represented as a relative file path: '{value}'.");
	}
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitParseValuesTests|FullyQualifiedName~GitVersionTests"`

Expected: PASS, 12 tests.

- [ ] **Step 8: Build the whole solution and run every test**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all 77 pre-existing tests plus the 12 new ones passing.

- [ ] **Step 9: Commit**

```bash
git add GitIntegration/Models GitIntegration/Parsing GitIntegration/Execution/GitExceptions.cs GitIntegration.Test/Models GitIntegration.Test/Parsing
git commit -m "[minor] Add read-only result models and parsing primitives"
```

---
## Task 2: `--version` — the first complete verb

The smallest verb, done end to end, so the parser/builder/test shape is settled before the harder verbs copy it. It is also a real dependency: Phase 5 gates `fetch --porcelain` on git ≥ 2.41.

**Files:**
- Create: `GitIntegration/Parsing/GitVersionParser.cs`
- Create: `GitIntegration/Builders/GitVersionBuilder.cs`
- Test: `GitIntegration.Test/Parsing/GitVersionParserTests.cs`
- Test: `GitIntegration.Test/Builders/GitVersionBuilderTests.cs`

**Interfaces:**
- Consumes: `GitVersion`, `GitParseException` (Task 1); `GitCommandBuilder<TResult>`, `IGitCommandBuilder<TResult>`, `GitProcessResult`, `IGitProcessRunner` (existing); `RecordingGitProcessRunner` (existing test fake).
- Produces:
  - `internal static class GitVersionParser` — `internal static GitVersion Parse(string output)`.
  - `public interface IGitVersionBuilder : IGitCommandBuilder<GitVersion>` (no members of its own).
  - `internal sealed class GitVersionBuilder(IGitProcessRunner runner)`.

- [ ] **Step 1: Write the failing parser test**

`GitIntegration.Test/Parsing/GitVersionParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitVersionParserTests
{
	[TestMethod]
	public void ParsesAPlainThreePartVersion()
	{
		GitVersion version = GitVersionParser.Parse("git version 2.41.0\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(41, version.Minor);
		Assert.AreEqual(0, version.Patch);
		Assert.AreEqual("2.41.0", version.Raw);
	}

	[TestMethod]
	public void ParsesTheWindowsBuildSuffix()
	{
		// Captured verbatim from the git this library was developed against. The trailing
		// ".windows.1" is why Raw exists and why the parser cannot assume three components.
		GitVersion version = GitVersionParser.Parse("git version 2.50.1.windows.1\n");

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
		Assert.AreEqual(1, version.Patch);
		Assert.AreEqual("2.50.1.windows.1", version.Raw);
	}

	[TestMethod]
	public void ParsesAVersionWithNoPatchComponent()
	{
		GitVersion version = GitVersionParser.Parse("git version 3.0\n");

		Assert.AreEqual(3, version.Major);
		Assert.AreEqual(0, version.Minor);
		Assert.AreEqual(0, version.Patch);
	}

	[TestMethod]
	public void RejectsOutputWithoutTheExpectedPrefix()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitVersionParser.Parse("2.41.0\n"));
	}

	[TestMethod]
	public void RejectsANonNumericMajorComponent()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitVersionParser.Parse("git version next\n"));
	}
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitVersionParserTests"`

Expected: compilation failure — `GitVersionParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitVersionParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Globalization;

/// <summary>
/// Reads <c>git --version</c>.
/// </summary>
internal static class GitVersionParser
{
	private const string Prefix = "git version ";

	/// <summary>
	/// Parses the output of <c>git --version</c>.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed version.</returns>
	/// <exception cref="GitParseException">The output did not have the expected shape.</exception>
	internal static GitVersion Parse(string output)
	{
		Ensure.NotNull(output);

		string trimmed = output.Trim();

		if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
		{
			throw new GitParseException($"Unrecognised 'git --version' output: '{trimmed}'.");
		}

		string raw = trimmed[Prefix.Length..];
		string[] components = raw.Split('.');

		// The major component must be a number for the value to mean anything. Minor and patch
		// default to zero, because git has shipped two-component versions and because a build
		// suffix such as ".windows.1" makes trailing components non-numeric by design.
		if (components.Length == 0 ||
			!int.TryParse(components[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
		{
			throw new GitParseException($"Unrecognised git version number: '{raw}'.");
		}

		return new GitVersion
		{
			Major = major,
			Minor = ReadComponent(components, 1),
			Patch = ReadComponent(components, 2),
			Raw = raw,
		};
	}

	private static int ReadComponent(string[] components, int index) =>
		index < components.Length &&
		int.TryParse(components[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
			? value
			: 0;
}
```

- [ ] **Step 4: Run the parser test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~GitVersionParserTests"`

Expected: PASS, 5 tests.

- [ ] **Step 5: Write the failing builder test**

`GitIntegration.Test/Builders/GitVersionBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitVersionBuilderTests
{
	[TestMethod]
	public void BuildsTheVersionArgumentVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitVersionBuilder builder = new(runner);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		// No -C: --version is not repository-scoped, so it must run anywhere, including where no
		// repository exists at all.
		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"--version",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteParsesTheVersionAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "git version 2.50.1.windows.1\n" };
		GitVersionBuilder builder = new(runner);

		GitVersion version = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitVersionBuilderTests"`

Expected: compilation failure — `GitVersionBuilder` does not exist.

- [ ] **Step 7: Write the builder**

`GitIntegration/Builders/GitVersionBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// Reports the version of the git binary being invoked.
/// </summary>
public interface IGitVersionBuilder : IGitCommandBuilder<GitVersion>
{
}

/// <summary>
/// Builds <c>git --version</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
internal sealed class GitVersionBuilder(IGitProcessRunner runner)
	: GitCommandBuilder<GitVersion>(runner, repositoryPath: null), IGitVersionBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments) =>
		Ensure.NotNull(arguments).Add("--version");

	/// <inheritdoc />
	protected override GitVersion ParseResult(GitProcessResult result) =>
		GitVersionParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 8: Run both test classes to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitVersionParserTests|FullyQualifiedName~GitVersionBuilderTests"`

Expected: PASS, 7 tests.

- [ ] **Step 9: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 10: Commit**

```bash
git add GitIntegration/Parsing/GitVersionParser.cs GitIntegration/Builders/GitVersionBuilder.cs GitIntegration.Test/Parsing/GitVersionParserTests.cs GitIntegration.Test/Builders/GitVersionBuilderTests.cs
git commit -m "[minor] Add the git --version verb"
```

---

## Task 3: `GitStatusParser`

The hardest parser, and the one with the most spec-mandated edge cases. Written and tested before any `status` builder exists.

**Files:**
- Create: `GitIntegration/Parsing/GitStatusParser.cs`
- Test: `GitIntegration.Test/Parsing/GitStatusParserTests.cs`

**Interfaces:**
- Consumes: `GitStatus`, `GitStatusEntry`, `GitFileState`, `GitParseValues`, `GitParseException` (Task 1).
- Produces: `internal static class GitStatusParser` — `internal static GitStatus Parse(string output)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitStatusParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitStatusParserTests
{
	// Fixtures are inline rather than files on disk: git's porcelain v2 format embeds NUL, which
	// makes a fixture file binary, unreviewable in a diff, and exempt from this repo's EOL
	// normalisation. NUL is written as the six-character escape u0000 (backslash-u-0-0-0-0) rather
	// than backslash-zero, so it can never read as an octal escape when followed by a digit.
	private const string Nul = "\u0000";

	private const string CleanOnMain =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head main" + Nul;

	private const string TrackingAheadOne =
		"# branch.oid 9429d2063d91f1097de51a196cb8203b06335738" + Nul +
		"# branch.head main" + Nul +
		"# branch.upstream origin/main" + Nul +
		"# branch.ab +1 -0" + Nul;

	private const string DivergedFromUpstream =
		"# branch.oid 9429d2063d91f1097de51a196cb8203b06335738" + Nul +
		"# branch.head main" + Nul +
		"# branch.upstream origin/main" + Nul +
		"# branch.ab +3 -7" + Nul;

	private const string DetachedHead =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head (detached)" + Nul;

	private const string EmptyRepository =
		"# branch.oid (initial)" + Nul +
		"# branch.head main" + Nul;

	// Captured from a real repository: a rename with a working-tree modification on top, a staged
	// add, and an untracked file.
	private const string MixedWorkingTree =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head main" + Nul +
		"2 RM N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 R100 renamed.txt" + Nul + "a.txt" + Nul +
		"1 A. N... 000000 100644 100644 0000000000000000000000000000000000000000 " +
			"54f9d6da5c91d556e6b54340b1327573073030af staged.txt" + Nul +
		"? untracked.txt" + Nul;

	private const string PathsWithSpacesAndNonAscii =
		"# branch.head main" + Nul +
		"1 .M N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 dir with spaces/ünïcødé.txt" + Nul;

	private const string UnmergedFile =
		"# branch.head main" + Nul +
		"u UU N... 100644 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"54f9d6da5c91d556e6b54340b1327573073030af 6f8b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b " +
			"conflicted.txt" + Nul;

	private const string IgnoredFile =
		"# branch.head main" + Nul +
		"! bin/output.dll" + Nul;

	[TestMethod]
	public void ReadsTheCurrentBranchAndReportsCleanWhenThereAreNoEntries()
	{
		GitStatus status = GitStatusParser.Parse(CleanOnMain);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
		Assert.IsNull(status.Upstream);
		Assert.IsFalse(status.IsDetached);
		Assert.IsTrue(status.IsClean);
		Assert.AreEqual(0, status.Entries.Count);
	}

	[TestMethod]
	public void ReadsTheUpstreamAndAheadCount()
	{
		GitStatus status = GitStatusParser.Parse(TrackingAheadOne);

		Assert.AreEqual("origin/main".As<GitBranchName>(), status.Upstream);
		Assert.AreEqual(1, status.Ahead);
		Assert.AreEqual(0, status.Behind);
	}

	[TestMethod]
	public void ReadsBothSidesOfADivergedBranch()
	{
		GitStatus status = GitStatusParser.Parse(DivergedFromUpstream);

		Assert.AreEqual(3, status.Ahead);
		Assert.AreEqual(7, status.Behind);
	}

	[TestMethod]
	public void ReportsDetachedHeadWithNoBranch()
	{
		GitStatus status = GitStatusParser.Parse(DetachedHead);

		Assert.IsTrue(status.IsDetached);
		Assert.IsNull(status.Branch);
	}

	[TestMethod]
	public void HandlesAnEmptyRepositoryWithNoCommits()
	{
		// "# branch.oid (initial)" is not a sha and must not be treated as one; the parser ignores
		// branch.oid entirely rather than trying to interpret it.
		GitStatus status = GitStatusParser.Parse(EmptyRepository);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
		Assert.IsTrue(status.IsClean);
	}

	[TestMethod]
	public void HandlesCompletelyEmptyOutput()
	{
		GitStatus status = GitStatusParser.Parse(string.Empty);

		Assert.IsNull(status.Branch);
		Assert.IsTrue(status.IsClean);
	}

	[TestMethod]
	public void ReadsARenameWithItsOriginalPathFromTheFollowingField()
	{
		// The load-bearing case: a '2' record's original path is a separate NUL-terminated field,
		// so a parser that splits on NUL and treats each chunk as a record emits a bogus entry here.
		GitStatus status = GitStatusParser.Parse(MixedWorkingTree);

		GitStatusEntry rename = status.Entries[0];
		Assert.AreEqual(GitFileState.Renamed, rename.IndexState);
		Assert.AreEqual(GitFileState.Modified, rename.WorkTreeState);
		Assert.AreEqual("renamed.txt".As<RelativeFilePath>(), rename.Path);
		Assert.AreEqual("a.txt".As<RelativeFilePath>(), rename.OriginalPath);
	}

	[TestMethod]
	public void ReadsAStagedAddAndAnUntrackedFileAlongsideTheRename()
	{
		GitStatus status = GitStatusParser.Parse(MixedWorkingTree);

		Assert.AreEqual(3, status.Entries.Count);

		GitStatusEntry staged = status.Entries[1];
		Assert.AreEqual(GitFileState.Added, staged.IndexState);
		Assert.AreEqual(GitFileState.Unmodified, staged.WorkTreeState);
		Assert.AreEqual("staged.txt".As<RelativeFilePath>(), staged.Path);
		Assert.IsNull(staged.OriginalPath);

		GitStatusEntry untracked = status.Entries[2];
		Assert.AreEqual(GitFileState.Untracked, untracked.IndexState);
		Assert.AreEqual(GitFileState.Untracked, untracked.WorkTreeState);
		Assert.AreEqual("untracked.txt".As<RelativeFilePath>(), untracked.Path);
	}

	[TestMethod]
	public void ReadsAPathContainingSpacesAndNonAsciiCharacters()
	{
		// The path is the last space-separated field, so splitting must be bounded by the field
		// count or a space in the path truncates it. core.quotepath=false is what keeps the
		// non-ASCII characters literal rather than octal-escaped.
		GitStatus status = GitStatusParser.Parse(PathsWithSpacesAndNonAscii);

		Assert.AreEqual(
			"dir with spaces/ünïcødé.txt".As<RelativeFilePath>(),
			status.Entries[0].Path);
	}

	[TestMethod]
	public void ReportsAnUnmergedFileAsUnmergedOnBothSides()
	{
		GitStatus status = GitStatusParser.Parse(UnmergedFile);

		GitStatusEntry entry = status.Entries[0];
		Assert.AreEqual(GitFileState.Unmerged, entry.IndexState);
		Assert.AreEqual(GitFileState.Unmerged, entry.WorkTreeState);
		Assert.AreEqual("conflicted.txt".As<RelativeFilePath>(), entry.Path);
	}

	[TestMethod]
	public void ReadsAnIgnoredFile()
	{
		GitStatus status = GitStatusParser.Parse(IgnoredFile);

		Assert.AreEqual(GitFileState.Ignored, status.Entries[0].IndexState);
		Assert.AreEqual("bin/output.dll".As<RelativeFilePath>(), status.Entries[0].Path);
	}

	[TestMethod]
	public void RejectsAnUnrecognisedRecordPrefix()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitStatusParser.Parse("x something" + Nul));
	}

	[TestMethod]
	public void RejectsATruncatedOrdinaryRecord()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitStatusParser.Parse("1 .M N... 100644" + Nul));
	}

	[TestMethod]
	public void RejectsARenameRecordWithNoFollowingOriginalPath()
	{
		string truncated =
			"2 R. N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 R100 renamed.txt" + Nul;

		Assert.ThrowsExactly<GitParseException>(() => GitStatusParser.Parse(truncated));
	}

	[TestMethod]
	public void IgnoresAHeaderItDoesNotRecognise()
	{
		// Forward compatibility: a future git adding a header must not break every caller.
		GitStatus status = GitStatusParser.Parse(
			"# branch.head main" + Nul + "# something.new whatever" + Nul);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitStatusParserTests"`

Expected: compilation failure — `GitStatusParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitStatusParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git status --porcelain=v2 --branch -z</c>.
/// </summary>
/// <remarks>
/// The v2 porcelain format is documented and stable across git versions, which is why it is parsed
/// rather than the human-facing output. <c>-z</c> terminates every record with a NUL so that a path
/// containing a space or a quote survives intact.
/// </remarks>
internal static class GitStatusParser
{
	/// <summary>
	/// Parses porcelain v2 status output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed status.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitStatus Parse(string output)
	{
		Ensure.NotNull(output);

		GitBranchName? branch = null;
		GitBranchName? upstream = null;
		int ahead = 0;
		int behind = 0;
		bool isDetached = false;
		List<GitStatusEntry> entries = [];

		// Records are NUL-terminated rather than NUL-separated, so the final element is always
		// empty; empty elements are skipped rather than counted.
		string[] records = output.Split('\0');

		for (int index = 0; index < records.Length; index++)
		{
			string record = records[index];

			if (record.Length == 0)
			{
				continue;
			}

			switch (record[0])
			{
				case '#':
					ReadHeader(record, ref branch, ref upstream, ref ahead, ref behind, ref isDetached);
					break;

				case '1':
					entries.Add(ReadOrdinaryEntry(record));
					break;

				case '2':
					// A rename or copy carries its original path as the next NUL-terminated field,
					// which is consumed here so the outer loop does not see it as a record.
					if (index + 1 >= records.Length)
					{
						throw new GitParseException(
							$"A rename status record has no following original path: '{record}'.");
					}

					entries.Add(ReadRenameEntry(record, records[++index]));
					break;

				case 'u':
					entries.Add(ReadUnmergedEntry(record));
					break;

				case '?':
					entries.Add(ReadPathOnlyEntry(record, GitFileState.Untracked));
					break;

				case '!':
					entries.Add(ReadPathOnlyEntry(record, GitFileState.Ignored));
					break;

				default:
					throw new GitParseException($"Unrecognised status record: '{record}'.");
			}
		}

		return new GitStatus
		{
			Branch = branch,
			Upstream = upstream,
			Ahead = ahead,
			Behind = behind,
			IsDetached = isDetached,
			Entries = entries,
		};
	}

	private static void ReadHeader(
		string record,
		ref GitBranchName? branch,
		ref GitBranchName? upstream,
		ref int ahead,
		ref int behind,
		ref bool isDetached)
	{
		// "# branch.head main" splits into "#", "branch.head", "main"; the value keeps any spaces
		// it contains, which matters for "# branch.ab +1 -0".
		string[] parts = record.Split(' ', 3);

		if (parts.Length < 3)
		{
			return;
		}

		switch (parts[1])
		{
			case "branch.head":
				if (string.Equals(parts[2], "(detached)", StringComparison.Ordinal))
				{
					isDetached = true;
				}
				else
				{
					branch = GitParseValues.ToSemantic<GitBranchName>(parts[2], "branch name");
				}

				break;

			case "branch.upstream":
				upstream = GitParseValues.ToSemantic<GitBranchName>(parts[2], "upstream branch name");
				break;

			case "branch.ab":
				ReadAheadBehind(parts[2], ref ahead, ref behind);
				break;

			default:
				// branch.oid is deliberately ignored — it is "(initial)" in a repository with no
				// commits, so it is not always an object id — and so is any header a future git
				// adds, which must not break existing callers.
				break;
		}
	}

	private static void ReadAheadBehind(string value, ref int ahead, ref int behind)
	{
		foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (token.Length < 2 ||
				!int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int count))
			{
				continue;
			}

			if (token[0] == '+')
			{
				ahead = count;
			}
			else if (token[0] == '-')
			{
				behind = count;
			}
		}
	}

	private static GitStatusEntry ReadOrdinaryEntry(string record)
	{
		// 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
		string[] fields = SplitFields(record, count: 9);

		return new GitStatusEntry
		{
			IndexState = ToFileState(fields[1][0]),
			WorkTreeState = ToFileState(fields[1][1]),
			Path = GitParseValues.ToRelativeFilePath(fields[8]),
		};
	}

	private static GitStatusEntry ReadRenameEntry(string record, string originalPath)
	{
		// 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>
		string[] fields = SplitFields(record, count: 10);

		return new GitStatusEntry
		{
			IndexState = ToFileState(fields[1][0]),
			WorkTreeState = ToFileState(fields[1][1]),
			Path = GitParseValues.ToRelativeFilePath(fields[9]),
			OriginalPath = GitParseValues.ToRelativeFilePath(originalPath),
		};
	}

	private static GitStatusEntry ReadUnmergedEntry(string record)
	{
		// u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
		string[] fields = SplitFields(record, count: 11);

		// Both sides report Unmerged rather than the individual XY letters. For a 'u' record the
		// letters describe which side of the merge contributed what — "AA" for both-added, "DU" for
		// deleted-by-us — and collapsing them to Added or Deleted would lose the one fact a caller
		// needs, which is that the path is conflicted.
		return new GitStatusEntry
		{
			IndexState = GitFileState.Unmerged,
			WorkTreeState = GitFileState.Unmerged,
			Path = GitParseValues.ToRelativeFilePath(fields[10]),
		};
	}

	private static GitStatusEntry ReadPathOnlyEntry(string record, GitFileState state)
	{
		// ? <path> and ! <path> carry no per-side detail, so the single state applies to both.
		string[] fields = SplitFields(record, count: 2);

		return new GitStatusEntry
		{
			IndexState = state,
			WorkTreeState = state,
			Path = GitParseValues.ToRelativeFilePath(fields[1]),
		};
	}

	private static string[] SplitFields(string record, int count)
	{
		// Bounded so the path, which is always last and may contain spaces, is never split.
		string[] fields = record.Split(' ', count);

		if (fields.Length < count || (count > 2 && fields[1].Length < 2))
		{
			throw new GitParseException($"Malformed status record: '{record}'.");
		}

		return fields;
	}

	private static GitFileState ToFileState(char code) => code switch
	{
		'.' => GitFileState.Unmodified,
		'M' => GitFileState.Modified,
		'A' => GitFileState.Added,
		'D' => GitFileState.Deleted,
		'R' => GitFileState.Renamed,
		'C' => GitFileState.Copied,
		'T' => GitFileState.TypeChanged,
		'U' => GitFileState.Unmerged,
		_ => throw new GitParseException($"Unrecognised status code '{code}'."),
	};
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitStatusParserTests"`

Expected: PASS, 15 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Parsing/GitStatusParser.cs GitIntegration.Test/Parsing/GitStatusParserTests.cs
git commit -m "[minor] Add the porcelain v2 status parser"
```

---

## Task 4: `GitStatusBuilder`

**Files:**
- Create: `GitIntegration/Builders/GitStatusBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitStatusBuilderTests.cs`

**Interfaces:**
- Consumes: `GitStatus`, `GitUntrackedFilesMode` (Task 1); `GitStatusParser` (Task 3); `GitCommandBuilder<TResult>`, `TestPaths.Root` (existing test helper in `GitIntegration.Test/GitRepositoryMetadataTests.cs`).
- Produces:
  - `public interface IGitStatusBuilder : IGitCommandBuilder<GitStatus>` with `WithUntrackedFiles(GitUntrackedFilesMode) → IGitStatusBuilder` and `IncludeIgnored() → IGitStatusBuilder`.
  - `internal sealed class GitStatusBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitStatusBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitStatusBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultStatusVector()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"status",
			"--porcelain=v2",
			"--branch",
			"-z",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void MapsTheUntrackedFilesModeToItsOption()
	{
		RecordingGitProcessRunner runner = new();

		GitStatusBuilder none = new(runner, TestPaths.Root);
		_ = none.WithUntrackedFiles(GitUntrackedFilesMode.No);
		CollectionAssert.Contains(none.BuildArguments().ToArray(), "--untracked-files=no");

		GitStatusBuilder normal = new(runner, TestPaths.Root);
		_ = normal.WithUntrackedFiles(GitUntrackedFilesMode.Normal);
		CollectionAssert.Contains(normal.BuildArguments().ToArray(), "--untracked-files=normal");

		GitStatusBuilder all = new(runner, TestPaths.Root);
		_ = all.WithUntrackedFiles(GitUntrackedFilesMode.All);
		CollectionAssert.Contains(all.BuildArguments().ToArray(), "--untracked-files=all");
	}

	[TestMethod]
	public void AddsTheIgnoredOptionOnlyWhenAsked()
	{
		RecordingGitProcessRunner runner = new();

		GitStatusBuilder without = new(runner, TestPaths.Root);
		CollectionAssert.DoesNotContain(without.BuildArguments().ToArray(), "--ignored=matching");

		GitStatusBuilder with = new(runner, TestPaths.Root);
		_ = with.IncludeIgnored();
		CollectionAssert.Contains(with.BuildArguments().ToArray(), "--ignored=matching");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		IGitStatusBuilder chained = builder
			.WithUntrackedFiles(GitUntrackedFilesMode.All)
			.IncludeIgnored();

		Assert.AreSame(builder, chained);
	}

	[TestMethod]
	public void RejectsAnUnrecognisedUntrackedFilesMode()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		// A value outside the enum reaches the builder whenever a caller casts an int, and mapping
		// it to a silent default would send git an option the caller never asked for.
		_ = builder.WithUntrackedFiles((GitUntrackedFilesMode)99);

		Assert.ThrowsExactly<System.ComponentModel.InvalidEnumArgumentException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public async Task ExecuteParsesTheStatusAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput = "# branch.head main" + Nul + "? untracked.txt" + Nul,
		};
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		GitStatus status = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(status.IsClean);
		Assert.AreEqual(1, status.Entries.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitStatusBuilderTests"`

Expected: compilation failure — `GitStatusBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitStatusBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.ComponentModel;

using ktsu.Semantics.Paths;

/// <summary>
/// Reports the working tree and index state of a repository.
/// </summary>
public interface IGitStatusBuilder : IGitCommandBuilder<GitStatus>
{
	/// <summary>
	/// Sets how much untracked detail git should report.
	/// </summary>
	/// <param name="mode">The reporting mode.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitStatusBuilder WithUntrackedFiles(GitUntrackedFilesMode mode);

	/// <summary>
	/// Includes ignored files in the reported entries.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitStatusBuilder IncludeIgnored();
}

/// <summary>
/// Builds <c>git status --porcelain=v2 --branch -z</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitStatusBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitStatus>(runner, repositoryPath), IGitStatusBuilder
{
	private GitUntrackedFilesMode? _untrackedFiles;
	private bool _includeIgnored;

	/// <inheritdoc />
	public IGitStatusBuilder WithUntrackedFiles(GitUntrackedFilesMode mode)
	{
		_untrackedFiles = mode;
		return this;
	}

	/// <inheritdoc />
	public IGitStatusBuilder IncludeIgnored()
	{
		_includeIgnored = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("status");

		// Porcelain v2 is the documented, version-stable machine format; --branch adds the header
		// records carrying the branch, upstream, and ahead/behind counts; -z NUL-terminates every
		// record so a path containing a space or a newline cannot be mistaken for a delimiter.
		arguments.Add("--porcelain=v2");
		arguments.Add("--branch");
		arguments.Add("-z");

		if (_untrackedFiles is GitUntrackedFilesMode mode)
		{
			arguments.Add("--untracked-files=" + ToOptionValue(mode));
		}

		if (_includeIgnored)
		{
			// "matching" rather than "traditional": it lists the ignored paths themselves instead of
			// collapsing an ignored directory to a single entry, which is what a caller asking for
			// ignored files almost always wants.
			arguments.Add("--ignored=matching");
		}
	}

	/// <inheritdoc />
	protected override GitStatus ParseResult(GitProcessResult result) =>
		GitStatusParser.Parse(Ensure.NotNull(result).StandardOutput);

	private static string ToOptionValue(GitUntrackedFilesMode mode) => mode switch
	{
		GitUntrackedFilesMode.No => "no",
		GitUntrackedFilesMode.Normal => "normal",
		GitUntrackedFilesMode.All => "all",
		_ => throw new InvalidEnumArgumentException(nameof(mode), (int)mode, typeof(GitUntrackedFilesMode)),
	};
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitStatusBuilderTests"`

Expected: PASS, 6 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitStatusBuilder.cs GitIntegration.Test/Builders/GitStatusBuilderTests.cs
git commit -m "[minor] Add the status verb builder"
```

---
## Task 5: `GitLogParser`

**Files:**
- Create: `GitIntegration/Parsing/GitLogParser.cs`
- Test: `GitIntegration.Test/Parsing/GitLogParserTests.cs`

**Interfaces:**
- Consumes: `GitCommit`, `GitSignature`, `GitOutputFormats.UnitSeparator`, `GitParseValues`, `GitParseException` (Task 1).
- Produces: `internal static class GitLogParser` — `internal static IReadOnlyList<GitCommit> Parse(string output)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitLogParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public class GitLogParserTests
{
	private const string Nul = "\u0000";
	private const string Us = "\u001f";

	private const string FirstSha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string SecondSha = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";
	private const string FirstTree = "f3758b7757b1f9bfe8c8e05fc5ac51bf3650c7d5";
	private const string SecondTree = "50997ba19ef5248ac46e7ec0992ec0b07d7c8f8b";

	// Captured verbatim from a real repository. The second record is the root commit, so its
	// parents field is empty, and it carries a two-paragraph body ending in the newline %b appends.
	private const string TwoCommits =
		FirstSha + Us + FirstTree + Us + SecondSha + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Second commit" + Us + Nul +
		SecondSha + Us + SecondTree + Us + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:04:59+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:04:59+10:00" + Us +
		"Initial commit" + Us + "A body line.\n\nAnd a second paragraph.\n" + Nul;

	private const string MergeCommit =
		FirstSha + Us + FirstTree + Us + SecondSha + " " + FirstTree + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Other Committer" + Us + "other@example.com" + Us + "2026-08-21T09:00:00-04:00" + Us +
		"Merge branch 'feature/x'" + Us + Nul;

	[TestMethod]
	public void ReadsEveryCommitInOrder()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(2, commits.Count);
		Assert.AreEqual(FirstSha.As<GitCommitSha>(), commits[0].Sha);
		Assert.AreEqual(SecondSha.As<GitCommitSha>(), commits[1].Sha);
	}

	[TestMethod]
	public void ReadsTheTreeAndParentShas()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(FirstTree.As<GitCommitSha>(), commits[0].TreeSha);
		CollectionAssert.AreEqual(
			new[] { SecondSha.As<GitCommitSha>() },
			commits[0].ParentShas.ToArray());
	}

	[TestMethod]
	public void ReportsARootCommitAsHavingNoParents()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(0, commits[1].ParentShas.Count);
	}

	[TestMethod]
	public void ReadsBothParentsOfAMergeCommit()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual(2, commits[0].ParentShas.Count);
		Assert.AreEqual(SecondSha.As<GitCommitSha>(), commits[0].ParentShas[0]);
		Assert.AreEqual(FirstTree.As<GitCommitSha>(), commits[0].ParentShas[1]);
	}

	[TestMethod]
	public void ReadsTheAuthorAndCommitterSeparately()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual("Fixture Author".As<GitAuthorName>(), commits[0].Author.Name);
		Assert.AreEqual("fixture@example.com".As<GitAuthorEmail>(), commits[0].Author.Email);
		Assert.AreEqual("Other Committer".As<GitAuthorName>(), commits[0].Committer.Name);
		Assert.AreEqual("other@example.com".As<GitAuthorEmail>(), commits[0].Committer.Email);
	}

	[TestMethod]
	public void PreservesTheCommittedTimeZoneOffset()
	{
		// %aI is strict ISO-8601 with the offset the commit was made in. Normalising to UTC would
		// throw away the local time, which is information a caller may want.
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual(TimeSpan.FromHours(10), commits[0].Author.Timestamp.Offset);
		Assert.AreEqual(TimeSpan.FromHours(-4), commits[0].Committer.Timestamp.Offset);
		Assert.AreEqual(
			new DateTimeOffset(2026, 8, 20, 0, 5, 20, TimeSpan.FromHours(10)),
			commits[0].Author.Timestamp);
	}

	[TestMethod]
	public void ReadsAMultiLineBodyWithoutSplittingTheRecord()
	{
		// -z is what makes this work: the blank line inside the body would otherwise be
		// indistinguishable from a record boundary in a newline-delimited format.
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual("Initial commit", commits[1].Subject);
		Assert.AreEqual("A body line.\n\nAnd a second paragraph.", commits[1].Body);
	}

	[TestMethod]
	public void ReportsAnEmptyBodyAsAnEmptyString()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(string.Empty, commits[0].Body);
	}

	[TestMethod]
	public void ReturnsAnEmptyListForNoOutput()
	{
		// An empty repository, or a log filtered down to nothing, produces no output at all.
		Assert.AreEqual(0, GitLogParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitLogParser.Parse(FirstSha + Us + FirstTree + Nul));
	}

	[TestMethod]
	public void RejectsAnUnparseableTimestamp()
	{
		string broken = TwoCommits.Replace("2026-08-20T00:05:20+10:00", "not-a-date", StringComparison.Ordinal);

		Assert.ThrowsExactly<GitParseException>(() => GitLogParser.Parse(broken));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitLogParserTests"`

Expected: compilation failure — `GitLogParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitLogParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git log -z</c> emitted with <see cref="GitOutputFormats.LogFormat"/>.
/// </summary>
internal static class GitLogParser
{
	private const int FieldCount = 11;

	/// <summary>
	/// Parses NUL-terminated, unit-separated log output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The commits, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitCommit> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitCommit> commits = [];

		// Records are NUL-terminated, so the final element is always empty.
		foreach (string record in output.Split('\0'))
		{
			if (record.Length == 0)
			{
				continue;
			}

			commits.Add(ReadCommit(record));
		}

		return commits;
	}

	private static GitCommit ReadCommit(string record)
	{
		string[] fields = record.Split(GitOutputFormats.UnitSeparator);

		if (fields.Length < FieldCount)
		{
			throw new GitParseException($"Malformed log record: '{record}'.");
		}

		return new GitCommit
		{
			Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[0], "commit id"),
			TreeSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "tree id"),
			ParentShas = ReadParents(fields[2]),
			Author = new GitSignature
			{
				Name = GitParseValues.ToSemantic<GitAuthorName>(fields[3], "author name"),
				Email = GitParseValues.ToSemantic<GitAuthorEmail>(fields[4], "author email"),
				Timestamp = ReadTimestamp(fields[5]),
			},
			Committer = new GitSignature
			{
				Name = GitParseValues.ToSemantic<GitAuthorName>(fields[6], "committer name"),
				Email = GitParseValues.ToSemantic<GitAuthorEmail>(fields[7], "committer email"),
				Timestamp = ReadTimestamp(fields[8]),
			},
			Subject = fields[9],

			// %b ends with the newline git appends after the body. That terminator is not part of
			// the message, and leaving it on would make every round trip through this type add
			// another one.
			Body = fields[10].TrimEnd('\n', '\r'),
		};
	}

	private static IReadOnlyList<GitCommitSha> ReadParents(string value)
	{
		// %P is space-separated and empty for a root commit.
		if (value.Length == 0)
		{
			return [];
		}

		string[] tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		GitCommitSha[] parents = new GitCommitSha[tokens.Length];

		for (int index = 0; index < tokens.Length; index++)
		{
			parents[index] = GitParseValues.ToSemantic<GitCommitSha>(tokens[index], "parent commit id");
		}

		return parents;
	}

	private static DateTimeOffset ReadTimestamp(string value) =>
		DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp)
			? timestamp
			: throw new GitParseException($"git reported a commit timestamp that is not valid: '{value}'.");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitLogParserTests"`

Expected: PASS, 11 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Parsing/GitLogParser.cs GitIntegration.Test/Parsing/GitLogParserTests.cs
git commit -m "[minor] Add the log parser"
```

---

## Task 6: `GitLogBuilder`

**Files:**
- Create: `GitIntegration/Builders/GitLogBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitLogBuilderTests.cs`

**Interfaces:**
- Consumes: `GitCommit`, `GitOutputFormats.LogFormat` (Task 1); `GitLogParser` (Task 5); `GitCommandBuilder<TResult>.AppendOperands` (existing); `GitRefName` (existing); `TestPaths.Root`.
- Produces:
  - `public interface IGitLogBuilder : IGitCommandBuilder<IReadOnlyList<GitCommit>>` with `Take(int) → IGitLogBuilder`, `Skip(int) → IGitLogBuilder`, `ForRevision(GitRefName) → IGitLogBuilder`, `ForPath(RelativeFilePath) → IGitLogBuilder`, `FirstParentOnly() → IGitLogBuilder`.
  - `internal sealed class GitLogBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitLogBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitLogBuilderTests
{
	private const string ExpectedFormat =
		"--format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b";

	[TestMethod]
	public void BuildsTheDefaultLogVector()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"log",
			"-z",
			ExpectedFormat,
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void PinsTheExactFormatStringSentToGit()
	{
		// Asserted literally, not by referencing GitOutputFormats, so that a change to the format
		// fails here rather than silently changing what every parser fixture is pinned against.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), ExpectedFormat);
	}

	[TestMethod]
	public void MapsTakeAndSkipToTheirOptions()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Take(5).Skip(2).FirstParentOnly();

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--max-count=5");
		CollectionAssert.Contains(arguments, "--skip=2");
		CollectionAssert.Contains(arguments, "--first-parent");
	}

	[TestMethod]
	public void PutsARevisionBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForRevision("main".As<GitRefName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("main", arguments[marker + 1]);
	}

	[TestMethod]
	public void PutsPathsAfterADoubleDash()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = builder.BuildArguments().ToArray();
		int separator = Array.IndexOf(arguments, "--");

		Assert.AreNotEqual(-1, separator);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[separator + 1]);
	}

	[TestMethod]
	public void PutsTheRevisionBeforeThePathSeparator()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForRevision("main".As<GitRefName>()).ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = builder.BuildArguments().ToArray();

		// git reads everything after -- as a pathspec, so a revision placed after it is silently
		// treated as a filename and the log comes back empty instead of failing.
		Assert.IsTrue(Array.IndexOf(arguments, "main") < Array.IndexOf(arguments, "--"));
	}

	[TestMethod]
	public void EmitsNoEndOfOptionsMarkerWhenThereAreNoCallerOperands()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void RejectsANegativeTakeOrSkip()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.Take(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.Skip(-1));
	}

	[TestMethod]
	public void RejectsANullRevisionOrPath()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForRevision(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitLogBuilderTests"`

Expected: compilation failure — `GitLogBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitLogBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists commits.
/// </summary>
public interface IGitLogBuilder : IGitCommandBuilder<IReadOnlyList<GitCommit>>
{
	/// <summary>Limits the result to at most this many commits.</summary>
	/// <param name="maxCount">The maximum number of commits to return.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative.</exception>
	public IGitLogBuilder Take(int maxCount);

	/// <summary>Skips this many commits before returning any.</summary>
	/// <param name="count">The number of commits to skip.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
	public IGitLogBuilder Skip(int count);

	/// <summary>
	/// Lists commits reachable from this revision instead of from HEAD. A range expression such as
	/// <c>main..feature</c> is accepted.
	/// </summary>
	/// <param name="revision">The revision or range.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	public IGitLogBuilder ForRevision(GitRefName revision);

	/// <summary>Limits the result to commits touching this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitLogBuilder ForPath(RelativeFilePath path);

	/// <summary>Follows only the first parent of a merge, hiding the merged-in history.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitLogBuilder FirstParentOnly();
}

/// <summary>
/// Builds <c>git log -z</c> with this library's pinned format.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitLogBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitCommit>>(runner, repositoryPath), IGitLogBuilder
{
	private readonly List<RelativeFilePath> _paths = [];
	private int? _maxCount;
	private int? _skip;
	private GitRefName? _revision;
	private bool _firstParentOnly;

	/// <inheritdoc />
	public IGitLogBuilder Take(int maxCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
		_maxCount = maxCount;
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder Skip(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		_skip = count;
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder ForRevision(GitRefName revision)
	{
		_revision = Ensure.NotNull(revision);
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder FirstParentOnly()
	{
		_firstParentOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("log");
		arguments.Add("-z");
		arguments.Add("--format=" + GitOutputFormats.LogFormat);

		if (_maxCount is int maxCount)
		{
			arguments.Add("--max-count=" + maxCount.ToString(CultureInfo.InvariantCulture));
		}

		if (_skip is int skip)
		{
			arguments.Add("--skip=" + skip.ToString(CultureInfo.InvariantCulture));
		}

		if (_firstParentOnly)
		{
			arguments.Add("--first-parent");
		}

		if (_revision is not null)
		{
			AppendOperands(arguments, _revision.WeakString);
		}

		if (_paths.Count > 0)
		{
			// A pathspec goes after --, which is its own end-of-options marker for that position.
			// The revision must already have been emitted: git reads everything after -- as a
			// filename, so a revision placed there returns an empty log rather than failing.
			arguments.Add("--");

			foreach (RelativeFilePath path in _paths)
			{
				arguments.Add(path.WeakString);
			}
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitCommit> ParseResult(GitProcessResult result) =>
		GitLogParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitLogBuilderTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitLogBuilder.cs GitIntegration.Test/Builders/GitLogBuilderTests.cs
git commit -m "[minor] Add the log verb builder"
```

---
## Task 7: `GitDiffParser`

**Files:**
- Create: `GitIntegration/Parsing/GitDiffParser.cs`
- Test: `GitIntegration.Test/Parsing/GitDiffParserTests.cs`

**Interfaces:**
- Consumes: `GitDiffEntry`, `GitChangeKind`, `GitParseValues`, `GitParseException` (Task 1).
- Produces: `internal static class GitDiffParser` — `internal static IReadOnlyList<GitDiffEntry> Parse(string output)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Parsing/GitDiffParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitDiffParserTests
{
	private const string Nul = "\u0000";

	// Captured verbatim. A delete and an add are two tokens each; the rename in the middle is
	// three — status, source, destination — which is why this cannot be parsed pairwise.
	private const string DeleteRenameAdd =
		"D" + Nul + "a.txt" + Nul +
		"R100" + Nul + "b.txt" + Nul + "b-renamed.txt" + Nul +
		"A" + Nul + "copy.txt" + Nul;

	private const string CopyWithPartialSimilarity =
		"C75" + Nul + "source.txt" + Nul + "destination.txt" + Nul;

	private const string PathWithSpacesAndNonAscii =
		"M" + Nul + "dir with spaces/ünïcødé.txt" + Nul;

	[TestMethod]
	public void ReadsATwoTokenRecord()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(DeleteRenameAdd);

		Assert.AreEqual(3, entries.Count);
		Assert.AreEqual(GitChangeKind.Deleted, entries[0].Kind);
		Assert.AreEqual("a.txt".As<RelativeFilePath>(), entries[0].Path);
		Assert.IsNull(entries[0].OriginalPath);
		Assert.IsNull(entries[0].SimilarityPercent);
	}

	[TestMethod]
	public void ReadsAThreeTokenRenameWithoutConsumingTheNextRecord()
	{
		// The failure this guards against is subtle: a pairwise parser reads "R100"/"b.txt" as one
		// entry and then "b-renamed.txt"/"A" as another, so the count still looks plausible.
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(DeleteRenameAdd);

		GitDiffEntry rename = entries[1];
		Assert.AreEqual(GitChangeKind.Renamed, rename.Kind);
		Assert.AreEqual("b-renamed.txt".As<RelativeFilePath>(), rename.Path);
		Assert.AreEqual("b.txt".As<RelativeFilePath>(), rename.OriginalPath);
		Assert.AreEqual(100, rename.SimilarityPercent);

		Assert.AreEqual(GitChangeKind.Added, entries[2].Kind);
		Assert.AreEqual("copy.txt".As<RelativeFilePath>(), entries[2].Path);
	}

	[TestMethod]
	public void ReadsACopyWithItsSimilarityScore()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(CopyWithPartialSimilarity);

		Assert.AreEqual(GitChangeKind.Copied, entries[0].Kind);
		Assert.AreEqual(75, entries[0].SimilarityPercent);
		Assert.AreEqual("source.txt".As<RelativeFilePath>(), entries[0].OriginalPath);
		Assert.AreEqual("destination.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void ReadsAPathContainingSpacesAndNonAsciiCharacters()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(PathWithSpacesAndNonAscii);

		Assert.AreEqual("dir with spaces/ünïcødé.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void MapsEveryDocumentedStatusLetter()
	{
		string output =
			"A" + Nul + "added.txt" + Nul +
			"D" + Nul + "deleted.txt" + Nul +
			"M" + Nul + "modified.txt" + Nul +
			"T" + Nul + "typechanged.txt" + Nul +
			"U" + Nul + "unmerged.txt" + Nul;

		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(output);

		Assert.AreEqual(GitChangeKind.Added, entries[0].Kind);
		Assert.AreEqual(GitChangeKind.Deleted, entries[1].Kind);
		Assert.AreEqual(GitChangeKind.Modified, entries[2].Kind);
		Assert.AreEqual(GitChangeKind.TypeChanged, entries[3].Kind);
		Assert.AreEqual(GitChangeKind.Unmerged, entries[4].Kind);
	}

	[TestMethod]
	public void ReportsAnUnrecognisedStatusLetterAsUnknownRatherThanThrowing()
	{
		// git emits 'B' for a broken pairing and 'X' for a state it calls a bug. Neither is worth
		// failing an entire diff over, and unlike the status format the set is not closed, so an
		// unknown letter degrades to Unknown instead of throwing.
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse("B" + Nul + "broken.txt" + Nul);

		Assert.AreEqual(GitChangeKind.Unknown, entries[0].Kind);
		Assert.AreEqual("broken.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void ReturnsAnEmptyListWhenNothingChanged()
	{
		Assert.AreEqual(0, GitDiffParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsAStatusWithNoFollowingPath()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitDiffParser.Parse("M"));
	}

	[TestMethod]
	public void RejectsARenameWithNoDestinationPath()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitDiffParser.Parse("R100" + Nul + "only-source.txt"));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitDiffParserTests"`

Expected: compilation failure — `GitDiffParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitDiffParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git diff --name-status -z</c>.
/// </summary>
/// <remarks>
/// The output is a stream of NUL-terminated tokens rather than fixed-size records: an ordinary
/// change is a status token followed by one path, while a rename or a copy is a status token
/// followed by the source path and then the destination path. Consuming it pairwise silently
/// misreads every rename and everything after it.
/// </remarks>
internal static class GitDiffParser
{
	/// <summary>
	/// Parses NUL-terminated name-status output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The changed paths, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record was missing a path.</exception>
	internal static IReadOnlyList<GitDiffEntry> Parse(string output)
	{
		Ensure.NotNull(output);

		string[] tokens = output.Split('\0');
		List<GitDiffEntry> entries = [];

		int index = 0;
		while (index < tokens.Length)
		{
			string status = tokens[index++];

			// Tokens are NUL-terminated, so the trailing element is empty.
			if (status.Length == 0)
			{
				continue;
			}

			GitChangeKind kind = ToChangeKind(status[0]);
			int? similarity = ReadSimilarity(status);

			if (kind is GitChangeKind.Renamed or GitChangeKind.Copied)
			{
				if (index + 1 >= tokens.Length)
				{
					throw new GitParseException(
						$"A '{status}' diff record is missing its source or destination path.");
				}

				string originalPath = tokens[index++];

				entries.Add(new GitDiffEntry
				{
					Kind = kind,
					Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
					OriginalPath = GitParseValues.ToRelativeFilePath(originalPath),
					SimilarityPercent = similarity,
				});
			}
			else
			{
				if (index >= tokens.Length)
				{
					throw new GitParseException($"A '{status}' diff record is missing its path.");
				}

				entries.Add(new GitDiffEntry
				{
					Kind = kind,
					Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
					SimilarityPercent = similarity,
				});
			}
		}

		return entries;
	}

	private static GitChangeKind ToChangeKind(char code) => code switch
	{
		'A' => GitChangeKind.Added,
		'C' => GitChangeKind.Copied,
		'D' => GitChangeKind.Deleted,
		'M' => GitChangeKind.Modified,
		'R' => GitChangeKind.Renamed,
		'T' => GitChangeKind.TypeChanged,
		'U' => GitChangeKind.Unmerged,

		// Unlike the status codes, this set is not closed: git also emits 'B' for a broken pairing
		// and 'X' for a state it documents as a bug. Failing the whole diff over one letter would
		// be worse than reporting the path with an unknown change kind.
		_ => GitChangeKind.Unknown,
	};

	private static int? ReadSimilarity(string status) =>
		status.Length > 1 &&
		int.TryParse(status.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int score)
			? score
			: null;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitDiffParserTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Parsing/GitDiffParser.cs GitIntegration.Test/Parsing/GitDiffParserTests.cs
git commit -m "[minor] Add the name-status diff parser"
```

---

## Task 8: `GitDiffBuilder`

**Files:**
- Create: `GitIntegration/Builders/GitDiffBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitDiffBuilderTests.cs`

**Interfaces:**
- Consumes: `GitDiffEntry` (Task 1); `GitDiffParser` (Task 7); `GitRefName`, `GitCommandBuilder<TResult>.AppendOperands`; `TestPaths.Root`.
- Produces:
  - `public interface IGitDiffBuilder : IGitCommandBuilder<IReadOnlyList<GitDiffEntry>>` with `Staged() → IGitDiffBuilder`, `Against(GitRefName) → IGitDiffBuilder`, `Between(GitRefName, GitRefName) → IGitDiffBuilder`, `DetectRenames() → IGitDiffBuilder`, `DetectCopies() → IGitDiffBuilder`, `ForPath(RelativeFilePath) → IGitDiffBuilder`.
  - `internal sealed class GitDiffBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitDiffBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitDiffBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultDiffVector()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"diff",
			"--name-status",
			"-z",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Staged().DetectRenames().DetectCopies();

		string[] arguments = builder.BuildArguments().ToArray();
		CollectionAssert.Contains(arguments, "--cached");
		CollectionAssert.Contains(arguments, "--find-renames");
		CollectionAssert.Contains(arguments, "--find-copies");
	}

	[TestMethod]
	public void PutsASingleRevisionBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Against("HEAD".As<GitRefName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("HEAD", arguments[marker + 1]);
	}

	[TestMethod]
	public void PutsBothRevisionsInOrderBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Between("main".As<GitRefName>(), "feature/x".As<GitRefName>());

		string[] arguments = builder.BuildArguments().ToArray();
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("main", arguments[marker + 1]);
		Assert.AreEqual("feature/x", arguments[marker + 2]);
	}

	[TestMethod]
	public void LastRevisionSelectionWins()
	{
		// Against and Between set the same slot, so a caller that calls both gets the later one
		// rather than a vector carrying three revisions that git would reject.
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Between("main".As<GitRefName>(), "feature/x".As<GitRefName>())
			.Against("HEAD".As<GitRefName>());

		string[] arguments = builder.BuildArguments().ToArray();

		CollectionAssert.Contains(arguments, "HEAD");
		CollectionAssert.DoesNotContain(arguments, "main");
		CollectionAssert.DoesNotContain(arguments, "feature/x");
	}

	[TestMethod]
	public void PutsPathsAfterADoubleDash()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = builder.BuildArguments().ToArray();
		int separator = Array.IndexOf(arguments, "--");

		Assert.AreNotEqual(-1, separator);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[separator + 1]);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Against(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Between(null!, "main".As<GitRefName>()));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Between("main".As<GitRefName>(), null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitDiffBuilderTests"`

Expected: compilation failure — `GitDiffBuilder` does not exist.

- [ ] **Step 3: Write the builder**

`GitIntegration/Builders/GitDiffBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the paths that differ between two states of the repository.
/// </summary>
public interface IGitDiffBuilder : IGitCommandBuilder<IReadOnlyList<GitDiffEntry>>
{
	/// <summary>Compares the index against HEAD instead of the working tree against the index.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder Staged();

	/// <summary>
	/// Compares against one revision. Replaces any previous revision selection.
	/// </summary>
	/// <param name="revision">The revision to compare against.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	public IGitDiffBuilder Against(GitRefName revision);

	/// <summary>
	/// Compares two revisions. Replaces any previous revision selection.
	/// </summary>
	/// <param name="from">The revision to compare from.</param>
	/// <param name="to">The revision to compare to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="from"/> or <paramref name="to"/> is <see langword="null"/>.
	/// </exception>
	public IGitDiffBuilder Between(GitRefName from, GitRefName to);

	/// <summary>Reports a delete and an add of similar content as a rename.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder DetectRenames();

	/// <summary>Reports an add whose content came from an existing file as a copy.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder DetectCopies();

	/// <summary>Limits the result to this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitDiffBuilder ForPath(RelativeFilePath path);
}

/// <summary>
/// Builds <c>git diff --name-status -z</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitDiffBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitDiffEntry>>(runner, repositoryPath), IGitDiffBuilder
{
	private readonly List<RelativeFilePath> _paths = [];

	// One slot, so Against and Between cannot combine into a three-revision vector git would reject.
	private string[] _revisions = [];
	private bool _staged;
	private bool _detectRenames;
	private bool _detectCopies;

	/// <inheritdoc />
	public IGitDiffBuilder Staged()
	{
		_staged = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder Against(GitRefName revision)
	{
		_revisions = [Ensure.NotNull(revision).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder Between(GitRefName from, GitRefName to)
	{
		_revisions = [Ensure.NotNull(from).WeakString, Ensure.NotNull(to).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder DetectRenames()
	{
		_detectRenames = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder DetectCopies()
	{
		_detectCopies = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("diff");
		arguments.Add("--name-status");
		arguments.Add("-z");

		if (_staged)
		{
			arguments.Add("--cached");
		}

		if (_detectRenames)
		{
			arguments.Add("--find-renames");
		}

		if (_detectCopies)
		{
			arguments.Add("--find-copies");
		}

		if (_revisions.Length > 0)
		{
			AppendOperands(arguments, _revisions);
		}

		if (_paths.Count > 0)
		{
			arguments.Add("--");

			foreach (RelativeFilePath path in _paths)
			{
				arguments.Add(path.WeakString);
			}
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitDiffEntry> ParseResult(GitProcessResult result) =>
		GitDiffParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitDiffBuilderTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Builders/GitDiffBuilder.cs GitIntegration.Test/Builders/GitDiffBuilderTests.cs
git commit -m "[minor] Add the diff verb builder"
```

---

## Task 9: Branches — `GitBranchParser` and `GitBranchListBuilder`

Parser and builder in one task because the format string is a shared decision: the parser's ability to tell local from remote depends on the leading `%(refname)` field the builder requests, so splitting them would leave one half unreviewable.

**Files:**
- Create: `GitIntegration/Parsing/GitBranchParser.cs`
- Create: `GitIntegration/Builders/GitBranchListBuilder.cs`
- Test: `GitIntegration.Test/Parsing/GitBranchParserTests.cs`
- Test: `GitIntegration.Test/Builders/GitBranchListBuilderTests.cs`

**Interfaces:**
- Consumes: `GitBranch`, `GitOutputFormats.ForEachRefFormat`, `GitOutputFormats.UnitSeparator`, `GitParseValues` (Task 1).
- Produces:
  - `internal static class GitBranchParser` — `internal static IReadOnlyList<GitBranch> Parse(string output)`.
  - `public interface IGitBranchListBuilder : IGitCommandBuilder<IReadOnlyList<GitBranch>>` with `LocalOnly() → IGitBranchListBuilder` and `RemoteOnly() → IGitBranchListBuilder`.
  - `internal sealed class GitBranchListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing parser tests**

`GitIntegration.Test/Parsing/GitBranchParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public class GitBranchParserTests
{
	private const string Us = "\u001f";

	private const string LocalSha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string RemoteSha = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";

	// Captured from a fresh clone. Note refs/remotes/origin/HEAD, whose short name is the bare
	// remote name, and the single space %(HEAD) emits for a branch that is not checked out.
	private const string FreshClone =
		"refs/heads/main" + Us + "main" + Us + LocalSha + Us + "origin/main" + Us + "*\n" +
		"refs/remotes/origin/HEAD" + Us + "origin" + Us + LocalSha + Us + Us + " \n" +
		"refs/remotes/origin/feature/x" + Us + "origin/feature/x" + Us + RemoteSha + Us + Us + " \n" +
		"refs/remotes/origin/main" + Us + "origin/main" + Us + RemoteSha + Us + Us + " \n";

	private const string LocalBranchNamedLikeARemote =
		"refs/heads/origin/main" + Us + "origin/main" + Us + LocalSha + Us + Us + " \n";

	[TestMethod]
	public void ReadsTheCurrentBranchWithItsUpstream()
	{
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		GitBranch main = branches[0];
		Assert.AreEqual("main".As<GitBranchName>(), main.Name);
		Assert.AreEqual(LocalSha.As<GitCommitSha>(), main.Sha);
		Assert.AreEqual("origin/main".As<GitBranchName>(), main.Upstream);
		Assert.IsTrue(main.IsCurrent);
		Assert.IsFalse(main.IsRemote);
	}

	[TestMethod]
	public void SkipsTheRemoteHeadSymbolicRef()
	{
		// refs/remotes/origin/HEAD shortens to the bare remote name, so without this filter every
		// clone reports a phantom branch called "origin".
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		Assert.AreEqual(3, branches.Count);
		Assert.IsFalse(branches.Any(branch => branch.Name == "origin".As<GitBranchName>()));
	}

	[TestMethod]
	public void MarksBranchesUnderRefsRemotesAsRemote()
	{
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		Assert.IsTrue(branches[1].IsRemote);
		Assert.AreEqual("origin/feature/x".As<GitBranchName>(), branches[1].Name);
		Assert.IsTrue(branches[2].IsRemote);
		Assert.IsNull(branches[1].Upstream);
		Assert.IsFalse(branches[1].IsCurrent);
	}

	[TestMethod]
	public void TreatsALocalBranchWithASlashAsLocal()
	{
		// The whole reason the format leads with %(refname): "origin/main" as a short name is
		// ambiguous, and only the full reference name settles it.
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(LocalBranchNamedLikeARemote);

		Assert.AreEqual(1, branches.Count);
		Assert.IsFalse(branches[0].IsRemote);
		Assert.AreEqual("origin/main".As<GitBranchName>(), branches[0].Name);
	}

	[TestMethod]
	public void ReturnsAnEmptyListForNoOutput()
	{
		// An empty repository has no branch references at all.
		Assert.AreEqual(0, GitBranchParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		string output = "refs/heads/main" + Us + "main" + Us + LocalSha + Us + Us + "*\r\n";

		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(output);

		Assert.AreEqual("main".As<GitBranchName>(), branches[0].Name);
		Assert.IsTrue(branches[0].IsCurrent);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitBranchParser.Parse("refs/heads/main" + Us + "main\n"));
	}
}
```

The test file needs `using System.Linq;` for `Any`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchParserTests"`

Expected: compilation failure — `GitBranchParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitBranchParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git for-each-ref</c> emitted with <see cref="GitOutputFormats.ForEachRefFormat"/>.
/// </summary>
/// <remarks>
/// Records are newline-separated rather than NUL-separated, which is safe here and only here: git
/// forbids control characters in a reference name, so a line break can never occur inside a record.
/// </remarks>
internal static class GitBranchParser
{
	private const string RemotePrefix = "refs/remotes/";
	private const string HeadSuffix = "/HEAD";
	private const int FieldCount = 5;

	/// <summary>
	/// Parses unit-separated reference records.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The branches, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitBranch> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitBranch> branches = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			if (record.Length == 0)
			{
				continue;
			}

			string[] fields = record.Split(GitOutputFormats.UnitSeparator);

			if (fields.Length < FieldCount)
			{
				throw new GitParseException($"Malformed for-each-ref record: '{record}'.");
			}

			string fullRefName = fields[0];
			bool isRemote = fullRefName.StartsWith(RemotePrefix, StringComparison.Ordinal);

			// refs/remotes/<remote>/HEAD is a symbolic reference naming the remote's default
			// branch, not a branch of its own. Its short name is the bare remote name, so leaving
			// it in reports a branch called "origin" in every clone.
			if (isRemote && fullRefName.EndsWith(HeadSuffix, StringComparison.Ordinal))
			{
				continue;
			}

			branches.Add(new GitBranch
			{
				Name = GitParseValues.ToSemantic<GitBranchName>(fields[1], "branch name"),
				Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[2], "branch object id"),

				// %(upstream:short) is empty when the branch tracks nothing.
				Upstream = fields[3].Length == 0
					? null
					: GitParseValues.ToSemantic<GitBranchName>(fields[3], "upstream branch name"),

				// %(HEAD) is "*" for the checked-out branch and a single space otherwise, never an
				// empty field.
				IsCurrent = string.Equals(fields[4], "*", StringComparison.Ordinal),
				IsRemote = isRemote,
			});
		}

		return branches;
	}
}
```

- [ ] **Step 4: Run the parser tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchParserTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Write the failing builder tests**

`GitIntegration.Test/Builders/GitBranchListBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

[TestClass]
public class GitBranchListBuilderTests
{
	private const string ExpectedFormat =
		"--format=%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)";

	[TestMethod]
	public void BuildsTheDefaultBranchListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"for-each-ref",
			ExpectedFormat,
			"refs/heads",
			"refs/remotes",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void PinsTheExactFormatStringSentToGit()
	{
		// Asserted literally rather than through GitOutputFormats. The leading %(refname) is what
		// lets the parser tell a local branch from a remote-tracking one and drop the remote HEAD
		// symbolic reference, so silently losing it would break both behaviours at once.
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), ExpectedFormat);
	}

	[TestMethod]
	public void LimitsTheReferencePrefixesOnRequest()
	{
		RecordingGitProcessRunner runner = new();

		GitBranchListBuilder local = new(runner, TestPaths.Root);
		_ = local.LocalOnly();
		string[] localArguments = local.BuildArguments().ToArray();
		CollectionAssert.Contains(localArguments, "refs/heads");
		CollectionAssert.DoesNotContain(localArguments, "refs/remotes");

		GitBranchListBuilder remote = new(runner, TestPaths.Root);
		_ = remote.RemoteOnly();
		string[] remoteArguments = remote.BuildArguments().ToArray();
		CollectionAssert.Contains(remoteArguments, "refs/remotes");
		CollectionAssert.DoesNotContain(remoteArguments, "refs/heads");
	}

	[TestMethod]
	public void TheLastPrefixSelectionWins()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		IGitBranchListBuilder chained = builder.LocalOnly().RemoteOnly();

		Assert.AreSame(builder, chained);
		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "refs/heads");
	}
}
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchListBuilderTests"`

Expected: compilation failure — `GitBranchListBuilder` does not exist.

- [ ] **Step 7: Write the builder**

`GitIntegration/Builders/GitBranchListBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists branch references.
/// </summary>
public interface IGitBranchListBuilder : IGitCommandBuilder<IReadOnlyList<GitBranch>>
{
	/// <summary>Lists only local branches. Replaces any previous selection.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchListBuilder LocalOnly();

	/// <summary>Lists only remote-tracking branches. Replaces any previous selection.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchListBuilder RemoteOnly();
}

/// <summary>
/// Builds <c>git for-each-ref</c> over the branch namespaces.
/// </summary>
/// <remarks>
/// <c>for-each-ref</c> rather than <c>branch --list</c>: it takes an explicit format string, so the
/// output is machine-readable by construction rather than by hoping a human-facing listing keeps its
/// shape.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitBranchListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitBranch>>(runner, repositoryPath), IGitBranchListBuilder
{
	private const string LocalPrefix = "refs/heads";
	private const string RemotePrefix = "refs/remotes";

	private string[] _prefixes = [LocalPrefix, RemotePrefix];

	/// <inheritdoc />
	public IGitBranchListBuilder LocalOnly()
	{
		_prefixes = [LocalPrefix];
		return this;
	}

	/// <inheritdoc />
	public IGitBranchListBuilder RemoteOnly()
	{
		_prefixes = [RemotePrefix];
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("for-each-ref");
		arguments.Add("--format=" + GitOutputFormats.ForEachRefFormat);

		// These prefixes are library constants, not caller-supplied operands, so they need no
		// --end-of-options guard: no caller value can reach this position.
		foreach (string prefix in _prefixes)
		{
			arguments.Add(prefix);
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitBranch> ParseResult(GitProcessResult result) =>
		GitBranchParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 8: Run the builder tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitBranchListBuilderTests"`

Expected: PASS, 4 tests.

- [ ] **Step 9: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 10: Commit**

```bash
git add GitIntegration/Parsing/GitBranchParser.cs GitIntegration/Builders/GitBranchListBuilder.cs GitIntegration.Test/Parsing/GitBranchParserTests.cs GitIntegration.Test/Builders/GitBranchListBuilderTests.cs
git commit -m "[minor] Add the branch listing verb"
```

---
## Task 10: Remotes — `GitRemoteParser` and `GitRemoteListBuilder`

Parser and builder together: `remote -v` takes no options, so the builder is four lines and has nothing a reviewer could accept or reject independently of the parser.

**Files:**
- Create: `GitIntegration/Parsing/GitRemoteParser.cs`
- Create: `GitIntegration/Builders/GitRemoteListBuilder.cs`
- Test: `GitIntegration.Test/Parsing/GitRemoteParserTests.cs`
- Test: `GitIntegration.Test/Builders/GitRemoteListBuilderTests.cs`

**Interfaces:**
- Consumes: `GitRemote`, `GitParseValues` (Task 1); `GitRemoteName`, `GitRepositoryRemotePath` (existing).
- Produces:
  - `internal static class GitRemoteParser` — `internal static IReadOnlyList<GitRemote> Parse(string output)`.
  - `public interface IGitRemoteListBuilder : IGitCommandBuilder<IReadOnlyList<GitRemote>>` (no members of its own).
  - `internal sealed class GitRemoteListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)`.

- [ ] **Step 1: Write the failing parser tests**

`GitIntegration.Test/Parsing/GitRemoteParserTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteParserTests
{
	private const string Tab = "\t";

	private const string SingleRemote =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (push)\n";

	private const string TwoRemotes =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (push)\n" +
		"upstream" + Tab + "git@github.com:someone/GitIntegration.git (fetch)\n" +
		"upstream" + Tab + "git@github.com:someone/GitIntegration.git (push)\n";

	private const string SeparatePushUrl =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "git@github.com:ktsu-dev/GitIntegration.git (push)\n";

	// A local remote is a filesystem path, which may contain spaces, which is why the parser
	// anchors on the trailing marker rather than on the last space.
	private const string LocalPathWithSpaces =
		"origin" + Tab + "C:/dev/my repos/upstream.git (fetch)\n" +
		"origin" + Tab + "C:/dev/my repos/upstream.git (push)\n";

	[TestMethod]
	public void ReadsARemoteWithMatchingFetchAndPushUrls()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(SingleRemote);

		Assert.AreEqual(1, remotes.Count);
		Assert.AreEqual("origin".As<GitRemoteName>(), remotes[0].Name);
		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
		Assert.AreEqual(remotes[0].FetchUrl, remotes[0].PushUrl);
	}

	[TestMethod]
	public void CollapsesTheFetchAndPushLinesIntoOneRemote()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(TwoRemotes);

		Assert.AreEqual(2, remotes.Count);
		Assert.AreEqual("origin".As<GitRemoteName>(), remotes[0].Name);
		Assert.AreEqual("upstream".As<GitRemoteName>(), remotes[1].Name);
	}

	[TestMethod]
	public void KeepsAPushUrlThatDiffersFromTheFetchUrl()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(SeparatePushUrl);

		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
		Assert.AreEqual(
			"git@github.com:ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].PushUrl);
	}

	[TestMethod]
	public void ReadsALocalPathContainingSpaces()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(LocalPathWithSpaces);

		Assert.AreEqual(
			"C:/dev/my repos/upstream.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
	}

	[TestMethod]
	public void ReturnsAnEmptyListWhenThereAreNoRemotes()
	{
		Assert.AreEqual(0, GitRemoteParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsALineWithNoTabSeparator()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitRemoteParser.Parse("origin https://example.com/repo.git (fetch)\n"));
	}

	[TestMethod]
	public void RejectsALineWithNoDirectionMarker()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitRemoteParser.Parse("origin" + Tab + "https://example.com/repo.git\n"));
	}
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteParserTests"`

Expected: compilation failure — `GitRemoteParser` does not exist.

- [ ] **Step 3: Write the parser**

`GitIntegration/Parsing/GitRemoteParser.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git remote -v</c>.
/// </summary>
/// <remarks>
/// The only verb in this library parsed from a format git does not let us specify. It is
/// nonetheless machine-stable: each line is the remote name, a tab, the URL, and a space-prefixed
/// direction marker. There is no porcelain alternative — <c>remote get-url</c> reports one remote
/// at a time and would need a listing invocation first.
/// </remarks>
internal static class GitRemoteParser
{
	private const string FetchSuffix = " (fetch)";
	private const string PushSuffix = " (push)";

	/// <summary>
	/// Parses verbose remote listing output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The remotes, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A line did not have the expected shape.</exception>
	internal static IReadOnlyList<GitRemote> Parse(string output)
	{
		Ensure.NotNull(output);

		// git prints each remote twice, once per direction. The order list preserves git's own
		// ordering, which a dictionary alone would not.
		List<string> order = [];
		Dictionary<string, string> fetchUrls = new(StringComparer.Ordinal);
		Dictionary<string, string> pushUrls = new(StringComparer.Ordinal);

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			if (record.Length == 0)
			{
				continue;
			}

			int tab = record.IndexOf('\t');

			if (tab <= 0)
			{
				throw new GitParseException($"Malformed 'remote -v' line: '{record}'.");
			}

			string name = record[..tab];
			string remainder = record[(tab + 1)..];

			if (!order.Contains(name))
			{
				order.Add(name);
			}

			// Anchored on the suffix rather than the last space: a local remote is a filesystem
			// path and may legitimately contain spaces.
			if (remainder.EndsWith(FetchSuffix, StringComparison.Ordinal))
			{
				fetchUrls[name] = remainder[..^FetchSuffix.Length];
			}
			else if (remainder.EndsWith(PushSuffix, StringComparison.Ordinal))
			{
				pushUrls[name] = remainder[..^PushSuffix.Length];
			}
			else
			{
				throw new GitParseException($"Malformed 'remote -v' line: '{record}'.");
			}
		}

		List<GitRemote> remotes = [];

		foreach (string name in order)
		{
			_ = fetchUrls.TryGetValue(name, out string? fetchUrl);
			_ = pushUrls.TryGetValue(name, out string? pushUrl);

			// git always prints both directions, but a remote configured with only one is still
			// better described by the URL that exists than rejected outright.
			fetchUrl ??= pushUrl;
			pushUrl ??= fetchUrl;

			if (fetchUrl is null || pushUrl is null)
			{
				throw new GitParseException($"git listed the remote '{name}' with no URL.");
			}

			remotes.Add(new GitRemote
			{
				Name = GitParseValues.ToSemantic<GitRemoteName>(name, "remote name"),
				FetchUrl = GitParseValues.ToSemantic<GitRepositoryRemotePath>(fetchUrl, "remote fetch url"),
				PushUrl = GitParseValues.ToSemantic<GitRepositoryRemotePath>(pushUrl, "remote push url"),
			});
		}

		return remotes;
	}
}
```

- [ ] **Step 4: Run the parser tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteParserTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Write the failing builder test**

`GitIntegration.Test/Builders/GitRemoteListBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitRemoteListBuilderTests
{
	[TestMethod]
	public void BuildsTheRemoteListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"remote",
			"-v",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteParsesTheRemotesAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput =
				"origin\thttps://example.com/repo.git (fetch)\n" +
				"origin\thttps://example.com/repo.git (push)\n",
		};
		GitRemoteListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitRemote> remotes =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, remotes.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteListBuilderTests"`

Expected: compilation failure — `GitRemoteListBuilder` does not exist.

- [ ] **Step 7: Write the builder**

`GitIntegration/Builders/GitRemoteListBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the configured remotes.
/// </summary>
public interface IGitRemoteListBuilder : IGitCommandBuilder<IReadOnlyList<GitRemote>>
{
}

/// <summary>
/// Builds <c>git remote -v</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitRemoteListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitRemote>>(runner, repositoryPath), IGitRemoteListBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("-v");
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitRemote> ParseResult(GitProcessResult result) =>
		GitRemoteParser.Parse(Ensure.NotNull(result).StandardOutput);
}
```

- [ ] **Step 8: Run the builder tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRemoteListBuilderTests"`

Expected: PASS, 2 tests.

- [ ] **Step 9: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 10: Commit**

```bash
git add GitIntegration/Parsing/GitRemoteParser.cs GitIntegration/Builders/GitRemoteListBuilder.cs GitIntegration.Test/Parsing/GitRemoteParserTests.cs GitIntegration.Test/Builders/GitRemoteListBuilderTests.cs
git commit -m "[minor] Add the remote listing verb"
```

---

## Task 11: `GitRevParseBuilder` and `GitTextBuilder`

Two builders that need no parser of their own: one resolves a revision to an object id, the other returns git's trimmed standard output verbatim and exists so `GitClient` can run its three fixed probe commands without three near-identical classes.

**Files:**
- Create: `GitIntegration/Builders/GitRevParseBuilder.cs`
- Create: `GitIntegration/Builders/GitTextBuilder.cs`
- Test: `GitIntegration.Test/Builders/GitRevParseBuilderTests.cs`

**Interfaces:**
- Consumes: `GitParseValues` (Task 1); `GitRefName`, `GitCommitSha`, `GitCommandBuilder<TResult>.AppendOperands`; `TestPaths.Root`.
- Produces:
  - `public interface IGitRevParseBuilder : IGitCommandBuilder<GitCommitSha>` (no members of its own).
  - `internal sealed class GitRevParseBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitRefName revision)`.
  - `internal sealed class GitTextBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath, params string[] verbArguments) : GitCommandBuilder<string>` — `ParseResult` returns `result.StandardOutput.Trim()`.

- [ ] **Step 1: Write the failing tests**

`GitIntegration.Test/Builders/GitRevParseBuilderTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRevParseBuilderTests
{
	private const string HeadSha = "9429d2063d91f1097de51a196cb8203b06335738";

	[TestMethod]
	public void BuildsTheRevParseVectorWithTheRevisionBehindTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-parse",
			"--verify",
			"--end-of-options",
			"HEAD",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteReturnsTheResolvedObjectIdAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = HeadSha + "\n" };
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		GitCommitSha sha = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HeadSha.As<GitCommitSha>(), sha);
	}

	[TestMethod]
	public async Task TryExecuteReportsAnUnknownRevisionAsAFailureAsync()
	{
		// Captured from git 2.50: an unresolvable revision exits 128 with this message. Callers
		// probing for a revision's existence should get a result, not an exception.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: Needed a single revision\n",
		};
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "nope".As<GitRefName>());

		GitResult<GitCommitSha> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ExecuteReportsOutputItCannotResolveAsAParseFailureAsync()
	{
		// git exiting zero with something that is not an object id is a parse problem, not a
		// command problem, so it must not masquerade as GitCommandException.
		RecordingGitProcessRunner runner = new() { StandardOutput = "not-a-sha\n" };
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void BuildsAFixedTextVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitTextBuilder builder = new(runner, null, "rev-parse", "--show-toplevel");

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-parse",
			"--show-toplevel",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task TextBuilderTrimsTheOutputAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "  true \n" };
		GitTextBuilder builder = new(runner, TestPaths.Root, "rev-parse", "--is-inside-work-tree");

		string value = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("true", value);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRevParseBuilderTests"`

Expected: compilation failure — `GitRevParseBuilder` and `GitTextBuilder` do not exist.

- [ ] **Step 3: Write `GitTextBuilder`**

`GitIntegration/Builders/GitTextBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Runs a fixed argument vector and returns git's trimmed standard output.
/// </summary>
/// <remarks>
/// Used by <see cref="GitClient"/> for the single-value probes — <c>rev-parse --show-toplevel</c>,
/// <c>rev-parse --is-inside-work-tree</c>, and <c>remote get-url origin</c> — that need no options
/// and produce one line. Internal, and deliberately so: it accepts an arbitrary vector, which is
/// safe only because every caller of it is inside this assembly and passes literals.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">
/// The repository to scope the command to, or <see langword="null"/> for a command that is not
/// repository-scoped.
/// </param>
/// <param name="verbArguments">The verb and its options, in order.</param>
internal sealed class GitTextBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath? repositoryPath,
	params string[] verbArguments)
	: GitCommandBuilder<string>(runner, repositoryPath)
{
	private readonly string[] _verbArguments = Ensure.NotNull(verbArguments);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		foreach (string argument in _verbArguments)
		{
			arguments.Add(argument);
		}
	}

	/// <inheritdoc />
	protected override string ParseResult(GitProcessResult result) =>
		Ensure.NotNull(result).StandardOutput.Trim();
}
```

- [ ] **Step 4: Write `GitRevParseBuilder`**

`GitIntegration/Builders/GitRevParseBuilder.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Resolves a revision to the object id it names.
/// </summary>
public interface IGitRevParseBuilder : IGitCommandBuilder<GitCommitSha>
{
}

/// <summary>
/// Builds <c>git rev-parse --verify</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="revision">The revision to resolve.</param>
internal sealed class GitRevParseBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName revision)
	: GitCommandBuilder<GitCommitSha>(runner, repositoryPath), IGitRevParseBuilder
{
	private readonly GitRefName _revision = Ensure.NotNull(revision);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("rev-parse");

		// --verify makes git fail on an unresolvable revision instead of echoing the input back,
		// which is what lets a non-zero exit code stand for "no such revision".
		arguments.Add("--verify");

		// The revision is caller-supplied, so it goes behind the end-of-options marker.
		AppendOperands(arguments, _revision.WeakString);
	}

	/// <inheritdoc />
	protected override GitCommitSha ParseResult(GitProcessResult result) =>
		GitParseValues.ToSemantic<GitCommitSha>(
			Ensure.NotNull(result).StandardOutput.Trim(),
			"resolved object id");
}
```

`CreateException` is deliberately **not** overridden. The design document cites rev-parse as a case a derived builder *could* specialise, but the exception hierarchy it fixes has no revision-specific type, and `TryExecuteAsync` already gives callers a non-throwing way to probe for a revision. Adding a type the spec does not define is a Phase 4 decision at the earliest.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRevParseBuilderTests"`

Expected: PASS, 6 tests.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors, all tests passing.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Builders/GitRevParseBuilder.cs GitIntegration/Builders/GitTextBuilder.cs GitIntegration.Test/Builders/GitRevParseBuilderTests.cs
git commit -m "[minor] Add the rev-parse verb and the fixed-vector text builder"
```

---
## Task 12: `IGitClient`, `GitClient`, `GitRepository` verbs, and DI

The last task wires everything into the surface a consumer actually touches.

**Files:**
- Create: `GitIntegration.Test/Fakes/ScriptedGitProcessRunner.cs`
- Create: `GitIntegration/IGitClient.cs`
- Create: `GitIntegration/GitClient.cs`
- Modify: `GitIntegration/GitRepository.cs`
- Modify: `GitIntegration/ServiceCollectionExtensions.cs`
- Test: `GitIntegration.Test/GitClientTests.cs`
- Test: `GitIntegration.Test/GitRepositoryVerbTests.cs`
- Test: `GitIntegration.Test/ServiceCollectionExtensionsTests.cs` (add two methods)

**Interfaces:**
- Consumes: every builder from Tasks 2, 4, 6, 8, 9, 10, 11; `GitVersion`, `GitParseException` (Task 1); `GitRepositoryNotFoundException`, `GitResult<T>` (existing).
- Produces:
  - `public interface IGitClient` — `GetVersionAsync`, `IsRepositoryAsync`, `OpenAsync`, `DiscoverAsync`.
  - `public sealed class GitClient(IGitProcessRunner runner) : IGitClient`.
  - `GitRepository.ProcessRunner { get; init; }` (nullable), `IsClonedAsync`, `Status()`, `Log()`, `Diff()`, `RevParse(GitRefName)`, `Branches()`, `Remotes()`.
  - `internal sealed class ScriptedGitProcessRunner : IGitProcessRunner` — `Then(string standardOutput, string standardError, int exitCode)`, `Invocations`.

### The `ProcessRunner` decision

`GitRepository` is produced two ways and only one of them can run git. `IGitClient.OpenAsync` returns a repository backed by a runner; a Phase 5 hosting provider returns one carrying `Name`, `WebURI`, and `RemotePath` for a repository that may not exist on disk yet. The runner is therefore `public IGitProcessRunner? ProcessRunner { get; init; }` — nullable for the same reason the metadata is, and public so an advanced consumer can construct a repository against a custom runner without a factory. Every verb factory goes through a private `RequireRunner()` that throws `InvalidOperationException` naming `IGitClient.OpenAsync` when it is absent.

It cannot simply be `required`: `GitRepositoryMetadataTests` and the Phase 5 providers both construct metadata-only repositories, and forcing a runner on them would mean inventing a null-object runner whose every method throws — the same failure, further from its cause.

- [ ] **Step 1: Write the scripted fake**

`GitIntegration.Test/Fakes/ScriptedGitProcessRunner.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Replays a queued sequence of results and records every argument vector it was given.
/// </summary>
/// <remarks>
/// <see cref="RecordingGitProcessRunner"/> replays one canned result, which is enough for a single
/// builder but not for <see cref="GitClient"/>: discovery runs <c>rev-parse --show-toplevel</c> and
/// then <c>remote get-url origin</c>, and the two need different answers.
/// </remarks>
internal sealed class ScriptedGitProcessRunner : IGitProcessRunner
{
	private readonly Queue<GitProcessResult> _responses = new();

	/// <summary>Gets every argument vector this runner was asked to run, in order.</summary>
	public List<IReadOnlyList<string>> Invocations { get; } = [];

	/// <summary>Queues the next result this runner will return.</summary>
	/// <param name="standardOutput">What git writes to standard output.</param>
	/// <param name="standardError">What git writes to standard error.</param>
	/// <param name="exitCode">The code git exits with.</param>
	/// <returns>The same runner, to allow chaining.</returns>
	public ScriptedGitProcessRunner Then(string standardOutput = "", string standardError = "", int exitCode = 0)
	{
		_responses.Enqueue(new GitProcessResult
		{
			ExitCode = exitCode,
			StandardOutput = standardOutput,
			StandardError = standardError,
			Arguments = [],
		});

		return this;
	}

	public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
	{
		// ArgumentNullException.ThrowIfNull rather than Ensure.NotNull: the library takes Polyfill
		// with PrivateAssets="all", so Ensure is not visible to the test project.
		ArgumentNullException.ThrowIfNull(request);

		Invocations.Add([.. request.Arguments]);

		// Running out of queued responses means the code under test issued a command the test did
		// not anticipate. Failing here names that command; returning a default would hide it.
		if (_responses.Count == 0)
		{
			throw new InvalidOperationException(
				$"No queued result for: git {string.Join(' ', request.Arguments)}");
		}

		GitProcessResult queued = _responses.Dequeue();

		return Task.FromResult(queued with { Arguments = [.. request.Arguments] });
	}
}
```

- [ ] **Step 2: Write the failing client tests**

`GitIntegration.Test/GitClientTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitClientTests
{
	private const string NotARepository =
		"fatal: not a git repository (or any of the parent directories): .git\n";

	private static string TopLevel =>
		OperatingSystem.IsWindows() ? "C:/dev/fixture-repo" : "/dev/fixture-repo";

	private static AbsoluteDirectoryPath ExpectedTopLevel => TopLevel.As<AbsoluteDirectoryPath>();

	[TestMethod]
	public async Task GetVersionRunsTheVersionCommandAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1.windows.1\n");
		GitClient client = new(runner);

		GitVersion version = await client.GetVersionAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--version");
	}

	[TestMethod]
	public async Task IsRepositoryReportsTrueWhenGitSaysSoAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner().Then(standardOutput: "true\n");
		GitClient client = new(runner);

		bool isRepository = await client
			.IsRepositoryAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(isRepository);

		string[] arguments = runner.Invocations[0].ToArray();
		CollectionAssert.Contains(arguments, "--is-inside-work-tree");
		CollectionAssert.Contains(arguments, "-C");
		CollectionAssert.Contains(arguments, TestPaths.Root.WeakString);
	}

	[TestMethod]
	public async Task IsRepositoryReportsFalseWithoutThrowingWhenGitFailsAsync()
	{
		// Both "there is no repository here" and "that directory does not exist" exit 128, and
		// neither is an error from this method's point of view — the answer is simply no.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		bool isRepository = await client
			.IsRepositoryAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(isRepository);
	}

	[TestMethod]
	public async Task DiscoverResolvesTheWorkingTreeRootAndBackFillsTheOriginRemoteAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardOutput: "https://github.com/ktsu-dev/GitIntegration.git\n");
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(repository);
		Assert.AreEqual(ExpectedTopLevel, repository.LocalPath);
		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			repository.RemotePath);
		Assert.IsNotNull(repository.ProcessRunner);

		// git's own upward walk is what makes DiscoverAsync work, which is why this phase needs no
		// filesystem abstraction: --show-toplevel from a subdirectory returns the root.
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--show-toplevel");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "get-url");
	}

	[TestMethod]
	public async Task DiscoverLeavesTheRemotePathNullWhenThereIsNoOriginAsync()
	{
		// "error: No such remote 'origin'" exits 2, not 128, and is not a discovery failure.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardError: "error: No such remote 'origin'\n", exitCode: 2);
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(repository);
		Assert.IsNull(repository.RemotePath);
	}

	[TestMethod]
	public async Task DiscoverReturnsNullWhenThereIsNoRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(repository);
	}

	[TestMethod]
	public async Task OpenThrowsRepositoryNotFoundWhereDiscoverWouldReturnNullAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		await Assert.ThrowsExactlyAsync<GitRepositoryNotFoundException>(
			async () => await client
				.OpenAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task OpenReturnsARepositoryThatCanRunVerbsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardError: "error: No such remote 'origin'\n", exitCode: 2);
		GitClient client = new(runner);

		GitRepository repository = await client
			.OpenAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// The verb is scoped to the discovered root, not to the path that was opened, which is the
		// point of resolving --show-toplevel first.
		CollectionAssert.Contains(repository.Status().BuildArguments().ToArray(), ExpectedTopLevel.WeakString);
	}

	[TestMethod]
	public async Task DiscoverReportsAnUnusableTopLevelAsAParseFailureAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "not-an-absolute-path\n");
		GitClient client = new(runner);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await client
				.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsANullRunnerOrPath()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new GitClient(null!));

		ScriptedGitProcessRunner runner = new();
		GitClient client = new(runner);

		// Discarded rather than returned: a lambda whose body yields a value binds to the
		// Func<object?> overload, which awaits nothing, so the ArgumentNullException raised before
		// the first await would go unobserved. The discard makes it an Action.
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.IsRepositoryAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.OpenAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.DiscoverAsync(null!));
	}

	public TestContext TestContext { get; set; } = null!;
}
```

Note on the last test: the null checks must be reached **before** the first `await`, or the exception surfaces only when the returned task is awaited and `Assert.ThrowsExactly` will not see it. That is why the implementation below validates in a non-async wrapper.

- [ ] **Step 3: Run the client tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitClientTests"`

Expected: compilation failure — `GitClient` and `ScriptedGitProcessRunner` do not exist.

- [ ] **Step 4: Write `IGitClient`**

`GitIntegration/IGitClient.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The entry point to the local layer: finds and opens repositories, and reports on the git binary.
/// </summary>
/// <remarks>
/// Phase 4 adds <c>Init</c> and <c>Clone</c> to this interface. It carries only the read-only
/// operations for now.
/// </remarks>
public interface IGitClient
{
	/// <summary>Reports the version of the git binary being invoked.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed version.</returns>
	public Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Decides whether a path is inside a git working tree.
	/// </summary>
	/// <remarks>
	/// Never throws for a path that is not a repository, or for a path that does not exist. Both
	/// answer the same question the same way.
	/// </remarks>
	/// <param name="path">The path to test.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when the path is inside a working tree.</returns>
	public Task<bool> IsRepositoryAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the repository containing a path.
	/// </summary>
	/// <remarks>
	/// The returned repository's <see cref="GitRepository.LocalPath"/> is the working tree root, not
	/// necessarily <paramref name="path"/>, and its
	/// <see cref="GitRepository.RemotePath"/> is back-filled from <c>origin</c> when one is
	/// configured.
	/// </remarks>
	/// <param name="path">A path inside the repository.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The opened repository.</returns>
	/// <exception cref="GitRepositoryNotFoundException">The path is not inside a working tree.</exception>
	public Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the repository containing a path, reporting absence as a result rather than an
	/// exception.
	/// </summary>
	/// <param name="startingPath">A path inside, or below, the repository.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The opened repository, or <see langword="null"/> when there is none.</returns>
	public Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Write `GitClient`**

`GitIntegration/GitClient.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The shipped <see cref="IGitClient"/>, running everything through an
/// <see cref="IGitProcessRunner"/>.
/// </summary>
/// <remarks>
/// Repository discovery is delegated to <c>git rev-parse --show-toplevel</c>, which performs the
/// upward walk itself. That is why this type takes no filesystem abstraction: there is nothing to
/// walk that git does not already walk, and asking git keeps the answer consistent with what every
/// subsequent verb will see. Phase 4's <c>Init</c> and <c>Clone</c> do need one, because they act on
/// a destination where no repository exists yet to be asked.
/// </remarks>
/// <param name="runner">Runs every command this client issues.</param>
public sealed class GitClient(IGitProcessRunner runner) : IGitClient
{
	private readonly IGitProcessRunner _runner = Ensure.NotNull(runner);

	/// <inheritdoc />
	public Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default) =>
		new GitVersionBuilder(_runner).ExecuteAsync(cancellationToken);

	/// <inheritdoc />
	public Task<bool> IsRepositoryAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default)
	{
		// Validated here rather than in the async body so that the exception is thrown by the call
		// itself, not deferred until the returned task is awaited.
		Ensure.NotNull(path);

		return IsRepositoryCoreAsync(path, cancellationToken);
	}

	/// <inheritdoc />
	public Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(path);

		return OpenCoreAsync(path, cancellationToken);
	}

	/// <inheritdoc />
	public Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(startingPath);

		return DiscoverCoreAsync(startingPath, cancellationToken);
	}

	private async Task<bool> IsRepositoryCoreAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken)
	{
		GitResult<string> result = await new GitTextBuilder(_runner, path, "rev-parse", "--is-inside-work-tree")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		// TryExecuteAsync rather than ExecuteAsync: "not a git repository" and "cannot change to
		// that directory" both exit 128, and both mean no rather than failure.
		return result.Success && string.Equals(result.Value, "true", StringComparison.Ordinal);
	}

	private async Task<GitRepository> OpenCoreAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken)
	{
		GitRepository? repository = await DiscoverCoreAsync(path, cancellationToken).ConfigureAwait(false);

		return repository ?? throw new GitRepositoryNotFoundException(
			$"'{path.WeakString}' is not inside a git working tree.");
	}

	private async Task<GitRepository?> DiscoverCoreAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken)
	{
		GitResult<string> topLevel = await new GitTextBuilder(_runner, startingPath, "rev-parse", "--show-toplevel")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		if (!topLevel.Success || string.IsNullOrEmpty(topLevel.Value))
		{
			return null;
		}

		// git prints the working tree root with forward slashes on every platform;
		// AbsoluteDirectoryPath canonicalises to the host separator.
		if (!AbsoluteDirectoryPath.TryCreate(topLevel.Value, out AbsoluteDirectoryPath? localPath) ||
			localPath is null)
		{
			throw new GitParseException(
				$"git reported a working tree root that is not an absolute directory path: '{topLevel.Value}'.");
		}

		return new GitRepository
		{
			LocalPath = localPath,
			RemotePath = await ReadOriginUrlAsync(localPath, cancellationToken).ConfigureAwait(false),
			ProcessRunner = _runner,
		};
	}

	private async Task<GitRepositoryRemotePath?> ReadOriginUrlAsync(
		AbsoluteDirectoryPath localPath,
		CancellationToken cancellationToken)
	{
		GitResult<string> originUrl = await new GitTextBuilder(_runner, localPath, "remote", "get-url", "origin")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A repository with no origin exits 2 here. That is an ordinary state, not a failure, so
		// the metadata stays null and discovery still succeeds.
		return originUrl.Success &&
			!string.IsNullOrEmpty(originUrl.Value) &&
			GitRepositoryRemotePath.TryCreate(originUrl.Value, out GitRepositoryRemotePath? remotePath)
				? remotePath
				: null;
	}
}
```

- [ ] **Step 6: Run the client tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitClientTests"`

Expected: PASS, 10 tests.

- [ ] **Step 7: Write the failing repository verb tests**

`GitIntegration.Test/GitRepositoryVerbTests.cs`:

```csharp
// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	[TestMethod]
	public void EveryVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Status().BuildArguments()],
			[.. repository.Log().BuildArguments()],
			[.. repository.Diff().BuildArguments()],
			[.. repository.Branches().BuildArguments()],
			[.. repository.Remotes().BuildArguments()],
			[.. repository.RevParse("HEAD".As<GitRefName>()).BuildArguments()],
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
		// Builders are mutable and single-use, so handing the same instance back would let one
		// caller's options leak into another's command.
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Status(), repository.Status());
		Assert.AreNotSame(repository.Log(), repository.Log());
	}

	[TestMethod]
	public void VerbsOnAMetadataOnlyRepositoryExplainWhatIsMissing()
	{
		// A repository produced by a hosting provider carries metadata for something that may not
		// exist on disk yet, so it has no runner.
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			Name = "GitIntegration".As<GitRepositoryName>(),
		};

		InvalidOperationException exception =
			Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Status());

		StringAssert.Contains(exception.Message, nameof(IGitClient.OpenAsync));
	}

	[TestMethod]
	public void RevParseRejectsANullRevision()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.RevParse(null!));
	}

	[TestMethod]
	public async Task IsClonedReportsTrueWhenGitFindsAWorkingTreeAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner().Then(standardOutput: "true\n");
		GitRepository repository = RepositoryOn(runner);

		bool isCloned = await repository.IsClonedAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(isCloned);
	}

	[TestMethod]
	public async Task IsClonedReportsFalseForAPathThatIsNotAWorkingTreeAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository (or any of the parent directories): .git\n", exitCode: 128);
		GitRepository repository = RepositoryOn(runner);

		bool isCloned = await repository.IsClonedAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(isCloned);
	}

	public TestContext TestContext { get; set; } = null!;
}
```

- [ ] **Step 8: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryVerbTests"`

Expected: compilation failure — `GitRepository` has no `ProcessRunner` or verb methods.

- [ ] **Step 9: Extend `GitRepository`**

Add to `GitIntegration/GitRepository.cs`. The existing `using` block gains `System.Threading`, `System.Threading.Tasks`, and `System.Diagnostics.CodeAnalysis` is already there. Insert the new members after the `RemotePath` property and before `OpenWebClient`:

```csharp
	/// <summary>
	/// Gets the runner this repository's verbs execute through, or <see langword="null"/> when this
	/// value carries hosting metadata only.
	/// </summary>
	/// <remarks>
	/// Nullable for the same reason the metadata is. A repository produced by
	/// <see cref="IGitClient.OpenAsync"/> or <see cref="IGitClient.DiscoverAsync"/> has one; a
	/// repository produced by a hosting provider describes something that may not exist on disk
	/// yet and has none. Calling a verb without one throws
	/// <see cref="InvalidOperationException"/> rather than failing later inside git.
	/// </remarks>
	public IGitProcessRunner? ProcessRunner { get; init; }

	/// <summary>
	/// Decides whether <see cref="LocalPath"/> currently holds a git working tree.
	/// </summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when the path is inside a working tree.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public async Task<bool> IsClonedAsync(CancellationToken cancellationToken = default)
	{
		GitResult<string> result = await new GitTextBuilder(
			RequireRunner(), LocalPath, "rev-parse", "--is-inside-work-tree")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return result.Success && string.Equals(result.Value, "true", StringComparison.Ordinal);
	}

	/// <summary>Reports the working tree and index state.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitStatusBuilder Status() => new GitStatusBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists commits.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitLogBuilder Log() => new GitLogBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists the paths that differ between two states of the repository.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitDiffBuilder Diff() => new GitDiffBuilder(RequireRunner(), LocalPath);

	/// <summary>Resolves a revision to the object id it names.</summary>
	/// <param name="revision">The revision to resolve.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRevParseBuilder RevParse(GitRefName revision) =>
		new GitRevParseBuilder(RequireRunner(), LocalPath, Ensure.NotNull(revision));

	/// <summary>Lists branch references.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchListBuilder Branches() => new GitBranchListBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists the configured remotes.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteListBuilder Remotes() => new GitRemoteListBuilder(RequireRunner(), LocalPath);

	private IGitProcessRunner RequireRunner() =>
		ProcessRunner ?? throw new InvalidOperationException(
			"This GitRepository carries hosting metadata only and has no process runner. Obtain one " +
			$"from {nameof(IGitClient)}.{nameof(IGitClient.OpenAsync)} or " +
			$"{nameof(IGitClient)}.{nameof(IGitClient.DiscoverAsync)} before running git commands against it.");
```

- [ ] **Step 10: Run the repository verb tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitRepositoryVerbTests"`

Expected: PASS, 6 tests.

- [ ] **Step 11: Write the failing DI tests**

Add to `GitIntegration.Test/ServiceCollectionExtensionsTests.cs`:

```csharp
	[TestMethod]
	public void RegistersTheGitClientByBothConcreteTypeAndInterface()
	{
		ServiceCollection services = new();
		_ = services.AddGitIntegration();

		using ServiceProvider provider = services.BuildServiceProvider();

		GitClient concrete = provider.GetRequiredService<GitClient>();
		IGitClient asInterface = provider.GetRequiredService<IGitClient>();

		// One singleton reached two ways, not two instances: a second client would mean two
		// independent runners once Phase 5 gives the client per-instance state.
		Assert.AreSame(concrete, asInterface);
	}

	[TestMethod]
	public void TheRegisteredClientUsesTheConfiguredExecutablePath()
	{
		ServiceCollection services = new();
		_ = services.AddGitIntegration(options => options.ExecutablePath = "/usr/local/bin/git");

		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.AreEqual("/usr/local/bin/git", provider.GetRequiredService<GitOptions>().ExecutablePath);
		Assert.IsNotNull(provider.GetRequiredService<IGitClient>());
	}
```

- [ ] **Step 12: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`

Expected: FAIL — no service registered for `GitClient`.

- [ ] **Step 13: Register the client**

In `GitIntegration/ServiceCollectionExtensions.cs`, inside `AddGitIntegration(this IServiceCollection, Action<GitOptions>)`, after the `IGitProcessRunner` registration and before `AddNativeFileSystemProvider`:

```csharp
		// Registered by concrete type first and then projected onto the interface, so both
		// resolutions return the same singleton rather than two independently-constructed clients.
		services.TryAddSingleton<GitClient>();
		services.TryAddSingleton<IGitClient>(static provider => provider.GetRequiredService<GitClient>());
```

- [ ] **Step 14: Run the DI tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ServiceCollectionExtensionsTests"`

Expected: PASS.

- [ ] **Step 15: Build and run every test**

Run: `dotnet build && dotnet test`

Expected: 0 warnings, 0 errors. Every pre-existing test plus roughly 100 new ones passing.

- [ ] **Step 16: Verify no suppressions were introduced**

Run: `git grep -n "SuppressMessage" -- "*.cs" "*.csproj" "*.props"`

Expected: no output. If anything is listed, fix the underlying analyzer complaint instead.

- [ ] **Step 17: Commit**

```bash
git add GitIntegration/IGitClient.cs GitIntegration/GitClient.cs GitIntegration/GitRepository.cs GitIntegration/ServiceCollectionExtensions.cs GitIntegration.Test/Fakes/ScriptedGitProcessRunner.cs GitIntegration.Test/GitClientTests.cs GitIntegration.Test/GitRepositoryVerbTests.cs GitIntegration.Test/ServiceCollectionExtensionsTests.cs
git commit -m "[minor] Add IGitClient, GitClient, and the read-only repository verbs"
```

---

## Verification against a real git binary

Tasks 1–12 need no git binary; the whole suite runs against fixtures and fakes. Before calling the phase done, confirm the fixtures still describe the installed git. This is a one-off manual check, not a committed test — real-git integration tests are Phase 4's deliverable, where `init`, `add`, and `commit` exist to build a repository from scratch.

- [ ] **Step 1: Recreate the fixture repository**

```bash
set -eu
export LC_ALL=C
R="$(mktemp -d)/fixture-repo"
mkdir -p "$R"
git init -q -b main "$R"
git -C "$R" config user.name "Fixture Author"
git -C "$R" config user.email fixture@example.com
echo one > "$R/a.txt"
mkdir -p "$R/dir with spaces"
echo two > "$R/dir with spaces/file.txt"
git -C "$R" add -A
git -C "$R" commit -q -m "Initial commit" -m "A body line."
git -C "$R" mv a.txt renamed.txt
echo three >> "$R/renamed.txt"
echo four > "$R/untracked.txt"
echo "$R"
```

- [ ] **Step 2: Compare each capture against its fixture**

```bash
git -C "$R" --no-pager -c core.quotepath=false -c color.ui=false status --porcelain=v2 --branch -z | xxd | head -20
git -C "$R" --no-pager log -z --format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b | xxd | head -20
git -C "$R" --no-pager -c core.quotepath=false diff --name-status -z --find-renames | xxd
git -C "$R" --no-pager for-each-ref --format='%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)' refs/heads refs/remotes
git -C "$R" --no-pager remote -v
git -C "$R" --no-pager rev-parse --verify --end-of-options HEAD
git --no-pager --version
```

Expected: the byte-level shapes match the "Findings" section — a `2` record followed by a separate NUL-terminated original path, NUL-terminated log records, three tokens for a rename in the diff, `*` versus a single space for `%(HEAD)`.

If any shape differs, the installed git changed the format. Update the fixture **and** the parser together, and record the git version that produced the new shape in the fixture's comment.

- [ ] **Step 3: Clean up**

```bash
rm -rf "$(dirname "$R")"
```

---

## What this phase deliberately does not do

Recorded so a reviewer does not read these as omissions.

- **No `IFileSystemProvider` use.** `git rev-parse --show-toplevel` performs the upward walk the spec describes, so there is nothing left for a filesystem abstraction to do here. Phase 4's `Init` and `Clone` act on destinations where no repository exists to be asked, and will use it.
- **No revision-specific exception type.** `rev-parse` failures surface as `GitCommandException`, or as a `GitResult<T>` failure through `TryExecuteAsync`. The spec's exception hierarchy defines no such type, and inventing one is at earliest a Phase 4 decision.
- **No integration tests against a real git binary.** They belong with Phase 4, which adds the mutating verbs needed to build a repository. The manual verification section above covers the fixtures in the meantime.
- **No `Init`, `Clone`, or mutating verbs on `IGitClient`.** Phase 4.
- **No `Fetch`, `Pull`, `Push`, or hosting providers.** Phase 5.
- **`GitVersion.AtLeast` is unused within this phase.** It exists now because Phase 5's `fetch --porcelain` fallback is gated on git ≥ 2.41, and because a version type with no comparison is not much of a version type. It is tested here rather than left untested until it has a caller.

## Parked items from the Phase 1–2 review

The spec lists five. Two overlap this phase and are addressed; three do not.

- [ ] **`GitProcessRequest.Progress` thread-safety.** The requirement is in the spec but not on the property's XML doc. Add a `<remarks>` to `GitIntegration/Execution/GitProcessRequest.cs` stating that the sink may be entered concurrently by the stdout and stderr readers and must therefore be thread-safe. Documentation only; commit on its own before Task 1:

```bash
git add GitIntegration/Execution/GitProcessRequest.cs
git commit -m "[patch] Document that the progress sink must be thread-safe"
```
- [ ] **`RunCommandGitProcessRunner`'s cancellation doc.** `IGitCommandBuilder<TResult>` and the runner still promise `OperationCanceledException` whenever the caller's token is signalled, but the implementation deliberately returns the result when git exited 0 first — a zero exit code proves git finished on its own, and discarding a valid result would be wrong. The behaviour is right and the promise is stale. Correct the `<exception>` documentation to say that cancellation is reported only when git did not complete. Documentation only; commit on its own before Task 1:

```bash
git add GitIntegration/Execution/RunCommandGitProcessRunner.cs GitIntegration/Builders/IGitCommandBuilder.cs
git commit -m "[patch] Correct the documented cancellation contract"
```

Not addressed here, because nothing in this phase touches them:

- `OpenWebClient` needs an injectable launch seam. It is Phase 5 that populates `WebURI` from provider JSON, so the seam belongs with that work.
- A throwing `IProgress<string>` faults the whole invocation. No read-only verb supplies a progress sink; `Fetch` and `Push` in Phase 5 are the first that will.
- `SemanticTypesAreDistinctAtCompileTime` is a weak test. Unchanged by this phase.

## Self-review

Checked against the spec's Phase 3 scope after writing.

**Spec coverage.** Every row of the spec's output-parsing table that belongs to Phase 3 has a task: `Status` (3, 4), `Log` (5, 6), `Diff` (7, 8), `Branches` (9), `Remotes` (10), `RevParse` (11). `Push`, `Fetch`, and `Commit` are Phases 4–5. Every result model in the spec's "Result models" section is created in Task 1, including `GitVersion`, which the spec lists as following the same shape. `IGitClient` and `GitRepository`'s read-only half land in Task 12. `Push`/`Fetch` result models are not created, since their verbs are Phase 5 and a model with no producer is dead code.

**Two deliberate divergences from the spec, both argued above:** the `for-each-ref` format gains a leading `%(refname)` (finding 4), and discovery uses `rev-parse` instead of an `IFileSystemProvider` walk (finding 7).

**One documented limitation:** a path git permits but `RelativeFilePath` refuses — a newline on Windows — raises `GitParseException` rather than being dropped (finding 9).

**Type consistency.** `GitParseValues.ToSemantic` / `ToRelativeFilePath`, `GitOutputFormats.UnitSeparator` / `LogFormat` / `ForEachRefFormat`, and every builder constructor signature `(IGitProcessRunner, AbsoluteDirectoryPath)` are used identically everywhere they appear. `GitTextBuilder` alone takes `AbsoluteDirectoryPath?`, because `GitClient` needs it both scoped and unscoped.







# Patch verbs: reading hunks, applying them, and unstaging

Status: approved design, not yet implemented.

## Why this exists

A caller that wants to show a diff, or stage part of a file, cannot do either through this library
today.

`Diff()` answers "which files changed, and by how many lines". It runs `--name-status`, or
`--raw --numstat` when line counts are asked for, and parses one `GitDiffEntry` per file carrying a
path, a change kind and two counts. There is no patch text and no hunks, so there is nothing to draw
and nothing to slice.

There is no `apply`, so no patch can reach the index. There is no `reset` or `restore`, so nothing
that has been staged can be unstaged. A caller that needs any of this has to shell out to git itself,
which is the thing this library exists to prevent.

The immediate consumer is a launcher's source control tab wanting hunk-level staging, but nothing
here is specific to it. These are three git verbs this library does not yet wrap.

## Scope

In scope:

- A patch reader: `Patch()`, returning files, their hunks, and each hunk's lines.
- Assembling a chosen subset of hunks into text `git apply` accepts.
- `Apply()`, with the options that make staging and unstaging a hunk possible.
- `Unstage()`, for the whole-file case.

Out of scope, deliberately:

- Computing a diff in managed code. Git produces the patch, and this library reads it.
- Conflict resolution, three-way application, and merge tooling.
- Binary patch application. Binary files are reported and staged whole-file.
- A staging facade. `Stage(hunk)` is a wrapper a caller can write in a few lines over `Apply`, and
  putting it here would mean this library deciding what staging means, which nothing else in it does.

## The patch model

```
GitPatch          Files
GitFilePatch      Path, OriginalPath, Kind, IsBinary, IsConflicted, Header, Hunks
GitHunk           OldStart, OldCount, NewStart, NewCount, Heading, Lines, Text
GitPatchLine      Kind, Text, OldNumber, NewNumber
```

`Kind` on the file reuses the existing `GitChangeKind`. Only one new enum is needed,
`GitPatchLineKind`, with `Context`, `Added` and `Removed`.

`Lines` is the structured form, for a caller drawing the diff. `Text` is the hunk exactly as git
emitted it, and `Header` is the file's header lines. Both are kept because they serve different
jobs, and because one cannot be derived from the other safely.

### Why hunk text is kept verbatim

Regenerating a hunk from its parsed lines loses `\ No newline at end of file`, which is a real line
in git's output and carries no leading space, plus or minus. A patch missing it is rejected on apply
for any file that lacks a trailing newline. Keeping git's own bytes is what makes the round trip
hold, and the alternative is a parser that must reproduce every marker git might emit, forever.

### Assembling a patch from chosen hunks

`GitFilePatch.PatchFor(IEnumerable<GitHunk> hunks)` concatenates the file's header with the text of
the hunks given, in file order, and returns something `apply` accepts.

Selecting a subset is safe because each hunk's `@@` positions it against the original file, which is
what the index still holds. It stops being safe once the index has moved, which is what `Checked()`
below is for.

A hunk from one `GitFilePatch` must not be handed to another's `PatchFor`. The method takes the
hunks it is given and does not verify they came from it, because the check would cost a reference
comparison per hunk to catch a caller error that no reasonable caller makes.

## Reading a patch

`Patch()` returns `GitPatchBuilder`, producing a `GitPatch`.

```
Staged()                  --cached, the patch of what is already staged
WithContext(int lines)    -U<n>
ForPath(path)             -- <path>, repeatable
Against(ref)              one revision
Between(a, b)             two revisions
DetectRenames()           --find-renames
```

The vector always carries `--no-ext-diff`, `--no-textconv` and `--no-color`, and never `-z`.

`--no-ext-diff` and `--no-textconv` are correctness, not tidiness. A repository with a `.gitattributes`
diff driver produces human-readable output in place of a patch, so a hunk from such a file would be
unapplyable. Game repositories commonly configure these for binary asset formats, which means the
feature would work everywhere except the repositories most likely to need it. `--no-color` guards
against a user config setting `color.diff = always`, which would inject escape sequences into text
that is handed back to `apply`.

`-z` is absent because the patch format is line-based. It changes only the name-status framing that
this builder does not use.

## Applying a patch

`Apply(string patchText)` returns `GitApplyBuilder`, producing `GitCompleted`.

```
ToIndex()      --cached
Reversed()     --reverse
Checked()      --check
```

Staging a hunk is `Apply(text).ToIndex()`. Unstaging one is `Apply(text).ToIndex().Reversed()`.

The vector also carries `--whitespace=nowarn`. Git otherwise honours `apply.whitespace`, and a user
who has set it to `error` would find that staging fails on trailing whitespace already present in
their own working tree. Staging existing content is not the moment to enforce a whitespace policy,
and the content reaching the index is identical either way.

### The temporary file

Git reads a patch from standard input or from a file. `GitProcessRequest` carries an argument vector
and a progress sink and has no standard input, and the upstream work it anticipates is about working
directories and environment variables rather than input. So `Apply` writes the patch to a temporary
file, passes that path, and deletes it in a `finally`.

The file is written as UTF-8 with no byte order mark, with the text's line endings preserved exactly.
A patch is byte-sensitive: a rewritten line ending or an inserted mark makes it unapplyable.

That the file exists at all is this builder's business. A caller has no reason to know that standard
input was unavailable, and if it later becomes available this changes without touching the surface.

### Checking before applying

`Checked()` emits `--check`, which reports whether the patch would apply and changes nothing.

It needs no result type: `TryExecuteAsync` already reports failure without throwing, so
`Checked().TryExecuteAsync()` answers "would this still apply?" as `Success`. A caller that checks
before staging turns a worktree that moved underneath into a refusal rather than a half-staged file.

### Errors are git's own

`Apply` is the first verb here that takes caller-supplied content rather than caller-supplied
arguments. There is no option-injection surface, because the patch reaches git through a file path
this library controls, but it does mean a malformed patch fails at runtime and git's message is the
only explanation anyone gets. The failure therefore carries git's standard error verbatim rather than
a summary of it.

## Unstaging

`Unstage(path)` returns `GitRestoreBuilder`, producing `GitCompleted`, and emits
`restore --staged -- <path>`.

`git restore` arrived in 2.23. Rather than declaring a floor, this probes the installed version and
emits `reset HEAD -- <path>` below it, the same shape `Fetch` already uses for `--porcelain`. Two
verbs, one meaning, chosen by what is installed.

This is the whole-file case only. Unstaging a hunk is `Apply(...).ToIndex().Reversed()`, and a caller
wanting to unstage a binary file has nothing else, since a binary file has no hunks.

## What a patch cannot express

Four cases where the model reports rather than pretends:

**Conflicted files.** An unmerged path produces combined format, with `@@@` and two columns, which is
not an applyable patch. The parser recognizes it and sets `IsConflicted`, leaving `Hunks` empty. A
caller stages such a file whole or not at all. Mis-parsing combined hunks into ordinary ones would
produce patches git rejects, with nothing on screen explaining why.

**Binary files.** Git emits either a one-line notice or an encoded blob. `IsBinary` is set and
`Hunks` is empty.

**Renames and mode changes.** Both produce a file entry with headers and no hunks. Nothing special is
needed beyond not assuming every file has them.

**Untracked files.** They never appear in a patch, because `git diff` does not show them. Staging one
is `Add`, and a caller building a staging view has to know that its file list comes from `Status` and
its hunks come from `Patch`, which are different sources that disagree about untracked content.

## Testing

Parser tests run against fixtures captured from a real git, following this repository's existing
practice of capturing output once and treating it as fixed rather than re-deriving it. Fixtures
cover: a modification with two hunks, a file with no trailing newline, a rename with no hunks, a
binary file, a combined conflict hunk, and content with CRLF line endings.

The test that matters most is the round trip, against a real repository in a temporary directory:
generate a patch, take one hunk of a two-hunk file through `PatchFor`, apply it to the index, and
assert the staged and unstaged halves split exactly. A parser that drops a byte passes every unit
test and fails this one.

Beside it, `Checked()` against a working tree that changed after the patch was generated, asserting
it reports failure and leaves the index untouched. That is the guarantee a caller's refusal depends
on.

The version fallback in `Unstage` is tested only if the probe can be driven without a second git
installation. If it cannot, the spec says so rather than asserting against a mock that proves the
mock.

## File layout

| File | Responsibility |
|---|---|
| `GitIntegration/Models/GitPatch.cs` | `GitPatch`, `GitFilePatch`, `GitHunk`, `GitPatchLine`, and `PatchFor` |
| `GitIntegration/Parsing/GitPatchParser.cs` | Unified diff to model |
| `GitIntegration/Builders/GitPatchBuilder.cs` | `Patch()` |
| `GitIntegration/Builders/GitApplyBuilder.cs` | `Apply()`, including the temporary file |
| `GitIntegration/Builders/GitRestoreBuilder.cs` | `Unstage()`, including the version fallback |

Modified: `GitRepository` gains `Patch()`, `Apply()` and `Unstage()`. `GitEnums` gains
`GitPatchLineKind`.

## Implementation order

1. The model and `PatchFor`, with no parser and no git. The assembly rule is the one piece of logic
   here that is pure, and it is worth having tested before anything produces input for it.
2. `GitPatchParser` against the captured fixtures.
3. `GitPatchBuilder`, which is then the first thing that runs git.
4. `GitApplyBuilder`, and with it the round-trip test that proves steps 1 to 3 preserved every byte.
5. `GitRestoreBuilder` and its version fallback.

Steps 1 and 2 need no git at all. Step 4 is where a mistake anywhere earlier becomes visible, which
is why the round trip belongs there rather than at the end.

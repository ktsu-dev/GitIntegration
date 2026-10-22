## v4.2.0 (minor)

Changes since v4.1.0:

- Merge remote-tracking branch 'origin/main' into fix/190-ignore-submodules-none (matt-edmondson)
- Use the MSTest assertions the analyzers suggest in the distinctness tests (Claude)
- Use the MSTest assertions the analyzers suggest in the new clone tests (Claude)
- Check every pair of semantic types for distinctness, not one [patch] (Claude)
- Make Clone().WithDepth shallow for a local-path source too [patch] (Claude)
- Report a fetch's null object id as null, as Push() does [patch] (Claude)
- Fall back to X-RateLimit-Reset for a GitHub 429 or secondary limit with no Retry-After [patch] (Claude)
- Replace the nested ternary in ReadTimestamp with statements (Claude)
- Read a commit timestamp whose offset is beyond 14 hours instead of failing Log() [patch] (Claude)
- Resolve a :/ message search in RevParse by peeling the resolved id [patch] (Claude)
- Read Retry-After on GitHub secondary rate limits [patch] (Claude)
- Merge main into fix/233-log-unborn-head (Claude)
- Serialize contained progress reports so a failed sink is never re-entered [patch] (Claude)
- Let OutOfMemoryException escape the contained progress sink [patch] (Claude)
- Describe GitHubProvider's owner-type routing and the worktree and patch verbs in the docs [patch] (Claude)
- Contain an exception thrown by a progress sink instead of faulting the verb [patch] (Claude)
- Route OpenWebClient's launch through an internal seam so tests can observe it [patch] (Claude)
- Refuse author values git would rewrite in WithAuthor (Claude)
- Return an empty Log() on a repository with no commits yet [patch] (Claude)
- Peel RevParse to the commit a revision names [patch] (Claude)
- Add FromFork so GitHub pull requests can be opened from a fork [minor] (Claude)
- Fall back to Retry-After for the Azure DevOps rate-limit reset time [patch] (Claude)
- Keep remote branches whose name ends in /HEAD in Branches() [patch] (Claude)
- Mark collapsed untracked and ignored directories in Status() [minor] (matt-edmondson)
- Pass --progress to network verbs when a progress sink is attached [patch] (Claude)
- Compare the changed path as a typed path so the test holds on Windows [patch] (Claude)
- Compare the submodule's path as a typed path so the test holds on Windows [patch] (Claude)
- Close revision operands with -- in Log, RevList, Diff and Patch [patch] (Claude)
- Pin --ignore-submodules=none in Status, Diff and Patch [patch] (Claude)
- Cover the fetch rejection exception and unreadable-output path [patch] (Claude)
- Report a refused reference from Fetch() instead of an empty error [minor] (Claude)
- Reject branch names git forbids in GitBranchName [patch] (Claude)
- Refuse Staged() with Between() before running git [patch] (Claude)
- Report a gone upstream from Status() as IsUpstreamGone [minor] (matt-edmondson)
- Pass --no-follow so Log().ForPath ignores log.follow [patch] (matt-edmondson)
- Merge main into fix/152-bare-worktree-detach (matt-edmondson)
- Report a repository git refuses as dubious ownership, not as missing [patch] (Claude)
- Detach a worktree added with no mode or commit-ish [patch] (Claude)
- Report every push URL of a remote, in git's order [minor] (Claude)
- Refuse an Add with nothing to stage, or with both All and UpdateTrackedOnly [patch] (Claude)
- Report no head for a worktree on an unborn branch [patch] (Claude)
- Name HEAD when ExcludingRemoteTrackingRefs has no revision [patch] (Claude)
- Create a lightweight tag with --no-sign so tag.gpgSign cannot open an editor [patch] (Claude)
- Keep #-leading lines in an annotated tag message [patch] (Claude)
- Skip a remote with no URL when listing remotes [patch] (Claude)
- Mark the newline-in-path parser test inconclusive on Windows (Claude)
- Compare the worktree path by its tail, not the temp root's spelling (Claude)
- List worktrees with -z and decode quoted lock/prunable reasons [patch] (Claude)
- Keep fetch --porcelain when submodule recursion is explicitly No [patch] (Claude)
- [patch] Forward IFileSystem.RandomAccess in the test filesystem provider (Claude)
- [patch] Update ktsu.Essentials to 2.10.7 and Testably.Abstractions to 10.4.0 (Claude)
- Report only a working-tree root as cloned, not a directory inside another repository [patch] (Claude)
- Keep "#" lines in a commit message where the host strips comments [patch] (Claude)
- Redact tokens, passwords and device codes from credential ToString [patch] (Claude)
- Read the patch number before Apple's build note in git --version [patch] (Claude)
- Merge remote-tracking branch 'origin/main' into fix/194-no-changes-added-to-commit (Claude)
- Send a valid User-Agent when the app name is not an HTTP token [patch] (Claude)
- Recognise "no changes added to commit" as nothing to commit [patch] (Claude)
- Take ktsu.RunCommand 1.9.5 so git output is read to EOF [patch] (matt-edmondson)
- Move the partial-rename round-trip test away from the end of the class (Claude)
- Write a partial rename patch as a plain change to the new path (Claude)
- Report conflicted binary files and modify/delete conflicts as unmerged in Patch() (Claude)
- Clear inherited GIT_DIR, GIT_INDEX_FILE and GIT_DIFF_OPTS before running git [patch] (Claude)
- Build test paths with Path.Join, as the other integration tests do (Claude)
- Report AlreadyExisted for a submodule, worktree or separate-git-dir target [patch] (Claude)
- Report branch and tag names unprefixed when a branch and tag share a name [patch] (Claude)
- Pass --no-renames from Diff unless detection is requested, matching Patch [patch] (Claude)
- Pass --no-show-signature so Log and Commit survive log.showSignature [patch] (Claude)
- Compare the submodule patch path as a RelativeFilePath so the test passes on Windows (Claude)
- Pin diff --submodule=short so Patch reports a moved submodule as its gitlink [patch] (Claude)
- Match caller file paths literally, so file[1].txt no longer also matches file1.txt [patch] (Claude)
- Move CI onto the shared ci-shared.yml pipeline (Claude)
- [patch] Unstage with reset so it works before the first commit (Claude)

## v4.1.12 (patch)

Changes since v4.1.11:

- Mark the newline-in-path parser test inconclusive on Windows (Claude)
- Compare the worktree path by its tail, not the temp root's spelling (Claude)
- List worktrees with -z and decode quoted lock/prunable reasons [patch] (Claude)
- Keep fetch --porcelain when submodule recursion is explicitly No [patch] (Claude)

## v4.1.11 (patch)

Changes since v4.1.10:

- Report only a working-tree root as cloned, not a directory inside another repository [patch] (Claude)

## v4.1.10 (patch)

Changes since v4.1.9:

- [patch] Forward IFileSystem.RandomAccess in the test filesystem provider (Claude)
- [patch] Update ktsu.Essentials to 2.10.7 and Testably.Abstractions to 10.4.0 (Claude)

## v4.1.9 (patch)

Changes since v4.1.8:

- Keep "#" lines in a commit message where the host strips comments [patch] (Claude)
- Redact tokens, passwords and device codes from credential ToString [patch] (Claude)
- Read the patch number before Apple's build note in git --version [patch] (Claude)

## v4.1.8 (patch)

Changes since v4.1.7:

- Merge remote-tracking branch 'origin/main' into fix/194-no-changes-added-to-commit (Claude)
- Recognise "no changes added to commit" as nothing to commit [patch] (Claude)
- Take ktsu.RunCommand 1.9.5 so git output is read to EOF [patch] (matt-edmondson)

## v4.1.7 (patch)

Changes since v4.1.6:

- Send a valid User-Agent when the app name is not an HTTP token [patch] (Claude)

## v4.1.6 (patch)

Changes since v4.1.5:

- Move the partial-rename round-trip test away from the end of the class (Claude)
- Write a partial rename patch as a plain change to the new path (Claude)
- Report conflicted binary files and modify/delete conflicts as unmerged in Patch() (Claude)
- Clear inherited GIT_DIR, GIT_INDEX_FILE and GIT_DIFF_OPTS before running git [patch] (Claude)

## v4.1.5 (patch)

Changes since v4.1.4:

- Build test paths with Path.Join, as the other integration tests do (Claude)
- Report AlreadyExisted for a submodule, worktree or separate-git-dir target [patch] (Claude)
- Report branch and tag names unprefixed when a branch and tag share a name [patch] (Claude)

## v4.1.4 (patch)

Changes since v4.1.3:

- Pass --no-renames from Diff unless detection is requested, matching Patch [patch] (Claude)
- Pass --no-show-signature so Log and Commit survive log.showSignature [patch] (Claude)

## v4.1.3 (patch)

Changes since v4.1.2:

- Compare the submodule patch path as a RelativeFilePath so the test passes on Windows (Claude)
- Pin diff --submodule=short so Patch reports a moved submodule as its gitlink [patch] (Claude)
- Match caller file paths literally, so file[1].txt no longer also matches file1.txt [patch] (Claude)

## v4.1.2 (patch)

Changes since v4.1.1:

- Move CI onto the shared ci-shared.yml pipeline (Claude)

## v4.1.1 (patch)

Changes since v4.1.0:

- [patch] Unstage with reset so it works before the first commit (Claude)

## v4.1.0 (minor)

Changes since v4.0.0:

- Merge main into fix/patch-header-paths (Claude)
- [patch] Keep the patch-fixture -text rule out of the SDK-managed root .gitattributes (Claude)
- [minor] Read a commit with an empty signature name or email (Claude)
- [patch] Cover the quoted-path decoder's escapes and error paths (Claude)
- [patch] Compare the parsed path in its platform form (Claude)
- [patch] Read quoted and " b/"-containing paths in Patch() headers (Claude)
- [patch] Pin context lines and rename detection in Patch() (Claude)

## v4.0.1 (patch)

Changes since v4.0.0:

- [patch] Pin context lines and rename detection in Patch() (Claude)

## v4.0.0 (major)

Changes since v3.0.0:

- [patch] Join path segments rather than combining them (matt-edmondson)
- [patch] Refuse the patch inputs Apply cannot honor (matt-edmondson)
- [patch] Make Patch survive a hostile diff configuration (matt-edmondson)
- [minor] Add Unstage for the whole-file case (matt-edmondson)
- [minor] Add the index-moved case Apply's Checked() actually guards (matt-edmondson)
- [major] Use US spelling throughout the sources and the docs (matt-edmondson)
- [minor] Add Apply, which puts a patch into the index (matt-edmondson)
- [minor] Add Patch, which reads a diff as hunks rather than counts (matt-edmondson)
- [patch] Use US spelling in the patch parser's diagnostics (matt-edmondson)
- [minor] Parse a unified diff into files, hunks and lines (matt-edmondson)
- [minor] Add the patch model and hunk assembly (matt-edmondson)
- [patch] Plan the patch verbs implementation (matt-edmondson)
- [patch] Design the patch verbs: reading hunks, applying them, unstaging (matt-edmondson)
- refactor: address the code quality findings on the pull request (matt-edmondson)
- Merge origin/main into feat/worktrees-and-github-auth (matt-edmondson)
- fix: guard the verification URI and align error taxonomy with the spec (matt-edmondson)
- docs: cover worktree and device-flow types in the API reference (matt-edmondson)
- docs: document worktree verbs, owner kinds, and device flow [minor] (matt-edmondson)
- fix: enforce the device flow's expiry deadline and poll interval floor (matt-edmondson)
- feat: obtain a GitHub credential through the OAuth device flow (matt-edmondson)
- chore: map hostname-derived author addresses (matt-edmondson)
- feat: report the single sign-on authorisation url on a forbidden response (matt-edmondson)
- feat: route GitHub repository enumeration by owner kind (matt-edmondson)
- feat: add the worktree removal and prune verbs (matt-edmondson)
- feat: add the worktree creation verb (matt-edmondson)
- feat: add the worktree listing verb (matt-edmondson)
- chore: stop ignoring .worktrees in a file ktsu.Sdk regenerates (matt-edmondson)
- feat: read git worktree list --porcelain into a typed model (matt-edmondson)
- fix: reject a null success value in GitResult.FromValue [minor] (Claude)
- test: use the MSTest count assertion the analyzer points at [patch] (matt-edmondson)
- fix: enumerate repositories on the route the credential entitles [patch] (matt-edmondson)
- docs: plan worktree verbs, GitHub owner kinds, and device flow (matt-edmondson)
- docs: design worktree verbs, GitHub owner kinds, and device flow (matt-edmondson)
- docs: state credential rationale without narrating history (matt-edmondson)
- feat: add bearer credentials and a credential injection seam (matt-edmondson)
- Gate Dependabot auto-merge on CI actually being green (Claude)
- test: use the MSTest assertions the analyzers point at [patch] (matt-edmondson)
- fix: pin --untracked-files so status.showUntrackedFiles cannot hide work [patch] (matt-edmondson)
- fix: collapse an unmerged submodule's merge stages into one entry [patch] (Claude)
- fix: route pull-request mapping through ToHostValue [patch] (matt-edmondson)
- docs: record the per-provider repository addressing split [patch] (matt-edmondson)
- fix: address a GitHub repository by name, and an id on GitHub's id route [patch] (matt-edmondson)
- test: let the fake handler 404 an unexpected path [patch] (matt-edmondson)
- ci: adopt the consolidated .NET workflow [patch] (Claude)
- test: assert the platform-correct outcome for a tab-named path [patch] (matt-edmondson)
- fix: bound the numstat split so a tab in a path does not throw [patch] (matt-edmondson)

## v3.3.0 (minor)

Changes since v3.2.0:

- refactor: address the code quality findings on the pull request (matt-edmondson)
- Merge origin/main into feat/worktrees-and-github-auth (matt-edmondson)
- fix: guard the verification URI and align error taxonomy with the spec (matt-edmondson)
- docs: cover worktree and device-flow types in the API reference (matt-edmondson)
- docs: document worktree verbs, owner kinds, and device flow [minor] (matt-edmondson)
- fix: enforce the device flow's expiry deadline and poll interval floor (matt-edmondson)
- feat: obtain a GitHub credential through the OAuth device flow (matt-edmondson)
- chore: map hostname-derived author addresses (matt-edmondson)
- feat: report the single sign-on authorisation url on a forbidden response (matt-edmondson)
- feat: route GitHub repository enumeration by owner kind (matt-edmondson)
- feat: add the worktree removal and prune verbs (matt-edmondson)
- feat: add the worktree creation verb (matt-edmondson)
- feat: add the worktree listing verb (matt-edmondson)
- chore: stop ignoring .worktrees in a file ktsu.Sdk regenerates (matt-edmondson)
- feat: read git worktree list --porcelain into a typed model (matt-edmondson)
- docs: plan worktree verbs, GitHub owner kinds, and device flow (matt-edmondson)
- docs: design worktree verbs, GitHub owner kinds, and device flow (matt-edmondson)

## v3.2.0 (minor)

Changes since v3.1.0:

- fix: reject a null success value in GitResult.FromValue [minor] (Claude)
- test: use the MSTest count assertion the analyzer points at [patch] (matt-edmondson)
- fix: enumerate repositories on the route the credential entitles [patch] (matt-edmondson)

## v3.1.1 (patch)

Changes since v3.1.0:

- test: use the MSTest count assertion the analyzer points at [patch] (matt-edmondson)
- fix: enumerate repositories on the route the credential entitles [patch] (matt-edmondson)

## v3.1.0 (minor)

Changes since v3.0.0:

- docs: state credential rationale without narrating history (matt-edmondson)
- feat: add bearer credentials and a credential injection seam (matt-edmondson)
- Gate Dependabot auto-merge on CI actually being green (Claude)
- test: use the MSTest assertions the analyzers point at [patch] (matt-edmondson)
- fix: pin --untracked-files so status.showUntrackedFiles cannot hide work [patch] (matt-edmondson)
- fix: collapse an unmerged submodule's merge stages into one entry [patch] (Claude)
- fix: route pull-request mapping through ToHostValue [patch] (matt-edmondson)
- docs: record the per-provider repository addressing split [patch] (matt-edmondson)
- fix: address a GitHub repository by name, and an id on GitHub's id route [patch] (matt-edmondson)
- test: let the fake handler 404 an unexpected path [patch] (matt-edmondson)
- ci: adopt the consolidated .NET workflow [patch] (Claude)
- test: assert the platform-correct outcome for a tab-named path [patch] (matt-edmondson)
- fix: bound the numstat split so a tab in a path does not throw [patch] (matt-edmondson)

## v3.0.4 (patch)

Changes since v3.0.3:

- Gate Dependabot auto-merge on CI actually being green (Claude)

## v3.0.3 (patch)

Changes since v3.0.2:

- test: use the MSTest assertions the analyzers point at [patch] (matt-edmondson)
- fix: pin --untracked-files so status.showUntrackedFiles cannot hide work [patch] (matt-edmondson)
- fix: collapse an unmerged submodule's merge stages into one entry [patch] (Claude)
- fix: route pull-request mapping through ToHostValue [patch] (matt-edmondson)

## v3.0.2 (patch)

Changes since v3.0.1:

- docs: record the per-provider repository addressing split [patch] (matt-edmondson)
- fix: address a GitHub repository by name, and an id on GitHub's id route [patch] (matt-edmondson)
- test: let the fake handler 404 an unexpected path [patch] (matt-edmondson)
- ci: adopt the consolidated .NET workflow [patch] (Claude)

## v3.0.1 (patch)

Changes since v3.0.0:

- test: assert the platform-correct outcome for a tab-named path [patch] (matt-edmondson)
- fix: bound the numstat split so a tab in a path does not throw [patch] (matt-edmondson)

## v3.0.0 (major)

Changes since v2.0.0:

- fix: resolve the first matching status line without a one-iteration loop [patch] (Claude)
- style: join rather than combine inside the fixture's path guard [patch] (Claude)
- style: apply the remaining github-code-quality findings [patch] (Claude)
- fix: match submodule paths using git's own spelling [patch] (Claude)
- docs: reconcile the documented scope and record the new verbs [patch] (Claude)
- feat: add submodule listing, update, and the recursion flags [minor] (Claude)
- feat: report insertion and deletion counts on GitDiffEntry [minor] (Claude)
- feat: query unpushed commits and count commits without listing them [minor] (Claude)
- feat: add tag list, create, and delete to the local layer [minor] (Claude)
- feat!: stop inventing local paths and address repositories by host id [major] (Claude)
- fix: close five hosting-layer gaps found in the Phase 5b review [minor] (Claude)
- fix: reject contradictory checkout flags and unify pull's two diagnostics [patch] (Claude)
- ci: make the SonarQube quality gate opt in [patch] (matt-edmondson)
- ci: adopt the unified dotnet workflow [patch] (matt-edmondson)
- [patch] Reject a whitespace-only repository name from a host (matt-edmondson)
- [patch] Fold local path containment into a single helper (matt-edmondson)
- [patch] Contain hosting repository local paths and clean up transport disposal (matt-edmondson)
- [patch] Correct two stale documentation claims on the transport (matt-edmondson)
- [patch] Map GitHub's 429 to GitHostingRateLimitException (matt-edmondson)
- [patch] Fix the final review findings on the hosting layer (matt-edmondson)
- [patch] Fix a false CreateHttpClient claim in CLAUDE.md and README.md (matt-edmondson)
- [minor] Register and document the hosting layer (matt-edmondson)
- [patch] Stop justifying the repositoryId substitution with a false parallel (matt-edmondson)
- [minor] Add Azure DevOps pull requests and hosting error mapping (matt-edmondson)
- [patch] Pin System.Text.Json to a net9.0-safe assembly version (matt-edmondson)
- [minor] Add the Azure DevOps provider with repository enumeration (matt-edmondson)
- [patch] Fix GitHubProvider transport leak and test isolation race (matt-edmondson)
- [minor] Implement the GitHub provider over Octokit (matt-edmondson)
- [minor] Add the pull request create builder (matt-edmondson)
- [patch] Document that GitProvider cannot be subclassed outside this assembly (matt-edmondson)
- [patch] Configure the credential test cache once, not once per test (matt-edmondson)
- [minor] Replace the dead provider members with a real hosting contract (matt-edmondson)
- [patch] Add a fake HTTP handler that records requests as well as scripting responses (matt-edmondson)
- [minor] Add the hosting exception hierarchy (matt-edmondson)
- [patch] Reject empty pull request number, author, and web URI (matt-edmondson)
- [minor] Add the pull request model and its semantic types (matt-edmondson)
- [patch] Redact the real repo tagline, default branch, and area labels (matt-edmondson)
- [patch] Close redaction gaps and fix a transcription bug in the hosting fixtures (matt-edmondson)
- [patch] Pin the stable api-version and settle the web-link finding (matt-edmondson)
- [patch] Capture the hosting API response fixtures and REST findings (matt-edmondson)
- [patch] Add the Phase 5b hosting layer implementation plan (matt-edmondson)
- [patch] Add the Phase 5b hosting layer design (matt-edmondson)
- [patch] Say why --no-rebase sits beside --rebase (matt-edmondson)
- [minor] Add Merge() to pull, reject contradictory fetch config, dedupe integration helpers (matt-edmondson)
- [patch] Pin pull.rebase so the conflicting-pull test works off Windows (matt-edmondson)
- [patch] Document the Phase 5a remote sync verbs and drop stale phase references (matt-edmondson)
- [patch] Parse the forced-push sha range and pin the progress sinks (matt-edmondson)
- [minor] Add integration tests for the remote sync verbs (matt-edmondson)
- [minor] Wire the remote sync verbs into GitRepository (matt-edmondson)
- [patch] Cover and fix the pull TryExecuteAsync stderr/stdout join (matt-edmondson)
- [minor] Add the pull verb builder (matt-edmondson)
- [patch] Fix the fetch porcelain fixture and make the version probe fail-soft (matt-edmondson)
- [minor] Add the fetch verb builder with its version probe (matt-edmondson)
- [minor] Add the push verb builder (matt-edmondson)
- [patch] Build the CRLF fetch parser test through Record so the flag survives (matt-edmondson)
- [patch] Fix the fetch CRLF test that passed for the wrong reason (matt-edmondson)
- [minor] Add the porcelain fetch parser (matt-edmondson)
- [minor] Add the porcelain push parser (matt-edmondson)
- [minor] Add remote sync result models and their exceptions (matt-edmondson)
- [patch] Apply two Phase 5a plan fixes from the pre-flight scan (matt-edmondson)
- [patch] Add the Phase 5a remote sync implementation plan (matt-edmondson)
- [patch] Repair doc comments displaced by the git-required switch (matt-edmondson)
- [patch] Add cross-platform CI and make missing git fail rather than skip in CI (matt-edmondson)
- [patch] Document Phase 4 mutating verbs in README and CLAUDE.md (matt-edmondson)
- [patch] Fix review findings: consumer FileNotFoundException, init probe, commit diagnostics (matt-edmondson)
- [minor] Add tier-3 integration tests against a real git binary (matt-edmondson)
- [minor] Wire the mutating verbs into GitRepository and IGitClient (matt-edmondson)
- [patch] Make the Testably interface reference private so it stays out of the shipped package (matt-edmondson)
- [minor] Add the clone verb with its destination pre-check (matt-edmondson)
- [minor] Add the init verb with its existing-repository probe (matt-edmondson)
- [patch] Fix typo in commit readback test method name (matt-edmondson)
- [patch] Fix a test-name typo and a test count in the Phase 4 plan (matt-edmondson)
- [minor] Add the commit verb with its log readback (matt-edmondson)
- [minor] Add the remote add, remove, and set-url verbs (matt-edmondson)
- [minor] Add the checkout verb builder (matt-edmondson)
- [minor] Add the branch create and delete verbs (matt-edmondson)
- [minor] Add the add verb builder (matt-edmondson)
- [patch] Strengthen the Task 8 progress test in the Phase 4 plan (matt-edmondson)
- [minor] Add mutating-verb foundations and the builder progress seam (matt-edmondson)
- [patch] Fix two Phase 4 plan defects found in the pre-flight scan (matt-edmondson)
- [patch] Add the Phase 4 mutating verbs implementation plan (matt-edmondson)
- [patch] Rewrite docs for the two-layer local git client + hosting provider library (matt-edmondson)
- [patch] Apply final whole-branch review fixes (matt-edmondson)
- [minor] Add IGitClient, GitClient, and the read-only repository verbs (matt-edmondson)
- [minor] Add the rev-parse verb and the fixed-vector text builder (matt-edmondson)
- [minor] Add the remote listing verb (matt-edmondson)
- [minor] Add the branch listing verb (matt-edmondson)
- [patch] Rename Between parameters to fromRevision and toRevision (matt-edmondson)
- [minor] Add the diff verb builder (matt-edmondson)
- [minor] Add the name-status diff parser (matt-edmondson)
- [minor] Add the log verb builder (matt-edmondson)
- [minor] Add the log parser (matt-edmondson)
- [minor] Fix NUL escape format to match Task 3 convention (matt-edmondson)
- [minor] Add the status verb builder (matt-edmondson)
- [minor] Add the porcelain v2 status parser (matt-edmondson)
- [minor] Add the git --version verb (matt-edmondson)
- [minor] Add read-only result models and parsing primitives (matt-edmondson)
- [patch] Correct the documented cancellation contract (matt-edmondson)
- [patch] Document that the progress sink must be thread-safe (matt-edmondson)
- [patch] Fix two plan defects found in the pre-flight cross-task scan (matt-edmondson)
- [patch] Add the Phase 3 read-only verbs implementation plan (matt-edmondson)

## v2.5.3 (patch)

Changes since v2.5.2:

- ci: make the SonarQube quality gate opt in [patch] (matt-edmondson)

## v2.5.2 (patch)

Changes since v2.5.1:

- ci: adopt the unified dotnet workflow [patch] (matt-edmondson)

## v2.5.1 (patch)

Changes since v2.5.0:

- [patch] Reject a whitespace-only repository name from a host (matt-edmondson)

## v2.5.0 (minor)

Changes since v2.4.0:

- [patch] Fold local path containment into a single helper (matt-edmondson)
- [patch] Contain hosting repository local paths and clean up transport disposal (matt-edmondson)
- [patch] Correct two stale documentation claims on the transport (matt-edmondson)
- [patch] Map GitHub's 429 to GitHostingRateLimitException (matt-edmondson)
- [patch] Fix the final review findings on the hosting layer (matt-edmondson)
- [patch] Fix a false CreateHttpClient claim in CLAUDE.md and README.md (matt-edmondson)
- [minor] Register and document the hosting layer (matt-edmondson)
- [patch] Stop justifying the repositoryId substitution with a false parallel (matt-edmondson)
- [minor] Add Azure DevOps pull requests and hosting error mapping (matt-edmondson)
- [patch] Pin System.Text.Json to a net9.0-safe assembly version (matt-edmondson)
- [minor] Add the Azure DevOps provider with repository enumeration (matt-edmondson)
- [patch] Fix GitHubProvider transport leak and test isolation race (matt-edmondson)
- [minor] Implement the GitHub provider over Octokit (matt-edmondson)
- [minor] Add the pull request create builder (matt-edmondson)
- [patch] Document that GitProvider cannot be subclassed outside this assembly (matt-edmondson)
- [patch] Configure the credential test cache once, not once per test (matt-edmondson)
- [minor] Replace the dead provider members with a real hosting contract (matt-edmondson)
- [patch] Add a fake HTTP handler that records requests as well as scripting responses (matt-edmondson)
- [minor] Add the hosting exception hierarchy (matt-edmondson)
- [patch] Reject empty pull request number, author, and web URI (matt-edmondson)
- [minor] Add the pull request model and its semantic types (matt-edmondson)
- [patch] Redact the real repo tagline, default branch, and area labels (matt-edmondson)
- [patch] Close redaction gaps and fix a transcription bug in the hosting fixtures (matt-edmondson)
- [patch] Pin the stable api-version and settle the web-link finding (matt-edmondson)
- [patch] Capture the hosting API response fixtures and REST findings (matt-edmondson)
- [patch] Add the Phase 5b hosting layer implementation plan (matt-edmondson)
- [patch] Add the Phase 5b hosting layer design (matt-edmondson)

## v2.4.0 (minor)

Changes since v2.3.0:

- [patch] Say why --no-rebase sits beside --rebase (matt-edmondson)
- [minor] Add Merge() to pull, reject contradictory fetch config, dedupe integration helpers (matt-edmondson)

## v2.3.0 (minor)

Changes since v2.2.0:

- [patch] Pin pull.rebase so the conflicting-pull test works off Windows (matt-edmondson)
- [patch] Document the Phase 5a remote sync verbs and drop stale phase references (matt-edmondson)
- [patch] Parse the forced-push sha range and pin the progress sinks (matt-edmondson)
- [minor] Add integration tests for the remote sync verbs (matt-edmondson)
- [minor] Wire the remote sync verbs into GitRepository (matt-edmondson)
- [patch] Cover and fix the pull TryExecuteAsync stderr/stdout join (matt-edmondson)
- [minor] Add the pull verb builder (matt-edmondson)
- [patch] Fix the fetch porcelain fixture and make the version probe fail-soft (matt-edmondson)
- [minor] Add the fetch verb builder with its version probe (matt-edmondson)
- [minor] Add the push verb builder (matt-edmondson)
- [patch] Build the CRLF fetch parser test through Record so the flag survives (matt-edmondson)
- [patch] Fix the fetch CRLF test that passed for the wrong reason (matt-edmondson)
- [minor] Add the porcelain fetch parser (matt-edmondson)
- [minor] Add the porcelain push parser (matt-edmondson)
- [minor] Add remote sync result models and their exceptions (matt-edmondson)
- [patch] Apply two Phase 5a plan fixes from the pre-flight scan (matt-edmondson)
- [patch] Add the Phase 5a remote sync implementation plan (matt-edmondson)
- [patch] Repair doc comments displaced by the git-required switch (matt-edmondson)
- [patch] Add cross-platform CI and make missing git fail rather than skip in CI (matt-edmondson)

## v2.2.1 (patch)

Changes since v2.2.0:

- [patch] Repair doc comments displaced by the git-required switch (matt-edmondson)
- [patch] Add cross-platform CI and make missing git fail rather than skip in CI (matt-edmondson)

## v2.2.0 (minor)

Changes since v2.1.0:

- [patch] Document Phase 4 mutating verbs in README and CLAUDE.md (matt-edmondson)
- [patch] Fix review findings: consumer FileNotFoundException, init probe, commit diagnostics (matt-edmondson)
- [minor] Add tier-3 integration tests against a real git binary (matt-edmondson)
- [minor] Wire the mutating verbs into GitRepository and IGitClient (matt-edmondson)
- [patch] Make the Testably interface reference private so it stays out of the shipped package (matt-edmondson)
- [minor] Add the clone verb with its destination pre-check (matt-edmondson)
- [minor] Add the init verb with its existing-repository probe (matt-edmondson)
- [patch] Fix typo in commit readback test method name (matt-edmondson)
- [patch] Fix a test-name typo and a test count in the Phase 4 plan (matt-edmondson)
- [minor] Add the commit verb with its log readback (matt-edmondson)
- [minor] Add the remote add, remove, and set-url verbs (matt-edmondson)
- [minor] Add the checkout verb builder (matt-edmondson)
- [minor] Add the branch create and delete verbs (matt-edmondson)
- [minor] Add the add verb builder (matt-edmondson)
- [patch] Strengthen the Task 8 progress test in the Phase 4 plan (matt-edmondson)
- [minor] Add mutating-verb foundations and the builder progress seam (matt-edmondson)
- [patch] Fix two Phase 4 plan defects found in the pre-flight scan (matt-edmondson)
- [patch] Add the Phase 4 mutating verbs implementation plan (matt-edmondson)

## v2.1.0 (minor)

Changes since v2.0.0:

- [patch] Rewrite docs for the two-layer local git client + hosting provider library (matt-edmondson)
- [patch] Apply final whole-branch review fixes (matt-edmondson)
- [minor] Add IGitClient, GitClient, and the read-only repository verbs (matt-edmondson)
- [minor] Add the rev-parse verb and the fixed-vector text builder (matt-edmondson)
- [minor] Add the remote listing verb (matt-edmondson)
- [minor] Add the branch listing verb (matt-edmondson)
- [patch] Rename Between parameters to fromRevision and toRevision (matt-edmondson)
- [minor] Add the diff verb builder (matt-edmondson)
- [minor] Add the name-status diff parser (matt-edmondson)
- [minor] Add the log verb builder (matt-edmondson)
- [minor] Add the log parser (matt-edmondson)
- [minor] Fix NUL escape format to match Task 3 convention (matt-edmondson)
- [minor] Add the status verb builder (matt-edmondson)
- [minor] Add the porcelain v2 status parser (matt-edmondson)
- [minor] Add the git --version verb (matt-edmondson)
- [minor] Add read-only result models and parsing primitives (matt-edmondson)
- [patch] Correct the documented cancellation contract (matt-edmondson)
- [patch] Document that the progress sink must be thread-safe (matt-edmondson)
- [patch] Fix two plan defects found in the pre-flight cross-task scan (matt-edmondson)
- [patch] Add the Phase 3 read-only verbs implementation plan (matt-edmondson)

## v2.0.0 (major)

Changes since v1.1.9:

- [patch] Stop the timeout tests depending on host process speed (matt-edmondson)
- Merge origin/main into feature/gitintegration-v2 (matt-edmondson)
- [minor] Force a non-interactive English git environment via RunCommand 1.5.0 (matt-edmondson)
- [patch] Adopt ktsu.RunCommand 1.4.29 cancellation fix (matt-edmondson)
- [major] Mark GitIntegration v2 as a breaking release (matt-edmondson)
- [patch] Refresh spec to match implemented code and upstream fixes (matt-edmondson)
- [minor] Trim unused API surface and package references (matt-edmondson)
- [patch] Close test-integrity gaps in the execution core (matt-edmondson)
- [patch] Remove all three analyzer suppressions (matt-edmondson)
- [minor] Fix required owner, timeout race, SHA-256 ids, and result aliasing (matt-edmondson)
- [minor] Add extension seams, a progress path, and an options snapshot (matt-edmondson)
- [minor] Close shell-execute and git option injection holes (matt-edmondson)
- [minor] Add tests for single-instance guarantee and idempotent options (matt-edmondson)
- [minor] Add dependency injection registration for git integration (matt-edmondson)
- [minor] Narrow AppendVerbArguments to ICollection\<string\> instead of suppressing CA1002 (matt-edmondson)
- [patch] Use ICollection in AppendVerbArguments signature in plan (matt-edmondson)
- [minor] Add git command builder base with global argument injection (matt-edmondson)
- [patch] Document RunCommand cancellation race in spec constraints (matt-edmondson)
- [minor] Classify raced git timeouts alongside faulted ones (matt-edmondson)
- [minor] Distinguish git timeout from caller cancellation (matt-edmondson)
- [patch] Add GitTimeoutException to spec and plan (matt-edmondson)
- [minor] Add RunCommand-backed git process runner (matt-edmondson)
- [minor] Fix default-instance and exit-code traps in GitResult and GitCommandException (matt-edmondson)
- [patch] Correct GitResult default-state trap and ExitCode sentinel in spec and plan (matt-edmondson)
- Revert "[patch] Ignore superpowers SDD scratch workspace" (matt-edmondson)
- [minor] Add git execution contract, result and exception types (matt-edmondson)
- [minor] Make repository metadata nullable and fix cross-platform browser launch (matt-edmondson)
- [patch] Correct line-ending constraint to match repo .gitattributes (matt-edmondson)
- [minor] Migrate semantic types from StrongStrings to Semantics (matt-edmondson)
- [minor] Swap package manifest to Semantics and add test project (matt-edmondson)
- [patch] Ignore superpowers SDD scratch workspace (matt-edmondson)
- [minor] Add GitIntegration v2 phase 1-2 implementation plan (matt-edmondson)
- [minor] Add GitIntegration v2 design spec (matt-edmondson)

## v1.1.9 (patch)

Changes since v1.1.8:

- chore: store icon.png in LFS as .gitattributes declares (matt-edmondson)
- docs: scope build badge to the default branch (matt-edmondson)
- docs: broaden TAGS.md for better topic coverage (matt-edmondson)
- docs: correct README, DESCRIPTION and TAGS metadata (matt-edmondson)

## v1.1.8 (patch)

Changes since v1.1.7:

- Stop Update SDKs failing when there is nothing to update (matt-edmondson)

## v1.1.7 (patch)

Changes since v1.1.6:

- Add PrivateAssets="all" to Polyfill package reference to fix KTSU0007 [patch] (matt-edmondson)
- Sync .github\workflows\dotnet.yml (KtsuTools)
- Sync .editorconfig (KtsuTools)
- Sync global.json (KtsuTools)
- Sync .github\workflows\dotnet.yml (KtsuTools)

## v1.1.6 (patch)

Changes since v1.1.5:

- chore: update ktsu.Sdk to 2.21.1 [patch] (matt-edmondson)

## v1.1.5 (patch)

Changes since v1.1.4:

- Sync .github\workflows\dotnet.yml (ktsu[bot])
- Sync global.json (ktsu[bot])

## v1.1.4 (patch)

Changes since v1.1.3:

- Sync .github\workflows\update-sdks.yml (ktsu[bot])
- Sync .github\workflows\dotnet.yml (ktsu[bot])
- Sync .github\workflows\dependabot-merge.yml (ktsu[bot])
- Sync .gitattributes (ktsu[bot])
- Sync global.json (ktsu[bot])

## v1.1.3 (patch)

Changes since v1.1.2:

- Merge remote-tracking branch 'refs/remotes/origin/main' (ktsu[bot])
- Sync .github\workflows\dotnet.yml (ktsu[bot])
- Sync global.json (ktsu[bot])

## v1.1.2 (patch)

Changes since v1.1.1:

- fix: clarify credential nullability in Git provider (matt-edmondson)
- refactor: trim package refs and multi-target GitIntegration (matt-edmondson)
- Add TAGS.md with NuGet package tags (matt-edmondson)
- Sync .github\workflows\dotnet.yml (KtsuTools)
- Remove legacy build scripts (matt-edmondson)
- Remove .github\workflows\project.yml (matt-edmondson)
- Update project configuration and scripts, including new SDK management, enhanced CI/CD workflows, and updated copyright information. (matt-edmondson)
- Update configuration files and scripts for improved build and test processes (matt-edmondson)
- Update ktsu.AppDataStorage package version to 1.15.5 (matt-edmondson)

## v1.1.2-pre.18 (prerelease)

Changes since v1.1.2-pre.17:

- Merge remote-tracking branch 'refs/remotes/origin/main' (ktsu[bot])
- Sync scripts\PSBuild.psm1 (ktsu[bot])
- Sync .editorconfig (ktsu[bot])
- Sync .gitattributes (ktsu[bot])
- Sync .gitignore (ktsu[bot])
- Sync .mailmap (ktsu[bot])
- Sync .runsettings (ktsu[bot])

## v1.1.2-pre.17 (prerelease)

Changes since v1.1.2-pre.16:

- Update ktsu.AppDataStorage to 1.15.4 (dependabot[bot])

## v1.1.2-pre.16 (prerelease)

Changes since v1.1.2-pre.15:

- Sync scripts\PSBuild.psm1 (ktsu[bot])
- Sync .editorconfig (ktsu[bot])
- Sync .gitattributes (ktsu[bot])
- Sync .gitignore (ktsu[bot])
- Sync .mailmap (ktsu[bot])
- Sync .runsettings (ktsu[bot])

## v1.1.2-pre.15 (prerelease)

No significant changes detected since v1.1.2-pre.14.

## v1.1.2-pre.14 (prerelease)

No significant changes detected since v1.1.2-pre.13.

## v1.1.2-pre.13 (prerelease)

No significant changes detected since v1.1.2-pre.12.

## v1.1.2-pre.12 (prerelease)

No significant changes detected since v1.1.2-pre.11.

## v1.1.2-pre.11 (prerelease)

No significant changes detected since v1.1.2-pre.10.

## v1.1.2-pre.10 (prerelease)

No significant changes detected since v1.1.2-pre.9.

## v1.1.2-pre.9 (prerelease)

No significant changes detected since v1.1.2-pre.8.

## v1.1.2-pre.8 (prerelease)

No significant changes detected since v1.1.2-pre.7.

## v1.1.2-pre.7 (prerelease)

No significant changes detected since v1.1.2-pre.6.

## v1.1.2-pre.6 (prerelease)

No significant changes detected since v1.1.2-pre.5.

## v1.1.2-pre.5 (prerelease)

No significant changes detected since v1.1.2-pre.4.

## v1.1.2-pre.4 (prerelease)

No significant changes detected since v1.1.2-pre.3.

## v1.1.2-pre.3 (prerelease)

Changes since v1.1.2-pre.2:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.1.2-pre.2 (prerelease)

No significant changes detected since v1.1.2-pre.1.

## v1.1.2-pre.1 (prerelease)

Changes since v1.1.1:

- Sync scripts\PSBuild.psm1 (ktsu[bot])

## v1.1.1 (patch)

Changes since v1.1.0:

- Remove Directory.Build.props and Directory.Build.targets files; add copyright headers to Git integration classes; delete unused PowerShell scripts for commit metadata, changelog, license, and version management. (matt-edmondson)
- Update project SDK in GitIntegration.csproj and fix README formatting (matt-edmondson)

## v1.1.1-pre.7 (prerelease)

Changes since v1.1.1-pre.6:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.1.1-pre.6 (prerelease)

Changes since v1.1.1-pre.5:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.1.1-pre.5 (prerelease)

Changes since v1.1.1-pre.4:

- Sync .github\workflows\dotnet.yml (ktsu[bot])
- Sync .editorconfig (ktsu[bot])
- Sync .runsettings (ktsu[bot])

## v1.1.1-pre.4 (prerelease)

Changes since v1.1.1-pre.3:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.1.1-pre.3 (prerelease)

Changes since v1.1.1-pre.2:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.1.1-pre.2 (prerelease)

Changes since v1.1.1-pre.1:

- Sync .editorconfig (ktsu[bot])

## v1.1.1-pre.1 (prerelease)

Changes since v1.1.0:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.1.0 (minor)

Changes since v1.0.0-alpha.5:

- Update packages and code style fixes (matt-edmondson)
- Add LICENSE template (matt-edmondson)
- Add mailmap (matt-edmondson)
- Add automation scripts for metadata generation and project management (matt-edmondson)
- Renamed metadata files (matt-edmondson)

## v1.0.0-alpha.5 (prerelease)

Changes since v1.0.0-alpha.4:

- Sync icon.png (ktsu[bot])

## v1.0.0-alpha.4 (prerelease)

Changes since v1.0.0-alpha.3:

- Sync Directory.Build.props (ktsu[bot])

## v1.0.0-alpha.3 (prerelease)

Changes since v1.0.0-alpha.2:

- Sync Directory.Build.props (ktsu[bot])

## v1.0.0-alpha.2 (prerelease)

Changes since v1.0.0-alpha.1:

- Replace LICENSE file with LICENSE.md and update copyright information (matt-edmondson)
- Update class name and package versions (matt-edmondson)
- Update license file format and content (matt-edmondson)
- Bump ktsu.AppDataStorage, ktsu.CredentialCache, and ktsu.StrongPaths package versions for updates (matt-edmondson)
- Bump ktsu.CredentialCache and ktsu.StrongPaths package versions for updates (matt-edmondson)
- Bump ktsu package versions for improved functionality and stability (matt-edmondson)

## v1.0.0-alpha.1 (prerelease)

- Update VERSION to 1.0.0-alpha.1 (matt-edmondson)
- Initial commit (matt-edmondson)


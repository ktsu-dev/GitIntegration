## v2.3.0 (minor)

Changes since v2.2.0:

- [patch] Pin pull.rebase so the conflicting-pull test works off Windows ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Document the Phase 5a remote sync verbs and drop stale phase references ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Parse the forced-push sha range and pin the progress sinks ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add integration tests for the remote sync verbs ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Wire the remote sync verbs into GitRepository ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Cover and fix the pull TryExecuteAsync stderr/stdout join ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the pull verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix the fetch porcelain fixture and make the version probe fail-soft ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the fetch verb builder with its version probe ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the push verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Build the CRLF fetch parser test through Record so the flag survives ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix the fetch CRLF test that passed for the wrong reason ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the porcelain fetch parser ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the porcelain push parser ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add remote sync result models and their exceptions ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Apply two Phase 5a plan fixes from the pre-flight scan ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add the Phase 5a remote sync implementation plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Repair doc comments displaced by the git-required switch ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add cross-platform CI and make missing git fail rather than skip in CI ([@matt-edmondson](https://github.com/matt-edmondson))

## v2.2.1 (patch)

Changes since v2.2.0:

- [patch] Repair doc comments displaced by the git-required switch ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add cross-platform CI and make missing git fail rather than skip in CI ([@matt-edmondson](https://github.com/matt-edmondson))

## v2.2.0 (minor)

Changes since v2.1.0:

- [patch] Document Phase 4 mutating verbs in README and CLAUDE.md ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix review findings: consumer FileNotFoundException, init probe, commit diagnostics ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add tier-3 integration tests against a real git binary ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Wire the mutating verbs into GitRepository and IGitClient ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Make the Testably interface reference private so it stays out of the shipped package ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the clone verb with its destination pre-check ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the init verb with its existing-repository probe ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix typo in commit readback test method name ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix a test-name typo and a test count in the Phase 4 plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the commit verb with its log readback ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the remote add, remove, and set-url verbs ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the checkout verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the branch create and delete verbs ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the add verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Strengthen the Task 8 progress test in the Phase 4 plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add mutating-verb foundations and the builder progress seam ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix two Phase 4 plan defects found in the pre-flight scan ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add the Phase 4 mutating verbs implementation plan ([@matt-edmondson](https://github.com/matt-edmondson))

## v2.1.0 (minor)

Changes since v2.0.0:

- [patch] Rewrite docs for the two-layer local git client + hosting provider library ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Apply final whole-branch review fixes ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add IGitClient, GitClient, and the read-only repository verbs ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the rev-parse verb and the fixed-vector text builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the remote listing verb ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the branch listing verb ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Rename Between parameters to fromRevision and toRevision ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the diff verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the name-status diff parser ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the log verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the log parser ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Fix NUL escape format to match Task 3 convention ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the status verb builder ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the porcelain v2 status parser ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add the git --version verb ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add read-only result models and parsing primitives ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Correct the documented cancellation contract ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Document that the progress sink must be thread-safe ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Fix two plan defects found in the pre-flight cross-task scan ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add the Phase 3 read-only verbs implementation plan ([@matt-edmondson](https://github.com/matt-edmondson))

## v2.0.0 (major)

Changes since v1.1.9:

- [patch] Stop the timeout tests depending on host process speed ([@matt-edmondson](https://github.com/matt-edmondson))
- Merge origin/main into feature/gitintegration-v2 ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Force a non-interactive English git environment via RunCommand 1.5.0 ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Adopt ktsu.RunCommand 1.4.29 cancellation fix ([@matt-edmondson](https://github.com/matt-edmondson))
- [major] Mark GitIntegration v2 as a breaking release ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Refresh spec to match implemented code and upstream fixes ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Trim unused API surface and package references ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Close test-integrity gaps in the execution core ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Remove all three analyzer suppressions ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Fix required owner, timeout race, SHA-256 ids, and result aliasing ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add extension seams, a progress path, and an options snapshot ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Close shell-execute and git option injection holes ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add tests for single-instance guarantee and idempotent options ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add dependency injection registration for git integration ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Narrow AppendVerbArguments to ICollection<string> instead of suppressing CA1002 ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Use ICollection in AppendVerbArguments signature in plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add git command builder base with global argument injection ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Document RunCommand cancellation race in spec constraints ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Classify raced git timeouts alongside faulted ones ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Distinguish git timeout from caller cancellation ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Add GitTimeoutException to spec and plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add RunCommand-backed git process runner ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Fix default-instance and exit-code traps in GitResult and GitCommandException ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Correct GitResult default-state trap and ExitCode sentinel in spec and plan ([@matt-edmondson](https://github.com/matt-edmondson))
- Revert "[patch] Ignore superpowers SDD scratch workspace" ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add git execution contract, result and exception types ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Make repository metadata nullable and fix cross-platform browser launch ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Correct line-ending constraint to match repo .gitattributes ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Migrate semantic types from StrongStrings to Semantics ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Swap package manifest to Semantics and add test project ([@matt-edmondson](https://github.com/matt-edmondson))
- [patch] Ignore superpowers SDD scratch workspace ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add GitIntegration v2 phase 1-2 implementation plan ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Add GitIntegration v2 design spec ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.9 (patch)

Changes since v1.1.8:

- chore: store icon.png in LFS as .gitattributes declares ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: scope build badge to the default branch ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: broaden TAGS.md for better topic coverage ([@matt-edmondson](https://github.com/matt-edmondson))
- docs: correct README, DESCRIPTION and TAGS metadata ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.8 (patch)

Changes since v1.1.7:

- Stop Update SDKs failing when there is nothing to update ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.7 (patch)

Changes since v1.1.6:

- Add PrivateAssets="all" to Polyfill package reference to fix KTSU0007 [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- Sync .editorconfig ([@KtsuTools](https://github.com/KtsuTools))
- Sync global.json ([@KtsuTools](https://github.com/KtsuTools))

## v1.1.6 (patch)

Changes since v1.1.5:

- chore: update ktsu.Sdk to 2.21.1 [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.5 (patch)

Changes since v1.1.4:

- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.4 (patch)

Changes since v1.1.3:

- Sync .github\workflows\update-sdks.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dependabot-merge.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitattributes ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.3 (patch)

Changes since v1.1.2:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.2 (patch)

Changes since v1.1.1:

- fix: clarify credential nullability in Git provider ([@matt-edmondson](https://github.com/matt-edmondson))
- refactor: trim package refs and multi-target GitIntegration ([@matt-edmondson](https://github.com/matt-edmondson))
- Add TAGS.md with NuGet package tags ([@matt-edmondson](https://github.com/matt-edmondson))
- Remove legacy build scripts ([@matt-edmondson](https://github.com/matt-edmondson))
- Update project configuration and scripts, including new SDK management, enhanced CI/CD workflows, and updated copyright information. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update configuration files and scripts for improved build and test processes ([@matt-edmondson](https://github.com/matt-edmondson))
- Update ktsu.AppDataStorage package version to 1.15.5 ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.2-pre.18 (prerelease)

Changes since v1.1.2-pre.17:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitattributes ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitignore ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .mailmap ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.2-pre.17 (prerelease)

Changes since v1.1.2-pre.16:

- Update ktsu.AppDataStorage to 1.15.4 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.2-pre.16 (prerelease)

Changes since v1.1.2-pre.15:

- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitattributes ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitignore ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .mailmap ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

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

- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.2-pre.2 (prerelease)

No significant changes detected since v1.1.2-pre.1.

## v1.1.2-pre.1 (prerelease)

No significant changes detected since v1.1.2.

## v1.1.1 (patch)

Changes since v1.1.0:

- Remove Directory.Build.props and Directory.Build.targets files; add copyright headers to Git integration classes; delete unused PowerShell scripts for commit metadata, changelog, license, and version management. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update project SDK in GitIntegration.csproj and fix README formatting ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.1-pre.7 (prerelease)

Changes since v1.1.1-pre.6:

- Bump the ktsu group with 3 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.1-pre.6 (prerelease)

Changes since v1.1.1-pre.5:

- Bump the ktsu group with 3 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.1-pre.5 (prerelease)

Changes since v1.1.1-pre.4:

- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.1-pre.4 (prerelease)

Changes since v1.1.1-pre.3:

- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.1-pre.3 (prerelease)

Changes since v1.1.1-pre.2:

- Bump the ktsu group with 3 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.1.1-pre.2 (prerelease)

Changes since v1.1.1-pre.1:

- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.1.1-pre.1 (prerelease)

No significant changes detected since v1.1.1.

## v1.1.0 (minor)

Changes since v1.0.0-alpha.5:

- Update packages and code style fixes ([@matt-edmondson](https://github.com/matt-edmondson))
- Add LICENSE template ([@matt-edmondson](https://github.com/matt-edmondson))
- Add mailmap ([@matt-edmondson](https://github.com/matt-edmondson))
- Add automation scripts for metadata generation and project management ([@matt-edmondson](https://github.com/matt-edmondson))
- Renamed metadata files ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.0-alpha.5 (prerelease)

Changes since v1.0.0-alpha.4:

- Sync icon.png ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.0.0-alpha.4 (prerelease)

Changes since v1.0.0-alpha.3:

- Sync Directory.Build.props ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.0.0-alpha.3 (prerelease)

Changes since v1.0.0-alpha.2:

- Sync Directory.Build.props ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.0.0-alpha.2 (prerelease)

Changes since v1.0.0-alpha.1:

- Replace LICENSE file with LICENSE.md and update copyright information ([@matt-edmondson](https://github.com/matt-edmondson))
- Update class name and package versions ([@matt-edmondson](https://github.com/matt-edmondson))
- Update license file format and content ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu.AppDataStorage, ktsu.CredentialCache, and ktsu.StrongPaths package versions for updates ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu.CredentialCache and ktsu.StrongPaths package versions for updates ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu package versions for improved functionality and stability ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.0-alpha.1 (prerelease)

- Update VERSION to 1.0.0-alpha.1 ([@matt-edmondson](https://github.com/matt-edmondson))
- Initial commit ([@matt-edmondson](https://github.com/matt-edmondson))


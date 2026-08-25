# GitIntegration Phase 5b — Hosting Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the hosting layer from a stub into a working subsystem that enumerates repositories and lists and creates pull requests against GitHub and Azure DevOps.

**Architecture:** `IGitHostingProvider` fronts an abstract `GitProvider` with two sealed implementations — `GitHubProvider` over Octokit, `AzureDevOpsProvider` over raw `HttpClient`. Both receive an `HttpMessageHandler` through an `internal` init property, so one `FakeHttpMessageHandler` fakes both in tests without transport types reaching the public API. A single builder collects pull request creation state and delegates execution to a provider-implemented core method, so validation is written once.

**Tech Stack:** .NET (`net10.0;net9.0`), `ktsu.Sdk`, MSTest via `MSTest.Sdk`, Octokit 14, `System.Text.Json`, `ktsu.CredentialCache`, `ktsu.Semantics.Strings`.

**Spec:** `docs/superpowers/specs/2026-08-21-gitintegration-hosting-layer-design.md` — read it; this plan argues from it and does not restate its reasoning.

## Global Constraints

- **Tabs** for indentation. **LF** line endings — `.gitattributes` sets `* text=auto eol=lf`. Do not convert.
- **File-scoped namespaces.** `using` directives go **inside** the namespace. Library namespace `ktsu.GitIntegration`, test namespace `ktsu.GitIntegration.Test`, whatever folder the file is in.
- **Every file's first line** is `// Copyright (c) 2023-2026 ktsu-dev contributors`, then a blank line.
- **No `this.` qualifiers.** Explicit accessibility everywhere, including `public` on interface members.
- **Nullable reference types on; warnings are errors.** `dotnet build` must end `0 Warning(s)`, `0 Error(s)`.
- **XML doc comments required on every public member** — a missing one is a build error. A `<see cref="..."/>` to a type that does not exist yet is CS1574, also an error: never cref a type a later task creates.
- **Zero `[SuppressMessage]` attributes**, and none in project properties. Five shipped phases have none. Every analyzer complaint so far was fixable in code. If you believe a suppression is unavoidable, stop and report a blocker.
- **Every `await` gets `.ConfigureAwait(false)`** (CA2007), library and tests.
- **The library uses `Ensure.NotNull(x)`** from `Polyfill`. `Polyfill` is `PrivateAssets="all"`, so `Ensure` is invisible to the test project — tests use `ArgumentNullException.ThrowIfNull`.
- **Validate arguments before state.** A method that null-checks an argument and also requires object state checks the argument first.
- **MSTest semantic assertions:** `Assert.AreEqual`, `IsNotNull`, `IsNull`, `IsTrue`, `IsFalse`, `AreSame`, `AreNotSame`, `ThrowsExactly`, `ThrowsExactlyAsync`, `CollectionAssert.AreEqual`, `Contains`, `DoesNotContain`, `StringAssert.Contains`. **Never `Assert.IsTrue(a == b)`.**
- **Async test methods end in `Async`** (MSTEST0032/0065). A class needing a token declares `public TestContext TestContext { get; set; } = null!;` and passes `TestContext.CancellationTokenSource.Token`.
- Asserting a **value-returning** call throws needs a discard: `Assert.ThrowsExactly<T>(() => _ = thing.Method(null!));`
- **Analyzer IDE0305** makes `string[] x = y.ToArray();` an **error** for an explicitly-typed local — write `string[] x = [.. y];`. The same expression passed directly as a method argument is not flagged.
- **Comments explain *why*, not what.** Never mention tasks, phases, or plans in shipped source.
- **Build:** `dotnet build`. **Test:** plain `dotnet test`. **NEVER `dotnet test --nologo`** — under Microsoft Testing Platform it runs zero tests and exits 5, which looks like success.
- **No new `PackageReference` is needed for this phase.** Octokit and `ktsu.CredentialCache` are already referenced; `System.Text.Json` is in-box on both target frameworks. If you believe a package is required, stop and report a blocker — a library `PackageReference` added to satisfy analyzer KTSU0006 needs **both** `PrivateAssets="all"` **and** a `VersionOverride` pinned to the lowest version a consumer could resolve, and getting that wrong shipped a `FileNotFoundException` to every consumer in Phase 4.
- **Version tag:** the branch ships as `[minor]` → 2.4.0. Removing `RefreshRemoteRepositories()` and `Repositories` is a deliberate exception to semver, decided because neither ever worked. Recognised tags are `[major]`, `[minor]`, `[patch]`, `[pre]` — `[fix]` is not one and silently fails to signal a bump. Never add `Co-Authored-By` lines. Never edit `VERSION.md`, `CHANGELOG.md`, `LATEST_CHANGELOG.md`, `LICENSE.md`.
- **The test project has `InternalsVisibleTo("ktsu.GitIntegration.Test")`**, so tests reach `internal` types directly.

## Known hazards, learned the expensive way

- **Some file-writing tools silently convert `\t`, `\n`, and `\uXXXX` escapes in supplied content into real control bytes.** The file compiles and tests pass, but the source now holds an unprintable byte that is unreviewable in a diff. This bit five tasks across Phases 3 and 4. After writing any file containing such escapes, verify the bytes:

  ```bash
  python -c "
  import io,sys
  s=io.open(sys.argv[1],encoding='utf-8',newline='').read()
  bad=sorted({hex(ord(c)) for c in s if ord(c)<32 and c not in chr(10)+chr(9)})
  print(('BAD ' if bad else 'clean'), sys.argv[1], bad)
  " path/to/File.cs
  ```

  Only newline (0x0A) and tab (0x09) may appear.

- **Three tests across Phase 5a passed for the wrong reason** — twice a hand-built fixture made a parser read one character off while the test asserted only an unaffected field, and once a test asserted an invocation count for a behaviour the fake could not observe. Two rules follow, and they are binding:
  1. **Never hand-author an API response fixture.** Every JSON fixture comes from Task 1's captured files.
  2. **A test whose name claims a forwarding or wiring behaviour must assert on the specific property it reads.** A count, or "it did not throw", is not an assertion about wiring.

## File Structure

**Created — library:**

| File | Responsibility |
|---|---|
| `GitIntegration/Hosting/IGitHostingProvider.cs` | The public hosting contract, plus `IGitPullRequestCreateBuilder` and `GitPullRequestSpecification` — declared here because `IGitHostingProvider.CreatePullRequest` returns the builder interface, and a return type naming a type no task has created yet does not compile. |
| `GitIntegration/Hosting/GitHostingExceptions.cs` | `GitHostingException` and its four subtypes. |
| `GitIntegration/Hosting/GitPullRequestCreateBuilder.cs` | The one internal builder implementation shared by both providers. |
| `GitIntegration/Hosting/AzureDevOpsProvider.cs` | The Azure DevOps implementation over `HttpClient`. |
| `GitIntegration/Hosting/AzureDevOpsJson.cs` | `internal` DTOs and `JsonSerializerContext` for Azure DevOps payloads. |
| `GitIntegration/Models/GitPullRequest.cs` | The `GitPullRequest` record and `GitPullRequestState`. |

**Modified — library:**

| File | Change |
|---|---|
| `GitIntegration/GitProvider.cs` | Implements `IGitHostingProvider`; loses `RefreshRemoteRepositories()` and `Repositories`; gains the credential resolution, the transport seam, and the create-builder plumbing. |
| `GitIntegration/GitHubProvider.cs` | Real implementations over Octokit, taking the injected handler. |
| `GitIntegration/SemanticTypes/GitProviderTypes.cs` | Four new pull request semantic types. |
| `GitIntegration/ServiceCollectionExtensions.cs` | Nothing in Tasks 1-9; Task 10 only. |

**Created — tests:**

| File | Responsibility |
|---|---|
| `GitIntegration.Test/Fakes/FakeHttpMessageHandler.cs` | Scripts responses **and** records complete requests. |
| `GitIntegration.Test/Fixtures/` | Captured JSON, one file per response shape, from Task 1. |
| `GitIntegration.Test/Hosting/*.cs` | One test class per production type. |

## Task ordering

Tasks 2, 3, and 4 are independent of each other and depend only on Task 1. Tasks 6-7 (GitHub) and 8-9 (Azure DevOps) are independent of each other and both depend on Task 5.

---

### Task 1: Verify the Azure DevOps REST contract and capture fixtures

**This task writes no production code.** Its deliverable is knowledge and fixture files, and every later task depends on it. Do not guess any value in it — a client built against assumed field names works against its author's beliefs rather than the service.

**Files:**
- Create: `GitIntegration.Test/Fixtures/azure-devops-repositories.json`
- Create: `GitIntegration.Test/Fixtures/azure-devops-pullrequests.json`
- Create: `GitIntegration.Test/Fixtures/azure-devops-pullrequest-created.json`
- Create: `GitIntegration.Test/Fixtures/azure-devops-error.json`
- Create: `GitIntegration.Test/Fixtures/github-repositories.json`
- Create: `GitIntegration.Test/Fixtures/github-pullrequests.json`
- Create: `GitIntegration.Test/Fixtures/github-pullrequest-created.json`
- Create: `docs/superpowers/research/2026-08-21-azure-devops-rest-findings.md`

**Interfaces:**
- Consumes: nothing.
- Produces: the fixture files above, and a findings document recording the exact `api-version`, URL templates, JSON field names, pagination mechanism, and error body shape that Tasks 8 and 9 implement against.

- [ ] **Step 1: Read the published reference**

Use WebFetch or the Microsoft Learn tools against the Azure DevOps REST reference. Record, with the documentation URL beside each:

- The current `api-version` value for Git endpoints, and whether it is required.
- `GET /_apis/git/repositories` (organization-wide) and `GET /{project}/_apis/git/repositories` — the response envelope (`count`/`value`?) and each field the spec's model needs: repository id, name, web URL, remote/clone URL, and the project each repository belongs to.
- `GET /{project}/_apis/git/repositories/{repositoryId}/pullrequests` — the query parameter that filters to active pull requests, and the field names for id, title, description, `sourceRefName`, `targetRefName`, `createdBy`, `status`, `isDraft`, creation date, and `_links.web.href`.
- `POST /{project}/_apis/git/repositories/{repositoryId}/pullrequests` — the exact request body property names and the response shape.
- Pagination: which endpoints use `$top`/`$skip`, which use a continuation token, and the header the token arrives in.
- The error response body shape (Azure DevOps typically returns `message` and `typeKey`).
- Whether the rate-limit response carries `Retry-After` or an Azure-specific header.

- [ ] **Step 2: Record the findings**

Write `docs/superpowers/research/2026-08-21-azure-devops-rest-findings.md` with one section per bullet above, each carrying the documentation URL it came from. Later tasks cite this file rather than re-deriving it.

**If the documentation contradicts anything in the spec, say so in the findings and stop to report it** — the spec was written from design reasoning, not from the reference, and it defers to the reference on these details.

- [ ] **Step 3: Capture the response fixtures**

Fixtures must reflect real response shapes, not invented ones. In order of preference:

1. If you have Azure DevOps and GitHub credentials available, call the endpoints and save the responses, **redacting** organization names, project names, user identifiers, email addresses, tokens, and any private repository name. Replace them with obviously-fake stable values (`contoso`, `ExampleProject`, `example-user`).
2. Otherwise, copy the **example response payloads printed in the published reference documentation** verbatim, then redact the same way. Note in the findings file which fixtures came from live calls and which from documentation examples.

Each fixture must include at least two elements in any collection, so that a test asserting on `[0]` cannot pass by accident on a single-element list.

For `github-*.json`, capture from the GitHub REST reference for `GET /users/{owner}/repos`, `GET /repos/{owner}/{repo}/pulls`, and `POST /repos/{owner}/{repo}/pulls`. Octokit parses these, but the tests drive Octokit through a fake handler, so the fixtures must be shaped as GitHub really replies.

- [ ] **Step 4: Make the fixtures reachable from tests**

Fixture files must be copied to the test output directory. Add to `GitIntegration.Test/GitIntegration.Test.csproj`:

```xml
<ItemGroup>
  <None Include="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Verify the fixtures parse**

Run a throwaway check that each file is valid JSON, then delete it:

```bash
for f in GitIntegration.Test/Fixtures/*.json; do python -c "import json,sys; json.load(open(sys.argv[1])); print('ok', sys.argv[1])" "$f"; done
```

Expected: `ok` for every file.

- [ ] **Step 6: Byte-check every fixture**

Run the byte-check command from **Known hazards** against each `.json` file. Expected: `clean` for all.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration.Test/Fixtures GitIntegration.Test/GitIntegration.Test.csproj docs/superpowers/research
git commit -m "[patch] Capture the hosting API response fixtures and REST findings"
```

---

### Task 2: Pull request models and semantic types

**Files:**
- Create: `GitIntegration/Models/GitPullRequest.cs`
- Modify: `GitIntegration/SemanticTypes/GitProviderTypes.cs`
- Test: `GitIntegration.Test/Hosting/GitPullRequestTests.cs`

**Interfaces:**
- Consumes: existing `GitBranchName` from `SemanticTypes/GitRefTypes.cs`.
- Produces: `GitPullRequest` (record, properties exactly as below), `GitPullRequestState` (enum: `Open`, `Merged`, `Closed`), and the semantic types `GitPullRequestNumber`, `GitPullRequestTitle`, `GitPullRequestAuthor`, `GitPullRequestWebURI`.

- [ ] **Step 1: Read the neighbouring types first**

Read `GitIntegration/SemanticTypes/GitProviderTypes.cs` and `GitIntegration/SemanticTypes/GitRefTypes.cs` to see how existing types declare validation attributes, and `GitIntegration/Models/GitFetchResult.cs` for how a result record documents nullability. Match both.

- [ ] **Step 2: Write the failing tests**

In `GitIntegration.Test/Hosting/GitPullRequestTests.cs`:

```csharp
[TestMethod]
public void RejectsANonNumericPullRequestNumber() =>
    Assert.IsFalse(GitPullRequestNumber.TryCreate("not-a-number", out _));

[TestMethod]
public void AcceptsAPositivePullRequestNumber() =>
    Assert.IsTrue(GitPullRequestNumber.TryCreate("42", out _));

[TestMethod]
public void RejectsAnEmptyTitle() =>
    Assert.IsFalse(GitPullRequestTitle.TryCreate(string.Empty, out _));

[TestMethod]
public void CarriesEveryFieldItWasGiven()
{
    GitPullRequest pullRequest = new()
    {
        Number = "42".As<GitPullRequestNumber>(),
        Title = "Add the thing".As<GitPullRequestTitle>(),
        Description = "Because the thing was missing.",
        SourceBranch = "feature/thing".As<GitBranchName>(),
        TargetBranch = "main".As<GitBranchName>(),
        Author = "example-user".As<GitPullRequestAuthor>(),
        State = GitPullRequestState.Open,
        IsDraft = true,
        WebURI = "https://example.invalid/pr/42".As<GitPullRequestWebURI>(),
        CreatedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
    };

    Assert.AreEqual("42".As<GitPullRequestNumber>(), pullRequest.Number);
    Assert.AreEqual("Add the thing".As<GitPullRequestTitle>(), pullRequest.Title);
    Assert.AreEqual("Because the thing was missing.", pullRequest.Description);
    Assert.AreEqual("feature/thing".As<GitBranchName>(), pullRequest.SourceBranch);
    Assert.AreEqual("main".As<GitBranchName>(), pullRequest.TargetBranch);
    Assert.AreEqual("example-user".As<GitPullRequestAuthor>(), pullRequest.Author);
    Assert.AreEqual(GitPullRequestState.Open, pullRequest.State);
    Assert.IsTrue(pullRequest.IsDraft);
    Assert.AreEqual("https://example.invalid/pr/42".As<GitPullRequestWebURI>(), pullRequest.WebURI);
    Assert.AreEqual(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero), pullRequest.CreatedAt);
}

[TestMethod]
public void LeavesTheOptionalFieldsNullWhenNotSupplied()
{
    GitPullRequest pullRequest = new()
    {
        Number = "1".As<GitPullRequestNumber>(),
        Title = "t".As<GitPullRequestTitle>(),
        SourceBranch = "a".As<GitBranchName>(),
        TargetBranch = "b".As<GitBranchName>(),
        State = GitPullRequestState.Closed,
    };

    Assert.IsNull(pullRequest.Description);
    Assert.IsNull(pullRequest.Author);
    Assert.IsNull(pullRequest.WebURI);
    Assert.IsNull(pullRequest.CreatedAt);
    Assert.IsFalse(pullRequest.IsDraft);
}
```

The last test matters more than it looks: null means "not known", which is distinct from "known to be empty", and it is what stops a provider that failed to read a field from being indistinguishable from one whose host genuinely lacks it.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitPullRequestTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 4: Add the semantic types**

Append to `GitIntegration/SemanticTypes/GitProviderTypes.cs`, matching the file's existing declaration style and giving each an XML doc:

```csharp
/// <summary>A pull request's host-assigned number.</summary>
[RegexMatch(@"^[0-9]+$")]
public sealed record GitPullRequestNumber : SemanticString<GitPullRequestNumber> { }

/// <summary>A pull request's title.</summary>
[RegexMatch(@"^.+$")]
public sealed record GitPullRequestTitle : SemanticString<GitPullRequestTitle> { }

/// <summary>
/// The host's identifier for the account that opened a pull request.
/// </summary>
/// <remarks>
/// The hosts do not agree on what identifies a user: GitHub supplies a login, Azure DevOps a
/// unique name that is usually an email address. This type carries whichever the host gave,
/// unaltered, rather than normalising two different concepts into one that matches neither.
/// </remarks>
public sealed record GitPullRequestAuthor : SemanticString<GitPullRequestAuthor> { }

/// <summary>The browser address of a pull request.</summary>
public sealed record GitPullRequestWebURI : SemanticString<GitPullRequestWebURI> { }
```

Check the validation attribute name against the neighbouring types before writing — use whatever that file already uses.

- [ ] **Step 5: Add the model**

Create `GitIntegration/Models/GitPullRequest.cs` with the record exactly as the spec's **Models** section defines it, plus the `GitPullRequestState` enum. Every member gets an XML doc. Document on the record that a null optional field means "not known", not "empty".

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitPullRequestTests"`
Expected: PASS, and `dotnet build` at 0 warnings / 0 errors.

- [ ] **Step 7: Commit**

```bash
git add GitIntegration/Models/GitPullRequest.cs GitIntegration/SemanticTypes/GitProviderTypes.cs GitIntegration.Test/Hosting/GitPullRequestTests.cs
git commit -m "[minor] Add the pull request model and its semantic types"
```

---

### Task 3: The hosting exception hierarchy

**Files:**
- Create: `GitIntegration/Hosting/GitHostingExceptions.cs`
- Test: `GitIntegration.Test/Hosting/GitHostingExceptionTests.cs`

**Interfaces:**
- Consumes: `GitProviderName` from `SemanticTypes/GitProviderTypes.cs`.
- Produces: `GitHostingException` (base), `GitHostingAuthenticationException`, `GitHostingNotFoundException`, `GitHostingRateLimitException`, `GitHostingRequestException`. Every one exposes `ProviderName` (`GitProviderName`), `StatusCode` (`System.Net.HttpStatusCode`), and `ResponseBody` (`string`). `GitHostingRateLimitException` additionally exposes `ResetsAt` (`DateTimeOffset?`).

- [ ] **Step 1: Read the existing hierarchy**

Read `GitIntegration/Execution/GitExceptions.cs`. It has nine types and a settled convention — each carries the context needed to reproduce the failure, and each declares the standard constructor set the analyzers require (CA1032). Match that convention exactly, including how the parameterless and message-only constructors are handled for types with required context.

`GitHostingException` derives from `Exception`, **not** from `GitException`. The spec's **Errors** section says why; put a short version of that reasoning in the type's `<remarks>`.

- [ ] **Step 2: Write the failing tests**

In `GitIntegration.Test/Hosting/GitHostingExceptionTests.cs`, one test per type asserting the context survives construction, plus one asserting the hierarchy:

```csharp
[TestMethod]
public void AnAuthenticationFailureCarriesItsContext()
{
    GitHostingAuthenticationException exception = new(
        "unauthorized", "GitHub".As<GitProviderName>(), HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");

    Assert.AreEqual("GitHub".As<GitProviderName>(), exception.ProviderName);
    Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
    StringAssert.Contains(exception.ResponseBody, "Bad credentials");
    Assert.IsInstanceOfType<GitHostingException>(exception);
}

[TestMethod]
public void ARateLimitFailureCarriesItsResetTime()
{
    DateTimeOffset reset = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    GitHostingRateLimitException exception = new(
        "rate limited", "AzureDevOps".As<GitProviderName>(), HttpStatusCode.TooManyRequests, "{}", reset);

    Assert.AreEqual(reset, exception.ResetsAt);
    Assert.IsInstanceOfType<GitHostingException>(exception);
}

[TestMethod]
public void ARateLimitFailureToleratesAnUnknownResetTime()
{
    GitHostingRateLimitException exception = new(
        "rate limited", "AzureDevOps".As<GitProviderName>(), HttpStatusCode.TooManyRequests, "{}", resetsAt: null);

    Assert.IsNull(exception.ResetsAt);
}

[TestMethod]
public void TheHostingHierarchyIsSeparateFromTheProcessHierarchy()
{
    GitHostingNotFoundException exception = new(
        "missing", "GitHub".As<GitProviderName>(), HttpStatusCode.NotFound, "{}");

    Assert.IsNotInstanceOfType<GitException>(exception);
}
```

Write the equivalent context test for `GitHostingNotFoundException` and `GitHostingRequestException` too — five types, five context tests, plus the two above. Do not collapse them into a loop: a per-type test names the type in its failure output.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitHostingExceptionTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 4: Implement the hierarchy**

Create `GitIntegration/Hosting/GitHostingExceptions.cs`. All five types live in this one file, mirroring how `GitExceptions.cs` holds its nine.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitHostingExceptionTests"`
Expected: PASS, build clean.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/Hosting/GitHostingExceptions.cs GitIntegration.Test/Hosting/GitHostingExceptionTests.cs
git commit -m "[minor] Add the hosting exception hierarchy"
```

---

### Task 4: `FakeHttpMessageHandler`

**Files:**
- Create: `GitIntegration.Test/Fakes/FakeHttpMessageHandler.cs`
- Test: `GitIntegration.Test/Fakes/FakeHttpMessageHandlerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed class FakeHttpMessageHandler : HttpMessageHandler` with:
  - `FakeHttpMessageHandler Respond(HttpStatusCode status, string body, params (string Name, string Value)[] headers)` — queues one response, returns `this` for chaining.
  - `IReadOnlyList<RecordedRequest> Requests { get; }` — every request received, in order.
  - `sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers)`.
  - Throws `InvalidOperationException` when a request arrives with no queued response, naming how many were queued and how many arrived.

- [ ] **Step 1: Read the fakes you are mirroring**

Read `GitIntegration.Test/Fakes/RecordingGitProcessRunner.cs` and `ScriptedGitProcessRunner.cs`. This fake deliberately combines both: it scripts responses **and** records complete requests.

That combination is the whole point. `ScriptedGitProcessRunner` recorded only argument vectors, and a Phase 5a test asserting progress-sink forwarding could therefore observe nothing and passed while asserting nothing at all. **Record the whole request, including headers and body, from the start.**

- [ ] **Step 2: Write the failing tests**

```csharp
[TestMethod]
public async Task RecordsTheMethodUriHeadersAndBodyOfEveryRequestAsync()
{
    using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
        .Respond(HttpStatusCode.OK, "{\"first\":true}")
        .Respond(HttpStatusCode.Created, "{\"second\":true}");
    using HttpClient client = new(handler);

    using HttpRequestMessage first = new(HttpMethod.Get, "https://example.invalid/one");
    first.Headers.Add("Authorization", "Basic dXNlcjpwYXNz");
    _ = await client.SendAsync(first, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    using StringContent payload = new("{\"title\":\"t\"}", Encoding.UTF8, "application/json");
    _ = await client.PostAsync("https://example.invalid/two", payload, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    Assert.AreEqual(2, handler.Requests.Count);

    Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
    Assert.AreEqual(new Uri("https://example.invalid/one"), handler.Requests[0].Uri);
    Assert.AreEqual("Basic dXNlcjpwYXNz", handler.Requests[0].Headers["Authorization"]);

    Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
    Assert.AreEqual(new Uri("https://example.invalid/two"), handler.Requests[1].Uri);
    Assert.AreEqual("{\"title\":\"t\"}", handler.Requests[1].Body);
}

[TestMethod]
public async Task ReturnsQueuedResponsesInOrderAsync()
{
    using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
        .Respond(HttpStatusCode.OK, "first")
        .Respond(HttpStatusCode.NotFound, "second");
    using HttpClient client = new(handler);

    using HttpResponseMessage one = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
    using HttpResponseMessage two = await client.GetAsync(new Uri("https://example.invalid/b"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    Assert.AreEqual(HttpStatusCode.OK, one.StatusCode);
    Assert.AreEqual("first", await one.Content.ReadAsStringAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false));
    Assert.AreEqual(HttpStatusCode.NotFound, two.StatusCode);
    Assert.AreEqual("second", await two.Content.ReadAsStringAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false));
}

[TestMethod]
public async Task ThrowsWhenARequestArrivesWithNoQueuedResponseAsync()
{
    using FakeHttpMessageHandler handler = new();
    using HttpClient client = new(handler);

    await Assert.ThrowsExactlyAsync<InvalidOperationException>(
        async () => await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
        .ConfigureAwait(false);
}

[TestMethod]
public async Task AttachesResponseHeadersWhenGivenAsync()
{
    using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
        .Respond(HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "60"));
    using HttpClient client = new(handler);

    using HttpResponseMessage response = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    Assert.AreEqual("60", response.Headers.GetValues("Retry-After").Single());
}
```

The class needs `public TestContext TestContext { get; set; } = null!;`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~FakeHttpMessageHandlerTests"`
Expected: FAIL — the type does not exist.

- [ ] **Step 4: Implement the fake**

Override `SendAsync`, reading and recording the request body before returning the queued response. A response header that `HttpResponseMessage.Headers` rejects (a content header such as `Content-Type`) must go on `Content.Headers` instead — handle both rather than throwing.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FakeHttpMessageHandlerTests"`
Expected: PASS, build clean.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration.Test/Fakes/FakeHttpMessageHandler.cs GitIntegration.Test/Fakes/FakeHttpMessageHandlerTests.cs
git commit -m "[patch] Add a fake HTTP handler that records requests as well as scripting responses"
```

---

### Task 5: Rework `GitProvider` — the contract, credentials, and the transport seam

This is the task the two provider pairs build on. It removes the two dead members and establishes everything both providers share.

**Files:**
- Create: `GitIntegration/Hosting/IGitHostingProvider.cs`
- Modify: `GitIntegration/GitProvider.cs`
- Test: `GitIntegration.Test/Hosting/GitProviderTests.cs`

**Interfaces:**
- Consumes: `GitPullRequest` (Task 2), `GitHostingException` and subtypes (Task 3), `FakeHttpMessageHandler` (Task 4).
- Produces:
  - `public interface IGitHostingProvider` exactly as the spec's **The abstraction** section defines it.
  - `public abstract class GitProvider : IGitHostingProvider` keeping `Name`, `Owner`, `PersonaGUID`, `IsAuthenticated`, `TryGetCredential`.
  - `internal HttpMessageHandler? Handler { get; init; }` — the transport seam. Null means "construct a real one".
  - `protected HttpClient CreateHttpClient()` — returns a client over `Handler` when set, a real one otherwise.
  - `protected HostingCredential ResolveCredential()` — returns a discriminated result the providers apply; throws for an unrecognised credential subtype.
  - `public abstract Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken ct = default);`
  - `public abstract Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repo, CancellationToken ct = default);`
  - `public interface IGitPullRequestCreateBuilder` — **declared here, not in Task 6**, exactly as the spec's **The create builder** section defines it. `IGitHostingProvider.CreatePullRequest` returns this type, so it must exist by the end of this task or the interface will not compile: a `<see cref="..."/>` or return type naming something a later task creates is CS1574, an error here. Task 6 supplies the implementation.
  - `internal sealed record GitPullRequestSpecification(GitBranchName Source, GitBranchName Target, GitPullRequestTitle Title, string? Description, bool IsDraft)` — also declared here, for the same reason.
  - `public IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repo)` — concrete. Until Task 6 lands, throw `NotImplementedException` with a comment; Task 6 replaces the body with construction of its builder.
  - `protected internal abstract Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repo, GitPullRequestSpecification specification, CancellationToken ct);`

- [ ] **Step 1: Write the failing credential tests**

`ResolveCredential` is where the current code's silent bug lives, so it gets the most coverage. Use a minimal test double deriving from `GitProvider`.

```csharp
[TestMethod]
public void UsesATokenCredential() { /* seed the cache with CredentialWithToken; assert the resolved result carries the token */ }

[TestMethod]
public void UsesAUsernamePasswordCredential() { /* assert both parts survive */ }

[TestMethod]
public void ProceedsUnauthenticatedWhenNoCredentialIsResolved() { /* assert the result reports "none", and does not throw */ }

[TestMethod]
public void ProceedsUnauthenticatedForCredentialWithNothing() { /* same */ }

[TestMethod]
public void ThrowsForAnUnrecognisedCredentialSubtype() { /* assert InvalidOperationException naming the type */ }
```

Fill in each body against the real `ktsu.CredentialCache` API — read it first; do not assume the seeding method's name. If the cache cannot be seeded in-process, introduce a `protected virtual` credential-resolution hook on `GitProvider` that the test double overrides, and say so in your report; do **not** skip these tests.

The last two are the point of the task. Proceeding unauthenticated is legitimate — public repository enumeration works without credentials. Silently ignoring a credential the caller *did* configure is not, and is what ships today.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitProviderTests"`
Expected: FAIL.

- [ ] **Step 3: Write `IGitHostingProvider`**

Create the interface with full XML docs. Document on `GetPullRequestsAsync` that it returns **open** pull requests only, and that this is requested explicitly of each host rather than relying on either vendor's default.

- [ ] **Step 4: Rework `GitProvider`**

Delete `RefreshRemoteRepositories()` and `ConcurrentBag<GitRepository> Repositories`, and drop the now-unused `System.Collections.Concurrent` using. Add the members listed under **Produces**.

`HostingCredential` is a small `internal` type in the same file expressing the three outcomes — token, username and password, or none. A nested discriminated shape or a record with a kind enum are both fine; pick one and document it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitProviderTests"`
Expected: PASS.

- [ ] **Step 6: Confirm nothing else referenced the deleted members**

Run: `dotnet build`
Expected: 0 warnings, 0 errors. `GitHubProvider` still overrides `RefreshRemoteRepositories`, so it **will** fail to compile until Step 7.

- [ ] **Step 7: Keep `GitHubProvider` compiling**

Replace its `RefreshRemoteRepositories` override with `NotImplementedException`-throwing overrides of the three new abstract members. This keeps the tree building between tasks; **Task 7** replaces them with the real implementations.

Two `NotImplementedException`s therefore ship in this task's commit — `GitHubProvider`'s three overrides and `GitProvider.CreatePullRequest`. That is deliberate scaffolding, not an oversight, and it is gone by the end of Task 7. **Say so explicitly in your report** so a reviewer reading this task's diff in isolation does not flag them as incomplete work.

- [ ] **Step 8: Run the full suite and commit**

Run: `dotnet test`
Expected: everything passes.

```bash
git add GitIntegration/Hosting/IGitHostingProvider.cs GitIntegration/GitProvider.cs GitIntegration/GitHubProvider.cs GitIntegration.Test/Hosting/GitProviderTests.cs
git commit -m "[minor] Replace the dead provider members with a real hosting contract"
```

---

### Task 6: The pull request create builder

**Files:**
- Create: `GitIntegration/Hosting/GitPullRequestCreateBuilder.cs`
- Test: `GitIntegration.Test/Hosting/GitPullRequestCreateBuilderTests.cs`

**Interfaces:**
- Consumes: `IGitPullRequestCreateBuilder`, `GitPullRequestSpecification`, and `CreatePullRequestCoreAsync` (all Task 5); `GitPullRequest` (Task 2).
- Produces:
  - `internal sealed class GitPullRequestCreateBuilder : IGitPullRequestCreateBuilder`, constructed as
    `GitPullRequestCreateBuilder(Func<GitPullRequestSpecification, CancellationToken, Task<GitPullRequest>> execute)`.
  - The real body of `GitProvider.CreatePullRequest`, replacing Task 5's `NotImplementedException`.

One builder serves both providers. Validation is written once here, and each provider implements only the call.

**The builder takes a delegate rather than a `GitProvider`.** `GitProvider.CreatePullRequest` supplies `(specification, ct) => CreatePullRequestCoreAsync(repo, specification, ct)`. This keeps the builder ignorant of providers entirely, so its tests need no provider at all — they assert against a recording lambda. A builder holding a provider reference would drag the whole provider surface into every builder test.

- [ ] **Step 1: Write the failing tests**

Because the builder takes a delegate, its own tests need no provider. A local helper captures what the builder produced:

```csharp
private static (GitPullRequestCreateBuilder Builder, Func<GitPullRequestSpecification?> Captured) Recording()
{
    GitPullRequestSpecification? captured = null;
    GitPullRequestCreateBuilder builder = new((specification, _) =>
    {
        captured = specification;
        return Task.FromResult(SomeCannedPullRequest);
    });

    return (builder, () => captured);
}

[TestMethod]
public async Task PassesEveryConfiguredValueToTheExecuteDelegateAsync()
{
    (GitPullRequestCreateBuilder builder, Func<GitPullRequestSpecification?> captured) = Recording();

    _ = await builder
        .From("feature/x".As<GitBranchName>())
        .Into("main".As<GitBranchName>())
        .Titled("Add x".As<GitPullRequestTitle>())
        .Describing("because")
        .AsDraft()
        .ExecuteAsync(TestContext.CancellationTokenSource.Token)
        .ConfigureAwait(false);

    GitPullRequestSpecification specification = captured()!;
    Assert.AreEqual("feature/x".As<GitBranchName>(), specification.Source);
    Assert.AreEqual("main".As<GitBranchName>(), specification.Target);
    Assert.AreEqual("Add x".As<GitPullRequestTitle>(), specification.Title);
    Assert.AreEqual("because", specification.Description);
    Assert.IsTrue(specification.IsDraft);
}

[TestMethod]
public async Task DefaultsDescriptionToNullAndDraftToFalseAsync()
{
    (GitPullRequestCreateBuilder builder, Func<GitPullRequestSpecification?> captured) = Recording();

    _ = await builder
        .From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
        .ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    Assert.IsNull(captured()!.Description);
    Assert.IsFalse(captured()!.IsDraft);
}
```

One further test covers the wiring the delegate design leaves untested — that `GitProvider.CreatePullRequest` actually routes to `CreatePullRequestCoreAsync` with the repository it was given. Use a minimal `GitProvider` double capturing both arguments, and assert on the **repository name and specification it received**, not merely that it was called:

```csharp
[TestMethod]
public async Task ProviderRoutesTheBuilderToItsCoreMethodAsync()
{
    RecordingProvider provider = new() { Owner = "contoso".As<GitProviderOwner>() };

    _ = await provider.CreatePullRequest("repo".As<GitRepositoryName>())
        .From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
        .ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    Assert.AreEqual("repo".As<GitRepositoryName>(), provider.Repository);
    Assert.AreEqual("a".As<GitBranchName>(), provider.Specification!.Source);
}
```

Then one test per missing required value — source, target, title — each asserting `InvalidOperationException` from `ExecuteAsync`, and each asserting the message names the missing member so a caller can act on it:

```csharp
[TestMethod]
public async Task ThrowsWhenTheSourceBranchIsMissingAsync()
{
    (GitPullRequestCreateBuilder builder, _) = Recording();

    InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
        async () => await builder
            .Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
            .ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
        .ConfigureAwait(false);

    StringAssert.Contains(exception.Message, "From");
}
```

Write the `Into` and `Titled` equivalents. Each asserts the message names the missing member, so a caller learns what to set rather than only that something was wrong.

Also assert each fluent method rejects null:

```csharp
[TestMethod]
public void RejectsANullSourceBranch()
{
    (GitPullRequestCreateBuilder builder, _) = Recording();

    Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.From(null!));
}
```

Write the `Into`, `Titled`, and `Describing` equivalents.

`RecordingProvider`, used only by the routing test above, is a private test double in this file deriving from `GitProvider`. It captures the repository and specification handed to `CreatePullRequestCoreAsync`, returns a canned `GitPullRequest`, and throws from the two enumeration members, which that test never reaches.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitPullRequestCreateBuilderTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the builder**

Validation lives in `ExecuteAsync`, not the setters — a caller may supply the parts in any order, so only the finished configuration knows whether it is complete. Document the `InvalidOperationException` on `From`, `Into`, `Titled`, **and** `ExecuteAsync`. That documentation requirement is not optional: a previous phase's review caught exactly this omission.

Guard every fluent argument with `Ensure.NotNull`. The instance is single-use and not thread-safe; say so in the interface's XML docs.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GitPullRequestCreateBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add GitIntegration/Hosting/GitPullRequestCreateBuilder.cs GitIntegration.Test/Hosting/GitPullRequestCreateBuilderTests.cs
git commit -m "[minor] Add the pull request create builder"
```

---

### Task 7: `GitHubProvider`

**Files:**
- Modify: `GitIntegration/GitHubProvider.cs`
- Test: `GitIntegration.Test/Hosting/GitHubProviderTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2-6, plus `GitIntegration.Test/Fixtures/github-*.json`.
- Produces: a sealed `GitHubProvider` implementing the three abstract members over Octokit.

- [ ] **Step 1: Wire Octokit to the injected handler**

Octokit accepts a custom transport:

```csharp
private GitHubClient CreateClient()
{
    ProductHeaderValue product = new(AppDomain.CurrentDomain.FriendlyName);

    return Handler is null
        ? new GitHubClient(product)
        : new GitHubClient(new Connection(product, new HttpClientAdapter(() => Handler)));
}
```

Verify that constructor shape against Octokit 14.0.0 before relying on it — `HttpClientAdapter` and `IHttpClient` are confirmed present, but check the exact `Connection` overload. Adjust if it differs and note the difference in your report.

- [ ] **Step 2: Write the failing tests**

Load fixtures from Task 1. Assert on **parsed fields**, never on counts alone:

```csharp
[TestMethod]
public async Task EnumeratesRepositoriesForTheOwnerAsync()
{
    using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
        .Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
    GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

    IReadOnlyList<GitRepository> repositories =
        await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

    // Assert on the first two repositories' Name and RemotePath against the fixture's real values.
    // A count-only assertion would pass if every field were dropped.
}

[TestMethod]
public async Task MapsAMergedPullRequestToMergedAsync() { /* state:closed + merged:true -> Merged */ }

[TestMethod]
public async Task MapsAClosedUnmergedPullRequestToClosedAsync() { /* state:closed + merged:false -> Closed */ }

[TestMethod]
public async Task MapsAnOpenPullRequestToOpenAsync() { /* state:open -> Open */ }

[TestMethod]
public async Task RequestsOnlyOpenPullRequestsAsync()
{
    // Assert on handler.Requests[0].Uri that the state filter is present and set to open.
    // This is the test that proves the library defines the contract rather than inheriting
    // GitHub's default.
}

[TestMethod]
public async Task SendsTheConfiguredValuesInTheCreateBodyAsync()
{
    // Assert handler.Requests[0].Body contains the title, head, base, and draft values,
    // and that the returned GitPullRequest carries the number and web URI from the fixture.
}
```

**All three state mappings need their own test.** `GitPullRequestState` has three members and the GitHub mapping has three inputs; a table covering two of three is how a previous phase shipped a parser that silently dropped a case.

Add a `private static string Fixture(string name)` helper reading from the fixtures directory, and use it everywhere — no inline JSON.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GitHubProviderTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the three members**

Apply the credential from `ResolveCredential()` to the client. Map Octokit's exceptions to the Task 3 hierarchy — `AuthorizationException` and `RateLimitExceededException` have direct counterparts; `NotFoundException` maps to `GitHostingNotFoundException`; anything else becomes `GitHostingRequestException`.

- [ ] **Step 5: Run the tests to verify they pass, then the whole suite**

Run: `dotnet test`
Expected: all pass, build clean.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/GitHubProvider.cs GitIntegration.Test/Hosting/GitHubProviderTests.cs
git commit -m "[minor] Implement the GitHub provider over Octokit"
```

---

### Task 8: `AzureDevOpsProvider` — repository enumeration

**Files:**
- Create: `GitIntegration/Hosting/AzureDevOpsProvider.cs`
- Create: `GitIntegration/Hosting/AzureDevOpsJson.cs`
- Test: `GitIntegration.Test/Hosting/AzureDevOpsProviderTests.cs`

**Interfaces:**
- Consumes: Tasks 2-6, the Task 1 findings document, and `azure-devops-repositories.json`.
- Produces: `public sealed class AzureDevOpsProvider : GitProvider` with `AzureDevOpsProjectName? Project { get; init; }`, implementing `GetRepositoriesAsync`; and `internal` JSON DTOs with a `JsonSerializerContext`.

**Every URL, `api-version`, and field name in this task comes from Task 1's findings document.** If a value you need is not recorded there, stop and report it rather than inventing one.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public async Task RequestsTheOrganizationWideRepositoryEndpointWhenNoProjectIsSetAsync()
{
    // Assert handler.Requests[0].Uri matches the org-wide template and carries the api-version
    // recorded in the findings document.
}

[TestMethod]
public async Task RequestsTheProjectScopedEndpointWhenAProjectIsSetAsync()
{
    // Same, for the project-scoped template.
}

[TestMethod]
public async Task SendsBasicAuthWithAnEmptyUsernameForATokenCredentialAsync()
{
    // Assert handler.Requests[0].Headers["Authorization"] is "Basic " + base64(":" + token).
    // Decode it in the assertion rather than hardcoding the base64 — a hardcoded string
    // would still match if the username were wrong.
}

[TestMethod]
public async Task SendsNoAuthorizationHeaderWhenUnauthenticatedAsync()
{
    // Assert the header is absent, not empty.
}

[TestMethod]
public async Task ParsesEveryRepositoryFieldAsync()
{
    // Assert Name, RemotePath and WebURI for the first TWO repositories in the fixture.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AzureDevOpsProviderTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the DTOs and enumeration**

Use `System.Text.Json` with a source-generated `JsonSerializerContext` — reflection-based serialization warns under trimming analyzers, which are errors here.

Handle pagination exactly as the findings document records it. If the endpoint pages, **fetch every page**; a provider that silently returns the first page is a data-loss bug that a fixture with two elements will not catch. Add a test with two scripted responses proving the second page is fetched and its contents included.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AzureDevOpsProviderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add GitIntegration/Hosting/AzureDevOpsProvider.cs GitIntegration/Hosting/AzureDevOpsJson.cs GitIntegration.Test/Hosting/AzureDevOpsProviderTests.cs
git commit -m "[minor] Add the Azure DevOps provider with repository enumeration"
```

---

### Task 9: `AzureDevOpsProvider` — pull requests and error mapping

**Files:**
- Modify: `GitIntegration/Hosting/AzureDevOpsProvider.cs`
- Modify: `GitIntegration/Hosting/AzureDevOpsJson.cs`
- Test: `GitIntegration.Test/Hosting/AzureDevOpsProviderTests.cs`

**Interfaces:**
- Consumes: Task 8, plus `azure-devops-pullrequests.json`, `azure-devops-pullrequest-created.json`, `azure-devops-error.json`.
- Produces: `GetPullRequestsAsync` and `CreatePullRequestCoreAsync` on `AzureDevOpsProvider`, and the shared status-to-exception mapping.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public async Task ThrowsWhenPullRequestsAreRequestedWithoutAProjectAsync()
{
    AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>() };

    InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
        async () => await provider.GetPullRequestsAsync("repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
        .ConfigureAwait(false);

    StringAssert.Contains(exception.Message, nameof(AzureDevOpsProvider.Project));
}

[TestMethod]
public async Task ThrowsWhenAPullRequestIsCreatedWithoutAProjectAsync() { /* the same, through the builder */ }

[TestMethod]
public async Task StripsRefsHeadsFromTheBranchNamesAsync()
{
    // The fixture carries refs/heads/... — assert SourceBranch and TargetBranch are bare.
    // This is the normalisation that stops callers branching on host.
}

[TestMethod]
public async Task TakesTheWebUriFromTheWebLinkNotTheApiUrlAsync()
{
    // Assert WebURI equals _links.web.href from the fixture, and is NOT the "url" field.
    // Assert them as different values, so a regression to "url" fails.
}

[TestMethod]
public async Task LeavesTheWebUriNullWhenTheWebLinkIsAbsentAsync() { /* scripted response without _links */ }

[TestMethod]
public async Task MapsActiveCompletedAndAbandonedAsync() { /* all THREE, each asserted */ }

[TestMethod]
public async Task RequestsOnlyActivePullRequestsAsync() { /* assert the status filter on the request URI */ }
```

Then one test per status-to-exception mapping — 401, 403 without rate-limit headers, 403 with them, 404, 429, and one other non-success — each asserting the exact exception type **and** that `StatusCode` and `ResponseBody` survived:

```csharp
[TestMethod]
public async Task MapsUnauthorizedToAnAuthenticationFailureAsync()
{
    using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
        .Respond(HttpStatusCode.Unauthorized, Fixture("azure-devops-error.json"));
    AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

    GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
        async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
        .ConfigureAwait(false);

    Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
    StringAssert.Contains(exception.ResponseBody, "message");
}
```

**403 needs both tests.** With rate-limit headers it is a rate limit; without them it is an authentication failure. A single 403 test would pin only one branch and leave the other free to regress.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AzureDevOpsProviderTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Put the status-to-exception mapping in one `private static` method used by every request path, so the two verbs cannot drift apart.

- [ ] **Step 4: Run the tests to verify they pass, then the whole suite**

Run: `dotnet test`
Expected: all pass, build clean.

- [ ] **Step 5: Commit**

```bash
git add GitIntegration/Hosting GitIntegration.Test/Hosting
git commit -m "[minor] Add Azure DevOps pull requests and hosting error mapping"
```

---

### Task 10: DI registration and documentation

**Files:**
- Modify: `GitIntegration/ServiceCollectionExtensions.cs`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Test: `GitIntegration.Test/Hosting/ServiceCollectionExtensionsHostingTests.cs`

**Interfaces:**
- Consumes: Tasks 5-9.
- Produces: nothing later tasks rely on — this is the last task.

- [ ] **Step 1: Decide what registration means, and write the failing test**

Providers carry `required` init properties (`Owner`), so they cannot be resolved from an empty container. Register the **factory shape** a consumer needs rather than pretending a provider can be constructed without an owner:

```csharp
[TestMethod]
public void ResolvesAGitHubProviderThroughTheRegisteredFactory()
{
    ServiceCollection services = new();
    _ = services.AddGitIntegration();

    ServiceProvider container = services.BuildServiceProvider();
    // Assert the registered factory produces a GitHubProvider for a supplied owner,
    // and that a second call returns a distinct instance.
}
```

If, on reading `ServiceCollectionExtensions.cs`, you conclude no useful registration is possible without inventing configuration this phase does not have, **that is a legitimate finding** — report it, add no registration, and document in `CLAUDE.md` that providers are constructed directly. Do not invent an options type to justify a registration nobody asked for.

- [ ] **Step 2: Implement whichever the previous step concluded**

- [ ] **Step 3: Update `CLAUDE.md`**

Replace the **Hosting layer** paragraph, which currently says Azure DevOps hosting support is not implemented and that `AzureDevOpsProjectName` has no provider behind it. It must now record:

- Both providers, what each is built on, and that one `HttpMessageHandler` seam fakes both.
- That `GetPullRequestsAsync` returns open pull requests only, requested explicitly.
- That Azure DevOps pull request operations require `Project`.
- That the layer has **no integration tier**, and why — its tests verify the client against our understanding of each API, not against the APIs themselves.
- Keep the existing warning about `Microsoft.TeamFoundationServer.Client` and `System.Data.SqlClient`, updated to say the REST approach is what shipped and why the package is still refused.

- [ ] **Step 4: Update `README.md`**

Add the hosting layer to the feature list and add a usage example covering enumeration and creating a pull request. Use real API read from the source, not from this plan.

- [ ] **Step 5: Run the full suite**

Run: `KTSU_GIT_INTEGRATION_TESTS_REQUIRED=1 dotnet test`
Expected: all pass, 0 skipped. The integration tier from earlier phases must still run — this phase adds none of its own.

- [ ] **Step 6: Commit**

```bash
git add GitIntegration/ServiceCollectionExtensions.cs CLAUDE.md README.md GitIntegration.Test/Hosting
git commit -m "[minor] Register and document the hosting layer"
```

---

## Self-review notes

Spec coverage was checked section by section. Every section maps to a task: the abstraction and removals to Task 5, credentials to Task 5, models and semantic types to Task 2, state mapping and both normalisations to Tasks 7 and 9, errors to Tasks 3 and 9, transport and the fake to Tasks 4-5, the research requirement to Task 1, the project-required decision to Task 9, and the documentation amendment to Task 10.

Two spec statements are deliberately **not** tasks: that this phase adds no integration tier (an absence, recorded in Task 10's documentation step) and the versioning decision (recorded in Global Constraints).

Three known-hazard rules are enforced at the point of use rather than only stated once: no hand-authored fixtures (Tasks 1, 7, 8, 9), every enum member gets its own test (Tasks 7 and 9), and forwarding tests must name the property they read (Tasks 4, 7, 8).

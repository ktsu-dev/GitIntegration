# Azure DevOps REST contract findings (Phase 5b, Task 1)

Every value below is attributed to the documentation URL it was read from. Where I could not confirm
a value from the published reference, that is stated explicitly rather than guessed. Nothing here
was written from memory of how the API "usually" works.

Live API calls were not available: no Azure DevOps organization/PAT and no GitHub write credentials
exist in this environment. GitHub's read endpoints are unauthenticated-friendly, so those three
fixtures are genuine live captures against the public GitHub API. Azure DevOps has no unauthenticated
read surface, so all four Azure DevOps fixtures are transcribed from the example payloads printed in
Microsoft's published reference pages, then redacted.

## 1. `api-version` for Git endpoints

**Value: `7.2-preview.2`.** Confirmed on all three Git endpoints this task covers, each stating
"API Version: 7.2-preview.2" and requiring it as a query parameter (`api-version | query | True`).

- Repositories - List: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/repositories/list?view=azure-devops-rest-7.2>
- Pull Requests - Get Pull Requests: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/get-pull-requests?view=azure-devops-rest-7.2>
- Pull Requests - Create: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/create?view=azure-devops-rest-7.2>

`api-version` is marked `Required: True` in the URI parameter table on every one of these pages. It is
not optional.

Note the version string carries a `-preview.2` suffix. That is the documented current value for REST
API 7.2, not a placeholder — do not strip the suffix when building the query string.

## 2. `GET .../_apis/git/repositories` — org-wide and project-scoped

Source: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/repositories/list?view=azure-devops-rest-7.2>

URL template as documented:
`GET https://dev.azure.com/{organization}/{project}/_apis/git/repositories?api-version=7.2-preview.2`

`project` is a path parameter with `Required` left blank (not `True`) — it is optional. The
documentation's own sample request omits it for the org-wide form:
`GET https://dev.azure.com/fabrikam/_apis/git/repositories?api-version=7.2-preview.2`. So both forms
in the brief (org-wide and project-scoped) are the same endpoint with `project` present or absent from
the path, not two different routes.

**Response envelope:** `{ "count": <int>, "value": [ GitRepository, ... ] }` — confirmed by the sample
response on the page.

**`GitRepository` fields relevant to the spec's model** (full field table on the page):

| Spec need | REST field | Notes |
|---|---|---|
| repository id | `id` (uuid) | |
| name | `name` | |
| web URL | `webUrl` | Documented in the schema table but **absent from the sample response** — the example objects show `url` and `remoteUrl` only, no `webUrl`. I could not confirm what a populated `webUrl` value looks like from this page. |
| remote/clone URL | `remoteUrl` | Present in every example object, e.g. `"https://dev.azure.com/fabrikam/_git/Fabrikam-Fiber-Git"`. |
| project | `project` (`TeamProjectReference`: `id`, `name`, `url`, `state`, ...) | Present as a nested object in every example. |

Also present in the schema but not exercised by the spec's model: `sshUrl`, `defaultBranch`,
`isDisabled`, `isFork`, `isInMaintenance`, `size`, `validRemoteUrls`, `_links`, `creationDate`,
`parentRepository`.

**Gap:** the sample response never populates `webUrl`, so I cannot confirm its exact format (e.g.
whether it differs from `remoteUrl` the way the pull-request `url`/`_links.web.href` split does). Task
8 should treat `webUrl` as documented-by-schema-only, not confirmed-by-example.

## 3. `GET .../pullrequests` — listing, filter, and field names

Source: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/get-pull-requests?view=azure-devops-rest-7.2>

URL template: `GET https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullrequests?api-version=7.2-preview.2`

**Filter for active pull requests:** `searchCriteria.status`, type `PullRequestStatus`
(`notSet` | `active` | `abandoned` | `completed` | `all`). Documented default: **"Defaults to Active if
unset."** The spec (see "Which pull requests `GetPullRequestsAsync` returns") requires the filter be
sent explicitly rather than relying on the default — that instruction is compatible with what the
reference documents; nothing here contradicts it. Send `searchCriteria.status=active` explicitly.

**Response envelope:** `{ "value": [ GitPullRequest, ... ], "count": <int> }` — confirmed by three
separate sample responses on the page ("Just completed pull requests", "Pull requests by repository",
"Targeting a specific branch").

**Field names the spec's model needs**, all confirmed present in the `GitPullRequest` schema table and
populated in the sample responses:

| Spec need | REST field | Confirmed in example? |
|---|---|---|
| id | `pullRequestId` (int32) | yes |
| title | `title` | yes |
| description | `description` | yes |
| source branch | `sourceRefName` (e.g. `refs/heads/npaulk/my_work`) | yes |
| target branch | `targetRefName` (e.g. `refs/heads/new_feature`) | yes |
| author | `createdBy` (`IdentityRef`: `id`, `displayName`, `uniqueName`, `url`, `imageUrl`) | yes |
| status | `status` (`PullRequestStatus`: `notSet`\|`active`\|`abandoned`\|`completed`\|`all`) | yes |
| draft flag | `isDraft` (boolean) | **field exists in the schema table but does not appear in any of the three sample response objects on this page** — every example PR is non-draft and the field is simply omitted rather than shown as `false`. Confirms the field name but not a populated example. |
| creation date | `creationDate` (date-time) | yes |
| web link | `_links.web.href` | **not confirmed — see contradiction below.** |

`_links` is documented on `GitPullRequest` only as `Links to other related objects` (type
`ReferenceLinks`, itself just `{ links: object }` with no enumerated key set). **None of the three
sample list responses on this page include a populated `_links` object at all** — the example PR
objects go straight from `reviewers` to `url` with no `_links` key present. The `searchCriteria.
includeLinks` query parameter's description is "Whether to include the `_links` field **on the shallow
references**" (i.e. on embedded objects like the repository reference), which reads as distinct from
whether the top-level PR's own `_links` is populated — I could not confirm which query parameter, if
any, turns on the top-level PR's own `_links`.

## 4. `POST .../pullrequests` — create

Source: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/create?view=azure-devops-rest-7.2>

URL template: `POST https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullrequests?api-version=7.2-preview.2`

**Request body**, from the page's own sample request (verbatim field names):

```json
{
  "sourceRefName": "refs/heads/npaulk/my_work",
  "targetRefName": "refs/heads/new_feature",
  "title": "A new feature",
  "description": "Adding a new feature",
  "reviewers": [ { "id": "<identity-guid>" } ]
}
```

`sourceRefName`, `targetRefName`, `title`, `description` match the spec's builder fields exactly.
`isDraft` is documented in the request body schema table (boolean, "Draft / WIP pull request") but
**does not appear in the sample request** — its presence as a settable field is schema-confirmed, not
example-confirmed.

**Response:** `200 OK` in the schema/responses table, but the worked example's status line reads
`Status code: 201`, and the response body is a full `GitPullRequest`. Task 8/9 authors: **trust the
example's 201, not the table's 200** — response tables on this documentation generator are known to
lag the worked example (see also the discrepancy nowhere else on these three pages, which is otherwise
internally consistent).

The response example is the **only** one of the three fetched pages that shows a populated `_links`
object. Its keys, verbatim: `self`, `repository`, `workItems`, `sourceBranch`, `targetBranch`,
`sourceCommit`, `targetCommit`, `createdBy`, `iterations`. **There is no `web` key.** See the
contradiction section below — this directly bears on the spec's `WebURI` normalisation rule.

## 5. Pagination

- **Pull request list** (`GET .../pullrequests`): uses **`$skip`/`$top`** query parameters, both
  optional integers, documented as `$skip` — "The number of pull requests to ignore" and `$top` —
  "The number of pull requests to retrieve." No continuation-token header is documented on this
  endpoint. Source: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/get-pull-requests?view=azure-devops-rest-7.2>
- **Repository list** (`GET .../repositories`): **no pagination parameters at all** are documented —
  no `$top`/`$skip`, no continuation token. The response is the full `GitRepository[]` for the
  organization or project every time. Source: <https://learn.microsoft.com/en-us/rest/api/azure/devops/git/repositories/list?view=azure-devops-rest-7.2>
- **Continuation-token header:** I found no Azure DevOps Git endpoint in the three pages fetched for
  this task that documents a continuation-token response header (the pattern Azure DevOps uses
  elsewhere, e.g. work item queries, via `x-ms-continuationtoken`). **I could not confirm this
  mechanism exists for any endpoint Task 8/9 implements** — do not build continuation-token handling
  into the Azure DevOps client for repositories or pull requests without further confirmation; treat
  `$skip`/`$top` as the only pagination Task 8/9 needs.

## 6. Error response body shape

**Field names — confirmed from an official Microsoft Learn reference page**, the `WrappedException`
interface: <https://learn.microsoft.com/en-us/javascript/api/azure-devops-extension-api/wrappedexception>

| Field | Type |
|---|---|
| `customProperties` | `{[key: string]: any}` |
| `errorCode` | `number` |
| `eventId` | `number` |
| `helpLink` | `string` |
| `innerException` | `WrappedException` (recursive) |
| `message` | `string` |
| `stackTrace` | `string` |
| `typeKey` | `string` |
| `typeName` | `string` |

This confirms the field **names** `message` and `typeKey` that the spec's Errors section and the task
brief both call out. It is a TypeScript interface reference for the extension SDK, not a REST API
error-body reference page, so I am treating it as strong-but-indirect confirmation of the wire shape,
not a REST-specific guarantee.

**What I could NOT confirm from official documentation:** a populated example instance of this body. No
page I fetched shows a filled-in error response for a Git REST call. `azure-devops-error.json` is
therefore populated with example **values** (`message: "TF401180: The requested pull request was not
found."`, `typeKey: "GitPullRequestNotFoundException"`, `errorCode: 0`, `eventId: 3000`) drawn from
third-party/community-reported real responses (Microsoft Q&A and a public GitHub discussion), **not**
from Microsoft's own published reference. Field *names* used in the fixture are official; field
*values* are illustrative and should not be treated as guaranteed wire text. The fixture also includes
a `$id` property, which is a JSON.NET `PreserveReferencesHandling` artifact reported in those same
community sources — it is **not** part of the documented `WrappedException` interface, and I could not
confirm from an official page whether current Azure DevOps REST responses still emit it. Task 9's
parser should not depend on `$id` being present.

## 7. Rate-limit response

Source: <https://learn.microsoft.com/en-us/azure/devops/integrate/concepts/rate-limits?view=azure-devops>
(official Microsoft Learn conceptual page, not a REST reference page, but directly on-topic and
authoritative for this product).

Confirmed headers, verbatim names and descriptions from the page:

| Header | Meaning |
|---|---|
| `Retry-After` | RFC 6585 header, seconds to wait before retrying. |
| `X-RateLimit-Resource` | Custom header naming the service/threshold reached; documented as unstable across time, "recommend displaying ... but not relying on it for computation." |
| `X-RateLimit-Delay` | How long the request was delayed, seconds with up to 3 decimals. |
| `X-RateLimit-Limit` | Total TSTUs allowed before delays. |
| `X-RateLimit-Remaining` | TSTUs remaining before delays start; 0 once delayed/blocked. |
| `X-RateLimit-Reset` | Unix epoch time when usage returns to 0. |
| `X-RateLimit-Cost` | TSTUs consumed by this request, if present. |

The page also states a **blocked** request (not merely delayed) "receives responses with HTTP code 429
(too many requests)" and a body of the form:
`TF400733: The request has been canceled: Request was blocked due to exceeding usage of resource
<resource name> in namespace <namespace ID>.` — this is plain text embedded in prose on the page, not
JSON; I did not find a JSON-wrapped version of this specific message. It is plausible it arrives inside
the same `WrappedException.message` field as any other error, but I could not confirm that connection
from this page (it does not show a full JSON error envelope for the 429 case).

The spec's Errors section maps `429, or 403 with rate-limit headers` to `GitHostingRateLimitException`.
`Retry-After` is confirmed as the header to read; `X-RateLimit-Reset` is confirmed as the header
carrying the reset time the spec says that exception "carries."

## Fixture provenance

| Fixture | Source | Detail |
|---|---|---|
| `azure-devops-repositories.json` | Documentation example | Repositories - List sample response, verbatim, redacted (`fabrikam`→`contoso`, project/repo names→`ExampleProject`/`AnotherExampleProject`/`example-repo-1`). |
| `azure-devops-pullrequests.json` | Documentation example | Get Pull Requests, "Pull requests by repository" sample (3 active PRs), verbatim, redacted. |
| `azure-devops-pullrequest-created.json` | Documentation example | Create sample response (201), verbatim, redacted. This is the only ADO fixture with a populated `_links`. |
| `azure-devops-error.json` | Field names: documentation (`WrappedException` interface). Field values: community-reported real responses (Microsoft Q&A, a GitHub discussion), not an official filled example. | See section 6 for the exact caveat. |
| `github-repositories.json` | **Live call**, unauthenticated | `GET https://api.github.com/users/octocat/repos?per_page=3`, captured 2026-08-21, redacted (`octocat`→`example-user`, repo names→`example-repo-{1,2,3}`). |
| `github-pullrequests.json` | **Live call**, unauthenticated | `GET https://api.github.com/repos/dotnet/runtime/pulls?state=open&per_page=3`, captured 2026-08-21, redacted (real GitHub usernames→`example-user-{1..4}`, `dotnet/runtime`→`contoso/example-repo`, branch labels redacted, numeric ids replaced with stable fake values). |
| `github-pullrequest-created.json` | **Live call**, unauthenticated, substituted endpoint | `GET https://api.github.com/repos/dotnet/runtime/pulls/132594` (a single open PR), same redaction as above. GitHub's `POST /repos/{owner}/{repo}/pulls` needs write credentials I don't have, and neither GitHub REST doc page fetched for this task (`pulls#create-a-pull-request`) shows a filled JSON response body — it lists only the response *schema*. Per GitHub's own OpenAPI description, `POST .../pulls` and `GET .../pulls/{number}` both return the same `pull-request` object shape, so a live GET of a real, currently-open PR is a genuine server response with the identical schema the create endpoint would return. This is a substitution, not the documented POST call itself — flagged here explicitly rather than left implicit. |

None of the GitHub or Azure DevOps fixtures came from Azure DevOps credentials, since none exist in
this environment (as expected — see the task dispatch notes).

Every top-level list a Task 8/9 test would plausibly assert on (`value` in the two Azure DevOps list
fixtures, the bare array in both GitHub list fixtures) has 3 elements. Some *nested* incidental arrays
inside individual objects — `reviewers` on an Azure DevOps PR, `labels`/`assignees`/`topics` on a
GitHub object — have 0 or 1 elements because that is what the real/documented data actually contains,
and none of those fields appear in the spec's `GitPullRequest` model, so no planned test should index
into them.

## Contradictions and gaps against the spec — report these

1. **`_links.web.href` is not confirmed to exist anywhere in the Azure DevOps Git pull request
   payload.** The spec's "Two normalisations" section states "the web link is at `_links.web.href`."
   The *only* example response across the three fetched pages that shows a populated `_links` at all
   is the Create response, and its keys are `self`, `repository`, `workItems`, `sourceBranch`,
   `targetBranch`, `sourceCommit`, `targetCommit`, `createdBy`, `iterations` — **no `web` key.** The
   `GetPullRequest` (singular) reference page has no example response section at all, and the
   `GetPullRequests` (list) page's three examples never populate `_links`. I could not find any
   official Azure DevOps REST example, across any page fetched for this task, showing a `web` key
   inside a pull request's `_links`. This does not prove the field never exists — `ReferenceLinks` is
   documented only as a generic, unenumerated `{links: object}`, so its actual key set at runtime may
   depend on server version, `includeLinks`, or other unstated conditions — but it means Task 9 cannot
   build `WebURI` population against a confirmed example. **Recommendation for Task 9: treat
   `_links.web.href` as "read defensively, populate `WebURI` only if present" (which is already the
   spec's stated null-means-not-known discipline), and do not add a test asserting `WebURI` is
   populated for a plain PR-list or PR-create response, since neither captured fixture demonstrates
   the field.**
2. **Basic auth with empty username, PAT as password — no contradiction, confirmed.** The spec's
   Credentials table says Azure DevOps uses "Basic, empty username, token as password." The official
   getting-started page confirms this exact scheme:
   `Authorization: Basic BASE64PATSTRING` where the pre-encoded string is `{0}:{1}` with the username
   left `""` and the password the PAT. Source:
   <https://learn.microsoft.com/en-us/rest/api/azure/devops/?view=azure-devops-rest-7.2> (Authenticate
   section).
3. **Response table says `200` for Create, worked example says `201`.** See section 4 — not a
   contradiction with the spec (which doesn't commit to a status code), but a documentation
   inconsistency Task 8/9 authors should know about: trust the worked example's `201`.
4. **No contradiction found on the state-mapping table** (`active`/`completed`/`abandoned` for Azure
   DevOps `status`). The `PullRequestStatus` enumeration on both fetched pull-request pages confirms
   exactly these three values plus `notSet` and `all`, matching the spec's mapping table verbatim.

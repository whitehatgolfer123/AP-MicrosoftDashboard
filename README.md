# Microsoft Ads Performance Auditor

An internal web application that audits Microsoft Advertising accounts reachable through a
manager (MCC) account, and reports what is working, what is not, what changed, and where money
may be going to waste.

**Auditing is read-only by construction** — the reporting path has no write method at all.
Applying a recommendation back to an account is a separate, opt-in capability that is switched
off by default; see *Applying changes* below.

Built with C# on **.NET 10** (the current LTS) and ASP.NET Core Razor Pages.

---

## Current status

| Phase | Scope | State |
|---|---|---|
| 1 | Application foundation, interface, metrics, rules engine, demo data, tests | **Complete** |
| 2 | Microsoft Advertising OAuth authentication and account discovery | **Code complete — needs your credentials to exercise** |
| 3 | Live performance reporting (account, campaign, keyword) | **Code complete — unverified against a live account** |
| 4 | Live search term and keyword auditing | **Code complete — unverified against a live account** |
| 5 | Live change history retrieval | **Code complete — unverified against a live account** |
| 6 | PDF export, persistence, final hardening | CSV export, Entra ID sign-in and the change-approval workflow complete; PDF not started |
| — | Portfolio health scan across all accounts | **Complete** |
| — | Review and apply changes (negative keywords, pausing) | **Code complete — the write path has never run against a live account** |

Phases 2–5 are written against the real `Microsoft.BingAds.SDK` and compile, and their parsing and
mapping layers are unit tested. **They have not been run against a live Microsoft Advertising
account**, because that needs a developer token and an app registration that only you can obtain —
see *Setting up live API access* below. Expect to shake out real-world details (field values,
throttling, account quirks) on the first live run.

---

## Running it

The repository includes `dotnet-env.sh` for the locally-installed SDK. If `dotnet` is already on
your `PATH`, skip the `source` step.

```bash
source ./dotnet-env.sh          # only if dotnet is not already on PATH
dotnet restore
dotnet build
dotnet test                     # 289 tests, no credentials needed
dotnet run --project src/MsAds.Auditor.Web
```

Then open the URL printed in the console.

The OAuth redirect URI is registered as `http://localhost:5114/signin-microsoftads`, so run it on
that port when exercising the Microsoft Advertising connection:

```bash
dotnet run --project src/MsAds.Auditor.Web --no-launch-profile --urls http://localhost:5114
```

The application starts in **demo mode**. Every figure is synthetic, is labelled as such throughout
the interface, and is marked `DEMO DATA` in every CSV export.

---

## Using it

### Account health scan

**Account Health** in the sidebar scans every account the connected user can reach and reports
which way each one moved. All accounts are selected by default; untick any you want to leave out.

It reads account totals only — two light reports per account rather than the twelve a full audit
pulls — so it stays usable as a morning overview across a whole book of clients. Accounts are
queried a few at a time to stay inside Microsoft's API limits, and one account failing never fails
the scan: it appears with its error so the gap is visible.

Each row gives a direction, the figures behind it, and a sparkline, with **Audit** to drill into
the full analysis. Rows are ordered with the accounts needing attention first.

Direction is judged on **efficiency, not spend** — an account spending more is not thereby doing
worse, or better:

| Verdict | Meaning |
|---|---|
| Stopped delivering | Delivered in the previous period, nothing now. The most urgent state. |
| Declining | CPA worsened beyond the change threshold, or spend with no conversions at all. |
| Stable | Movement inside the threshold. |
| Improving | CPA improved beyond the threshold. |
| Dormant | No delivery in either period — idle rather than broken. |
| Unknown | No comparison period, or the account could not be scanned. |

Where an account returns no conversion data the verdict falls back to cost of traffic, and the row
says *"cost and traffic only"* so a favourable direction is not over-read.

### Auditing one account

1. Open the dashboard.
2. Pick a client account, or type an account ID.
3. Choose **Last 7 / 14 / 30 days**.
4. Leave *Compare with previous period* selected to get the preceding equivalent window.
5. Choose which analyses to run — each is a separate report request, so deselecting one is faster.
6. **Generate Report.**
7. Work through Campaign Analysis, Search Term Waste, Keyword Analysis, Change History and
   Recommendations from the sidebar.
8. Export any table to CSV.

Dates are resolved in the **account's own reporting time zone**, and the exact start and end dates
used are shown on every screen and in every export. Today is excluded by default, because its data
is still accumulating and including it would distort a period-on-period comparison.

---

## Architecture

```
src/
  MsAds.Auditor.Core/      Domain, metric calculations, rules engine, demo data, CSV export.
                           No dependency on any advertising API.
  MsAds.Auditor.BingAds/   Microsoft Advertising integration: OAuth, account discovery through
                           the Customer Management service, and the Reporting API data source.
  MsAds.Auditor.Ai/        Optional Claude-backed commentary. Isolated here so Core carries no
                           AI dependency and the tool runs identically without it.
  MsAds.Auditor.Web/       Razor Pages interface, charts, orchestration.
tests/
  MsAds.Auditor.Tests/     xUnit tests for everything above.
```

Everything sits behind two abstractions in `MsAds.Auditor.Core/Abstractions`:

- `IAdsDataSource` — retrieves performance data for a scope.
- `IAccountDirectory` — lists accessible accounts and validates access to one.

Demo and live implementations are interchangeable, which is why the analysis layer is fully
testable without credentials.

### Reading and writing are separate paths

`IAdsDataSource` — the reporting and analysis path — exposes **no write operation at all**. No
amount of auditing can alter an account, and a write cannot be added to that path by accident.

Writes live behind a separate, narrow `IChangeApplier`, which is **disabled by default**. See
*Applying changes* below.

---

## How metrics are calculated

All metrics come from retrieved data through one set of tested helpers
(`Core/Metrics/MetricMath.cs`), so the zero-denominator behaviour is defined in exactly one place.

| Metric | Calculation | Undefined when |
|---|---|---|
| CTR | clicks ÷ impressions × 100 | no impressions |
| Average CPC | spend ÷ clicks | no clicks |
| Conversion rate | conversions ÷ clicks × 100 | no clicks, or no conversion data |
| CPA | spend ÷ conversions | **no conversions** — never shown as an enormous number |
| ROAS | conversion value ÷ spend | no spend, or no conversion value data |
| Percentage change | (current − previous) ÷ \|previous\| × 100 | previous is zero → reported as *New activity* |

An undefined metric renders as `—`, never as zero. The application distinguishes throughout between
**zero conversions**, **missing conversion data**, **an unavailable report column**, and **a report
that failed to download** — and says which it is.

Conversions and conversion value reflect the conversion goals included in the account's reporting
and its configured attribution model. Figures from accounts with different goals or attribution
settings are not combined. Conversions are credited to the click date, so recent days keep accruing.

### Direction is not assumed

Each metric carries a polarity. A rise in conversion value is favourable; a rise in CPA is not; a
rise in spend is neither on its own. Status is never communicated by colour alone — every indicator
carries an arrow glyph and a word.

---

## Optional AI commentary

**Off by default, and free to leave off.** Every figure, finding and recommendation is produced
by the deterministic engine below without any AI involvement. This layer only adds a written
commentary *alongside* that analysis.

Either **OpenAI** or **Anthropic** works — paste a key on the Settings page and pick the service.
The key is encrypted with ASP.NET Core Data Protection before it touches disk, stored outside the
repository, and never shown again beyond its last four characters.

For a shared or deployed instance, set it in configuration instead:

```bash
cd src/MsAds.Auditor.Web
dotnet user-secrets set "Auditor:Ai:Provider" "OpenAi"      # or "Anthropic"
dotnet user-secrets set "Auditor:Ai:ApiKey"   "<api key>"
dotnet user-secrets set "Auditor:Ai:Model"    "gpt-4o-mini" # optional
```

Defaults to `gpt-4o-mini` on OpenAI and `claude-opus-5` on Anthropic. Measured cost on a full demo
report with `gpt-4o-mini` is **about $0.0006** — roughly 0.05p. Settings shows the per-commentary
cost for whichever model is configured, and the provider can be switched without a restart.

### It cannot change the numbers

The model is handed a closed brief of **already-calculated** figures and told to work only from
them. It never sees raw data to compute from, is never asked whether a threshold was crossed,
and is explicitly instructed not to introduce or recalculate a number. The brief also carries:

- which direction is favourable for each metric, so it cannot decide rising spend is good news
- each finding's confidence, so stated uncertainty is not quietly firmed up
- an explicit instruction to draw no conversion conclusions when conversion data is unavailable
- the search term coverage caveat

The prompt and brief are built by `AiPromptBuilder`, a pure function, so these properties are
asserted in tests without calling the API.

### Cost and data, stated plainly

- **Nothing is spent generating a report.** A commentary is a separate click, per report, and the
  result is stored so it is written once rather than re-billed on every page view.
- Each commentary shows its token counts, duration and estimated cost.
- **Enabling this sends that account's figures** — spend, conversions, campaign and search term
  names — to whichever provider you configure. For an agency auditing client accounts, check that
  is acceptable first.
- If the call fails or is rate limited, the report is unaffected and says the commentary was
  unavailable. A missing commentary never costs you an audit.

## The recommendation engine

Deterministic C# rules in `Core/Rules/Implementations`. **No AI service calculates a metric or
decides whether a threshold has been crossed.** Every finding carries five things:

1. the finding, 2. the supporting data, 3. why it was flagged, 4. the suggested next action,
5. any caveats and data limitations.

Rules refuse to draw conclusions from thin data: findings below the configured click threshold are
marked low-confidence and say so, and nothing is called bad purely for having no conversions over a
short period. Where no target CPA or ROAS is configured, the target-based rules switch off and
performance is described relative to the account's own averages instead of against an objective the
application cannot know.

All thresholds are editable at **Settings**, which also lists every active rule and what it looks
for.

---

## Search term waste analysis

The search term table flags terms on configurable rules: spend above a threshold with zero
conversions, CPA above target, a conversion rate well below the account's own, unusually expensive
clicks, and low wording overlap with the keyword that matched.

That last one is a **lexical** check, not a judgement of meaning, and says so in its caveat: a query
can be a strong commercial match while sharing no words with the keyword.

A term is only proposed as a negative keyword when it clears every bar — real spend, real clicks, and
a *confirmed* zero conversions. Terms with insufficient evidence are listed as "Continue monitoring".

**Microsoft Advertising does not publish every search query.** Low-volume and privacy-withheld
queries are excluded, so the spend in this table is a subset of total spend. The interface states
this wherever search term data appears: the absence of a wasteful term is never evidence that none
exists.

---

## Connecting to a live account

Once the credentials below are configured and `Auditor:Mode` is `Live`, open **Connection** in the
sidebar and click *Connect to Microsoft Advertising*. You are sent to Microsoft to sign in; the
application never sees your password. On return it stores only a refresh token, encrypted at rest
with ASP.NET Core Data Protection (set `PersistRefreshToken` to `false` to hold it in memory only
and re-consent after each restart).

The Connection page then lists every account the signed-in user can reach, with each account's own
id, manager (customer) id, currency and reporting time zone.

### Agency and manager account notes

- A developer token is **universal**: one token covers every account the signed-in user can already
  see, and grants no permission of its own. One token is enough for the whole agency.
- Each account carries **its own** `CustomerId`, taken from the account's `ParentCustomerId`. API
  calls against a managed client account must use the client's customer id, not the agency's. The
  application does this automatically and will tell you if you type a manager id where an account
  id belongs.

## Setting up live API access

These steps are manual and must be done by a person with the right access.

1. **Developer token** — request one from the Microsoft Advertising account you will audit with.
   The sandbox accepts the universal token `BBD37VB98`.
2. **Register an Entra ID application** — in the Azure portal, *App registrations* → *New
   registration*, and under supported account types choose **Any Entra ID Tenant + Personal
   Microsoft accounts**. You must sign in with a work or school account; personal Microsoft
   accounts can no longer register applications. Register it as a **web application with a client
   secret** — Microsoft's documentation states that a refresh token issued to a public client is
   bound to the granted device, so a server-side application must not use the `nativeclient`
   redirect URI.
3. **Add the redirect URI** to the registration, matching `Auditor:MicrosoftAdvertising:RedirectUri`
   exactly.
4. **Store the credentials outside source control:**

```bash
cd src/MsAds.Auditor.Web
dotnet user-secrets init
dotnet user-secrets set "Auditor:MicrosoftAdvertising:DeveloperToken" "<token>"
dotnet user-secrets set "Auditor:MicrosoftAdvertising:ClientId"       "<client id>"
dotnet user-secrets set "Auditor:MicrosoftAdvertising:ClientSecret"   "<secret>"
dotnet user-secrets set "Auditor:Mode" "Live"
```

A Microsoft 365 login alone does **not** grant API access: the developer token, the app
registration, and a Microsoft Advertising user with permission on the account are all required.

### Verified API facts

Checked against Microsoft's current documentation (October 2026):

| Item | Value |
|---|---|
| SDK package | `Microsoft.BingAds.SDK` 13.0.30 (targets .NET Standard 2.0, so it runs on .NET 10) |
| OAuth scope | `https://ads.microsoft.com/msads.manage offline_access` |
| Endpoints | `https://login.microsoftonline.com/common/oauth2/v2.0/{authorize,token}` |
| Tenant | `common` — supports both work/school and personal Microsoft accounts |
| Account performance | `AccountPerformanceReportRequest` |
| Campaign performance | `CampaignPerformanceReportRequest` (carries impression share) |
| Keyword performance | `KeywordPerformanceReportRequest` |
| Search terms | `SearchQueryPerformanceReportRequest` |
| Change history | `SearchCampaignChangeHistoryReportRequest` (old and new values, user, date) |
| Negative keyword conflicts | `NegativeKeywordConflictReportRequest` |
| Keyword impression share | `ShareOfVoiceReportRequest` |

A manager (customer) ID and an advertising account ID are **different identifiers and are not
interchangeable**; the application carries both and says so when an ID looks wrong.

---

## Applying changes

The audit can propose changes and apply them, but only through a deliberate path. Reporting stays
read-only; this is a separate capability with its own switches.

**Off by default.** While off, the applier registered in its place refuses everything, so the
read-only behaviour holds even if a request reaches it — a forced POST that bypasses the interface
is still refused server-side.

```bash
dotnet user-secrets set "Auditor:Changes:Enabled"      "true"   # allow exclusions
dotnet user-secrets set "Auditor:Changes:AllowPausing" "true"   # also allow pausing
```

The two are gated separately on purpose: adding a negative keyword and switching off a campaign
are different decisions, and you may want the first without the second.

### What it can and cannot change

| Action | Risk | Gate |
|---|---|---|
| Add a negative keyword to a campaign | Adds an exclusion | `Enabled` |
| Pause a keyword | Stops delivery | `Enabled` **and** `AllowPausing` |
| Pause a campaign | Stops delivery | `Enabled` **and** `AllowPausing` |
| Change budgets or bids | — | Not implemented |
| Edit targeting | — | Not implemented |
| Modify conversion tracking | — | Not implemented |

Only findings that map onto a **single unambiguous platform operation** become proposals.
Everything requiring judgement — reviewing a landing page, rewriting ad copy, reallocating budget —
stays a recommendation to read, because the application cannot make that call.

Negative keywords are proposed as **exact match** deliberately: that blocks the query actually
observed wasting money and nothing wider. A phrase negative on a common word would catch more
variations but risks blocking traffic you want, so that judgement is left to you.

### The approval path

1. **Select.** Nothing is ticked by default. Each proposal shows its target, why it was proposed
   from the account's own figures, and what to be careful of.
2. **Review.** The next screen shows the exact API call each change would make. Nothing is sent.
3. **Confirm.** A confirmation box must be ticked. Applying without it is refused.

Each run is written to a change log with who ran it, when, and which changes landed or failed.
Changes are sent one at a time, so a single rejection cannot take the rest of the batch with it.

In demo mode changes are **simulated** — the workflow runs end to end but nothing is sent, and
every outcome is labelled as simulated.

## Who can open the application

This is a different question from the Microsoft Advertising connection. That connection authorises
the application to **read advertising data** using one shared account; this controls which
**people** may see that data once it is on screen.

Sign-in uses Entra ID and is **single-tenant**: the authority is pinned to your directory, so only
accounts in it can authenticate. Colleagues sign in with their normal work accounts, while the
application keeps using the one shared login underneath for the API.

```bash
cd src/MsAds.Auditor.Web
dotnet user-secrets set "Auditor:SignIn:Enabled"      "true"
dotnet user-secrets set "Auditor:SignIn:TenantId"     "<your directory (tenant) id>"
dotnet user-secrets set "Auditor:SignIn:ClientId"     "<client id>"
dotnet user-secrets set "Auditor:SignIn:ClientSecret" "<secret>"
```

Add `/signin-oidc` and `/signout-callback-oidc` as redirect URIs on the registration. You may reuse
the Microsoft Advertising registration or create a second one — sign-in needs only the `openid`,
`profile` and `email` scopes, and requests no access token.

To narrow access further than "anyone in the directory", list the accounts allowed:

```json
"Auditor": { "SignIn": { "AllowedUsers": [ "tom@example.com", "someone@example.com" ] } }
```

Matching is case-insensitive and checks both the user principal name and the email claim. Removing
someone from the list takes effect at their next sign-in, without touching the directory.

### The exposure guard

Sign-in is **off by default**, so a fresh checkout runs on localhost with no configuration. To stop
that becoming an accidental leak, the application **refuses to start** if it is told to listen on a
network-reachable address while sign-in is off:

```
Refusing to start. The application is set to listen on http://0.0.0.0:5290, which is reachable
beyond this machine, but sign-in is disabled — anyone who could reach that address would see
every client's data.
```

Loopback addresses (`localhost`, `127.0.0.1`, `::1`) are fine. Wildcards (`*`, `+`, `0.0.0.0`,
`[::]`), real IPs and host names are not. `Auditor:SignIn:AllowAnonymousAccessOnNetwork` overrides
it, and should only be used on a trusted isolated network.

## Security

- No credential, access token or refresh token is written to source control; `.gitignore` covers the
  usual accidents.
- Credentials are read from configuration providers (user secrets, environment variables) and are
  never logged or displayed — the interface shows only whether each is present.
- Reports are held **in memory only** and cleared on restart, so client performance data is not
  written to disk. Report files downloaded from the Reporting API are written to a temporary
  directory and deleted immediately after parsing.
- What *is* stored is deliberately limited: the change log, the analysis thresholds, and portfolio
  scan verdicts. See *Persistence* below.
- The Microsoft Advertising refresh token is encrypted at rest with ASP.NET Core Data Protection,
  or held in memory only when `PersistRefreshToken` is `false`.
- Access to the application itself is controlled by Entra ID sign-in, as above.
- Writing to advertising accounts is disabled by default, gated by two separate switches, and
  always requires an explicit per-item confirmation. Every run is recorded in the change log.

---

## Persistence

Most of the application is deliberately stateless. What survives a restart is limited to three
things, and the limit is a privacy decision rather than an oversight:

| Stored | Why |
|---|---|
| The change log | It records what this tool altered in a client's account and who authorised it. If a client asks what changed last Tuesday, there has to be an answer. |
| Analysis thresholds | Operator preferences. Re-entering them after every restart is a nuisance, and they are not client data. |
| Portfolio scan verdicts | So a scan can say which accounts have got *worse* since the last one, rather than only what is true now. |

Everything else — reports, metrics, campaign, keyword and search-term rows, AI commentary — stays
in process memory and is cleared on restart.

The scan history stores the **verdict only**. It does not store the prose reason behind a verdict,
because that reason quotes the account's own spend and conversion figures ("spend fell from £1,200
to £300"), and keeping it would put client performance data on disk through the back door. A
verdict transition is enough to say an account needs a look; the figures are re-read live when
someone opens it.

### Where it lives

SQLite by default, in a single file under the user's local application data — outside the
repository, so it cannot be committed:

| Platform | Path |
|---|---|
| Linux | `~/.local/share/MsAdsAuditor/msads-auditor.db` |
| macOS | `~/Library/Application Support/MsAdsAuditor/msads-auditor.db` |
| Windows | `%LOCALAPPDATA%\MsAdsAuditor\msads-auditor.db` |

A backup is a file copy. There is no server to run, administer or pay for, which suits a
single-machine deployment.

### Configuration

```jsonc
{
  "Auditor": {
    "Persistence": {
      "Provider": "Sqlite",          // Sqlite (default) | Postgres | None
      "ConnectionString": null,       // unset for SQLite; required for Postgres
      "MigrateOnStartup": true,       // apply outstanding migrations when the app starts
      "ChangeLogLimit": 0,            // 0 = keep everything, which is right for an audit trail
      "ScanHistoryLimit": 500
    }
  }
}
```

Set `Provider` to `None` to run with nothing written to disk at all, which is the behaviour the
application had before persistence existed. The change log then lives only for the session.

### A note on PostgreSQL

`Provider: Postgres` is recognised and the Npgsql provider is referenced, but **selecting it
currently throws on startup**, deliberately. EF Core migrations contain provider-specific SQL, and
the migrations in `MsAds.Auditor.Data` are generated for SQLite. There is no PostgreSQL instance
available to verify a second set against, and creating the schema from the model instead would
leave a hosted database outside migration control — working until the first schema change, then
with no upgrade path.

PostgreSQL belongs with hosted deployment (ROADMAP.md Phase 27). When there is an instance to test
against, generate its migrations into their own assembly:

```bash
dotnet ef migrations add InitialCreate \
  --project src/MsAds.Auditor.Data \
  --context AuditorDbContext \
  --output-dir Migrations/Postgres \
  -- --provider postgres
```

### Working with migrations

```bash
# Add one after changing the model
dotnet ef migrations add <Name> --project src/MsAds.Auditor.Data \
  --context AuditorDbContext -- --provider sqlite

# Apply them by hand (MigrateOnStartup does this automatically)
dotnet ef database update --project src/MsAds.Auditor.Data \
  --context AuditorDbContext -- --provider sqlite

# See the SQL without running it
dotnet ef migrations script --project src/MsAds.Auditor.Data \
  --context AuditorDbContext -- --provider sqlite
```

`dotnet ef` is pinned as a local tool, so `dotnet tool restore` is all that is needed to get it.

---

## Downloadable reports

Two PDFs per audit, alongside the CSV exports:

| Artefact | What it is |
|---|---|
| Executive summary | The verdict, the headline figures and the material findings. A few pages, suitable for forwarding. |
| Full account audit | The above plus the campaign, keyword, search-term and change-history detail. |

Both are rendered from the same content model as the dashboard (`Core/Reports/Documents`), so a
PDF and the screen cannot show different numbers for the same account. The renderer makes no
content decisions; it only decides how to draw a heading or a table.

Every page carries its provenance — account, manager id, period, comparison period, time zone,
currency, data source, reconciliation state and generated time — plus a methodology note. A
printed page outlives the screen it came from and will be read by someone who was not there.

**Demo data is marked three times over**: a banner on the face of the document, a line in the
footer of every page, and the PDF's own metadata, plus `-DEMO` in the filename. A demo PDF
reaching a client unlabelled is the worst thing this feature could do.

### Branding

Brand values live in `Auditor:Brand`, defaulting to the palette in the `:root` block of
`wwwroot/css/site.css`. A PDF cannot read CSS, so the palette is necessarily stated twice — and a
test parses the stylesheet and fails if the two disagree, so changing a brand colour in one place
without the other breaks the build rather than quietly producing a PDF in last year's colours.

### Why PdfSharpCore

| Library | Licence | Verdict |
|---|---|---|
| **PdfSharpCore 1.3.67** | MIT | **Chosen.** Free unconditionally. Lower-level, so layout and pagination are hand-rolled. |
| QuestPDF | Community licence needs annual gross revenue under USD 1,000,000 | Better output, but production use above the threshold requires payment. |
| PDFsharp 6.x | AGPL-3.0-only | Copyleft with a network clause — unsuitable for an application served over a network. |

PdfSharpCore depends on SixLabors.ImageSharp, whose 2.1.x line carries advisories with no
Apache-2.0 fix (the fixes are in ImageSharp 3, which moved to a licence with the same revenue
threshold). Every one of those advisories is in an image decoder, and these documents draw no
images, so the vulnerable code is unreachable. The advisories are suppressed in the project files
with that reasoning written out, and a test asserts no image API is used — so if a logo is ever
added, the suppression's justification stops being true and the build fails rather than the
suppression quietly covering a now-reachable vulnerability.

### Fonts

PdfSharpCore has no fonts of its own and embeds one from the host. DejaVu is preferred, then
Liberation, then FreeSans — wide script coverage matters because search terms arrive in whatever
the searcher typed. The Dockerfile installs `fonts-dejavu-core`. Where no font is found, PDF
export reports that clearly and CSV export is unaffected; the application still starts.

---

## Scheduled scans

A portfolio scan can run unattended, so the tool tells you when to look rather than waiting to be
opened. **Off by default**: a scheduled scan calls the Microsoft Advertising API against every
reachable client account without anyone asking it to, and it is pointless before live reporting is
trusted, since scanning demo data tells nobody anything.

```jsonc
{
  "Auditor": {
    "Schedule": {
      "Enabled": false,
      "TimeOfDay": "07:00",        // local time, HH:mm
      "Days": [ "Monday" ],         // omit for Monday only
      "TimeZone": null,             // IANA or Windows id; null = this machine's zone
      "Period": "Last7Days",
      "MaxConcurrency": 4,          // accounts queried at once
      "MaxRetries": 3,
      "InitialRetrySeconds": 60,    // doubles each retry, up to MaxRetrySeconds
      "MaxRetrySeconds": 900
    }
  }
}
```

The schedule is expressed in a named time zone rather than UTC, so "07:00 on Monday" stays 07:00
after the clocks change. The two awkward days a year are handled explicitly: a scheduled time that
does not exist (the spring-forward gap) moves to the first valid instant rather than being skipped,
and one that happens twice (the autumn overlap) runs once, at the earlier instant.

**What it reports.** Only material events, per the roadmap: a scan that failed, or an account whose
health verdict has got *worse* since the last scan. A weekly "nothing changed" message trains people
to ignore the channel it arrives on. Delivery is to the application log for now — Microsoft Graph
mail is Phase 18.

**Retries.** A failed scan backs off exponentially and retries. A scan in which *every* account
failed also counts as a failure worth retrying: the scanner catches each account's error separately
so that one bad account cannot lose the sweep, which means an expired token or a throttled API
otherwise produces a "successful" scan in which nothing was actually read. Whatever happens, the
next scheduled run is computed afresh, so a run that exhausts its retries is left until next time
rather than blocking the schedule.

The schedule runs as an ASP.NET Core hosted service. The roadmap names Hangfire or Azure Functions
"as scale requires"; Hangfire needs a PostgreSQL or SQL Server store, which this deployment does not
have, and one weekly job on one machine does not need a job server.

---

## Testing

```bash
dotnet test
```

289 tests, no live data required, covering metric calculations and zero-value edge cases, reporting
period and comparison-period generation, recommendation thresholds (including the cases where a rule
must stay silent), search term waste classification, missing and incomplete data, account access
validation, export formatting, report-cell parsing and the mapping of live report rows onto the
domain models, the sign-in allow-list and network exposure guard, the health verdict used by the portfolio scan,
the change-approval gates, and the AI brief's factual constraints, plus an end-to-end audit over
the demo data that asserts each planted scenario is found.

---

## Theme

Brand values live in one `:root` block at the top of `wwwroot/css/site.css`. Change them there and
the whole interface follows; nothing else hardcodes a colour.

Chart colours use an emphasis pattern — the current period in brand blue `#1769AA`, the comparison
period in a de-emphasis grey `#8795A8`, dashed. That pair was validated for colour-vision
accessibility against the white card surface (CVD ΔE 15.7 and normal-vision ΔE 18.3, both above the
required floors, both at or above 3:1 contrast), and the dash means the two series never depend on
colour to be told apart.


---

## Working on the Razor views

The Razor source generator in .NET SDK **10.0.401** mis-compiles certain tag helper constructs:
once a page contains a code block (`@if`, `@foreach`, …), any tag helper attribute that takes a
**non-string** value gets its source span mis-mapped, and the generated C# is garbled with
confusing errors far from the real cause. Reproduced on a stock `dotnet new webapp`, so it is the
SDK rather than anything in this project.

The views therefore avoid these constructs. If you add markup, keep to the right-hand column:

| Avoid | Use instead |
|---|---|
| `<partial name="x" model="y" />` | `@await Html.PartialAsync("x", y)` |
| `<text>…</text>` inside a code block | a real element such as `<span>`, or `@:` |
| `asp-for="X"` | `id="X" name="X" value="@Model.X"` (add a hidden `false` companion for checkboxes) |
| `asp-items="…"` | a `@foreach` emitting `<option>` elements |
| `asp-all-route-data="dict"` | `asp-route-<name>="@value"` |
| `asp-append-version="true"` | `?v=@AssetVersionProvider.Current` |
| `@if (x) { <span>y</span> }` on one line | the same block spread over multiple lines |
| `@string.Join(…)` | `@(string.Join(…))` — an implicit expression cannot start with a keyword |
| a multi-line `switch` inside `@( … )` | a local function in the page's `@{ }` block |

String-valued tag helper attributes (`asp-page`, `asp-route-*`, `asp-fragment`, `asp-page-handler`)
are unaffected and are used throughout.

A second trap: a class library that a web project references must take ASP.NET Core types through
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`, **not** a `Microsoft.AspNetCore.*`
NuGet package. Mixing the shared framework with NuGet assemblies breaks tag helper discovery in the
referencing web project, which fails as hundreds of unrelated Razor errors.

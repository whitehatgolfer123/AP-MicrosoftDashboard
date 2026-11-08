# Microsoft Ads Optimisation Platform

**Phased Build Plan and Roadmap**

This roadmap makes the product described by
[`add-people/opti-tool`](https://github.com/add-people/opti-tool) work for Microsoft Advertising.
The Google roadmap is the primary model for scope and sequencing. Microsoft equivalents replace
Google-specific tools and operations; duplicate ideas retain the Google plan's meaning and delivery
order. Microsoft Advertising is the first paid-media integration, while Microsoft Entra ID, Microsoft
Graph, Azure, Microsoft 365, and Microsoft-supported .NET libraries provide the platform.

**Current implementation:** Razor Pages on .NET 10, C#, ASP.NET Core, EF/Core where persistence is
added, the Microsoft Advertising/Bing Ads SDK, and a provider-neutral optimisation engine.

## Guiding principles

- Every phase ends with a runnable application.
- Adapter-first data access: CSV or manual exports work first, Microsoft Advertising pulls work
  through a swappable `IAdDataSource`, and no analysis rule knows the transport.
- Read-only reporting is isolated from writes. Auditing must never require an account mutation.
- Proposal, approval, and execution are separate operations. No change reaches Microsoft Advertising
  without explicit configuration and independent approval.
- Microsoft data is reconciled against Microsoft Advertising totals before it is used for decisions.
- Store the minimum client data necessary. Prefer Azure-managed secrets and encrypted persistence.
- Keep AI advisory: it may explain findings, but cannot alter metrics, scores, or policy gates.
- Use Microsoft-native identity and operations wherever they materially improve integration.
- Preserve the Google roadmap's vertical, portal, scheduler, portal, notification, and hardening
  outcomes while expressing them as Microsoft-native implementations.

## Confirmed Microsoft stack

- **Language and framework:** C# and .NET 10, ASP.NET Core Razor Pages (matching the current codebase)
- **Data access:** EF Core, PostgreSQL, and migrations
- **API integration:** `Microsoft.BingAds.SDK` for Microsoft Advertising reporting and writes
- **Authentication:** Microsoft Entra ID OpenID Connect/OAuth 2.0
- **Permissions:** Microsoft Advertising developer token plus OAuth consent for account access
- **Background work:** ASP.NET Core hosted services now; Hangfire or Azure Functions as scale requires
- **Secrets:** dotnet user secrets locally; Azure Key Vault in hosted environments
- **Hosting:** Azure Container Apps for later production deployment, Azure App Service as an alternative
- **Persistence target:** Azure Database for PostgreSQL, with managed backup and private networking
- **Storage:** Azure Blob Storage only for user-requested artefacts, where necessary
- **Monitoring:** OpenTelemetry, Azure Monitor, and Application Insights
- **Email and collaboration:** Microsoft Graph API for Microsoft 365 notification delivery
- **AI:** Azure OpenAI by default; Claude/OpenAI remain optional and behind the existing provider contract
- **Identity operations:** Entra groups and app roles for staff and tenant separation

## At a glance

States below were re-checked against the code on 8 November 2026, and several phases marked Done
were not. Where a phase is partly delivered it now says so, with the gap named. See
*Verification notes* at the end of this document.

| Phase | Outcome | State |
|---|---|---|
| 0 | Foundations and local environment | Done |
| 1 | Data model and CSV/export ingestion | Partly done — see notes |
| 2 | Entra ID authentication and staff allow-list | Done, no roles beyond an allow-list |
| 3 | Microsoft Ads rule engine and health scoring | Done |
| 4 | Dashboard, account, campaign, keyword, and search-term views | Done |
| 5 | Recommendations and provider-swappable AI commentary | Done |
| 6 | Approvals and controlled Microsoft Advertising writes | Code complete, never run live |
| 7 | Scheduled portfolio scans and job processing | Done |
| 8 | Client onboarding and managed script/export ingestion | Planned |
| 9 | Conversion landing-page and tracking validation | Planned |
| 10 | Hardening, observability, resilience, and recovery | Planned |
| 11 | Client portal and tenant separation | Planned |
| 12 | Microsoft Advertising manager-account read/write integration | In validation |
| 13 | E-commerce and lead-generation verticals | Planned |
| 14 | Additional optimisation scanners | Planned |
| 15 | Guardrails and optimisation findings | Planned |
| 16 | Optimisation presentation and accessibility | Planned |
| 17 | Client frontend application | Planned |
| 18 | Microsoft 365 notification templates and branding | Planned |
| 19 | Manual review and defect remediation | Planned |
| 20 | Separate staff and client sign-in flows | Planned |
| 21 | Microsoft 365/Dataverse account metadata integration | Planned |
| 22 | Daily, approver-gated AI strategist | Planned |
| 23 | Downloadable account and portfolio audit artefacts | Account PDFs done; portfolio PDF left |
| 23b | Product naming, subscription stages, and usage controls | Planned |
| 24 | Approvals-first staff UI and role hardening | Planned |
| 25 | Client portal experience upgrade | Planned |
| 26 | Security, production configuration, and release qualification | Planned |
| 27 | Azure production release | Planned |

---

## Phase 0 — Foundations and environment — Done

- Create a solution containing Core, Microsoft Advertising integration, AI, Web, and test projects.
- Run locally on a supported .NET SDK and configure launch profiles for HTTP and HTTPS.
- Add PostgreSQL for local development, initially to enable persistence and later to support jobs,
  portal data, and audit history.
- Provide one-command local startup, a container definition, and deterministic demo mode.
- Ensure all figures in demo mode are synthetic and visibly labelled.
- Add GitHub build, test, formatting, and secret-scanning checks.

**Microsoft implementation:** Keep the repository runbook operational on macOS, Windows, and Linux;
prefer dev containers or Docker Compose for PostgreSQL; deploy from the same solution to Azure.

## Phase 1 — Data model and ingestion — Partly done

- Model clients, Microsoft Advertising accounts, audit runs, metrics, recommendations, changes, and users.
- Add EF Core and PostgreSQL migrations.
- Derive metrics rather than persist redundant values when a stable formula exists:
  CTR, CPC, CPA, ROAS, conversion rate, and average order value.
- Store impressions, clicks, spend/cost, conversions, conversion value, new-customer conversions,
  and all available impression-share lost values.
- Put Microsoft Advertising, CSV, and spreadsheet access behind `IAdDataSource`; never let the
  rules engine depend on the transport.
- Resolve period boundaries using each account's configured time zone.
- Apply the same impression-weighted aggregation rules used by the source roadmap: never sum an
  already weighted percentage, and preserve unavailable display or Performance Max values as null.
- Verify a complete import against the source sample and reconcile totals before analysis.

## Phase 2 — Authentication — Done

- Use Microsoft Entra ID OpenID Connect for staff sign-in with secure ASP.NET Core cookies.
- Restrict access to allowed tenants, optionally require app roles or Entra groups, and maintain a
  database allow-list for early deployment.
- Separate application roles from advertising-account permissions. Viewer access must not imply the
  ability to initiate an account connection or change Microsoft Advertising.
- Support local demo authentication, then require Entra ID in every hosted environment.
- Add application claims for staff identity, tenant, role, and account access.
- Ensure all approval records identify the actual Entra user rather than a mutable display name.

## Phase 3 — Rule engine and health scoring — Done

- Keep the engine pure and deterministic: build `RuleContext` from metrics, run ordered rules, and
  return severity, score impact, evidence, and a recommendation.
- Start from Microsoft-ready versions of traffic, spend, conversion, delivery, and quality findings:
  no conversions with spend, low ROAS, low CTR, low conversion rate, idle account, low traffic,
  low impression share, lost impression share to budget, lost impression share to rank, low
  conversion volume, and unexpected spend or pacing changes.
- Preserve the source roadmap's waterfall: traffic-starved, delivery, CTR, conversion volume,
  conversion-rate, budget/rank pressure, and visibility headroom.
- Start rules in C# so thresholds and formulas are auditable; move thresholds to configuration only
  when operational evidence justifies it.
- Run analysis automatically on import and make stored findings replaceable and idempotent.
- Start health at 100 and subtract rule impacts clamped to 0-100. Health moves on efficiency and
  diagnosis, not spend alone.

## Phase 4 — Dashboard — Done

- Show portfolio health, account movement, executive summary, period comparison, campaigns,
  keywords, search terms, change history, recommendations, and data quality.
- Expose account totals before deeper campaign and keyword figures.
- Compare current and previous periods using account time zones and date boundaries.
- Use no-client-data mode or synthetic demo data for screenshots and local development.
- Keep one authoritative metric representation shared by pages, exports, emails, and PDFs.

## Phase 5 — Recommendations and AI analysis — Done

- Produce deterministic recommendations without AI: waste classification, delivery changes, budget
  pressure, tracking gaps, conversion quality, and change-history context.
- Add optional AI commentary through a provider contract, with Azure OpenAI as the Microsoft-native
  default and OpenAI/Anthropic-compatible providers as alternatives.
- The AI can explain evidence but cannot edit metrics, rules, severity, health score, approval state,
  or proposal actions.
- Record the provider, model, prompt version, token usage, and estimated cost when commentary is
  generated.
- Exclude secrets, refresh tokens, and unnecessary client data from prompts.
- Fail closed in the recommendation engine if stale or incomplete source data is used.

## Phase 6 — Change management and approval queue — Code complete / live verification required

Microsoft Advertising has a direct write path; controls must be stronger than simple UI confirmation.

- Maintain the change lifecycle: proposed, approved, rejected, queued, submitted, applied, and failed.
- Require a user identity, reason, target, old/new value, requested time, approver, and execution
  result for every change.
- Keep reporting methods free of write operations.
- Gate writes with separate application configuration and live-account authorization.
- Support Microsoft Advertising negative keywords and campaign pause/enable as independently enabled
  actions.
- Validate targets against the currently connected account hierarchy before submission.
- Handle partial failures per item. A rejected change must not prevent unrelated approved items from
  being processed without creating a misleading success state.
- Record every submitted operation and result in an append-only change log.
- Reconcile after an operation and raise a mismatch if Microsoft Advertising does not show the
  expected state.
- Do not enable write operations until reporting is reconciled against live Microsoft UI totals.

## Phase 7 — Scheduling and Microsoft-native job processing — Done

- Add hosted schedules for account health, portfolio health, and required-data checks.
- Use Hangfire on PostgreSQL initially when local or on-premises operation matters.
- Move eligible workloads to Azure Functions or Container Apps Jobs in Azure-hosted production.
- Store job history, retry history, run duration, source-account status, and execution result.
- Apply exponential retry and account-level throttling limits; never fan a full manager hierarchy
  into simultaneous API calls.
- Notify only after a job fails materially, an account needs attention, or a scheduled result changes
  state.
- Make every scheduled scan resumable and independently retryable.

## Phase 8 — Client onboarding and script ingestion

- Onboard a client through Microsoft Entra tenant information and Microsoft Advertising account
  discovery, rather than by manually collecting advertising identifiers when delegation is available.
- Maintain account ownership, manager relationship, currency, time zone, consent, and active status.
- Import Microsoft Advertising exports and legacy agency scripts through explicit, audited adapters.
- Render a dry-run preview before import and record a normalised, reversible import batch.
- Detect account renames, duplicated imports, manager/customer ID confusion, and schema drift.
- Persist onboarding status and required integration credentials as separate secrets or configuration,
  never in imported spreadsheets.
- Add API adapters after CSV and export workflows prove the domain model.

## Phase 9 — Landing-page and tag validation

- Validate Microsoft Advertising conversion tracking configuration and UET landing-page events.
- Verify the configured conversion action, goal value, category, count method, and attribution window.
- Detect UET implementation gaps without claiming browser privacy or consent outcomes that cannot be
  observed from the server.
- Compare tag-reported behaviour with Microsoft Advertising totals and report caveats for consent,
  redirects, cross-domain journeys, and incomplete sessions.
- Add a Microsoft-native website checker, where licensing and website access permit, but never confuse
  a page-check result with advertising conversion value.

## Phase 10 — Hardening and monitoring

- Add health endpoints, correlation IDs, structured logging, dependency telemetry, and application metrics.
- Send alerts for Azure App Service or Container Apps failures, API auth expiry, refresh-token failure,
  rate limits, export drift, job saturation, and overdue approvals.
- Add retry policies for transient Microsoft API failures, Circuit Breaker patterns for repeated failures,
  and bounded concurrency for manager-account scans.
- Validate live credentials at startup only where safe; never write secrets or refresh tokens to logs.
- Encrypt refresh tokens and sensitive configuration with the platform-supported key protection service.
- Add backup and restore tests for PostgreSQL, Blob Storage, Key Vault references, and application configuration.
- Complete privacy, retention, account deletion, tenant isolation, and key rotation procedures.

## Phase 11 — Client portal

- Use separate Entra app roles or tenants for staff and client identities.
- Give clients read access to their own portfolio only; prohibit cross-client navigation and API calls.
- Hide credentials, budgets that they are not entitled to see, approval authorisation, and internal notes.
- Surface current health, explanations, downloadable artefacts, outstanding approvals, and change history.
- Use separate sign-in layouts and application policies for staff and client experiences.
- Keep each data access decision server-side and enforce it in queries, not only Razor page visibility.

## Phase 12 — Microsoft Advertising MCC access — Live pull and controlled push

This is the current integration-critical phase.

### Credentials and connection

- Acquire a Microsoft Advertising developer token and register an Entra application with a matching
  redirect URI.
- Use sandbox credentials for the first OAuth and data-shape exercise.
- Store the client secret or federated credential in Azure Key Vault and never in source control.
- Complete OAuth, obtain and refresh Microsoft Advertising access, store account/currency/time-zone
  relationships, and support multi-account manager discovery.

### Reporting validation

- Validate account, campaign, keyword, search-term, conversion, and change-history reports.
- Reconcile one fixed date range against Microsoft Advertising UI totals before trusting derived rules.
- Test manager/customer ID relationships, empty columns, missing conversion tracking, currencies,
  time zones, empty accounts, and API throttling.
- Repeat reconciliation with a second account that has a different currency/time-zone shape.

### Write validation

- Enable writes only after reporting trust exists.
- Run the approval flow in demo mode.
- Enable account writes only on a low-risk account.
- Add one negative keyword, reconcile it, and record its result.
- Enable pause/enable only after negative-keyword operations are reliable.
- Retry failed batches item by item and verify a rejected item cannot poison the batch result.

## Phase 13 — E-commerce and lead-generation verticals

- Introduce vertical-specific configuration without duplicating the core engine.
- E-commerce: transaction value, new-customer value, product/merchant data, margin, gross profit,
  average order value, and return-adjusted ROAS where source data exists.
- Lead generation: qualified leads, call/conversion quality, cost per qualified lead, pipeline value,
  revenue stages, and Microsoft Advertising offline conversion import where integrated.
- Define explicit score cards for each objective and show which proxy or conversion action is active.
- Require source evidence for conversion values and do not invent offline revenue from page events.

## Phase 14 — Additional optimisation scanners

Implement these in Microsoft Advertising terms and behind shared contracts:

- Budget pacing and loss of impression share to budget.
- Ad rank pressure and lost impression share to rank.
- Wasted search-term spend, near-match waste, and branded/non-branded segmentation.
- Negative-keyword conflicts and search-term eligibility.
- Campaign, ad group, keyword, and conversion-setting drift.
- Duplicate and competing keywords across campaign structures.
- Landing-page URL, query, redirect, and tracking inconsistencies.
- Change-history findings: moved without evidence, repeated failures, and disabled guardrails.
- Portfolio concentration, inactive accounts, and anomalous week-over-week movement.

## Phase 15 — Guardrails and optimisation findings

- Add account-level guardrails for client consent, risk, minimum spend, budget change size, and approval.
- Distinguish a finding from an executable proposal and show the data that supports each finding.
- Prevent proposal actions that are unsupported by source fields or blocked by current account configuration.
- Apply severity, impact, confidence, action type, evidence, and suggested owner.
- Add suppression only when a reason and expiry are recorded.
- Never automatically enable a recommendation; Microsoft Advertising writes remain separate and opt-in.

## Phase 16 — Optimisation presentation and UI/UX

- Present recommendations as evidence, risk, proposed action, estimated effect, and required approval.
- Give each finding a stable identifier for change history, exports, and support.
- Show loading, stale, empty, permission-denied, API-throttled, and reconciliation states explicitly.
- Meet accessible form, table, contrast, keyboard, focus, and screen-reader requirements.
- Preserve one brand implementation for Razor Pages, email, client portal, and downloadable reports.
- Make tables responsive; do not hide decision-critical figures in charts.

## Phase 17 — Client frontend application

- Share domain contracts and generated API/OpenAPI metadata with the staff and client frontend.
- Implement the portal as a separate experience with independent authentication and API policy.
- Keep account health, findings, approvals, exports, and audit history consistent with staff views.
- Use server-side rendered Razor by default; add richer client UI only where a measured workflow needs it.
- Support Microsoft 365 sign-in for eligible external tenants and controlled guest access.

## Phase 18 — Microsoft 365 notifications and brand update

- Send notifications via Microsoft Graph Mail for approved senders and delegated mailboxes, not SMTP in
  application code.
- Template scheduled scan failures, attention-required findings, approvals, rejected changes, and
  completed writes.
- Include recipient-safe deep links, correlation IDs, and direct links to Microsoft Advertising only
  when the recipient has access.
- Keep templates data-driven for subject, summary, action, portal link, and legal/brand footer.
- Record delivery status without storing message body or private account payload longer than required.
- Apply the approved Microsoft-compatible visual kit consistently to staff and client surfaces.

## Phase 19 — Manual review and defect remediation

- Run desktop and mobile UI checks for staff and client flows.
- Exercise each approval and write path with role and account variations.
- Test no-data, stale-data, partial API failure, denied consent, expired OAuth, and duplicated imports.
- Include accessibility, keyboard-only, session expiry, refresh-token expiry, and permission changes.
- Fix production defects found by the reviews before adding the next large feature.
- Add regression coverage for each fixed defect.

## Phase 20 — Staff and client login split

- Separate staff Entra configuration and authorization policies from client/external configuration.
- Allow external guest access only by explicit Entra invitation or verified tenant policy.
- Remove broad staff roles from client-facing routes.
- Support emergency break-glass administration with MFA, JIT entitlement, short-lived access, and audit.
- Review session lifetime, token refresh, sign-out, and sensitive route behavior at every role boundary.

## Phase 21 — Microsoft 365 account metadata

- Integrate approved Microsoft 365, Dataverse, SharePoint, or agency-maintained sources for account owner,
  company, stage, contract, and primary contact data.
- Keep Microsoft Advertising account identity authoritative for advertising identifiers.
- Start with read-only synchronisation; do not overwrite either system's source-of-truth fields.
- Map Graph tenants, users, groups, and Dataverse organisations explicitly.
- Display source, last sync, and sync failure on client records.
- Treat account metadata integration as optional; core Microsoft Advertising audits must work without it.

## Phase 22 — AI strategist

- Generate a daily queue of concrete, evidence-backed proposals, not autonomous account changes.
- Make every proposal explain the finding, target, action, old and new value, source dates, confidence,
  risk, and expiry.
- Reuse Azure OpenAI or the provider contract without altering rule or approval behaviour.
- Support Microsoft-native grounding and prompt injection controls, but never trust external page or
  account text as instructions.
- Keep new proposal types disabled until covered by policy, tests, guardrails, and live validation.
- Require an approver who is independent of the proposing identity for high-risk changes.

## Phase 23 — Downloadable audit artefacts — Account PDFs done

- Provide CSV, executive PDF, account PDF, and portfolio PDF exports.
- Render PDF from one shared content model rather than duplicating dashboard text or calculations.
- Include Microsoft Advertising account identifier, currency, period, data source, demo/live status,
  reconciliation, generated time, and methodology.
- Add branding configuration to prevent hardcoded theme values.
- Secure exports for staff/client roles and set explicit retention.
- Generate files in Azure Blob Storage only when needed for asynchronous delivery; stream small files
  directly where possible.

## Phase 23b — Naming, stages, and usage controls

- Introduce the Microsoft Ads Optimisation product name consistently across the application.
- Distinguish trial, growth, agency, and enterprise capabilities if those commercial stages are used.
- Hide unavailable features by policy and capability, not by DOM visibility alone.
- Use stage-aware AI limits, rate limits, account-count limits, and usage reporting.
- Keep data contracts and route names stable during branding changes.

## Phase 24 — Approvals-first staff redesign

- Make proposals, review queue, evidence, identity, and risk the primary staff workflow.
- Remove Viewer capability that implies approval but grants no operational accountability.
- Define Administrator, Reviewer, Operator, and Client roles with least privilege.
- Operators may propose and prepare changes. Reviewers approve them. Separate configuration cannot
  grant the ability to approve one's own proposal.
- Add bulk review only with individual evidence, target validation, and partial failure handling.
- Add an activity timeline for proposal, approval, execution, reconciliation, and failure.

## Phase 25 — Client portal experience upgrade

- Keep animation, responsive layout, and motion optional and reduced-motion compatible.
- Make health trends and next actions understandable without advertising expertise.
- Keep the staff and client portals visually consistent and accessibly distinct.
- Use Microsoft Fluent-style layout and interaction patterns appropriate to .NET and Microsoft 365.
- Do not hide unresolved data gaps behind success state or polished visuals.

## Phase 26 — Security and production qualification

- Complete secret scanning, dependency scanning, source-code analysis, and container/image scanning.
- Add Data Protection key, PostgreSQL backup, application configuration, and disaster recovery runbooks.
- Verify production settings, HTTPS, cookies, CORS, CSP, forwarded headers, managed identity, Key Vault,
  private networking, telemetry sampling, and log redaction.
- Run Microsoft-supported security response, vulnerability remediation, and patch procedures.
- Exercise live OAuth, live reporting, scheduled scans, approval, write, rollback, reconciliation, and
  notification workflows in a staging Microsoft Advertising account.
- Record the manual production switch-on and the accountable owner.

## Phase 27 — Azure production release

Choose the smallest production service that meets recovery and scale requirements.

Recommended initial architecture:

- Azure Container Apps for the Razor application and background job workers.
- Azure Database for PostgreSQL Flexible Server.
- Azure Blob Storage for requested exports only.
- Azure Key Vault for application secrets.
- Azure Monitor and Application Insights for operations.
- Microsoft Graph for notification delivery.
- Managed identity for Azure resource access wherever supported.

Release requirements:

- Use deployment slots or immutable revisions and automatic rollback.
- Back up PostgreSQL with tested restore procedures.
- Encrypt in transit and at rest; isolate dependencies with private endpoints where practical.
- Keep release configuration in Azure App Configuration or Key Vault references.
- Use a CI/CD pipeline that builds once, tests the artefact, and promotes it through environments.
- Document ownership, support hours, incident response, token expiry, developer-token renewal, and backup
  verification.

## Delivery order

1. Obtain and validate Microsoft Advertising credentials.
2. Reconcile live reporting on one Microsoft Advertising account, then a second account.
3. Complete persistence for change history, scheduled results, settings, and audit evidence.
4. Validate the approval and write path on a low-risk account.
5. Complete PDF and spreadsheet artefacts.
6. Add scheduler-backed portfolio scans.
7. Add onboarding, client portal, notifications, and metadata integrations.
8. Complete hardening and staged production deployment.
9. Enable production writes and scheduled jobs only after reconciliation and recovery tests pass.

## Definition of complete

The platform is complete when:

- an authorised Microsoft Advertising manager can connect and reconcile multiple accounts;
- staff can review deterministic findings and controlled AI commentary with one set of figures;
- proposals have independent approval and a complete change record;
- client access is isolated and policy enforced;
- Microsoft data can be scheduled, exported, persisted, restored, and observed;
- live writes are opt-in and reconciled; and
- the application can be rebuilt, deployed, monitored, backed up, and operated in Azure.

---

## Verification notes

Added 8 November 2026, after checking each phase's bullets against the code rather than against
the status table. Recorded here so the same ground is not re-covered.

### What was marked Done but was not

**Phase 0.** Three of six bullets had never been delivered: there was no CI configuration of any
kind (no `.github/`, and `git log --diff-filter=A` confirmed there never had been), no container
definition, and no formatting or secret-scanning checks. Nor was there a `Directory.Build.props`,
so all five projects repeated `TargetFramework`, `Nullable` and `ImplicitUsings` independently.
All now exist. PostgreSQL for local development is available through `docker-compose.yml` behind a
`postgres` profile, but see the note on providers below.

**Phase 1.** "Add EF Core and PostgreSQL migrations" was marked Done with no EF Core, no Npgsql
and no migrations anywhere in the solution — every store was in-memory and a restart cleared it.
Persistence now exists for the change log, the analysis thresholds and portfolio scan verdicts. The
rest of Phase 1 is genuinely done: metrics are derived rather than stored, impression-weighted
percentages are never summed, unavailable values stay null, `IAdDataSource` is in place as
`IAdsDataSource`, and periods resolve in each account's own time zone.

What Phase 1 still does *not* have, and what "Partly done" refers to:

- No client, user or audit-run entities. The data model covers what must survive a restart, not
  the full relational model the phase describes; the rest has no consumer yet.
- CSV **export** is complete; CSV and spreadsheet **ingestion** does not exist. Phase 8 owns it.

**Phase 2.** Entra sign-in, tenant restriction and the allow-list all work. But there are no
application roles: no Administrator, Reviewer, Operator or Client, and therefore nothing yet
separating "may view" from "may approve a change". Phases 20 and 24 own that, and until then the
Phase 2 bullet about separating application roles from advertising-account permissions is
aspirational rather than implemented.

**Phase 6.** The gates, the per-item refusal and the change log are real and now durable. What the
phase describes and the code does not have:

- No lifecycle state machine. `ChangeStatus` is Previewed / Applied / Failed / Skipped; there is no
  proposed, approved, rejected, queued or submitted state.
- No approval separate from the person proposing. One person selects, reviews and confirms in a
  single flow, and `ChangeRunResult` records who ran it but has no approver field.
- No post-write reconciliation. Nothing re-reads the account to confirm Microsoft Advertising
  shows the expected state.

None of this is a blocker for the sandbox exercise, which tests whether a write lands at all, but
all three are prerequisites for using the write path on a client account.

### Persistence: what is stored, and what deliberately is not

Only three things survive a restart — the change log, the analysis thresholds, and portfolio scan
verdicts. Reports, metrics, campaign/keyword/search-term rows and AI commentary stay in memory, as
they always have.

The scan history stores the **verdict only**, not the prose reason behind it. That reason quotes the
account's own spend and conversion figures, and storing it would put client performance data on
disk through the back door. A verdict transition is enough to flag an account for attention.

### Provider choice: SQLite, not PostgreSQL

The *Confirmed Microsoft stack* section names EF Core with PostgreSQL. The implementation uses EF
Core with **SQLite** as the working provider, for two reasons:

1. There is no budget for paid services and no Azure subscription, so Azure Database for PostgreSQL
   is not available. SQLite needs no server, costs nothing, and a backup is a file copy — which
   suits the single machine this actually runs on.
2. EF Core migrations contain provider-specific SQL. The migrations here are generated for SQLite,
   and there is no PostgreSQL instance available to verify a second set against. Selecting
   `Provider: Postgres` therefore throws on startup rather than creating a schema outside migration
   control, which would work until the first schema change and then have no upgrade path.

PostgreSQL remains the right target for a hosted deployment and belongs with Phase 27. The README
records the command to generate its migrations.

Two provider-specific faults were found and fixed while building this, both of which would only
have appeared at runtime: SQLite refuses to order by a `DateTimeOffset` (instants are now stored as
UTC ticks), and a `decimal` left to the provider default becomes a floating-point `REAL` that would
round money (decimals are now mapped with explicit precision).

### Scheduling: hosted service, not Hangfire

Phase 7 names Hangfire on PostgreSQL as the starting point. Hangfire needs a PostgreSQL or SQL
Server store, which this deployment does not have, and one weekly job on one machine does not need
a job server. The phase's own wording — "ASP.NET Core hosted services now; Hangfire or Azure
Functions as scale requires" — is what has been followed.

### Azure-dependent phases

Phases 10, 18, 21, 26 and 27 assume Azure resources (Key Vault, Container Apps, Blob Storage,
Azure OpenAI, Application Insights) or a Microsoft 365 tenant with Graph mail permissions. None of
those exist, and the no-budget constraint means they cannot be provisioned. Work on these phases
can go as far as configuration, abstractions and deployment manifests; it cannot be exercised. Each
should be treated as blocked rather than planned until there is a subscription.

### Blocked on credentials

Phases 12 and the live half of 6 need a Microsoft Advertising developer token and an Entra app
registration, which are being arranged. The sandbox developer token `BBD37VB98` allows a dry run
of the OAuth round trip and the report shapes before a production token is approved.

One thing worth doing before that exercise: `BingAdsReportingDataSource` is the largest file in the
integration project and only its one static time-zone helper is covered by tests. The parse layer
beneath it is well covered, so the risk is concentrated in the submit / poll / download / unzip
orchestration — which is exactly the part sandbox credentials will exercise first. Likewise
`BingAdsChangeApplier`, the only code that mutates a live account, has no test coverage at all: the
gating around it is proven, the sending is not.

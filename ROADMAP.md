# Roadmap

What is finished, what is left, and what each remaining item actually involves.

Last reviewed: 20 October 2026 — build clean, 289/289 tests passing.

---

## At a glance

### Done

- [x] Application foundation — .NET 10, Razor Pages, project layout, DI wiring
- [x] Demo mode with synthetic accounts, labelled as demo everywhere
- [x] Metric engine — spend, clicks, CTR, CPC, conversions, CPA, ROAS, impression share
- [x] Period and comparison-period resolution, per-account time zone
- [x] Recommendation engine — account, delivery, conversion and change-history rules
- [x] Search term waste analysis and classification
- [x] Executive summary generation
- [x] Portfolio health scan across every reachable account
- [x] Interface — dashboard, campaigns, keywords, search terms, history, recommendations, scan
- [x] CSV export
- [x] Entra ID sign-in, allow-list, network exposure guard
- [x] Optional AI commentary (Claude and OpenAI narrators, cost shown, cannot alter figures)
- [x] OAuth connection flow and account discovery *(code complete)*
- [x] Live reporting — account, campaign, keyword *(code complete)*
- [x] Live search term and keyword auditing *(code complete)*
- [x] Live change history retrieval *(code complete)*
- [x] Change proposals, three-step approval path, two independent write gates *(code complete)*
- [x] Change log with who, when and outcome *(in memory)*
- [x] 289 unit tests, no credentials required
- [x] `start.sh` for running the project from a terminal

### Left

- [ ] **Obtain live credentials** — developer token and Entra app registration
- [ ] **Verify live reporting against a real account** (phases 2–5 have never run live)
- [ ] **Verify the write path against a real account** (has never run live)
- [ ] **PDF export** — not started
- [ ] **Persistence** — reports, scans, settings and the change log are lost on restart
- [ ] Scheduled or unattended scans
- [ ] Deployment — where this runs day to day, and how it is backed up

---

## 1. Obtain live credentials — blocks items 2 and 3

Nothing live can be tested until these exist. All three are needed; a Microsoft 365 login on its
own grants nothing.

| Needed | Where from | Notes |
|---|---|---|
| Developer token | Microsoft Advertising, from the account you audit with | Universal — one token covers every account you can already see. Sandbox token `BBD37VB98` works for a dry run first. |
| Entra app registration | Azure portal → App registrations | Web app **with a client secret**. Supported account types: *Any Entra ID tenant + personal Microsoft accounts*. Must be registered from a work or school account. |
| Redirect URI | On that registration | Must match `Auditor:MicrosoftAdvertising:RedirectUri` exactly — `http://localhost:5114/signin-microsoftads` as configured. |

Then store them outside source control and switch the mode (full commands in the README, *Setting
up live API access*).

**Effort:** mostly waiting on Microsoft's token approval. The configuration itself is ten minutes.

---

## 2. Verify live reporting — the largest remaining risk

Phases 2–5 are written against the real `Microsoft.BingAds.SDK`, compile, and their parsing and
mapping layers are unit tested against recorded report shapes. They have **never executed against a
live account**. Expect real-world details to shake out on the first run.

Work through it in this order, one account first, then a second with a different shape:

- [ ] Connect — OAuth round trip completes and a refresh token is stored
- [ ] Account discovery — every account you expect is listed, with the right customer id, currency and time zone
- [ ] Account performance report — figures reconcile against the Microsoft Advertising interface for the same dates
- [ ] Campaign report — including impression share
- [ ] Keyword report
- [ ] Search term report — and that waste classification looks sane on real queries
- [ ] Change history
- [ ] Portfolio scan across the whole MCC — watch for throttling with many accounts
- [ ] A second account on a different currency and time zone

**What to watch for:** throttling under a full-MCC scan, report columns that arrive empty or in an
unexpected format, accounts with no conversion tracking, and manager-vs-account id mix-ups (the
application should catch these and say so — confirm it does).

**Reconciliation is the real test.** A report that returns without error but disagrees with the
Microsoft interface is worse than one that fails. Check totals for one account over one fixed date
range before trusting anything downstream.

---

## 3. Verify the write path

Separate from reporting and riskier, so treat it as its own exercise. The gates are off by default
and stay that way until reporting is trusted.

- [ ] Run the approval flow end to end in demo mode once more, to re-read every screen
- [ ] Enable `Auditor:Changes:Enabled` only, on a **low-stakes account**
- [ ] Apply a single negative keyword; confirm it appears in the account and in the change log
- [ ] Only then consider `AllowPausing`, and only on something you are content to switch off
- [ ] Confirm a rejected change does not take the rest of the batch with it

**Do not test pausing on a live client account first.** A paused campaign is visible to the client
within minutes. Use your own account, or an account with negligible spend.

---

## 4. PDF export — not started

CSV export is complete; PDF is the only unstarted feature. The content already exists — the
executive summary, metric tables and recommendations are all generated for the interface — so this
is rendering work, not analysis work.

Decisions to make first:
- **Library.** Needs to be free and permissively licensed, given the no-budget constraint. QuestPDF's
  Community licence is free below a revenue threshold; check that threshold against the agency
  before committing to it.
- **Scope.** One account audit per PDF, or a portfolio summary too?
- **Branding.** The theme lives in one `:root` block in `wwwroot/css/site.css`; the PDF should pull
  the same brand values rather than hardcoding them again.

**Effort:** a day or so once the library is chosen.

---

## 5. Persistence — everything is in memory

Reports, scans, settings, narratives and the change log are all registered as in-memory stores in
`Program.cs`, so **a restart loses all of them**. That was a deliberate privacy decision for client
performance data, and it should stay that way for reports. Two things arguably should survive:

- **The change log.** It records what was changed in client accounts, by whom. Losing that on a
  restart is the one that would actually hurt — if a client asks what changed last Tuesday, there
  needs to be an answer.
- **Settings.** A minor annoyance rather than a risk; re-entering them after each restart.

The refresh token already persists correctly, encrypted with Data Protection.

**Suggested approach:** SQLite for the change log only — free, no server, one file to back up, and
it keeps client performance data out of storage entirely, which is the property worth protecting.

---

## 6. Scheduled or unattended scans

Not built, and not required for the tool to be useful. Worth considering once live reporting is
trusted: a portfolio scan that runs weekly and flags accounts whose health verdict has dropped
would turn this from something you open into something that tells you when to look.

Depends on persistence (item 5) — a scheduled scan with nowhere to write its result is pointless.

---

## 7. Deployment

Currently runs from a terminal on one machine. Before it becomes something the agency relies on,
decide where it lives, who can reach it, and what gets backed up.

The exposure guard and Entra sign-in already exist, so the security groundwork is done — the
question is hosting, and that runs into the no-budget constraint. An internal machine that is
already paid for is the obvious answer.

---

## Suggested order

1. Request the developer token now — it is the long pole and everything waits on it
2. While waiting: PDF export (item 4), which needs no credentials
3. Credentials arrive → verify live reporting (item 2), one account at a time
4. Change-log persistence (item 5)
5. Write path on a low-stakes account (item 3)
6. Deployment (item 7), then scheduling (item 6) if it earns its place

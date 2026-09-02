# Marking Calendar V2 — Public History Implementation Plan

> Execute only after stages A, B and C are complete and verified. Apply test-driven development and commit every numbered task separately.

**Goal:** Publish a daily, validated calendar change history from the shared C# core and make a complete public history available to new installations.

**Architecture:** A cross-platform runner reuses Core and Infrastructure and writes deterministic data artifacts. GitHub Actions owns publication to an orphan `data` branch. The desktop app downloads a strict, bounded public feed and merges it with local history using snapshot lineage.

**Tech Stack:** .NET 10, xUnit, `HttpClient`, `System.Text.Json`, `XmlWriter`, PowerShell, GitHub Actions, WPF/WebView2, Vitest.

---

### D1 — Add the cross-platform history runner

**Files:** create `src/MarkingCalendar.Runner/**`, runner tests and resource; modify solution, `ChangeBatch`, store retention, update service and CI.

- [ ] Add failing compatibility tests for nullable snapshot ids/source and runner integration tests for seed, changed, duplicate, anomaly, dry-run and `--accept-anomaly`.
- [ ] Expose the fetched raw payload without duplicating HTTP/parser logic.
- [ ] Implement `check --data ...` with deterministic stdout and exit codes 0/2/3/4.
- [ ] Keep the default local history limit unchanged and make the public limit configurable (500).
- [ ] Add an Ubuntu job for Core, Infrastructure and Runner; keep App tests Windows-only.
- [ ] Commit `feat: add history runner`.

### D2 — Publish to an orphan data branch

**Files:** create `.github/workflows/history.yml`; add any small runner manifest output types/tests.

- [ ] Start with `workflow_dispatch` only; implement concurrency, contents write permission and first-run orphan initialization.
- [ ] Run the runner against `./data`, commit only when stdout contains `CHANGED=...`, and upload rejected payloads for validation exit code 2.
- [ ] Never commit `rejected/`; never use secrets beyond `GITHUB_TOKEN`.
- [ ] Commit `ci: publish calendar history to data branch`.
- [ ] External checkpoint: manually run and inspect the first `data` branch, then add `schedule: 0 6 * * *` in a follow-up commit.

### D3 — Render Markdown and Atom

**Files:** create Core formatters/tests; extend runner commands/output.

- [ ] Add a literal snapshot test for three Markdown batches and XML-reader tests for 50 Atom entries in Moscow time.
- [ ] Implement `ChangeMarkdownFormatter`, Atom writer and reuse C7 `ChangeSummaryTextFormatter`.
- [ ] Regenerate human-readable files only with a changed snapshot.
- [ ] Commit `feat: render changelog and atom feed`.

### D4 — Merge public history into the app

**Files:** create `PublicHistoryClient`, `ChangeHistoryMerger`; modify state, bootstrapper, view models/router/UI and README.

- [ ] Test URL allow-list, 10 MB bound, 30-second timeout, schema validation and deserialization.
- [ ] Test graph coverage, public precedence, old local batches, ordering and 500-batch limit.
- [ ] Test first/subsequent seen rules, once-daily sync and disabled setting.
- [ ] Merge before the local refresh; public batches never produce a modal notice.
- [ ] Add the enabled-by-default About toggle and document privacy behavior.
- [ ] Commit `feat: merge public history into the app`.

### D5 — Build releases from public data

**Files:** modify `build/update-bundled.ps1`, release workflow, bundled loaders/resources and tests.

- [ ] Add `-FromPublic` and an injectable reference time for the seven-day freshness check.
- [ ] Embed both snapshot and history; test offline first-launch fallback and seen behavior.
- [ ] Make release packaging fail on stale or inconsistent public artifacts.
- [ ] Commit `build: refresh bundled data from public history`.

### D6 — Add public links

**Files:** modify README, ProductInfo, router tests and About UI tests.

- [ ] Link the data-branch changelog/feed and label them as automated, non-legal summaries.
- [ ] Add an allow-listed «История на GitHub» action.
- [ ] Commit `docs: link public history`.

### D7 — Add optional Telegram announcements

**Files:** modify runner and workflow; add truncation tests.

- [ ] Test `render-telegram` output at the 3500-character boundary with `и ещё N`.
- [ ] Send plain text only when both secrets exist and `CHANGED` was emitted.
- [ ] Keep send failures non-fatal and secrets out of output.
- [ ] Commit `ci: optional telegram announcements`.

### D8 — Finalize and verify

**Files:** modify CONTRIBUTING, main spec and operational docs.

- [ ] Document local dry-run, manual workflow, rejected artifact triage and explicit anomaly acceptance.
- [ ] Run all Windows tests/build, Ubuntu-equivalent Core/Infrastructure/Runner tests, UI tests and package checks.
- [ ] Verify manual first publication, no-op second run, and empty-profile desktop startup behavior.

External limitation: enabling the daily schedule is blocked until the user or maintainer reviews the first manually generated `data` branch on GitHub. Do not claim D2 complete before that checkpoint.

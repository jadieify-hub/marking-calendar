# Private GitHub Publication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Audit the complete repository for personal data and secrets, then publish a release-ready private GitHub repository under `jadieify-hub/marking-calendar`.

**Architecture:** Scan both the working tree and every reachable Git blob before any push. Keep `KRS` as the product publisher while replacing the unavailable GitHub owner `KRS` with the authenticated account `jadieify-hub` in every runtime, documentation, build, workflow, and test URL. Publish only tracked Git content, verify the remote remains private, and prepare version `0.1.0` as a draft release with verified artifacts.

**Tech Stack:** Git, GitHub CLI, PowerShell, .NET 10, Vitest/Vite, GitHub Actions, Velopack.

**Spec:** `docs/superpowers/specs/2026-09-02-marking-calendar-v2-design.md`

## Global Constraints

- The repository must remain private until the owner explicitly changes its visibility.
- Never print credentials or persist GitHub tokens in repository files.
- Scan the complete reachable Git history, not only the current checkout.
- `build/.work/`, `artifacts/`, local application data, logs, and IDE files must not be committed or pushed.
- Keep `Разработчик: Руслан Керусов` and `Издатель и владелец: KRS` consistent across product metadata and README.
- GitHub links used at runtime must point to the actual repository owner `jadieify-hub`.
- The existing test suites and Release build must remain green.

---

### Task 1: Audit tracked content and Git history

**Files:**
- Inspect: all tracked files and all reachable Git blobs
- Inspect: commit author metadata and screenshots under `assets/screenshots/`

- [ ] Search the current tree and complete history for credential formats, private keys, tokens, passwords, e-mail addresses, phone numbers, local user paths, IP addresses, and identifiers.
- [ ] Review every match manually and separate expected public product data from personal or secret data.
- [ ] Inspect screenshots for desktop chrome, user names, account names, notifications, or unrelated applications.
- [ ] Confirm ignored build outputs and local data are absent from `git ls-files`.

### Task 2: Point publication and update links at the real repository

**Files:**
- Modify: `src/MarkingCalendar.App/ProductInfo.cs`
- Modify: `src/MarkingCalendar.Infrastructure/Source/PublicHistoryClient.cs`
- Modify: `src/MarkingCalendar.App/Web/WebMessageRouter.cs`
- Modify: `src/MarkingCalendar.Runner/HistoryRunner.cs`
- Modify: `build/update-bundled.ps1`
- Modify: `README.md`
- Modify: related `.NET` and TypeScript tests and fixtures

- [ ] Write or update tests so the actual repository, history, and allowlisted paths are `jadieify-hub/marking-calendar`.
- [ ] Run the focused tests and confirm they fail against the old hard-coded owner.
- [ ] Replace only GitHub ownership URLs; retain `KRS` as publisher metadata.
- [ ] Run focused tests and confirm they pass.

### Task 3: Add responsible security-reporting guidance

**Files:**
- Create: `SECURITY.md`
- Modify: `README.md`

- [ ] Document supported version `0.1.x`, GitHub private vulnerability reporting as the preferred channel, what diagnostic excerpts may contain, and a prohibition on posting sensitive logs in public issues.
- [ ] Link the security policy from README without adding personal contact details.
- [ ] Review documentation for consistent product name, developer, publisher, support URL, repository URL, license, and issue-reporting instructions.

### Task 4: Verify and commit publication preparation

**Files:**
- Test: entire solution and UI suite
- Build: Release application and Velopack artifacts

- [ ] Run `dotnet test MarkingCalendar.slnx -c Release --no-restore -p:SkipUiBuild=true`.
- [ ] Run `npm test -- --run` in `src/MarkingCalendar.UI`.
- [ ] Run `dotnet build src/MarkingCalendar.App/MarkingCalendar.App.csproj -c Release --no-restore`.
- [ ] Run `pwsh build/pack.ps1 -Version 0.1.0` and `pwsh build/verify-package.ps1`.
- [ ] Run `git diff --check` and show `git status`; confirm ignored build directories are not staged.
- [ ] Commit the publication preparation as one reviewable commit.

### Task 5: Create and verify the private GitHub repository

**Remote:** `https://github.com/jadieify-hub/marking-calendar`

- [ ] Create `jadieify-hub/marking-calendar` with private visibility, the existing README, Issues enabled, and a concise Russian description.
- [ ] Push `main` without force and set it as the default branch.
- [ ] Configure repository topics and disable unused wiki and projects features.
- [ ] Verify through the GitHub API that visibility is `private`, the default branch is `main`, and no ignored local artifacts are present remotely.

### Task 6: Prepare the first release without publishing it

**Release:** draft `v0.1.0`

- [ ] Create a Russian draft release with installation, system requirements, update behavior, limitations, and checksum instructions.
- [ ] Upload `MarkingCalendar-Setup.exe`, `MarkingCalendar-Portable.zip`, the full Velopack package, `releases.win.json`, and `SHA256SUMS.txt` from the verified `artifacts/` directory.
- [ ] Verify the release is a draft, the repository is still private, and every expected asset is attached exactly once.

# Marking Calendar V2 Release Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Подготовить надёжный релиз 0.1.0 до изменения календарного интерфейса.

**Architecture:** Исправления остаются в существующих слоях Core, Infrastructure, App и UI. Общие ошибки и диагностика проходят через минимальный `IAppLogger`; UI-команды возвращают типизированный результат и никогда не переводят рабочее приложение в фатальный экран.

**Tech Stack:** .NET 10, WPF, WebView2, xUnit, TypeScript, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-02-marking-calendar-v2-hardening-feed-design.md`

## Global Constraints

- Бизнес-логика только в C#; локальное UI-состояние и отображение — TypeScript.
- Любая дата в логике поступает через `TimeProvider` или view model.
- Каждый пункт выполняется RED → GREEN → REFACTOR и отдельным коммитом.
- `build/pack.ps1`, `build/verify-package.ps1` и workflows не менять до A10.
- `Update-CHZ-Calendar.ps1` используется только как фикстура до A10.

---

### Task 1: A1 — Isolate user data

**Files:**
- Modify: `src/MarkingCalendar.Infrastructure/Storage/AppPaths.cs`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Storage/AppPathsTests.cs`
- Modify: `README.md`, `CONTRIBUTING.md`, `docs/superpowers/specs/2026-09-02-marking-calendar-v2-design.md`

**Interfaces:** Produces `AppPaths.ForCurrentUser()` rooted at `%LOCALAPPDATA%\KRS\MarkingCalendar`.

- [ ] Write `ForCurrentUser_UsesPublisherDirectorySeparateFromInstallRoot` asserting the final two path segments are `KRS/MarkingCalendar`.
- [ ] Run `dotnet test tests/MarkingCalendar.Infrastructure.Tests/MarkingCalendar.Infrastructure.Tests.csproj --filter AppPathsTests` and observe RED.
- [ ] Change the factory to `Path.Combine(LocalApplicationData, "KRS", "MarkingCalendar")`; update documentation paths.
- [ ] Re-run the test project and commit `fix: isolate application data from installation`.

### Task 2: A2 — Exact event classification

**Files:**
- Modify: `src/MarkingCalendar.Core/Events/EventClassifier.cs`
- Modify: `tests/MarkingCalendar.Core.Tests/Events/EventClassifierTests.cs`
- Modify: `tests/MarkingCalendar.Core.Tests/MarkingCalendar.Core.Tests.csproj`
- Create: `tests/MarkingCalendar.Core.Tests/Fixtures/bundled-source.json`

**Interfaces:** Produces `EventClassifier.Classify(string? type, string? stage)` with exact known-type mapping and ordered fallback.

- [ ] Add theory cases for all twelve known types plus permit/registration regression cases and a fixture-wide mapping test.
- [ ] Run the filtered tests and confirm permit/registration failures.
- [ ] Normalize with `Trim`, collapsed whitespace, lowercase and `ё→е`; map exact types before ordered keyword fallback.
- [ ] Run Core tests and commit `fix: classify known event types exactly`.

### Task 3: A5 — File diagnostics

**Files:**
- Create: `src/MarkingCalendar.Infrastructure/Diagnostics/IAppLogger.cs`
- Create: `src/MarkingCalendar.Infrastructure/Diagnostics/FileAppLogger.cs`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Diagnostics/FileAppLoggerTests.cs`
- Modify: update/storage/migration/bootstrap/update-service files and About UI contract.

**Interfaces:** Produces `Log(AppLogLevel level, string source, string message, Exception? error = null)` and `SaveRejectedJsonAsync`.

- [ ] Write tests for line format, concurrent readable file, swallowed IO failure and rejected JSON naming; observe RED.
- [ ] Implement the logger with `FileShare.ReadWrite`, `TimeProvider`, sanitized exception messages and no external package.
- [ ] Inject it into migration, calendar update, snapshot rejection, app update and bootstrap paths; add `openLogs` command and About action tests first.
- [ ] Add AppDomain, TaskScheduler and Dispatcher handlers; update issue template; run all .NET/UI tests.
- [ ] Commit `feat: add application diagnostics logging`.

### Task 4: A3 — Non-fatal UI command failures

**Files:**
- Modify: `src/MarkingCalendar.App/Hosting/DesktopServices.cs`
- Modify: `src/MarkingCalendar.App/Web/WebMessageRouter.cs`, `ViewModels.cs`
- Modify: `src/MarkingCalendar.App/MainWindow.xaml.cs`
- Modify: UI contracts/render/styles
- Test: App router/desktop tests and Vitest toast tests.

**Interfaces:** `WebCommandResult` becomes `{ Kind: Handled|Rejected|Failed, Message }`; `AppViewModel.Toast` is nullable.

- [ ] Add failing tests for three clipboard attempts, failed command result, toast rendering/timeout and WebView staying visible.
- [ ] Implement retry delay 50 ms and `ClipboardUnavailableException`; catch/log router command errors.
- [ ] Send toast state without calling `ShowFatalError`; handle `NewWindowRequested` with `Handled=true`.
- [ ] Run App and UI tests; commit `fix: keep ui usable after command failures`.

### Task 5: A4 — Preserve mounted UI state

**Files:**
- Refactor: `src/MarkingCalendar.UI/src/render.ts`, `main.ts`
- Modify: `src/MarkingCalendar.App/Updates/AppUpdateService.cs`
- Test: `render.test.ts`, `AppUpdateServiceTests.cs`

**Interfaces:** UI exports `mount(root, send)` once and `update(model)` repeatedly; updater coalesces progress.

- [ ] Add failing Vitest proving selected category and open dialog survive two `update()` calls.
- [ ] Add failing xUnit test for progress events below one second/5 percent being suppressed.
- [ ] Split DOM mounting from region updates and keep `UiState` outside the model.
- [ ] Implement progress throttling with injected `TimeProvider`; run tests and commit `fix: preserve ui state across host updates`.

### Task 6: A6 — Recover corrupt storage

**Files:**
- Modify: `CalendarStore.cs`, `AppBootstrapper.cs`, status view model
- Test: `CalendarStoreTests.cs`, bootstrapper recovery tests.

**Interfaces:** Load methods quarantine corrupt files and return empty results; bootstrapper selects current → archive → bundled.

- [ ] Add failing tests for truncated current/history and archive fallback.
- [ ] Implement quarantine names using injected `TimeProvider`, logging each recovery.
- [ ] Expose status `Открыта резервная копия от …`; run Infrastructure/App tests.
- [ ] Commit `fix: recover from corrupt local storage`.

### Task 7: A7 — Import V1 history

**Files:**
- Modify: `LegacyCalendarImporter.cs`
- Create: V1 history fixtures under `tests/MarkingCalendar.Infrastructure.Tests/Fixtures/`
- Modify: importer tests.

**Interfaces:** Importer appends normalized V1 batches using the same stable batch-id factory as updates.

- [ ] Copy minimal valid/corrupt history fixtures and add tests for import, partial skip and byte-identical sources.
- [ ] Extract/share batch-id construction if needed; parse batches independently and log rejected ones.
- [ ] Run migration/update tests and commit `feat: import compatible v1 history`.

### Task 8: A8 — Track unread history batches

**Files:**
- Modify: `AppStateStore.cs`, `AppBootstrapper.cs`, view models/router/UI.
- Test: state-store, factory/router and Vitest history tests.

**Interfaces:** `AppState` v2 contains `SeenBatchIds`, `SelectedGroups`, `Theme`; old `LastShownBatchId` migrates. Commands include `markHistorySeen`.

- [ ] Add failing state migration and unread-count tests.
- [ ] Implement version-tolerant state DTO and set-based unread calculation.
- [ ] Add target batch scroll/highlight and four visible counters with tests.
- [ ] Run all affected tests; commit `fix: track unread change batches`.

### Task 9: A9 — Accessible dialogs and help menu

**Files:**
- Modify: UI renderer/styles/tests.

**Interfaces:** Shared dialog controller owns opener, focusable list, Escape/backdrop close and focus restoration.

- [ ] Add failing tests for Escape, backdrop, focus wrap/restore, menu arrows and outside click.
- [ ] Implement one reusable controller and ARIA labels; respect reduced motion.
- [ ] Run Vitest/build and commit `fix: make dialogs and help menu accessible`.

### Task 10: A10 — Repository hygiene and bundled refresh

**Files:**
- Create: `.gitattributes`, `build/update-bundled.ps1`, bundled metadata.
- Modify: App csproj/bootstrapper/docs/issue template.
- Delete: `app/`, `README.txt`, `Установить календарь маркировки.cmd` after fixtures exist.

**Interfaces:** Refresh script validates at least 100 `data.items` and writes JSON plus `retrievedAt` metadata atomically.

- [ ] Add a script test or validation mode that fails on fewer than 100 items; verify failure with a fixture.
- [ ] Implement refresh script and metadata loader test; update documentation.
- [ ] Add `.gitattributes`; verify every V1-dependent test uses its own fixture, then remove V1 files.
- [ ] Run complete restore/build/tests/package verification and commit `chore: remove v1 repository artifacts`.

### Task 11: Stage A review

- [ ] Map every A requirement to a test or manual check.
- [ ] Run fresh full verification including packaging and `git diff --check`.
- [ ] Review logging privacy, storage recovery, UI command isolation and absence of V1 runtime dependencies.

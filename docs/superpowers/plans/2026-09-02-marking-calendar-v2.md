# Marking Calendar V2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Переписать «Календарь маркировки» как поддерживаемое Windows-приложение на .NET 10/WPF/WebView2 с безопасным обновлением данных, понятной легендой, миграцией V1 и публичной GitHub-поставкой.

**Architecture:** Вся нормализация, валидация, классификация и фиксация изменений находится в независимом C#-ядре. Инфраструктура изолирует HTTP и файловое хранилище, WPF управляет жизненным циклом, а TypeScript/HTML/CSS только отображают переданную view model и отправляют типизированные команды.

**Tech Stack:** .NET 10 LTS, WPF, WebView2, C# 14, xUnit, TypeScript 5, Vite/Vitest, Velopack, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-02-marking-calendar-v2-design.md`

## Global Constraints

- Пользовательское название: «Календарь маркировки»; `CHZ` не используется в именах V2.
- Первая поддерживаемая платформа: Windows x64; целевая платформа `net10.0-windows`.
- Бизнес-логика существует только в C#; TypeScript является слоем отображения.
- Интерфейс открывается сразу с последним корректным или встроенным снимком.
- Пустой, повреждённый или аномальный ответ не заменяет рабочие данные.
- Категорийные фильтры одновременно являются легендой цветов и доступны с клавиатуры.
- Поддержка разработчика ненавязчива; CloudTips URL: `https://pay.cloudtips.ru/p/53698013`.
- Разработчик: Руслан Керусов; владелец/издатель: KRS.
- Официальные бинарные сборки публикуются только в GitHub Releases репозитория `marking-calendar`.
- Старый каталог `%LOCALAPPDATA%\CHZ-MarkingCalendar` при миграции не изменяется и не удаляется.

---

### Task 1: Repository and solution foundation

**Files:**
- Create: `.gitignore`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `MarkingCalendar.slnx`
- Create: `src/MarkingCalendar.Core/MarkingCalendar.Core.csproj`
- Create: `tests/MarkingCalendar.Core.Tests/MarkingCalendar.Core.Tests.csproj`
- Create: `tests/MarkingCalendar.Core.Tests/Normalization/EventNormalizerTests.cs`

**Interfaces:**
- Produces: solution-wide nullable/implicit-using/analyzer settings and the initial `IEventNormalizer` contract.

- [ ] **Step 1: Initialize Git and preserve the V1 baseline**

Run:

```powershell
git init -b main
git add .
git commit -m "chore: preserve legacy calendar baseline"
git switch -c feat/v2-rewrite
```

Expected: repository is on `feat/v2-rewrite`; V1 files remain unchanged.

- [ ] **Step 2: Create the solution/test scaffolding and the first failing normalization test**

The test uses a literal expected value and verifies HTML decoding, Russian date conversion and absolute source URLs:

```csharp
[Fact]
public void Normalize_ConvertsSourceFieldsToStableEvent()
{
    var source = new SourceEvent("01.09.2026", "", "с 1 сентября 2026",
        "Антисептики и&nbsp;дезинфицирующие средства", "Розничная продажа",
        "Старт", "Описание", "/business/projects/children/");

    var actual = new EventNormalizer().Normalize(source);

    Assert.Equal(new DateOnly(2026, 9, 1), actual.Start);
    Assert.Equal("Антисептики и дезинфицирующие средства", actual.Group);
    Assert.Equal("https://честныйзнак.рф/business/projects/children/", actual.Url.AbsoluteUri);
    Assert.False(string.IsNullOrWhiteSpace(actual.Id));
}
```

- [ ] **Step 3: Run the test and verify RED**

Run: `dotnet test tests/MarkingCalendar.Core.Tests/MarkingCalendar.Core.Tests.csproj --no-restore`

Expected: compilation fails because `SourceEvent` and `EventNormalizer` do not exist.

- [ ] **Step 4: Add minimal domain types and normalization implementation**

Create focused files under `src/MarkingCalendar.Core/Events/`: `SourceEvent.cs`, `CalendarEvent.cs`, `EventNormalizer.cs`, `EventId.cs`. `EventNormalizer.Normalize(SourceEvent)` decodes entities, parses `dd.MM.yyyy`/`yyyy-MM-dd`, rejects missing group/type/stage, and calculates a deterministic SHA-256-based lowercase identifier from canonical content.

- [ ] **Step 5: Verify GREEN and commit**

Run: `dotnet test MarkingCalendar.slnx`

Expected: all created tests pass with zero warnings.

Commit: `feat: add calendar domain normalization`

### Task 2: Diff engine, categories and summaries

**Files:**
- Create: `src/MarkingCalendar.Core/Changes/ChangeKind.cs`
- Create: `src/MarkingCalendar.Core/Changes/EventChange.cs`
- Create: `src/MarkingCalendar.Core/Changes/ChangeSet.cs`
- Create: `src/MarkingCalendar.Core/Changes/EventDiffEngine.cs`
- Create: `src/MarkingCalendar.Core/Changes/ChangeSummaryFactory.cs`
- Create: `src/MarkingCalendar.Core/Events/EventCategory.cs`
- Create: `src/MarkingCalendar.Core/Events/EventClassifier.cs`
- Test: `tests/MarkingCalendar.Core.Tests/Changes/EventDiffEngineTests.cs`
- Test: `tests/MarkingCalendar.Core.Tests/Changes/ChangeSummaryFactoryTests.cs`
- Test: `tests/MarkingCalendar.Core.Tests/Events/EventClassifierTests.cs`

**Interfaces:**
- Consumes: `CalendarEvent` from Task 1.
- Produces: `ChangeSet EventDiffEngine.Compare(IReadOnlyList<CalendarEvent> previous, IReadOnlyList<CalendarEvent> current)` and `IReadOnlyList<ChangeSummary> ChangeSummaryFactory.Create(ChangeSet changes, int limit, DateOnly today)`.

- [ ] **Step 1: Write failing diff tests**

Cover exact duplicates before fuzzy pairing, date move instead of add/remove, text edit on the same date, additions and removals. A move assertion must literally expect `2026-12-01 → 2027-06-01` and zero additions/removals.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/MarkingCalendar.Core.Tests/MarkingCalendar.Core.Tests.csproj --filter EventDiffEngineTests`

Expected: compilation fails because diff types do not exist.

- [ ] **Step 3: Implement the deterministic diff engine**

Pair exact canonical content first. For remaining records, pair by normalized `(Group, Type, Stage)` and smallest date distance. As a fallback, pair same `(Group, Type, Start, End)` as a content change. Return immutable arrays for `Added`, `Removed`, `Moved`, `Changed`.

- [ ] **Step 4: Add failing category and summary tests**

Assert mappings for retail, EDO, ban, permit, marking, registration and other. Assert that summaries sort future/near events first, moves before other equal-date changes, stop at eight items, and retain total counts.

- [ ] **Step 5: Implement classifier and summaries, verify GREEN, commit**

Run: `dotnet test MarkingCalendar.slnx`

Expected: all tests pass.

Commit: `feat: add event diff and classification engine`

### Task 3: Snapshot validation and durable storage

**Files:**
- Create: `src/MarkingCalendar.Core/Snapshots/CalendarSnapshot.cs`
- Create: `src/MarkingCalendar.Core/Snapshots/SnapshotValidationResult.cs`
- Create: `src/MarkingCalendar.Core/Snapshots/SnapshotValidator.cs`
- Create: `src/MarkingCalendar.Infrastructure/MarkingCalendar.Infrastructure.csproj`
- Create: `src/MarkingCalendar.Infrastructure/Storage/AppPaths.cs`
- Create: `src/MarkingCalendar.Infrastructure/Storage/AtomicFileWriter.cs`
- Create: `src/MarkingCalendar.Infrastructure/Storage/CalendarStore.cs`
- Create: `src/MarkingCalendar.Infrastructure/Storage/RetentionPolicy.cs`
- Test: `tests/MarkingCalendar.Core.Tests/Snapshots/SnapshotValidatorTests.cs`
- Create: `tests/MarkingCalendar.Infrastructure.Tests/MarkingCalendar.Infrastructure.Tests.csproj`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Storage/CalendarStoreTests.cs`

**Interfaces:**
- Consumes: `CalendarEvent`, `ChangeSet`.
- Produces: `SnapshotValidator.Validate(CalendarSnapshot candidate, CalendarSnapshot? baseline)`; `CalendarStore.LoadCurrentAsync`; `CalendarStore.SaveValidatedAsync`; `CalendarStore.LoadHistoryAsync`.

- [ ] **Step 1: Write failing validator tests**

Use hand-built snapshots to assert: zero events rejected; invalid required field rejected; duplicate IDs rejected; a decrease from 432 to 20 rejected; a change from 432 to 400 accepted; no baseline accepts a non-empty valid bundled snapshot.

- [ ] **Step 2: Verify RED, implement validation, verify GREEN**

Run RED then GREEN with:

`dotnet test tests/MarkingCalendar.Core.Tests/MarkingCalendar.Core.Tests.csproj --filter SnapshotValidatorTests`

The anomaly rule rejects a candidate below both 100 events and 50% of a baseline that contains at least 100 events. Values live in `SnapshotValidationOptions` and are dependency-injected.

- [ ] **Step 3: Write failing atomic storage tests**

Tests use a real temporary directory and assert that a saved snapshot can be reloaded, an invalid candidate leaves current unchanged, a stale `.tmp` file is ignored, and retention leaves no more than 20 archives/30 logs.

- [ ] **Step 4: Implement storage and retention**

Use `System.Text.Json`, write-through temporary files in the same directory, deserialize the temporary file, then `File.Move(temp, destination, true)`. Never delete the current snapshot during replacement.

- [ ] **Step 5: Run all tests and commit**

Run: `dotnet test MarkingCalendar.slnx`

Commit: `feat: add validated atomic calendar storage`

### Task 4: Source client, update orchestration and V1 migration

**Files:**
- Create: `src/MarkingCalendar.Infrastructure/Source/MarkingCalendarClient.cs`
- Create: `src/MarkingCalendar.Infrastructure/Source/SourceResponse.cs`
- Create: `src/MarkingCalendar.Infrastructure/Updates/CalendarUpdateService.cs`
- Create: `src/MarkingCalendar.Infrastructure/Updates/CalendarUpdateResult.cs`
- Create: `src/MarkingCalendar.Infrastructure/Migration/LegacyCalendarImporter.cs`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Source/MarkingCalendarClientTests.cs`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Updates/CalendarUpdateServiceTests.cs`
- Test: `tests/MarkingCalendar.Infrastructure.Tests/Migration/LegacyCalendarImporterTests.cs`

**Interfaces:**
- Consumes: normalizer, validator, diff engine and store.
- Produces: `Task<CalendarUpdateResult> CalendarUpdateService.CheckAsync(CancellationToken)` and `Task<LegacyImportResult> LegacyCalendarImporter.ImportOnceAsync(CancellationToken)`.

- [ ] **Step 1: Write failing HTTP contract tests**

Use a custom `HttpMessageHandler` returning complete JSON fixtures. Assert `data.items` is required, relative links are normalized, a non-success status is reported, and cancellation is propagated.

- [ ] **Step 2: Verify RED, implement the client, verify GREEN**

The client endpoint is `https://xn--80ajghhoc2aj1c8b.xn--p1ai/bitrix/services/main/ajax.php?mode=class&c=dev%3AmarkingCalendar&action=getSheduleList`; user agent identifies `MarkingCalendar/<version>`; timeout is 30 seconds.

- [ ] **Step 3: Write failing orchestration tests**

Assert: equal snapshot returns `NoChanges`; changed valid snapshot is saved with one history batch; rejected candidate returns `Rejected` and leaves current intact; network failure returns `Failed` with the baseline still available; the same snapshot cannot create duplicate history batches.

- [ ] **Step 4: Implement orchestration and history idempotency**

The batch identifier is SHA-256 of previous snapshot ID + candidate snapshot ID. Save the new snapshot and history as one recoverable operation guarded by a `SemaphoreSlim`; expose user-safe errors separately from diagnostic exceptions.

- [ ] **Step 5: Write failing migration tests and implement importer**

Use a real temporary legacy folder containing `calendar-data.js` and `change-history.json`. Parse only the assigned JSON expression, validate it through V2, import once into an empty V2 store, write a migration marker, and never alter legacy bytes. Re-running returns `AlreadyImported`.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet test MarkingCalendar.slnx`

Commit: `feat: add safe data updates and legacy migration`

### Task 5: TypeScript presentation layer

**Files:**
- Create: `src/MarkingCalendar.UI/package.json`
- Create: `src/MarkingCalendar.UI/package-lock.json`
- Create: `src/MarkingCalendar.UI/tsconfig.json`
- Create: `src/MarkingCalendar.UI/vite.config.ts`
- Create: `src/MarkingCalendar.UI/index.html`
- Create: `src/MarkingCalendar.UI/src/contracts.ts`
- Create: `src/MarkingCalendar.UI/src/bridge.ts`
- Create: `src/MarkingCalendar.UI/src/render.ts`
- Create: `src/MarkingCalendar.UI/src/main.ts`
- Create: `src/MarkingCalendar.UI/src/styles.css`
- Test: `src/MarkingCalendar.UI/src/render.test.ts`
- Test: `src/MarkingCalendar.UI/src/bridge.test.ts`

**Interfaces:**
- Consumes: serialized `AppViewModel` messages with `snapshot`, `history`, `status`, `updateNotice` and `about` fields.
- Produces commands: `{ type: "ready" }`, `{ type: "refresh" }`, `{ type: "openChanges"; batchId: string }`, `{ type: "openExternal"; url: string }`, `{ type: "copySupportUrl" }`.

- [ ] **Step 1: Create Node configuration and failing renderer tests**

Tests in jsdom assert that category buttons contain the matching color swatch and `aria-pressed`, keyboard activation changes the filter, the empty history text is exactly «Изменений пока нет», and update notice contains four counts plus at most eight summaries.

- [ ] **Step 2: Verify RED**

Run: `npm test -- --run` from `src/MarkingCalendar.UI`.

Expected: imports fail because presentation modules do not exist.

- [ ] **Step 3: Implement the UI without business rules**

Render the supplied category, event and change view models. CSS provides responsive dark/light themes, 44px minimum interactive targets, visible focus rings, non-color selected state, calendar month grid, event drawer and in-app modal. Remove «Локальный мониторинг» and explanatory history hints.

- [ ] **Step 4: Add bridge command tests and implementation**

Validate incoming message shape before rendering. The browser fallback uses a local development fixture only when `window.chrome.webview` is absent. External URLs are never opened by JavaScript directly; they are commands to the host.

- [ ] **Step 5: Verify tests/build and commit**

Run:

```powershell
npm test -- --run
npm run build
```

Commit: `feat: add accessible calendar web interface`

### Task 6: WPF host and application lifecycle

**Files:**
- Create: `src/MarkingCalendar.App/MarkingCalendar.App.csproj`
- Create: `src/MarkingCalendar.App/App.xaml`
- Create: `src/MarkingCalendar.App/App.xaml.cs`
- Create: `src/MarkingCalendar.App/MainWindow.xaml`
- Create: `src/MarkingCalendar.App/MainWindow.xaml.cs`
- Create: `src/MarkingCalendar.App/Hosting/AppBootstrapper.cs`
- Create: `src/MarkingCalendar.App/Web/AppViewModelFactory.cs`
- Create: `src/MarkingCalendar.App/Web/WebMessageRouter.cs`
- Create: `src/MarkingCalendar.App/Resources/bundled-calendar.json`
- Create: `tests/MarkingCalendar.App.Tests/MarkingCalendar.App.Tests.csproj`
- Test: `tests/MarkingCalendar.App.Tests/Web/AppViewModelFactoryTests.cs`
- Test: `tests/MarkingCalendar.App.Tests/Web/WebMessageRouterTests.cs`

**Interfaces:**
- Consumes: UI `dist`, `CalendarStore`, `CalendarUpdateService`, `LegacyCalendarImporter`.
- Produces: immediately visible main window, background update status, safe host command routing.

- [ ] **Step 1: Write failing view-model and router tests**

Assert category colors are assigned in C#, dates and status labels are localized before serialization, only CloudTips/GitHub/http(s) event links can be opened, `file:`, `javascript:` and arbitrary commands are rejected, and support URL copying uses the single `ProductInfo.SupportUrl` value.

- [ ] **Step 2: Verify RED, implement host-independent web services, verify GREEN**

Run: `dotnet test tests/MarkingCalendar.App.Tests/MarkingCalendar.App.Tests.csproj`.

- [ ] **Step 3: Implement WPF startup**

The window creates WebView2, maps the packaged `wwwroot` directory to `https://app.markingcalendar.local`, navigates to the UI, sends the baseline view model after `{type:"ready"}`, then starts migration/update work without awaiting it on the UI thread. WebView2 absence displays a concise actionable installation error in the WPF window.

- [ ] **Step 4: Package the bundled snapshot and UI output**

Convert V1 `calendar-data.js` to plain JSON during development and embed it as `Resources/bundled-calendar.json`. MSBuild runs `npm ci` and `npm run build`, then copies `dist/**` into publish output `wwwroot/`.

- [ ] **Step 5: Build, run smoke test and commit**

Run:

```powershell
dotnet test MarkingCalendar.slnx
dotnet build src/MarkingCalendar.App/MarkingCalendar.App.csproj -c Release
dotnet run --project src/MarkingCalendar.App/MarkingCalendar.App.csproj
```

Manual smoke: main window appears without console, bundled 432 events render, filters work, changes page opens, and app closes cleanly.

Commit: `feat: add WPF WebView2 application host`

### Task 7: About, support and application updates

**Files:**
- Create: `src/MarkingCalendar.App/ProductInfo.cs`
- Create: `src/MarkingCalendar.App/Updates/AppUpdateService.cs`
- Create: `src/MarkingCalendar.App/Resources/support-qr.png`
- Test: `tests/MarkingCalendar.App.Tests/Updates/AppUpdateServiceTests.cs`
- Modify: `src/MarkingCalendar.UI/src/render.ts`
- Modify: `src/MarkingCalendar.UI/src/styles.css`
- Modify: `src/MarkingCalendar.App/Web/WebMessageRouter.cs`

**Interfaces:**
- Consumes: Velopack update manager and host command router.
- Produces: Help menu views, non-intrusive donation flow, background app update state and apply-on-restart action.

- [ ] **Step 1: Write failing product-info/support tests**

Assert About data exposes `Календарь маркировки`, `Руслан Керусов`, `KRS`, repository URL and independent-project disclaimer; support command opens/copies exactly `https://pay.cloudtips.ru/p/53698013`.

- [ ] **Step 2: Implement About and Support UI**

Add `Справка` menu in the web header with `Поддержать разработку` and `О программе`. Support modal includes embedded QR, open/copy/close buttons. No support prompt is shown automatically.

- [ ] **Step 3: Write failing update-state tests**

Hide Velopack behind `IAppUpdateSource`. Assert `NoUpdate`, download progress, `ReadyToRestart` and failure states while keeping the app usable. Tests use a fake source but assert the real `AppUpdateService` state transitions.

- [ ] **Step 4: Implement Velopack integration**

Register Velopack startup before WPF initialization. Check GitHub Releases after the calendar is visible, download in background, and apply only on explicit restart or next launch. A failed check is logged and displayed only in the About/update area.

- [ ] **Step 5: Run all tests/build and commit**

Run: `dotnet test MarkingCalendar.slnx` and `npm test -- --run`.

Commit: `feat: add support pages and app updates`

### Task 8: Public repository, packaging and release automation

**Files:**
- Create: `README.md`
- Create: `LICENSE`
- Create: `CONTRIBUTING.md`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `build/pack.ps1`
- Create: `build/verify-package.ps1`
- Create: `assets/screenshots/calendar.png`
- Create: `assets/screenshots/changes.png`
- Modify: `src/MarkingCalendar.App/MarkingCalendar.App.csproj`

**Interfaces:**
- Consumes: release-ready app and UI from Tasks 1–7.
- Produces: reproducible CI, Setup/Portable/SHA-256 artifacts, complete public documentation.

- [ ] **Step 1: Write package verification script before packaging logic**

`verify-package.ps1` exits non-zero unless `artifacts/MarkingCalendar-Setup.exe`, `artifacts/MarkingCalendar-Portable.zip`, `artifacts/SHA256SUMS.txt`, `artifacts/releases.win.json` and one `MarkingCalendar-*-full.nupkg` exist, ZIP contains `MarkingCalendar.exe` and `wwwroot/index.html`, and every checksum matches. The JSON feed and full package are required by Velopack application updates.

- [ ] **Step 2: Run verification and observe RED**

Run: `pwsh -File build/verify-package.ps1`

Expected: failure because artifacts do not exist.

- [ ] **Step 3: Implement deterministic packaging**

`pack.ps1` accepts `-Version`, publishes framework-dependent `win-x64` output, invokes Velopack packaging with the `net10-x64-desktop` prerequisite for Setup and update feed, creates Portable ZIP from the same publish directory and writes uppercase SHA-256 lines for all release files. User-facing output filenames exactly match the specification; the update feed and full package retain Velopack-compatible names.

- [ ] **Step 4: Create CI and release workflows**

CI pins .NET 10 and Node 24, runs restore/build/test/UI build, and uploads test logs on failure. Release triggers only on `v*` tags, derives the version without `v`, runs `pack.ps1`, runs `verify-package.ps1`, and publishes the user-facing files plus the Velopack update feed/package to GitHub Releases with generated Russian release notes template.

- [ ] **Step 5: Write public documentation and license**

README covers purpose, requirements, screenshots, Setup/Portable installation, background data/app updates, official-build warning, privacy, limitations, developer/owner, build/test commands, license summary and CloudTips support. LICENSE grants free personal/commercial use but reserves sale and distribution of modified builds without written KRS permission.

- [ ] **Step 6: Capture final screenshots and verify links/metadata**

Run the Release build, capture calendar and changes screens to `assets/screenshots/`, confirm README paths, About data, assembly metadata and release filenames agree.

- [ ] **Step 7: Full verification and commit**

Run:

```powershell
dotnet restore MarkingCalendar.slnx -r win-x64 --locked-mode
dotnet build MarkingCalendar.slnx -c Release --no-restore
dotnet test MarkingCalendar.slnx -c Release --no-build
Push-Location src/MarkingCalendar.UI
npm ci
npm test -- --run
npm run build
Pop-Location
pwsh -File build/pack.ps1 -Version 0.1.0
pwsh -File build/verify-package.ps1
```

Expected: every command exits 0; all automated tests pass; all three release artifacts validate.

Commit: `chore: prepare public 0.1.0 release`

### Task 9: Final architecture and regression review

**Files:**
- Review: all changed files
- Update only when a verified issue is found: implementation/test/documentation file owning that issue

**Interfaces:**
- Consumes: completed V2.
- Produces: evidence-backed release readiness report.

- [ ] **Step 1: Review spec coverage line by line**

Map each acceptance criterion in section 22 of the spec to a test, build check or manual smoke result. Any uncovered criterion becomes a failing automated test where feasible before its fix.

- [ ] **Step 2: Review maintainability**

Check for duplicated normalization/diff/classification rules in TypeScript or WPF, oversized files, hidden global state, unbounded retention, swallowed exceptions, unsafe external URL handling and V1 dependencies in runtime startup.

- [ ] **Step 3: Run fresh complete verification**

Repeat the exact full verification command set from Task 8 after all review fixes. Record test counts, build result, package hashes and manual smoke observations.

- [ ] **Step 4: Prepare GitHub handoff**

Provide the local branch name, commit list, release artifact paths, suggested repository description and the exact remaining external actions: create `jadieify-hub/marking-calendar`, push branch, configure repository visibility/public Issues, and publish tag `v0.1.0`.

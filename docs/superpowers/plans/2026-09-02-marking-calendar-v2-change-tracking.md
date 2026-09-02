# Marking Calendar V2 Change Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Показывать проверенную историю изменений непосредственно в ленте, drawer и сводках.

**Architecture:** Core сопоставляет события, строит lineage и текстовые сводки. App формирует расширенную view model с учётом `TimeProvider` и выбранных групп. TypeScript отвечает только за фильтрацию/отображение бейджей, word diff, раскрытие и навигацию.

**Tech Stack:** .NET 10, xUnit, WPF/WebView2, TypeScript, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-02-marking-calendar-v2-hardening-feed-design.md`

## Global Constraints

- Предусловие: этапы A и B полностью зелёные.
- Не объяснять и не предполагать причины изменений.
- `today` только из `TimeProvider`/view model.
- Один коммит на пункт; RED перед production-кодом.
- Не делать GitHub-историю, ICS, автозапуск или системные уведомления.

---

### Task 1: C1 — Tolerant pairing in EventDiffEngine

**Files:**
- Modify: `src/MarkingCalendar.Core/Changes/EventDiffEngine.cs`, `EventChange.cs`, summary factory.
- Test: `tests/MarkingCalendar.Core.Tests/Changes/EventDiffEngineTests.cs`, summary tests.

**Interfaces:** `EventChange.WordingChanged` is true when stage, description, period or URL changed; unmatched pairing uses normalized group/type and stage-word similarity.

- [ ] Add failing tests for moved+edited becoming one Moved, two same-type candidates matching correctly, below-0.5 remaining added/removed and wording summary text.
- [ ] Normalize case, `ё/е` and whitespace; pre-index remaining candidates by group/type.
- [ ] Select unique candidates directly or maximum common-word ratio, breaking ties by date distance.
- [ ] Run Core tests and commit `feat: tolerant event pairing in diff engine`.

### Task 2: C2 — Event lineage

**Files:**
- Create: `src/MarkingCalendar.Core/Changes/EventLineage.cs`, `EventLineageBuilder.cs`.
- Test: `tests/MarkingCalendar.Core.Tests/Changes/EventLineageBuilderTests.cs`.

**Interfaces:** `Build(ChangeHistory, IReadOnlyList<CalendarEvent>)` returns current-id keyed lineage with newest-first entries, `MoveCount` and nullable `FirstSeen`.

- [ ] Write failing tests for three moves, no history, Added first-seen and Changed not increasing move count.
- [ ] Build per-batch current-id dictionaries and traverse `Previous.Id` once per entry.
- [ ] Verify ordering and complexity shape with a multi-batch fixture; run Core tests.
- [ ] Commit `feat: add event lineage tracking`.

### Task 3: C3 — Feed badges and drawer history

**Files:**
- Modify: App view models/factory and UI feed/render/styles.
- Test: factory tests, `feed.test.ts`, renderer tests.

**Interfaces:** Events expose nullable `RecentChange`, `MoveCount`, full `History`; one constant `RecentChangeWindowDays=60`.

- [ ] Add failing xUnit tests using fake time for 30/90-day changes and lineage mapping.
- [ ] Add failing Vitest for conditional legend, changed-only filter, Russian move-count text and newest-first drawer history.
- [ ] Populate view model in C#; filter/render in TypeScript without host round-trip.
- [ ] Run App/UI tests and commit `feat: event lineage and change badges`.

### Task 4: C4 — Word-level before/after diff

**Files:**
- Modify: change summary view models/factory/renderers.
- Create: `src/MarkingCalendar.UI/src/wordDiff.ts`, `wordDiff.test.ts`.

**Interfaces:** `ChangedFields` contains field, previous and current; `wordDiff` returns equal/insert/delete word segments or unhighlighted fallback above 2000 words.

- [ ] Add failing xUnit tests for Changed and Moved+WordingChanged fields.
- [ ] Add failing Vitest for insertion, deletion, replacement, identical and >2000-word fallback.
- [ ] Implement field extraction in C# and LCS/render toggle in TypeScript with semantic ins/del styling.
- [ ] Run tests/build and commit `feat: text diff for changed events`.

### Task 5: C5 — Changes scoped to selected groups

**Files:**
- Modify: `ChangeSummaryFactory`, result/view models/bootstrapper and changes UI.
- Test: Core/App/Vitest summary and notification tests.

**Interfaces:** Summary produces `MineCount`, `OthersCount`, per-item `Mine`; selected groups reorder rather than delete items.

- [ ] Add failing Core tests for priority/stable order/counts.
- [ ] Add failing UI tests for normal modal, mine modal, other-groups toast and history toggle/reveal.
- [ ] Implement three notification modes and immediate seen marking for other-only changes.
- [ ] Run tests and commit `feat: scope changes to selected groups`.

### Task 6: C6 — Compare with archived snapshot

**Files:**
- Modify: `CalendarStore`, App view models/router/bootstrapper and changes UI.
- Test: storage/router/UI comparison tests.

**Interfaces:** `ListArchivesAsync` returns validated metadata sorted newest first; `compareWith {id}` produces transient `ComparisonViewModel` only for known ids.

- [ ] Add failing storage tests for sorting and junk ignore; router test for unknown id.
- [ ] Add failing Vitest for selector, comparison result and close action.
- [ ] Implement metadata-only listing, id validation, diff/summary creation and transient state.
- [ ] Run tests and commit `feat: compare with archived snapshot`.

### Task 7: C7 — Copy change summaries

**Files:**
- Create: `src/MarkingCalendar.Core/Changes/ChangeSummaryTextFormatter.cs` and tests.
- Modify: router/bootstrapper/UI buttons/tests.

**Interfaces:** Formatter returns deterministic Russian text; commands `copyBatch`, `copyNotice`, `copyComparison` resolve server-side data before clipboard access.

- [ ] Add literal expected-text test, 30-line truncation test and empty-selected-groups test.
- [ ] Add router tests for unknown batch and successful copy toast.
- [ ] Implement formatter and commands using A3 clipboard retries; add buttons in all three surfaces.
- [ ] Run tests and commit `feat: copy change summary as text`.

### Task 8: C8 — Documentation, performance and final review

**Files:**
- Modify: README, base spec, screenshots and factory performance test.

- [ ] Build a deterministic 432-event/50×30-history performance fixture; assert under 200 ms and report measured duration.
- [ ] Update sections 8/9, README change-tracking section and screenshots with badges/drawer history.
- [ ] Run complete .NET/UI build and tests without warnings; inspect screenshots and run `git diff --check`.
- [ ] Commit `docs: describe change tracking`.

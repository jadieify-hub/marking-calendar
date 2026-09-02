# Marking Calendar V2 Timeline Feed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить календарные колонки непрерывной лентой событий с главным фильтром товарных групп.

**Architecture:** C# формирует полный стабильный view model, категории, `typeLabel`, группы и сохранённое состояние. TypeScript хранит локальные фильтры и чистыми функциями строит отфильтрованные события, сгруппированные карточки, месяцы и «Ближайшее»; host state не запрашивается на каждый клик.

**Tech Stack:** .NET 10, WPF/WebView2, TypeScript, Vite, Vitest, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-02-marking-calendar-v2-hardening-feed-design.md`

## Global Constraints

- `today` приходит из C# в формате `yyyy-MM-dd`.
- TypeScript не классифицирует события и не сохраняет AppState напрямую.
- Внешние URL открываются только командой хоста.
- Макет `lenta-maket.html` задаёт композицию и палитру; дефекты B9 не копируются.

---

### Task 1: B1 — Feed view model and persisted preferences

**Files:**
- Modify: `ViewModels.cs`, `AppViewModelFactory.cs`, router/bootstrapper/state store.
- Test: factory/router/state tests.

**Interfaces:** `AppViewModel` adds `Today`, `Groups`, `SelectedGroups`, `Theme`; event adds `TypeLabel`; category adds `ColorDark`; removes years.

- [ ] Add failing xUnit theories for every `typeLabel`, `today`, ru-RU group ordering/counts and preference round-trip.
- [ ] Implement mappings and commands `setGroups`, `setTheme`, `openLogs`, `markHistorySeen`; do not send state after `setGroups`.
- [ ] Run App/Infrastructure tests and commit `feat: add timeline feed view model`.

### Task 2: B8 foundation — Pure presentation selectors

**Files:**
- Create: `src/MarkingCalendar.UI/src/feed.ts`
- Create: `src/MarkingCalendar.UI/src/feed.test.ts`

**Interfaces:** Pure exports `filterEvents`, `groupFeed`, `buildUpcoming`, `highlightSegments`, `visibleCounts`; all receive `today`/filters explicitly.

- [ ] Build a 20–30 event fixture containing cross-year/current intervals, three same-group same-day categories, today and 2016.
- [ ] Write failing tests for current-month default, continuing intervals, one card/date/group, counters, 60-day fallback, search/highlight and visible totals.
- [ ] Implement the minimal pure selectors without DOM or host access; run Vitest.
- [ ] Commit `test: define timeline feed behavior` together with the tested selectors.

### Task 3: B2/B3 — Sidebar and continuous feed

**Files:**
- Rewrite calendar regions in `render.ts` and `styles.css`.
- Modify: `contracts.ts`, `development-fixture.ts`, renderer tests.

**Interfaces:** Mounted UI consumes selector results and sends only preference/host commands.

- [ ] Add failing DOM tests for sidebar order, group persistence command, category `aria-pressed`, year anchors, sticky month, today placement, semantic article/button card and reset empty state.
- [ ] Implement 1280/244px layout, responsive breakpoint, group/category counts and current filter summary.
- [ ] Render 90 day rows per page, continuing interval labels and `показано N из M` from the exact rendered event rows.
- [ ] Run Vitest/build and commit `feat: rework calendar as a timeline feed`.

### Task 4: B4 — Upcoming dates

**Files:**
- Modify: `feed.ts`, `render.ts`, `styles.css`, tests.

**Interfaces:** `buildUpcoming` returns actual window, total dates and up to four date groups.

- [ ] Add failing tests for 30→60→90→365 expansion, grouping by date, four-tile limit and empty state.
- [ ] Render compact tiles and scroll actions without applying text search.
- [ ] Verify click targets exact feed day; run Vitest and commit `feat: add upcoming event summary`.

### Task 5: B5/B6 — Event drawer and theme

**Files:**
- Modify: renderer/styles/contracts/tests.

**Interfaces:** Drawer receives one grouped card and sends `openExternal`; theme sends `setTheme`.

- [ ] Add failing tests for full event rows, host-only links, accessible dialog behavior and all three theme values.
- [ ] Implement 460px drawer, system fonts, root category variables for light/dark, 40px targets and visible focus.
- [ ] Run Vitest/build and commit `feat: polish timeline details and themes`.

### Task 6: B7 — Restyle history without behavior changes

**Files:**
- Modify: history renderer/styles/tests.

- [ ] Add snapshot/behavior assertions preserving A8 counts, unread badge and target highlight.
- [ ] Apply feed palette and typography without changing commands or history semantics.
- [ ] Run Vitest and commit `style: align change history with timeline`.

### Task 7: B10 — Screenshots, docs and final review

**Files:**
- Update: `assets/screenshots/calendar.png`, `assets/screenshots/changes.png`, dark screenshots, `README.md`.

- [ ] Run all .NET/UI tests and Release build with zero warnings.
- [ ] Capture 1440×900 light calendar and dark changes views; inspect saved files.
- [ ] Update README features/screenshots and run `git diff --check`.
- [ ] Review B1–B9 coverage and commit `docs: update timeline feed screenshots`.

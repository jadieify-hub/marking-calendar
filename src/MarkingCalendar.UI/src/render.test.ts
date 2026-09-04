import { beforeEach, describe, expect, it, vi } from "vitest";
import { mountApp, renderApp } from "./render";
import type { AppViewModel } from "./contracts";

const model = {
  updatedAt: "02.09.2026, 10:00",
  eventCount: 2,
  today: "2026-09-02",
  groups: [
    { key: "игрушки", name: "Игрушки", eventCount: 1 },
    { key: "обувь", name: "Обувь", eventCount: 1 },
  ],
  groupSuggestions: [],
  profile: { roles: [], sectors: [], selectedRoles: [], selectedSectors: [], manualGroups: {}, roleCategories: [], onboardingCompleted: true },
  selectedGroups: [],
  hasSelectedGroups: false,
  theme: "auto",
  categories: [
    { id: "retail", label: "Розничная продажа", color: "#1f93bb", colorDark: "#3fbde4" },
    { id: "marking", label: "Маркировка", color: "#1e9a63", colorDark: "#3fc98a" },
  ],
  events: [
    { id: "1", start: "2026-09-01", end: null, period: "с 1 сентября", group: "Игрушки", type: "Розничная продажа", typeLabel: "Розничная продажа", stage: "Старт", description: "", url: null, category: "retail", recentChange: null, moveCount: 0, history: [] },
    { id: "2", start: "2027-01-01", end: null, period: "с 1 января", group: "Обувь", type: "Маркировка", typeLabel: "Маркировка", stage: "Старт", description: "", url: null, category: "marking", recentChange: null, moveCount: 0, history: [] },
  ],
  archives: [],
  comparison: null,
  history: { unreadCount: 0, batches: [] },
  status: { kind: "ready", message: "Данные актуальны" },
  toast: null,
  updateNotice: null,
  appUpdate: { kind: "current", message: "Установлена последняя версия", progress: null, version: null, canRestart: false },
  about: { name: "Календарь маркировки", version: "0.1.5", developer: "Руслан Керусов", publisher: "KRS", repositoryUrl: "https://github.com/jadieify-hub/marking-calendar", historyUrl: "https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md", supportUrl: "https://pay.cloudtips.ru/p/a18da555", disclaimer: "Независимый проект", publicHistoryEnabled: true, changeNotificationsEnabled: true },
} as const;

const GUIDE_STORAGE_KEY = "marking-calendar.guide.v2";
const SUPPORT_PROMPT_STORAGE_KEY = "marking-calendar.support-prompt.v1";

describe("renderApp", () => {
  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(GUIDE_STORAGE_KEY, "done");
  });

  it("uses the browser's default interface scale", () => {
    const root = document.createElement("div");

    mountApp(root, vi.fn());

    expect(document.documentElement.style.zoom).toBe("");
  });

  it("mounts one stable shell and updates dependent regions without replacing it", () => {
    const root = document.createElement("div");
    const mounted = mountApp(root, vi.fn());
    mounted.update(model);
    const shell = root.querySelector(".app-shell");

    mounted.update({ ...model, status: { kind: "checking", message: "Проверяем обновления…" } });

    expect(root.querySelector(".app-shell")).toBe(shell);
    expect(root.querySelector(".status-copy strong")?.textContent).toBe("Проверяем обновления…");
  });

  it("keeps the pinned header visually stable while the event pane scrolls", () => {
    const root = document.createElement("div");
    mountApp(root, vi.fn());
    const topbar = root.querySelector<HTMLElement>(".topbar");
    const content = root.querySelector<HTMLElement>(".content");

    if (content) content.scrollTop = 120;
    content?.dispatchEvent(new Event("scroll"));
    expect(topbar?.classList.contains("is-compact")).toBe(false);
  });

  it("keeps event search and compact year navigation with the continuous feed", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());

    expect(Array.from(root.querySelectorAll<HTMLElement>(".sidebar-section")).map((item) => item.dataset.section)).toEqual([
      "groups", "categories", "past",
    ]);
    expect(root.querySelector('[data-filter="query"]')?.closest(".content")).not.toBeNull();
    expect(root.querySelector('[data-filter="query"]')?.closest(".sidebar")).toBeNull();
    expect(root.querySelectorAll(".feed-month")).toHaveLength(2);
    expect(Array.from(root.querySelectorAll<HTMLButtonElement>("[data-year]")).map((item) => item.dataset.year)).toEqual(["2026", "2027"]);
    expect(root.querySelector("[data-year-current]")?.textContent).toBe("2026");
    expect(root.querySelectorAll("[data-year-direction]")).toHaveLength(2);
    expect(root.querySelectorAll("article.feed-card")).toHaveLength(2);
    expect(root.querySelectorAll(".event-row")).toHaveLength(2);
    expect(root.querySelector(".feed-status")?.textContent).toContain("Показано 2 из 2");
    expect(root.querySelector(".filter-summary")?.textContent).toContain("2 категории");
  });

  it("keeps product group controls focused on switching the visible set", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());

    const groupSection = root.querySelector<HTMLElement>('[data-section="groups"]');
    expect(Array.from(groupSection?.querySelectorAll("button") ?? [], (button) => button.textContent?.trim())).toEqual([
      "Только мои",
      "Все",
    ]);
  });

  it("opens the changes view for a host notification", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const mounted = mountApp(root, send);
    mounted.update(model);

    mounted.openChanges("batch-1");

    expect(root.querySelector<HTMLElement>(".changes-view")?.hidden).toBe(false);
    expect(send).toHaveBeenCalledWith({ type: "openChanges", batchId: "batch-1" });
  });

  it("exports exactly the events visible after filters", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);
    const query = root.querySelector<HTMLInputElement>('[data-filter="query"]');
    if (query) {
      query.value = "Игрушки";
      query.dispatchEvent(new Event("input", { bubbles: true }));
    }

    const exportButton = root.querySelector<HTMLButtonElement>('[data-action="export-calendar"]');
    expect(exportButton?.textContent).toBe("Экспорт в календарь · 1 событие");
    exportButton?.click();

    expect(send).toHaveBeenCalledWith({ type: "exportCalendar", eventIds: ["1"] });
  });

  it("moves to the adjacent available year with the compact navigator", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());
    const current = root.querySelector<HTMLButtonElement>("[data-year-current]");
    const previous = root.querySelector<HTMLButtonElement>('[data-year-direction="previous"]');
    const next = root.querySelector<HTMLButtonElement>('[data-year-direction="next"]');

    expect(current?.textContent).toBe("2026");
    expect(previous?.disabled).toBe(true);
    expect(next?.disabled).toBe(false);

    next?.click();

    expect(current?.textContent).toBe("2027");
    expect(previous?.disabled).toBe(false);
    expect(next?.disabled).toBe(true);
  });

  it("renders 90 dates initially and keeps the visible event count exact when loading more", () => {
    const root = document.createElement("div");
    const start = Date.UTC(2026, 8, 2);
    const events = Array.from({ length: 95 }, (_, index) => ({
      ...model.events[0],
      id: `page-${index}`,
      start: new Date(start + index * 86_400_000).toISOString().slice(0, 10),
    }));
    renderApp(root, {
      ...model,
      eventCount: events.length,
      events,
      groups: [{ key: "игрушки", name: "Игрушки", eventCount: events.length }],
    }, vi.fn());

    expect(root.querySelectorAll(".feed-day")).toHaveLength(90);
    expect(root.querySelectorAll(".event-row")).toHaveLength(90);
    expect(root.querySelector(".feed-status")?.textContent).toContain("Показано 90 из 95");
    expect(root.querySelector('[data-action="load-more"]')?.textContent).toBe("Показать ещё 5 из 5");

    root.querySelector<HTMLButtonElement>('[data-action="load-more"]')?.click();

    expect(root.querySelectorAll(".feed-day")).toHaveLength(95);
    expect(root.querySelectorAll(".event-row")).toHaveLength(95);
    expect(root.querySelector(".feed-status")?.textContent).toContain("Показано 95 из 95");
    expect(root.querySelector('[data-action="load-more"]')).toBeNull();
  });

  it("renders Upcoming by date, expands its window and ignores the text search", () => {
    const root = document.createElement("div");
    const dates = ["2026-10-17", "2026-10-18", "2026-10-19", "2026-10-20", "2026-10-21"];
    const groups = ["БАД", "Вода", "Игрушки", "Обувь"];
    const events = [
      ...groups.map((group, index) => ({ ...model.events[0], id: `near-group-${index}`, start: dates[0]!, group })),
      ...dates.slice(1).map((start, index) => ({ ...model.events[0], id: `near-date-${index}`, start })),
    ];
    renderApp(root, {
      ...model,
      eventCount: events.length,
      events,
      groups: groups.map((name) => ({ key: name.toLocaleLowerCase("ru-RU"), name, eventCount: events.filter((event) => event.group === name).length })),
    }, vi.fn());

    expect(root.querySelector<HTMLElement>(".upcoming")?.hidden).toBe(false);
    expect(root.querySelector(".upcoming-window")?.textContent).toContain("60 дней");
    expect(root.querySelectorAll(".upcoming-tile")).toHaveLength(4);
    const firstUpcoming = root.querySelector<HTMLElement>(".upcoming-tile");
    expect(Array.from(firstUpcoming?.querySelectorAll(".upcoming-group-name") ?? [], (item) => item.textContent)).toEqual([
      "БАД",
      "Вода",
      "Игрушки",
    ]);
    expect(firstUpcoming?.querySelectorAll(".product-group-icon")).toHaveLength(3);
    expect(firstUpcoming?.querySelector(".upcoming-count")?.textContent).toBe("4 события");
    expect(firstUpcoming?.textContent).not.toContain("4 группы");
    expect(firstUpcoming?.textContent).toContain("ещё 1 группа");
    expect(root.querySelector(".upcoming-more")?.textContent).toContain("ещё 1 в ленте");

    const target = root.querySelector<HTMLElement>('[data-date="2026-10-17"]');
    const scrollIntoView = vi.fn();
    if (target) target.scrollIntoView = scrollIntoView;
    root.querySelector<HTMLButtonElement>('[data-upcoming-date="2026-10-17"]')?.click();
    expect(scrollIntoView).toHaveBeenCalledWith({ behavior: "smooth", block: "center" });

    const search = root.querySelector<HTMLInputElement>('[data-filter="query"]');
    if (search) {
      search.value = "несовпадение";
      search.dispatchEvent(new Event("input", { bubbles: true }));
    }
    expect(root.querySelector(".feed-empty")).not.toBeNull();
    expect(root.querySelectorAll(".upcoming-tile")).toHaveLength(4);
  });

  it("keeps group selection local and sends only normalized group keys", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);

    const checkbox = root.querySelector<HTMLInputElement>('[data-group="игрушки"]');
    expect(checkbox).not.toBeNull();
    if (checkbox) {
      checkbox.checked = true;
      checkbox.dispatchEvent(new Event("change", { bubbles: true }));
    }

    expect(send).toHaveBeenCalledWith({ type: "setGroups", groups: ["игрушки"] });
    expect(root.querySelector('[data-event-id="1"]')).not.toBeNull();
    expect(root.querySelector('[data-event-id="2"]')).toBeNull();
    expect(root.querySelector(".feed-status")?.textContent).toContain("Показано 1 из 2");
  });

  it("keeps a new-group banner visible across other filters and remembers a local hide across updates", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const withSuggestion: AppViewModel = {
      ...model,
      selectedGroups: ["обувь"],
      hasSelectedGroups: true,
      groupSuggestions: [{ key: "игрушки", name: "Игрушки", eventCount: 1, firstEventDate: "2026-10-01", message: "Новая группа в календаре" }],
    };
    const mounted = mountApp(root, send);
    mounted.update(withSuggestion);

    expect(root.querySelector(".group-suggestions")?.textContent).toContain("Игрушки");
    root.querySelector<HTMLButtonElement>('[data-hide-group="игрушки"]')?.click();
    mounted.update(withSuggestion);

    expect(root.querySelector(".group-suggestions")?.textContent).not.toContain("Игрушки");
    expect(send).toHaveBeenCalledWith({ type: "hideGroupSuggestion", key: "игрушки" });
  });

  it("adds a suggested group to the complete normalized selection", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, {
      ...model,
      selectedGroups: ["обувь"],
      hasSelectedGroups: true,
      groupSuggestions: [{ key: "игрушки", name: "Игрушки", eventCount: 1, firstEventDate: "2026-10-01", message: "Новая группа в календаре" }],
      groups: [
        { ...model.groups[0]!, isNew: true },
        { ...model.groups[1]!, isNew: false },
      ],
    }, send);

    root.querySelector<HTMLButtonElement>('[data-add-group="игрушки"]')?.click();

    expect(send).toHaveBeenCalledWith({ type: "setGroups", groups: ["игрушки", "обувь"] });
    expect(root.querySelector(".group-suggestions")?.textContent).not.toContain("Игрушки");
    expect(root.querySelector('[data-group="игрушки"]')?.parentElement?.querySelector(".group-new-badge")).not.toBeNull();
    expect(root.querySelector('[data-group="обувь"]')?.parentElement?.querySelector(".group-new-badge")).toBeNull();
  });

  it("labels a completed product group", () => {
    const root = document.createElement("div");
    renderApp(root, {
      ...model,
      groups: [{ ...model.groups[0]!, isCompleted: true }],
    }, vi.fn());

    expect(root.querySelector(".group-completed-badge")?.textContent).toBe("завершено");
  });

  it("supports multiple roles, sector unions, and persistent manual group choices in onboarding", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, {
      ...model,
      groups: [
        { key: "бад", name: "БАД", eventCount: 1 },
        { key: "лекарства", name: "Лекарства", eventCount: 1 },
      ],
      profile: {
        roles: [{ id: "retail", label: "Розница" }, { id: "producer", label: "Производство или импорт" }],
        sectors: [
          { id: "food", label: "Продукты", activeGroupCount: 1, groupKeys: ["бад"] },
          { id: "pharma", label: "Аптека", activeGroupCount: 2, groupKeys: ["бад", "лекарства"] },
        ],
        selectedRoles: [], selectedSectors: [], manualGroups: {}, roleCategories: [], onboardingCompleted: false,
      },
    }, send);

    root.querySelector<HTMLButtonElement>('[data-profile-role="retail"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-profile-role="producer"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-profile-sector="food"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-profile-sector="pharma"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-profile-sector="pharma"]')?.click();
    expect(root.querySelector<HTMLInputElement>('[data-profile-group="бад"]')?.checked).toBe(true);
    root.querySelector<HTMLButtonElement>('[data-profile-sector="food"]')?.click();
    expect(root.querySelector<HTMLInputElement>('[data-profile-group="бад"]')?.checked).toBe(false);
    const bad = root.querySelector<HTMLInputElement>('[data-profile-group="бад"]');
    if (bad) { bad.checked = true; bad.dispatchEvent(new Event("change", { bubbles: true })); }
    root.querySelector<HTMLButtonElement>('[data-profile-sector="food"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-profile-sector="food"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="profile-save"]')?.click();

    expect(send).toHaveBeenCalledWith({ type: "saveProfile", roles: ["retail", "producer"], sectors: [], groups: ["бад"] });
  });

  it("skips onboarding on Escape and reopens profile from Help", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const mounted = mountApp(root, send);
    mounted.update({ ...model, profile: { ...model.profile, onboardingCompleted: false } });

    root.querySelector<HTMLElement>('.profile-dialog')?.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(send).toHaveBeenCalledWith({ type: "skipProfile" });

    mounted.update(model);
    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="profile"]')?.click();
    expect(root.querySelector(".profile-dialog")).not.toBeNull();
  });

  it("opens one accessible dialog with every event from the date and group card", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const mounted = mountApp(root, send);
    const groupedModel = {
      ...model,
      eventCount: 2,
      groups: [{ key: "бад", name: "БАД", eventCount: 2 }],
      events: [
        { ...model.events[0], id: "same-a", start: "2026-09-10", group: "БАД", url: "https://честныйзнак.рф/a" },
        { ...model.events[1], id: "same-b", start: "2026-09-10", group: "БАД", description: "Полное описание", period: "с 10 сентября", url: "https://честныйзнак.рф/business/projects/grocery?from=calendar#details" },
      ],
    };
    mounted.update(groupedModel);
    const card = root.querySelector<HTMLElement>("article.feed-card");
    const opener = root.querySelector<HTMLButtonElement>("article.feed-card [data-card-key]");
    expect(card?.firstElementChild?.tagName).toBe("H3");
    expect(card?.querySelectorAll("button")).toHaveLength(1);
    expect(opener?.textContent).toBe("Подробнее");
    opener?.click();

    expect(root.querySelector(".event-dialog")?.getAttribute("aria-modal")).toBe("true");
    expect(root.querySelector(".modal-layer")?.classList.contains("has-drawer")).toBe(false);
    expect(root.querySelector(".drawer-title")?.textContent).toContain("10.09.2026");
    expect(root.querySelector(".drawer-title")?.textContent).toContain("БАД");
    expect(root.querySelectorAll(".drawer-event")).toHaveLength(2);
    expect(root.querySelectorAll(".drawer-category")).toHaveLength(2);
    expect(root.querySelector(".event-dialog")?.textContent).toContain("Полное описание");
    expect(root.querySelector(".event-dialog a")).toBeNull();

    root.querySelector<HTMLButtonElement>('[data-source-event-id="same-b"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "openExternal", url: "https://честныйзнак.рф/business/projects/grocery?from=calendar#details" });

    root.querySelector<HTMLButtonElement>('[data-goods-event-id="same-b"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "openExternal", url: "https://xn--80ajghhoc2aj1c8b.xn--p1ai/business/projects/grocery/mark_goods/?from=calendar#details" });

    mounted.update({ ...groupedModel, status: { kind: "checking", message: "Проверяем обновления…" } });
    expect(root.querySelectorAll(".drawer-event")).toHaveLength(2);
    expect(root.querySelector(".event-dialog")).not.toBeNull();
  });

  it("does not show external event actions when the source URL is absent", () => {
    const root = document.createElement("div");
    const event = { ...model.events[0], url: null };

    renderApp(root, { ...model, events: [event] }, vi.fn());
    root.querySelector<HTMLButtonElement>("[data-card-key]")?.click();

    expect(root.querySelector("[data-source-event-id]")).toBeNull();
    expect(root.querySelector("[data-goods-event-id]")).toBeNull();
  });

  it("hides the marked goods action for a group without that page", () => {
    const root = document.createElement("div");
    const event = {
      ...model.events[0],
      id: "no-goods-page",
      group: "Средства гигиены",
      url: "https://честныйзнак.рф/business/projects/chemistry/",
    };

    renderApp(root, {
      ...model,
      groups: [{ key: "средства гигиены", name: "Средства гигиены", eventCount: 1, hasGoodsPage: false }],
      events: [event],
    }, vi.fn());
    root.querySelector<HTMLButtonElement>("[data-card-key]")?.click();

    expect(root.querySelector("[data-source-event-id]")).not.toBeNull();
    expect(root.querySelector("[data-goods-event-id]")).toBeNull();
  });

  it("shows recent-change badges without adding a separate calendar legend", () => {
    const root = document.createElement("div");
    const changedEvent = {
      ...model.events[1],
      id: "changed",
      start: "2026-10-01",
      recentChange: { kind: "moved" as const, checkedAt: "2026-09-02T10:00:00+03:00", previousStart: "2026-09-15", previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] },
      moveCount: 2,
      history: [],
    };

    renderApp(root, { ...model, events: [model.events[0], changedEvent] }, vi.fn());

    expect(root.querySelector(".change-badge")?.textContent).toBe("перенесено с 15.09.2026");
    expect(root.querySelector(".move-count-badge")?.textContent).toBe("переносилось 2 раза");
    expect(root.querySelector(".change-legend")).toBeNull();

    renderApp(root, model, vi.fn());
    expect(root.querySelector(".change-badge")).toBeNull();
    expect(root.querySelector(".change-legend")).toBeNull();
  });

  it("filters the feed to recently changed events without asking the host for new state", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const changedEvent = {
      ...model.events[1],
      id: "changed",
      start: "2026-10-01",
      recentChange: { kind: "added" as const, checkedAt: "2026-09-02T10:00:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] },
    };
    renderApp(root, { ...model, events: [model.events[0], changedEvent] }, send);

    const checkbox = root.querySelector<HTMLInputElement>('[data-filter="changed"]');
    if (checkbox) {
      checkbox.checked = true;
      checkbox.dispatchEvent(new Event("change", { bubbles: true }));
    }

    expect(root.querySelector('[data-event-id="1"]')).toBeNull();
    expect(root.querySelector('[data-event-id="changed"]')).not.toBeNull();
    expect(send).not.toHaveBeenCalled();
  });

  it("renders event history newest first in the drawer", () => {
    const root = document.createElement("div");
    const eventWithHistory = {
      ...model.events[1],
      id: "history-event",
      start: "2026-10-01",
      history: [
        { kind: "moved" as const, checkedAt: "2026-09-02T10:00:00+03:00", previousStart: "2026-09-15", previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] },
        { kind: "changed" as const, checkedAt: "2026-08-20T09:30:00+03:00", previousStart: null, previousEnd: null, previousStage: "Прежний этап", previousDescription: "Прежнее описание", changedFields: [] },
        { kind: "added" as const, checkedAt: "2026-08-01T08:00:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] },
      ],
    };
    renderApp(root, { ...model, eventCount: 1, events: [eventWithHistory], groups: [{ key: "обувь", name: "Обувь", eventCount: 1 }] }, vi.fn());

    root.querySelector<HTMLButtonElement>("[data-card-key]")?.click();

    const items = Array.from(root.querySelectorAll<HTMLElement>(".event-history-item")).map((item) => item.textContent);
    expect(root.querySelector(".event-history")?.textContent).toContain("Проверено: 02.09.2026, 10:00");
    expect(items).toEqual([
      "02.09.2026 — перенесено с 15.09.2026 на 01.10.2026",
      "20.08.2026 — изменена формулировка",
      "01.08.2026 — добавлено",
    ]);
  });

  it("expands word-level before and after text in history, notice and drawer", () => {
    const changedFields = [{ field: "stage" as const, previous: "Начало передачи сведений", current: "Старт обязательной передачи сведений" }];
    const summary = { kind: "changed" as const, title: "Игрушки", detail: "изменены параметры", stage: "Старт", changedFields, mine: false };

    const historyRoot = document.createElement("div");
    renderApp(historyRoot, {
      ...model,
      history: { unreadCount: 0, batches: [{ id: "changed", checkedAt: "02.09.2026, 10:00", isUnread: false, counts: { moved: 0, added: 0, changed: 1, removed: 0, total: 1 }, mineCount: 0, othersCount: 1, items: [summary] }] },
    }, vi.fn());
    historyRoot.querySelector<HTMLButtonElement>('[data-view="changes"]')?.click();
    historyRoot.querySelector<HTMLButtonElement>(".history-batch .diff-toggle")?.click();
    expect(historyRoot.querySelector(".history-batch .text-diff del")?.textContent).toBe("Начало");
    expect(historyRoot.querySelector(".history-batch .text-diff ins")?.textContent).toContain("Старт обязательной");

    const noticeRoot = document.createElement("div");
    renderApp(noticeRoot, {
      ...model,
      updateNotice: { batchId: "changed", counts: { moved: 0, added: 0, changed: 1, removed: 0, total: 1 }, mineCount: 0, othersCount: 1, items: [summary] },
    }, vi.fn());
    noticeRoot.querySelector<HTMLButtonElement>(".notice-item .diff-toggle")?.click();
    expect(noticeRoot.querySelector(".notice-item .text-diff")?.textContent).toContain("Было");
    expect(noticeRoot.querySelector(".notice-item .text-diff")?.textContent).toContain("Стало");

    const drawerRoot = document.createElement("div");
    const eventWithDiff = {
      ...model.events[1],
      start: "2026-10-01",
      history: [{ kind: "changed" as const, checkedAt: "2026-09-02T10:00:00+03:00", previousStart: null, previousEnd: null, previousStage: "Начало передачи сведений", previousDescription: null, changedFields }],
    };
    renderApp(drawerRoot, { ...model, eventCount: 1, events: [eventWithDiff], groups: [{ key: "обувь", name: "Обувь", eventCount: 1 }] }, vi.fn());
    drawerRoot.querySelector<HTMLButtonElement>("[data-card-key]")?.click();
    drawerRoot.querySelector<HTMLButtonElement>(".event-history .diff-toggle")?.click();
    expect(drawerRoot.querySelector(".event-history .text-diff del")?.textContent).toBe("Начало");
    expect(drawerRoot.querySelector(".event-history .text-diff ins")?.textContent).toContain("Старт обязательной");
  });

  it("applies and persists a local theme choice from the themed menu without waiting for host state", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const mounted = mountApp(root, send);
    mounted.update(model);
    const theme = root.querySelector<HTMLButtonElement>('[data-theme-current]');
    const menu = root.querySelector<HTMLElement>('.theme-menu');
    expect(root.querySelector("select[data-theme-picker]")).toBeNull();
    expect(theme?.textContent?.trim()).toBe("Авто");
    expect(menu?.hidden).toBe(true);

    theme?.click();
    expect(menu?.hidden).toBe(false);
    root.querySelector<HTMLButtonElement>('[data-theme="dark"]')?.click();

    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(theme?.textContent?.trim()).toBe("Тёмная");
    expect(root.querySelector('[data-theme="dark"]')?.getAttribute("aria-selected")).toBe("true");
    expect(menu?.hidden).toBe(true);
    expect(send).toHaveBeenCalledWith({ type: "setTheme", theme: "dark" });

    mounted.update({ ...model, status: { kind: "checking", message: "Проверяем обновления…" } });
    expect(document.documentElement.dataset.theme).toBe("dark");

    theme?.click();
    root.querySelector<HTMLButtonElement>('[data-theme="auto"]')?.click();
    expect(document.documentElement.dataset.theme).toBeUndefined();
  });

  it("supports keyboard navigation and dismisses the theme menu", () => {
    const root = document.createElement("div");
    document.body.append(root);
    renderApp(root, model, vi.fn());
    const current = root.querySelector<HTMLButtonElement>("[data-theme-current]");
    const menu = root.querySelector<HTMLElement>(".theme-menu");
    const options = Array.from(root.querySelectorAll<HTMLButtonElement>('.theme-menu [role="option"]'));

    current?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    expect(menu?.hidden).toBe(false);
    expect(document.activeElement).toBe(options[0]);
    options[0]?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    expect(document.activeElement).toBe(options[1]);
    options[1]?.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(menu?.hidden).toBe(true);
    expect(document.activeElement).toBe(current);

    current?.click();
    root.querySelector<HTMLButtonElement>('[data-action="refresh"]')?.click();
    expect(menu?.hidden).toBe(true);
    expect(current?.getAttribute("aria-expanded")).toBe("false");
    root.remove();
  });

  it("resets an empty search and labels a day equal to injected today without a divider", () => {
    const root = document.createElement("div");
    renderApp(root, {
      ...model,
      events: [{ ...model.events[0], start: model.today }],
      eventCount: 1,
      groups: [{ key: "игрушки", name: "Игрушки", eventCount: 1 }],
    }, vi.fn());
    const search = root.querySelector<HTMLInputElement>('[data-filter="query"]');
    if (search) {
      search.value = "ничего не найдено";
      search.dispatchEvent(new Event("input", { bubbles: true }));
    }
    expect(root.querySelector(".feed-empty")).not.toBeNull();

    root.querySelector<HTMLButtonElement>('[data-action="reset-filters"]')?.click();

    expect(root.querySelector(".feed-empty")).toBeNull();
    expect(root.querySelector(".today-line")).toBeNull();
    expect(root.querySelector(".day-mark span")?.textContent).toBe("сегодня");
  });

  it("shows distinct decorative icons without replacing product group names", () => {
    const root = document.createElement("div");

    renderApp(root, model, vi.fn());

    const filterIcons = Array.from(root.querySelectorAll<HTMLElement>(".group-list .product-group-icon"));
    expect(filterIcons).toHaveLength(2);
    expect(new Set(filterIcons.map((icon) => icon.dataset.icon)).size).toBe(2);
    expect(filterIcons.every((icon) => icon.getAttribute("aria-hidden") === "true")).toBe(true);
    expect(root.querySelector('[data-group="игрушки"]')?.closest("label")?.textContent).toContain("Игрушки");
    expect(root.querySelector('[data-group="обувь"]')?.closest("label")?.textContent).toContain("Обувь");

    const cardIcons = Array.from(root.querySelectorAll<HTMLElement>(".card-title .product-group-icon"));
    expect(cardIcons).toHaveLength(2);
    expect(cardIcons.map((icon) => icon.dataset.icon)).toEqual(expect.arrayContaining(
      filterIcons.map((icon) => icon.dataset.icon),
    ));
    expect(Array.from(root.querySelectorAll(".card-title"), (title) => title.textContent)).toEqual(["Игрушки", "Обувь"]);
  });

  it("places the today divider before the next month heading", () => {
    const root = document.createElement("div");
    renderApp(root, {
      ...model,
      events: [
        { ...model.events[0], id: "before", start: "2026-09-01" },
        { ...model.events[1], id: "after", start: "2026-10-01" },
      ],
    }, vi.fn());

    const marker = root.querySelector<HTMLElement>(".today-line");
    const october = root.querySelectorAll<HTMLElement>(".feed-month")[1];
    expect(marker?.parentElement).toBe(root.querySelector(".timeline-feed"));
    expect(marker && october && (marker.compareDocumentPosition(october) & Node.DOCUMENT_POSITION_FOLLOWING)).toBeTruthy();
  });

  it("recalculates group and category counts from the other active filters", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());

    root.querySelector<HTMLButtonElement>('[data-category="retail"]')?.click();
    const groupLabels = Array.from(root.querySelectorAll<HTMLLabelElement>(".group-list label"));
    expect(groupLabels.map((label) => label.querySelector(".group-name")?.textContent)).toEqual(["Обувь", "Игрушки"]);
    expect(groupLabels[1]?.classList.contains("is-empty")).toBe(true);
    expect(groupLabels[1]?.querySelector(".filter-count")?.textContent).toBe("0");

    const shoes = root.querySelector<HTMLInputElement>('[data-group="обувь"]');
    if (shoes) {
      shoes.checked = true;
      shoes.dispatchEvent(new Event("change", { bubbles: true }));
    }
    expect(root.querySelector('[data-category="retail"] .filter-count')?.textContent).toBe("0");
    expect(root.querySelector('[data-category="marking"] .filter-count')?.textContent).toBe("1");
  });

  it("reveals past events when an old year is chosen from the compact navigator", () => {
    const root = document.createElement("div");
    renderApp(root, {
      ...model,
      eventCount: 2,
      groups: [{ key: "игрушки", name: "Игрушки", eventCount: 2 }],
      events: [
        { ...model.events[0], id: "old", start: "2016-01-01" },
        { ...model.events[0], id: "current", start: "2026-09-02" },
      ],
    }, vi.fn());

    const current = root.querySelector<HTMLButtonElement>("[data-year-current]");
    const menu = root.querySelector<HTMLElement>(".year-menu");
    const selectedYear = root.querySelector<HTMLElement>('[data-year="2026"]');
    const scrollIntoView = vi.fn();
    if (selectedYear) selectedYear.scrollIntoView = scrollIntoView;
    expect(Array.from(root.querySelectorAll<HTMLButtonElement>("[data-year]")).map((item) => item.dataset.year)).toEqual(["2016", "2026"]);
    expect(root.querySelector('[data-event-id="old"]')).toBeNull();
    current?.click();
    expect(menu?.hidden).toBe(false);
    expect(scrollIntoView).toHaveBeenCalledWith({ block: "nearest" });
    root.querySelector<HTMLButtonElement>('[data-year="2016"]')?.click();
    expect(root.querySelector<HTMLInputElement>('[data-filter="past"]')?.checked).toBe(true);
    expect(root.querySelector('[data-event-id="old"]')).not.toBeNull();
    expect(current?.textContent).toBe("2016");
    expect(menu?.hidden).toBe(true);
  });

  it("renders every matching date when past events are enabled", () => {
    const root = document.createElement("div");
    const pastEvents = Array.from({ length: 95 }, (_, index) => ({
      ...model.events[0],
      id: `past-${index}`,
      start: new Date(Date.UTC(2025, 0, index + 1)).toISOString().slice(0, 10),
    }));
    renderApp(root, {
      ...model,
      eventCount: 96,
      events: [...pastEvents, { ...model.events[0], id: "future", start: "2026-10-01" }],
      groups: [{ key: "игрушки", name: "Игрушки", eventCount: 96 }],
    }, vi.fn());

    const showPast = root.querySelector<HTMLInputElement>('[data-filter="past"]');
    if (showPast) {
      showPast.checked = true;
      showPast.dispatchEvent(new Event("change", { bubbles: true }));
    }

    expect(root.querySelectorAll(".feed-day")).toHaveLength(96);
    expect(root.querySelector('[data-action="load-more"]')).toBeNull();
  });

  it("smoothly returns to today after toggling past events", () => {
    const root = document.createElement("div");
    const scrolledElements: HTMLElement[] = [];
    const originalScrollIntoView = HTMLElement.prototype.scrollIntoView;
    const originalRequestAnimationFrame = window.requestAnimationFrame;
    const requestAnimationFrame = vi.fn((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });
    HTMLElement.prototype.scrollIntoView = function scrollIntoView(): void { scrolledElements.push(this); };
    window.requestAnimationFrame = requestAnimationFrame;
    try {
      renderApp(root, {
        ...model,
        eventCount: 3,
        events: [
          { ...model.events[0], id: "old", start: "2019-01-01" },
          { ...model.events[0], id: "month-start", start: "2026-09-01" },
          { ...model.events[0], id: "future", start: "2026-10-01" },
        ],
        groups: [{ key: "игрушки", name: "Игрушки", eventCount: 3 }],
      }, vi.fn());

      root.querySelector<HTMLButtonElement>("[data-year-current]")?.click();
      root.querySelector<HTMLButtonElement>('[data-year="2019"]')?.click();
      expect(root.querySelector("[data-year-current]")?.textContent).toBe("2019");
      scrolledElements.length = 0;

      const showPast = root.querySelector<HTMLInputElement>('[data-filter="past"]');
      if (showPast) {
        showPast.checked = false;
        showPast.dispatchEvent(new Event("change", { bubbles: true }));
      }

      expect(requestAnimationFrame).toHaveBeenCalledOnce();
      expect(scrolledElements).toHaveLength(1);
      expect(scrolledElements[0]?.classList.contains("today-line")).toBe(true);
      expect(root.querySelector("[data-year-current]")?.textContent).toBe("2026");
    } finally {
      if (originalScrollIntoView) HTMLElement.prototype.scrollIntoView = originalScrollIntoView;
      else delete (HTMLElement.prototype as Partial<HTMLElement>).scrollIntoView;
      window.requestAnimationFrame = originalRequestAnimationFrame;
    }
  });

  it("preserves category filters and an open dialog across host updates", () => {
    const root = document.createElement("div");
    const mounted = mountApp(root, vi.fn());
    mounted.update(model);

    root.querySelector<HTMLButtonElement>('[data-category="retail"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="about"]')?.click();
    mounted.update({ ...model, status: { kind: "checking", message: "Проверяем обновления…" } });

    expect(root.querySelector<HTMLButtonElement>('[data-category="retail"]')?.getAttribute("aria-pressed")).toBe("false");
    expect(root.querySelector('[data-event-id="1"]')).toBeNull();
    expect(root.querySelector(".about-dialog")).not.toBeNull();
  });
  it("shows a host failure as a non-blocking toast for five seconds", () => {
    vi.useFakeTimers();
    const root = document.createElement("div");
    renderApp(root, { ...model, toast: { kind: "error", message: "Не удалось скопировать ссылку.", action: null, batchId: null } }, vi.fn());

    const toast = root.querySelector<HTMLElement>(".toast");
    expect(toast?.hidden).toBe(false);
    expect(toast?.textContent).toBe("Не удалось скопировать ссылку.");

    vi.advanceTimersByTime(5_000);
    expect(toast?.hidden).toBe(true);
    vi.useRealTimers();
  });
  it("renders category controls as a color legend with non-color selected state", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());

    const retail = root.querySelector<HTMLButtonElement>('[data-category="retail"]');
    expect(retail?.getAttribute("aria-pressed")).toBe("true");
    expect(retail?.querySelector<HTMLElement>(".legend-swatch")?.style.backgroundColor).toContain("--category-current-retail");
    expect(retail?.querySelector(".legend-check")?.textContent).toBe("✓");
  });

  it("supports keyboard category filtering and hides matching events", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());
    const retail = root.querySelector<HTMLButtonElement>('[data-category="retail"]');

    retail?.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));

    expect(root.querySelector<HTMLButtonElement>('[data-category="retail"]')?.getAttribute("aria-pressed")).toBe("false");
    expect(root.querySelector('[data-event-id="1"]')).toBeNull();
    expect(root.querySelector('[data-event-id="2"]')).not.toBeNull();
  });

  it("uses concise empty history copy", () => {
    const root = document.createElement("div");
    renderApp(root, model, vi.fn());
    root.querySelector<HTMLButtonElement>('[data-view="changes"]')?.click();

    expect(root.querySelector(".history-empty")?.textContent).toBe("Изменений пока нет");
    expect(root.textContent).not.toContain("Более старые записи");
    expect(root.querySelector<HTMLElement>(".sidebar")?.hidden).toBe(true);
    expect(root.querySelector(".layout")?.classList.contains("is-changes")).toBe(true);

    root.querySelector<HTMLButtonElement>('[data-view="calendar"]')?.click();
    expect(root.querySelector<HTMLElement>(".sidebar")?.hidden).toBe(false);
  });

  it("shows only unread history count and marks history seen when opened", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, {
      ...model,
      history: {
        unreadCount: 1,
        batches: [{
          id: "batch-1",
          checkedAt: "02.09.2026, 10:00",
          isUnread: true,
          counts: { moved: 0, added: 1, changed: 0, removed: 0, total: 1 },
          mineCount: 0,
          othersCount: 1,
          items: [{ kind: "added", title: "Игрушки", detail: "01.10.2026", stage: "Старт", changedFields: [], mine: false }],
        }],
      },
    }, send);

    expect(root.querySelector(".history-badge")?.textContent).toBe("1");
    const historyBatch = root.querySelector('[data-batch-id="batch-1"]');
    expect(historyBatch?.classList.contains("is-unread")).toBe(true);
    expect(Array.from(historyBatch?.querySelectorAll(".change-count") ?? []).map((item) => item.textContent)).toEqual([
      "0Перенесено", "1Добавлено", "0Изменено", "0Удалено",
    ]);
    historyBatch?.querySelector<HTMLButtonElement>("[data-copy-batch]")?.click();
    expect(send).toHaveBeenCalledWith({ type: "copyBatch", batchId: "batch-1" });
    root.querySelector<HTMLButtonElement>('[data-view="changes"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "markHistorySeen" });
  });

  it("renders four change counts and no more than eight notice items", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const noticeModel: AppViewModel = {
      ...model,
      updateNotice: {
        batchId: "batch-1",
        counts: { moved: 2, added: 3, changed: 4, removed: 5, total: 14 },
        mineCount: 0,
        othersCount: 14,
        items: Array.from({ length: 12 }, (_, index) => ({ kind: "moved", title: `Событие ${index + 1}`, detail: "01.09.2026 → 01.10.2026", stage: "Старт", changedFields: [], mine: false })),
      },
    };

    renderApp(root, noticeModel, send);

    expect(root.querySelectorAll(".notice-count")).toHaveLength(4);
    expect(root.querySelectorAll(".notice-item")).toHaveLength(8);
    expect(root.querySelector('[data-action="all-changes"]')).not.toBeNull();
    expect(root.querySelector('[data-action="close-notice"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[data-action="copy-notice"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "copyNotice", batchId: "batch-1" });
  });

  it("prioritizes my-group notification text and keeps the generic mode without selections", () => {
    const mine = { kind: "added" as const, title: "Игрушки", detail: "01.10.2026", stage: "Старт", changedFields: [], mine: true };
    const root = document.createElement("div");
    renderApp(root, {
      ...model,
      selectedGroups: ["игрушки"],
      hasSelectedGroups: true,
      updateNotice: { batchId: "mine", counts: { moved: 0, added: 3, changed: 0, removed: 0, total: 3 }, mineCount: 1, othersCount: 2, items: [mine] },
    }, vi.fn());

    expect(root.querySelector("#update-title")?.textContent).toBe("Календарь обновлён: 1 изменение по вашим группам, ещё 2 по остальным");

    const genericRoot = document.createElement("div");
    renderApp(genericRoot, {
      ...model,
      updateNotice: { batchId: "all", counts: { moved: 0, added: 1, changed: 0, removed: 0, total: 1 }, mineCount: 0, othersCount: 1, items: [{ ...mine, mine: false }] },
    }, vi.fn());
    expect(genericRoot.querySelector("#update-title")?.textContent).toBe("Календарь обновлён");
  });

  it("opens all changes from a non-blocking other-groups toast", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const batch = {
      id: "others",
      checkedAt: "02.09.2026, 10:00",
      isUnread: false,
      counts: { moved: 0, added: 1, changed: 0, removed: 0, total: 1 },
      mineCount: 0,
      othersCount: 1,
      items: [{ kind: "added" as const, title: "Обувь", detail: "01.10.2026", stage: "Старт", changedFields: [], mine: false }],
    };
    renderApp(root, {
      ...model,
      selectedGroups: ["игрушки"],
      hasSelectedGroups: true,
      history: { unreadCount: 0, batches: [batch] },
      toast: { kind: "success", message: "Обновлено: 1 изменение по другим группам", action: "openChanges", batchId: batch.id },
    }, send);

    expect(root.querySelector(".update-dialog")).toBeNull();
    root.querySelector<HTMLButtonElement>(".toast-action")?.click();
    expect(root.querySelector<HTMLElement>(".changes-view")?.hidden).toBe(false);
    expect(send).toHaveBeenCalledWith({ type: "openChanges", batchId: "others" });
  });

  it("defaults history to my groups and can reveal other changes per batch or globally", () => {
    const root = document.createElement("div");
    const mine = { kind: "added" as const, title: "Игрушки", detail: "01.10.2026", stage: "Старт", changedFields: [], mine: true };
    const other = { kind: "added" as const, title: "Обувь", detail: "02.10.2026", stage: "Старт", changedFields: [], mine: false };
    renderApp(root, {
      ...model,
      selectedGroups: ["игрушки"],
      hasSelectedGroups: true,
      history: { unreadCount: 0, batches: [{
        id: "mixed",
        checkedAt: "02.09.2026, 10:00",
        isUnread: false,
        counts: { moved: 0, added: 2, changed: 0, removed: 0, total: 2 },
        mineCount: 1,
        othersCount: 1,
        items: [mine, other],
      }] },
    }, vi.fn());
    root.querySelector<HTMLButtonElement>('[data-view="changes"]')?.click();

    expect(root.querySelector('[data-history-mode="mine"]')?.getAttribute("aria-pressed")).toBe("true");
    expect(root.querySelectorAll(".history-batch .change-row")).toHaveLength(1);
    expect(root.querySelector(".history-batch")?.textContent).toContain("Игрушки");
    expect(root.querySelector(".history-batch")?.textContent).not.toContain("Обувь");
    expect(root.querySelector(".other-changes")?.textContent).toBe("ещё 1 по другим группам");

    root.querySelector<HTMLButtonElement>(".other-changes")?.click();
    expect(root.querySelectorAll(".history-batch .change-row")).toHaveLength(2);

    root.querySelector<HTMLButtonElement>('[data-history-mode="all"]')?.click();
    expect(root.querySelector('[data-history-mode="all"]')?.getAttribute("aria-pressed")).toBe("true");
    expect(root.querySelectorAll(".history-batch .change-row")).toHaveLength(2);
  });

  it("requests an archive comparison and renders a temporary result above history", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const item = { kind: "moved" as const, title: "Игрушки", detail: "01.09.2026 → 01.10.2026", stage: "Старт", changedFields: [], mine: false };
    renderApp(root, {
      ...model,
      archives: [
        { id: "20260801-070000-demo.json", retrievedAt: "01.08.2026, 10:00" },
        { id: "bundled", retrievedAt: "01.07.2026, 00:00" },
      ],
      comparison: {
        baseRetrievedAt: "01.08.2026, 10:00",
        counts: { moved: 1, added: 0, changed: 0, removed: 0, total: 1 },
        mineCount: 0,
        othersCount: 1,
        items: [item],
      },
    }, send);
    root.querySelector<HTMLButtonElement>('[data-view="changes"]')?.click();

    expect(root.querySelectorAll<HTMLSelectElement>("[data-archive] option")[1]?.textContent).toContain("Версия из установщика");
    expect(root.querySelector(".archive-hint")?.textContent).toBe("Доступны снимки за последние 1 проверок");
    expect(root.querySelector(".comparison-result")?.textContent).toContain("Изменения с 01.08.2026");
    expect(root.querySelector(".comparison-result")?.textContent).toContain("Игрушки");

    const select = root.querySelector<HTMLSelectElement>("[data-archive]");
    if (select) {
      select.value = "bundled";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    }
    root.querySelector<HTMLButtonElement>('[data-action="compare"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "compareWith", id: "bundled" });
    root.querySelector<HTMLButtonElement>('[data-action="copy-comparison"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "copyComparison" });

    root.querySelector<HTMLButtonElement>('[data-action="close-comparison"]')?.click();
    expect(root.querySelector<HTMLElement>(".comparison-result")?.hidden).toBe(true);
  });

  it("opens and highlights the matching history batch from an update notice", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    const batch = {
      id: "batch-1",
      checkedAt: "02.09.2026, 10:00",
      isUnread: true,
      counts: { moved: 0, added: 1, changed: 0, removed: 0, total: 1 },
      mineCount: 0,
      othersCount: 1,
      items: [{ kind: "added" as const, title: "Игрушки", detail: "01.10.2026", stage: "Старт", changedFields: [], mine: false }],
    };
    const relatedBatch = {
      ...batch,
      id: "batch-2",
      checkedAt: "02.09.2026, 11:00",
    };
    renderApp(root, {
      ...model,
      history: { unreadCount: 2, batches: [batch, relatedBatch] },
      updateNotice: {
        batchId: batch.id,
        relatedBatchIds: [batch.id, relatedBatch.id],
        counts: batch.counts,
        mineCount: batch.mineCount,
        othersCount: batch.othersCount,
        items: batch.items,
      },
    }, send);

    root.querySelector<HTMLButtonElement>('[data-action="all-changes"]')?.click();

    expect(root.querySelector('[data-batch-id="batch-1"]')?.classList.contains("is-highlighted")).toBe(true);
    expect(root.querySelector('[data-batch-id="batch-2"]')?.classList.contains("is-highlighted")).toBe(true);
    expect(send).toHaveBeenCalledWith({ type: "markHistorySeen" });
    expect(send).toHaveBeenCalledWith({ type: "openChanges", batchId: "batch-1" });
  });

  it("opens support from the help menu and sends only the configured support commands", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);

    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="support"]')?.click();

    const dialog = root.querySelector<HTMLElement>(".support-dialog");
    expect(dialog?.textContent).toContain("Поддержать разработку");
    expect(dialog?.textContent).toContain(model.about.supportUrl);
    expect(dialog?.querySelector<HTMLImageElement>(".support-qr")?.getAttribute("src")).toBe("/support-cloudtips-qr.png");

    dialog?.querySelector<HTMLButtonElement>('[data-action="open-support"]')?.click();
    dialog?.querySelector<HTMLButtonElement>('[data-action="copy-support"]')?.click();

    expect(send).toHaveBeenNthCalledWith(1, { type: "openExternal", url: model.about.supportUrl });
    expect(send).toHaveBeenNthCalledWith(2, { type: "copySupportUrl" });
  });

  it("offers optional support after ten launches and fourteen days without blocking startup", () => {
    vi.useFakeTimers();
    try {
      localStorage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify({
        firstSeen: "2026-08-19",
        launchCount: 9,
        lastShown: null,
        disabled: false,
      }));
      const root = document.createElement("div");
      const send = vi.fn();

      renderApp(root, model, send);
      expect(root.querySelector(".support-prompt")).toBeNull();

      vi.advanceTimersByTime(30_000);

      const prompt = root.querySelector<HTMLElement>(".support-prompt");
      expect(prompt?.textContent).toContain("Если календарь оказался полезен");
      expect(prompt?.getAttribute("aria-modal")).toBeNull();
      prompt?.querySelector<HTMLButtonElement>('[data-action="support-prompt-open"]')?.click();
      expect(send).toHaveBeenCalledWith({ type: "openExternal", url: model.about.supportUrl });
      expect(root.querySelector(".support-prompt")).toBeNull();

      const saved = JSON.parse(localStorage.getItem(SUPPORT_PROMPT_STORAGE_KEY) ?? "{}");
      expect(saved).toEqual({
        firstSeen: "2026-08-19",
        launchCount: 10,
        lastShown: "2026-09-02",
        disabled: false,
      });
    } finally {
      vi.useRealTimers();
    }
  });

  it("never offers support again after the user disables the reminder", () => {
    vi.useFakeTimers();
    try {
      localStorage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify({
        firstSeen: "2026-01-01",
        launchCount: 9,
        lastShown: null,
        disabled: false,
      }));
      const firstRoot = document.createElement("div");
      renderApp(firstRoot, model, vi.fn());
      vi.advanceTimersByTime(30_000);
      firstRoot.querySelector<HTMLButtonElement>('[data-action="support-prompt-disable"]')?.click();

      const secondRoot = document.createElement("div");
      renderApp(secondRoot, { ...model, today: "2027-09-02" }, vi.fn());
      vi.advanceTimersByTime(30_000);

      expect(secondRoot.querySelector(".support-prompt")).toBeNull();
      expect(JSON.parse(localStorage.getItem(SUPPORT_PROMPT_STORAGE_KEY) ?? "{}").disabled).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it("removes the support reminder when an application update starts", () => {
    vi.useFakeTimers();
    try {
      localStorage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify({
        firstSeen: "2026-01-01",
        launchCount: 9,
        lastShown: null,
        disabled: false,
      }));
      const root = document.createElement("div");
      const mounted = mountApp(root, vi.fn());
      mounted.update(model);
      vi.advanceTimersByTime(30_000);
      expect(root.querySelector(".support-prompt")).not.toBeNull();

      mounted.update({
        ...model,
        appUpdate: { kind: "checking", message: "Проверяем обновление…", progress: null, version: null, canRestart: false },
      });

      expect(root.querySelector(".support-prompt")).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it("does not prompt after the support page was opened manually", () => {
    vi.useFakeTimers();
    try {
      localStorage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify({
        firstSeen: "2026-01-01",
        launchCount: 9,
        lastShown: null,
        disabled: false,
      }));
      const root = document.createElement("div");
      renderApp(root, model, vi.fn());

      root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
      root.querySelector<HTMLButtonElement>('[data-action="support"]')?.click();
      root.querySelector<HTMLButtonElement>(".support-dialog .dialog-button:last-child")?.click();
      vi.advanceTimersByTime(30_000);

      expect(root.querySelector(".support-prompt")).toBeNull();
      expect(JSON.parse(localStorage.getItem(SUPPORT_PROMPT_STORAGE_KEY) ?? "{}").lastShown).toBe("2026-09-02");
    } finally {
      vi.useRealTimers();
    }
  });

  it("shows the concise interface guide once after profile setup and remembers dismissal", () => {
    localStorage.removeItem(GUIDE_STORAGE_KEY);
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);

    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Лента событий");
    expect(root.querySelector(".guide-progress")?.textContent).toBe("1 из 4");
    expect(root.querySelector(".guide-highlight")?.getAttribute("data-guide-target")).toBe("feed");

    root.querySelector<HTMLButtonElement>('[data-action="guide-next"]')?.click();
    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Фильтры");
    expect(root.querySelector(".guide-highlight")?.getAttribute("data-guide-target")).toBe("filters");

    root.querySelector<HTMLButtonElement>('[data-action="guide-next"]')?.click();
    expect(root.querySelector(".guide-dialog")?.textContent).toContain("История изменений");
    expect(root.querySelector(".guide-highlight")?.getAttribute("data-guide-target")).toBe("changes");

    root.querySelector<HTMLButtonElement>('[data-action="guide-next"]')?.click();
    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Настройки и справка");
    expect(root.querySelector(".guide-highlight")?.getAttribute("data-guide-target")).toBe("settings");
    expect(root.querySelector<HTMLButtonElement>('[data-action="guide-next"]')?.textContent).toBe("Готово");

    root.querySelector<HTMLButtonElement>('[data-action="guide-next"]')?.click();
    expect(root.querySelector(".guide-dialog")).toBeNull();
    expect(localStorage.getItem(GUIDE_STORAGE_KEY)).toBe("done");
    expect(send).not.toHaveBeenCalled();
  });

  it("shows the current interface guide when only the legacy guide was completed", () => {
    localStorage.clear();
    localStorage.setItem("marking-calendar.guide.v1", "done");
    const root = document.createElement("div");

    renderApp(root, model, vi.fn());

    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Лента событий");
  });

  it("waits for the initial profile setup before opening the interface guide", () => {
    localStorage.removeItem(GUIDE_STORAGE_KEY);
    const root = document.createElement("div");
    const mounted = mountApp(root, vi.fn());

    mounted.update({ ...model, profile: { ...model.profile, onboardingCompleted: false } });
    expect(root.querySelector(".profile-dialog")).not.toBeNull();
    expect(root.querySelector(".guide-dialog")).toBeNull();

    root.querySelector<HTMLButtonElement>('[data-action="profile-skip"]')?.click();
    mounted.update(model);

    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Лента событий");
  });

  it("reopens the interface guide from Help without changing application state", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);

    expect(root.querySelector(".guide-dialog")).toBeNull();
    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="guide"]')?.click();

    expect(root.querySelector(".guide-dialog")?.textContent).toContain("Лента событий");
    expect(send).not.toHaveBeenCalled();
  });

  it("closes a dialog with Escape and restores focus to its opener", () => {
    const root = document.createElement("div");
    document.body.append(root);
    renderApp(root, model, vi.fn());
    const help = root.querySelector<HTMLButtonElement>('[data-action="help"]');

    help?.click();
    root.querySelector<HTMLButtonElement>('[data-action="about"]')?.click();
    expect(root.querySelector(".about-dialog")).not.toBeNull();

    root.querySelector<HTMLElement>(".about-dialog")?.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(root.querySelector(".about-dialog")).toBeNull();
    expect(document.activeElement).toBe(help);
    root.remove();
  });

  it("traps focus inside a dialog and closes it from the backdrop", () => {
    const root = document.createElement("div");
    document.body.append(root);
    renderApp(root, model, vi.fn());
    const help = root.querySelector<HTMLButtonElement>('[data-action="help"]');
    help?.click();
    root.querySelector<HTMLButtonElement>('[data-action="support"]')?.click();
    const layer = root.querySelector<HTMLElement>(".modal-layer");
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>(".support-dialog button"));
    const first = buttons[0];
    const last = buttons.at(-1);

    last?.focus();
    last?.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true }));
    expect(document.activeElement).toBe(first);
    first?.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(last);

    layer?.click();
    expect(root.querySelector(".support-dialog")).toBeNull();
    expect(document.activeElement).toBe(help);
    root.remove();
  });

  it("supports arrow navigation and dismisses the help menu with Escape or an outside click", () => {
    const root = document.createElement("div");
    document.body.append(root);
    renderApp(root, model, vi.fn());
    const help = root.querySelector<HTMLButtonElement>('[data-action="help"]');
    const menu = root.querySelector<HTMLElement>(".help-menu");
    const items = Array.from(root.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));

    expect(help?.getAttribute("aria-controls")).toBe(menu?.id);
    help?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    expect(menu?.hidden).toBe(false);
    expect(document.activeElement).toBe(items[0]);
    items[0]?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
    expect(document.activeElement).toBe(items[1]);
    items[1]?.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(menu?.hidden).toBe(true);
    expect(document.activeElement).toBe(help);

    help?.click();
    root.querySelector<HTMLButtonElement>('[data-action="refresh"]')?.click();
    expect(menu?.hidden).toBe(true);
    expect(help?.getAttribute("aria-expanded")).toBe("false");
    root.remove();
  });

  it("shows product ownership and independent-project notice in About", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, model, send);

    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="about"]')?.click();

    const dialog = root.querySelector<HTMLElement>(".about-dialog");
    expect(dialog?.textContent).toContain(`${model.about.name} ${model.about.version}`);
    expect(dialog?.querySelectorAll("dt")[0]?.textContent).toBe("Разработчик:");
    expect(dialog?.querySelectorAll("dd")[0]?.textContent).toBe(model.about.developer);
    expect(dialog?.querySelectorAll("dt")[1]?.textContent).toBe("Владелец и издатель:");
    expect(dialog?.querySelectorAll("dd")[1]?.textContent).toBe(model.about.publisher);
    expect(dialog?.textContent).toContain(model.about.disclaimer);

    const publicHistory = dialog?.querySelector<HTMLInputElement>('[data-action="public-history"]');
    expect(publicHistory?.checked).toBe(true);
    if (publicHistory) {
      publicHistory.checked = false;
      publicHistory.dispatchEvent(new Event("change", { bubbles: true }));
    }
    const notifications = dialog?.querySelector<HTMLInputElement>('[data-action="change-notifications"]');
    expect(notifications?.checked).toBe(true);
    if (notifications) {
      notifications.checked = false;
      notifications.dispatchEvent(new Event("change", { bubbles: true }));
    }

    dialog?.querySelector<HTMLButtonElement>('[data-action="open-repository"]')?.click();
    dialog?.querySelector<HTMLButtonElement>('[data-action="open-public-history"]')?.click();
    dialog?.querySelector<HTMLButtonElement>('[data-action="open-logs"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "openExternal", url: model.about.repositoryUrl });
    expect(send).toHaveBeenCalledWith({ type: "openExternal", url: model.about.historyUrl });
    expect(send).toHaveBeenCalledWith({ type: "openLogs" });
    expect(send).toHaveBeenCalledWith({ type: "setPublicHistory", enabled: false });
    expect(send).toHaveBeenCalledWith({ type: "setChangeNotifications", enabled: false });
  });

  it("offers an explicit restart only after an application update is ready", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, {
      ...model,
      appUpdate: { kind: "ready", message: "Обновление готово к установке", progress: 100, version: "0.2.0", canRestart: true },
    }, send);

    root.querySelector<HTMLButtonElement>('[data-action="help"]')?.click();
    root.querySelector<HTMLButtonElement>('[data-action="about"]')?.click();

    const dialog = root.querySelector<HTMLElement>(".about-dialog");
    expect(dialog?.querySelector(".app-update-status")?.textContent).toContain("Обновление готово к установке");
    dialog?.querySelector<HTMLButtonElement>('[data-action="restart-update"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "restartForUpdate" });
  });

  it("announces a downloaded application update in the main window", () => {
    const root = document.createElement("div");
    const send = vi.fn();
    renderApp(root, {
      ...model,
      appUpdate: { kind: "ready", message: "Обновление готово к установке", progress: 100, version: "0.2.0", canRestart: true },
    }, send);

    const prompt = root.querySelector<HTMLElement>(".app-update-prompt");
    expect(prompt?.hidden).toBe(false);
    expect(prompt?.textContent).toContain("Обновление 0.2.0 загружено");
    expect(prompt?.textContent).toContain("Перезапустите приложение, чтобы установить его");

    prompt?.querySelector<HTMLButtonElement>('[data-action="restart-update"]')?.click();
    expect(send).toHaveBeenCalledWith({ type: "restartForUpdate" });
  });

  it("keeps a postponed application update hidden for the current session", () => {
    const root = document.createElement("div");
    const mounted = mountApp(root, vi.fn());
    const ready = {
      ...model,
      appUpdate: { kind: "ready", message: "Обновление готово к установке", progress: 100, version: "0.2.0", canRestart: true } as const,
    };
    mounted.update(ready);

    root.querySelector<HTMLButtonElement>('[data-action="update-later"]')?.click();
    expect(root.querySelector<HTMLElement>(".app-update-prompt")?.hidden).toBe(true);

    mounted.update(ready);
    expect(root.querySelector<HTMLElement>(".app-update-prompt")?.hidden).toBe(true);
  });
});

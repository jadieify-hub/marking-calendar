import { describe, expect, it } from "vitest";
import {
  buildUpcoming,
  filterEvents,
  groupFeed,
  highlightSegments,
  visibleCounts,
  type FeedEvent,
  type FeedFilters,
} from "./feed";

const today = "2026-09-02";

function event(
  id: string,
  start: string | null,
  group: string,
  overrides: Partial<FeedEvent> = {},
): FeedEvent {
  return {
    id,
    start,
    end: null,
    period: start ?? "",
    group,
    type: "Обязательная маркировка (ввод в оборот)",
    typeLabel: "Маркировка",
    stage: `Этап ${id}`,
    description: "Описание",
    url: null,
    category: "marking",
    recentChange: null,
    moveCount: 0,
    history: [],
    ...overrides,
  };
}

const fixture: FeedEvent[] = [
  event("old-2016", "2016-01-01", "Архив"),
  event("past", "2026-08-31", "Обувь"),
  event("interval", "2025-12-15", "Вода", { end: "2026-09-20", period: "15.12.2025–20.09.2026" }),
  event("same-1", "2026-09-10", "БАД", { category: "registration", type: "Обязательная регистрация", typeLabel: "Регистрация" }),
  event("same-2", "2026-09-10", "БАД", { category: "marking", stage: "Старт маркировки" }),
  event("same-3", "2026-09-10", "БАД", { category: "retail", type: "Розничная продажа", typeLabel: "Розничная продажа" }),
  event("today", "2026-09-02", "Игрушки"),
  event("october", "2026-10-17", "Обувь"),
  event("next-year", "2027-01-05", "Корма"),
  ...Array.from({ length: 15 }, (_, index) => event(`filler-${index + 1}`, `2026-11-${String(index + 1).padStart(2, "0")}`, `Группа ${index + 1}`)),
];

const allFilters: FeedFilters = {
  query: "",
  selectedGroups: new Set(),
  groupMode: "all",
  categories: new Set(["retail", "edo", "ban", "permit", "marking", "registration", "other"]),
  showPast: false,
  onlyChanged: false,
};

describe("timeline feed selectors", () => {
  it("starts at the current month even when an older interval is still active", () => {
    expect(fixture).toHaveLength(24);

    const filtered = filterEvents(fixture, allFilters, today);

    expect(filtered.map((item) => item.id)).not.toContain("old-2016");
    expect(filtered.map((item) => item.id)).not.toContain("past");
    expect(filtered.map((item) => item.id)).not.toContain("interval");
    expect(filtered.map((item) => item.id)).toContain("next-year");
  });

  it("creates one card for one date and group while keeping every event row", () => {
    const grouped = groupFeed(filterEvents(fixture, allFilters, today));
    const day = grouped.flatMap((month) => month.days).find((item) => item.date === "2026-09-10");
    const card = day?.cards.find((item) => item.group === "БАД");

    expect(card?.events).toHaveLength(3);
    expect(day?.cards.filter((item) => item.group === "БАД")).toHaveLength(1);
    expect(new Set(card?.events.map((item) => item.category))).toEqual(new Set(["registration", "marking", "retail"]));
  });

  it("filters by my groups, category and searchable event text and reports exact visible totals", () => {
    const filters: FeedFilters = {
      ...allFilters,
      query: "старт маркировки",
      selectedGroups: new Set(["БАД"]),
      groupMode: "mine",
      categories: new Set(["marking", "retail"]),
    };

    const grouped = groupFeed(filterEvents(fixture, filters, today));

    expect(grouped.flatMap((month) => month.days).flatMap((day) => day.cards).flatMap((card) => card.events).map((item) => item.id)).toEqual(["same-2"]);
    expect(visibleCounts(grouped)).toEqual({ months: 1, days: 1, cards: 1, events: 1 });
    expect(highlightSegments("Старт обязательной маркировки", "маркировки")).toEqual([
      { text: "Старт обязательной ", match: false },
      { text: "маркировки", match: true },
    ]);
    expect(highlightSegments("Партионный учёт", "учет")).toEqual([
      { text: "Партионный ", match: false },
      { text: "учёт", match: true },
    ]);
  });

  it("does not filter or highlight until the search contains two characters", () => {
    const filtered = filterEvents(fixture, { ...allFilters, query: "м" }, today);

    expect(filtered.map((item) => item.id)).toEqual(filterEvents(fixture, allFilters, today).map((item) => item.id));
    expect(highlightSegments("Маркировка", "м")).toEqual([{ text: "Маркировка", match: false }]);
  });

  it("filters locally to events with a recent change", () => {
    const recent = event("recent", "2026-09-12", "Игрушки", {
      recentChange: { kind: "added", checkedAt: "2026-09-01T10:00:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] },
      history: [{ kind: "added", checkedAt: "2026-09-01T10:00:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] }],
    });
    const unchanged = event("unchanged", "2026-09-13", "Обувь");

    expect(filterEvents([recent, unchanged], { ...allFilters, onlyChanged: true }, today).map((item) => item.id)).toEqual(["recent"]);
  });

  it("expands Upcoming to 60 days, groups a date and limits visible tiles", () => {
    const events = [
      event("future-1", "2026-10-17", "Обувь"),
      event("future-2", "2026-10-17", "БАД"),
      event("future-3", "2026-10-17", "БАД"),
      event("future-4", "2026-10-18", "Игрушки"),
      event("future-5", "2026-10-19", "Вода"),
      event("future-6", "2026-10-20", "Корма"),
      event("future-7", "2026-10-21", "Обувь"),
    ];

    const upcoming = buildUpcoming(events, today);

    expect(upcoming.actualDays).toBe(60);
    expect(upcoming.totalDates).toBe(5);
    expect(upcoming.tiles).toHaveLength(4);
    expect(upcoming.tiles[0]).toMatchObject({
      date: "2026-10-17",
      groups: ["БАД", "Обувь"],
      groupCount: 2,
      eventCount: 3,
    });
  });

  it.each([
    ["2026-09-02", 60],
    ["2026-11-02", 90],
    ["2027-01-03", 365],
  ] as const)("selects the smallest populated Upcoming window for %s", (start, expectedDays) => {
    expect(buildUpcoming([event("future", start, "Игрушки")], today).actualDays).toBe(expectedDays);
  });

  it("keeps Upcoming compact when no event exists within a year", () => {
    expect(buildUpcoming([event("far", "2027-09-03", "Игрушки")], today)).toEqual({
      actualDays: 365,
      totalDates: 0,
      tiles: [],
    });
  });
});

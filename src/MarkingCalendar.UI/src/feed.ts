import type { CategoryId, EventLineageEntryViewModel } from "./contracts";

const DAY_MS = 86_400_000;
const RU_COLLATOR = new Intl.Collator("ru-RU", { sensitivity: "base" });

export interface FeedEvent {
  readonly id: string;
  readonly start: string | null;
  readonly end: string | null;
  readonly period: string;
  readonly group: string;
  readonly type: string;
  readonly typeLabel: string;
  readonly stage: string;
  readonly description: string;
  readonly url: string | null;
  readonly category: CategoryId;
  readonly recentChange: EventLineageEntryViewModel | null;
  readonly moveCount: number;
  readonly history: ReadonlyArray<EventLineageEntryViewModel>;
}

export interface FeedFilters {
  readonly query: string;
  readonly selectedGroups: ReadonlySet<string>;
  readonly groupMode: "mine" | "all";
  readonly categories: ReadonlySet<CategoryId>;
  readonly showPast: boolean;
  readonly onlyChanged: boolean;
}

export interface PositionedFeedEvent extends FeedEvent {
  readonly displayDate: string;
  readonly isContinuing: boolean;
  readonly isPast: boolean;
}

export interface FeedCard {
  readonly key: string;
  readonly date: string;
  readonly group: string;
  readonly events: ReadonlyArray<PositionedFeedEvent>;
}

export interface FeedDay {
  readonly date: string;
  readonly cards: ReadonlyArray<FeedCard>;
  readonly eventCount: number;
}

export interface FeedMonth {
  readonly key: string;
  readonly year: number;
  readonly month: number;
  readonly days: ReadonlyArray<FeedDay>;
  readonly eventCount: number;
}

export interface UpcomingTile {
  readonly date: string;
  readonly groups: ReadonlyArray<string>;
  readonly groupCount: number;
  readonly eventCount: number;
}

export interface UpcomingResult {
  readonly actualDays: 60 | 90 | 365;
  readonly totalDates: number;
  readonly tiles: ReadonlyArray<UpcomingTile>;
}

export interface HighlightSegment {
  readonly text: string;
  readonly match: boolean;
}

export function filterEvents(
  events: ReadonlyArray<FeedEvent>,
  filters: FeedFilters,
  today: string,
): PositionedFeedEvent[] {
  const todayDay = parseIsoDay(today);
  const monthStart = `${today.slice(0, 8)}01`;
  const monthStartDay = parseIsoDay(monthStart);
  const normalizedQuery = normalize(filters.query);
  const query = normalizedQuery.length >= 2 ? normalizedQuery : "";
  const selectedGroups = new Set(Array.from(filters.selectedGroups, normalize));

  return events.flatMap((event): PositionedFeedEvent[] => {
    if (!filters.categories.has(event.category)) return [];
    if (filters.onlyChanged && event.recentChange === null) return [];
    if (filters.groupMode === "mine" && !selectedGroups.has(normalize(event.group))) return [];
    if (query && !searchText(event).includes(query)) return [];

    const startDay = event.start ? parseIsoDay(event.start) : null;
    const endDay = event.end ? parseIsoDay(event.end) : null;
    const primaryDay = startDay ?? endDay;
    if (primaryDay === null) return [];

    const continuesFromBefore = startDay !== null
      && startDay < monthStartDay
      && endDay !== null
      && endDay >= monthStartDay;
    if (!filters.showPast && primaryDay < monthStartDay) return [];

    const displayDay = primaryDay;
    const finalDay = endDay ?? startDay ?? primaryDay;
    return [{
      ...event,
      displayDate: formatIsoDay(displayDay),
      isContinuing: continuesFromBefore,
      isPast: finalDay < todayDay,
    }];
  }).sort(comparePositionedEvents);
}

export function groupFeed(events: ReadonlyArray<PositionedFeedEvent>): FeedMonth[] {
  const months = new Map<string, Map<string, Map<string, PositionedFeedEvent[]>>>();
  for (const event of [...events].sort(comparePositionedEvents)) {
    const monthKey = event.displayDate.slice(0, 7);
    const days = months.get(monthKey) ?? new Map<string, Map<string, PositionedFeedEvent[]>>();
    months.set(monthKey, days);
    const cards = days.get(event.displayDate) ?? new Map<string, PositionedFeedEvent[]>();
    days.set(event.displayDate, cards);
    const groupEvents = cards.get(event.group) ?? [];
    cards.set(event.group, [...groupEvents, event]);
  }

  return Array.from(months, ([key, days]) => {
    const feedDays = Array.from(days, ([date, cards]) => {
      const feedCards = Array.from(cards, ([group, cardEvents]) => ({
        key: `${date}|${group}`,
        date,
        group,
        events: cardEvents.sort(compareEventRows),
      })).sort((left, right) => RU_COLLATOR.compare(left.group, right.group));
      return {
        date,
        cards: feedCards,
        eventCount: feedCards.reduce((total, card) => total + card.events.length, 0),
      };
    }).sort((left, right) => left.date.localeCompare(right.date));
    return {
      key,
      year: Number(key.slice(0, 4)),
      month: Number(key.slice(5, 7)),
      days: feedDays,
      eventCount: feedDays.reduce((total, day) => total + day.eventCount, 0),
    };
  }).sort((left, right) => left.key.localeCompare(right.key));
}

export function buildUpcoming(events: ReadonlyArray<FeedEvent>, today: string): UpcomingResult {
  const todayDay = parseIsoDay(today);
  const dated = events.flatMap((event) => {
    const startDay = event.start ? parseIsoDay(event.start) : null;
    const endDay = event.end ? parseIsoDay(event.end) : null;
    if (startDay !== null && startDay <= todayDay && endDay !== null && endDay >= todayDay) {
      return [{ event, day: todayDay }];
    }
    const day = startDay !== null && startDay >= todayDay
      ? startDay
      : endDay !== null && endDay >= todayDay ? endDay : null;
    return day === null ? [] : [{ event, day }];
  });
  const windows = [60, 90, 365] as const;
  const actualDays = windows.find((days) => dated.some((item) => item.day - todayDay <= days)) ?? 365;
  const inWindow = dated.filter((item) => item.day - todayDay <= actualDays);
  const dates = new Map<number, FeedEvent[]>();
  for (const item of inWindow) dates.set(item.day, [...(dates.get(item.day) ?? []), item.event]);
  const allTiles = Array.from(dates, ([day, dayEvents]) => ({
    day,
    dayEvents,
  })).map(({ day, dayEvents }) => {
    const groups = Array.from(new Map(
      dayEvents.map((event) => [normalize(event.group), event.group] as const),
    ).values()).sort(RU_COLLATOR.compare);
    return {
      date: formatIsoDay(day),
      groups: groups.slice(0, 3),
      groupCount: groups.length,
      eventCount: dayEvents.length,
    };
  }).sort((left, right) => left.date.localeCompare(right.date));

  return {
    actualDays,
    totalDates: allTiles.length,
    tiles: allTiles.slice(0, 4),
  };
}

export function highlightSegments(value: string, query: string): HighlightSegment[] {
  const needle = query.trim();
  if (normalize(needle).length < 2) return [{ text: value, match: false }];
  const loweredValue = value.toLocaleLowerCase("ru-RU").replace(/ё/g, "е");
  const loweredNeedle = needle.toLocaleLowerCase("ru-RU").replace(/ё/g, "е");
  const result: HighlightSegment[] = [];
  let cursor = 0;
  while (cursor < value.length) {
    const index = loweredValue.indexOf(loweredNeedle, cursor);
    if (index < 0) {
      result.push({ text: value.slice(cursor), match: false });
      break;
    }
    if (index > cursor) result.push({ text: value.slice(cursor, index), match: false });
    result.push({ text: value.slice(index, index + needle.length), match: true });
    cursor = index + needle.length;
  }
  return result;
}

export function visibleCounts(months: ReadonlyArray<FeedMonth>): { months: number; days: number; cards: number; events: number } {
  const days = months.flatMap((month) => month.days);
  const cards = days.flatMap((day) => day.cards);
  return {
    months: months.length,
    days: days.length,
    cards: cards.length,
    events: cards.reduce((total, card) => total + card.events.length, 0),
  };
}

const searchText = (event: FeedEvent): string => normalize([
  event.group,
  event.type,
  event.typeLabel,
  event.stage,
  event.description,
  event.period,
].join(" "));

const comparePositionedEvents = (left: PositionedFeedEvent, right: PositionedFeedEvent): number =>
  left.displayDate.localeCompare(right.displayDate)
  || RU_COLLATOR.compare(left.group, right.group)
  || compareEventRows(left, right);

const compareEventRows = (left: FeedEvent, right: FeedEvent): number =>
  RU_COLLATOR.compare(left.typeLabel, right.typeLabel)
  || RU_COLLATOR.compare(left.stage, right.stage)
  || left.id.localeCompare(right.id);

const normalize = (value: string): string => value
  .replace(/\u00a0/g, " ")
  .replace(/\s+/g, " ")
  .trim()
  .toLocaleLowerCase("ru-RU")
  .replace(/ё/g, "е");

function parseIsoDay(value: string): number {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw new Error(`Некорректная ISO-дата: ${value}`);
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  if (Number.isNaN(timestamp)) throw new Error(`Некорректная ISO-дата: ${value}`);
  return Math.floor(timestamp / DAY_MS);
}

const formatIsoDay = (day: number): string => new Date(day * DAY_MS).toISOString().slice(0, 10);

import type {
  AppViewModel,
  CalendarEventViewModel,
  CategoryId,
  ChangedFieldViewModel,
  ChangeBatchViewModel,
  ChangeCountsViewModel,
  ChangeSummaryViewModel,
  CommandSink,
  ThemePreference,
} from "./contracts";
import {
  buildUpcoming,
  filterEvents,
  groupFeed,
  highlightSegments,
  visibleCounts,
  type FeedCard,
  type FeedMonth,
} from "./feed";
import { wordDiff, type DiffSegment } from "./wordDiff";
import { createProductGroupIcon } from "./productGroupIcon";

const MONTHS = [
  "январь", "февраль", "март", "апрель", "май", "июнь",
  "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь",
] as const;
const WEEKDAYS = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"] as const;
const RU_COLLATOR = new Intl.Collator("ru-RU", { sensitivity: "base" });
const CHANGE_LABELS = {
  moved: "Перенесено",
  added: "Добавлено",
  changed: "Изменено",
  removed: "Удалено",
} as const;
const THEME_LABELS: Record<ThemePreference, string> = {
  auto: "Авто",
  light: "Светлая",
  dark: "Тёмная",
};
const GUIDE_STORAGE_KEY = "marking-calendar.guide.v2";
const SUPPORT_PROMPT_STORAGE_KEY = "marking-calendar.support-prompt.v1";
const SUPPORT_PROMPT_DELAY_MS = 30_000;
const SUPPORT_PROMPT_MIN_LAUNCHES = 10;
const SUPPORT_PROMPT_MIN_AGE_DAYS = 14;
const SUPPORT_PROMPT_REPEAT_MONTHS = 6;
const GUIDE_STEPS = [
  {
    target: "feed",
    selector: '[data-guide="feed"]',
    title: "Лента событий",
    text: "События собраны по датам. «Подробнее» открывает полное описание и ссылку на источник.",
  },
  {
    target: "filters",
    selector: '[data-guide="filters"]',
    title: "Фильтры",
    text: "Слева можно оставить свои товарные группы, выбрать категории и показать прошедшие события.",
  },
  {
    target: "changes",
    selector: '[data-guide="changes"]',
    title: "История изменений",
    text: "Здесь видно, что добавилось, изменилось или перенеслось. Счётчик показывает непросмотренные записи.",
  },
  {
    target: "settings",
    selector: '[data-guide="settings"]',
    title: "Настройки и справка",
    text: "Профиль и справка находятся в меню «?». Тема выбирается рядом, в верхней панели.",
  },
] as const;

type ActiveView = "calendar" | "changes";
type OpenDialog = { readonly kind: "about" | "support" | "profile" } | {
  readonly kind: "events";
  readonly cardKey: string;
  readonly date: string;
  readonly group: string;
  readonly eventIds: ReadonlyArray<string>;
};

interface ProfileDraft {
  readonly roles: Set<string>;
  readonly sectors: Set<string>;
  readonly manualGroups: Map<string, boolean>;
  groups: Set<string>;
}

interface SupportPromptState {
  readonly firstSeen: string;
  readonly launchCount: number;
  readonly lastShown: string | null;
  readonly disabled: boolean;
}

interface UiState {
  readonly activeCategories: Set<CategoryId>;
  readonly knownCategories: Set<CategoryId>;
  readonly selectedGroups: Set<string>;
  readonly dismissedNoticeIds: Set<string>;
  readonly dismissedGroupSuggestions: Set<string>;
  readonly expandedHistoryBatchIds: Set<string>;
  initialized: boolean;
  groupMode: "mine" | "all";
  query: string;
  groupQuery: string;
  showPast: boolean;
  onlyChanged: boolean;
  visibleDayLimit: number;
  activeView: ActiveView;
  dialog: OpenDialog | null;
  helpOpen: boolean;
  guideStep: number | null;
  theme: ThemePreference;
  historyMode: "mine" | "all";
  hasSelectedGroups: boolean;
  selectedArchiveId: string;
  dismissedComparisonBase: string | null;
}

export interface MountedApp {
  update(model: AppViewModel): void;
}

export function mountApp(root: HTMLElement, send: CommandSink): MountedApp {
  return new TimelineRenderer(root, send);
}

export function renderApp(root: HTMLElement, model: AppViewModel, send: CommandSink): void {
  mountApp(root, send).update(model);
}

class TimelineRenderer implements MountedApp {
  private readonly state: UiState = {
    activeCategories: new Set<CategoryId>(),
    knownCategories: new Set<CategoryId>(),
    selectedGroups: new Set<string>(),
    dismissedNoticeIds: new Set<string>(),
    dismissedGroupSuggestions: new Set<string>(),
    expandedHistoryBatchIds: new Set<string>(),
    initialized: false,
    groupMode: "all",
    query: "",
    groupQuery: "",
    showPast: false,
    onlyChanged: false,
    visibleDayLimit: 90,
    activeView: "calendar",
    dialog: null,
    helpOpen: false,
    guideStep: null,
    theme: "auto",
    historyMode: "all",
    hasSelectedGroups: false,
    selectedArchiveId: "",
    dismissedComparisonBase: null,
  };
  private model: AppViewModel | null = null;
  private readonly cards = new Map<string, FeedCard>();
  private dialogController: DialogController | null = null;
  private profileDraft: ProfileDraft | null = null;
  private activeYear: number | null = null;
  private guideCompleted: boolean;
  private guideOpener: HTMLElement | null = null;
  private supportPromptState: SupportPromptState | null = null;
  private supportPromptTimer: number | null = null;
  private supportLaunchRecorded = false;

  public constructor(
    private readonly root: HTMLElement,
    private readonly send: CommandSink,
  ) {
    this.guideCompleted = this.readGuideCompletion();
    this.mountShell();
    this.bindShell();
  }

  public update(model: AppViewModel): void {
    const scrolling = this.root.ownerDocument.scrollingElement;
    const scrollTop = scrolling?.scrollTop ?? 0;
    this.model = model;
    this.initializeState(model);
    this.applyTheme(model);
    this.renderHeader();
    this.renderGroups();
    this.renderCategories();
    this.renderCalendar();
    this.renderArchives();
    this.renderComparison();
    this.renderHistory();
    this.renderViews();
    if (!model.profile.onboardingCompleted && this.state.dialog === null) {
      this.state.dialog = { kind: "profile" };
      this.profileDraft = this.createProfileDraft();
    } else if (model.profile.onboardingCompleted
      && !this.guideCompleted
      && this.state.guideStep === null
      && this.state.dialog === null) {
      this.state.guideStep = 0;
    }
    this.renderOverlay();
    this.scheduleSupportPrompt();
    if (model.toast) showToast(
      required(this.root.querySelector<HTMLElement>(".toast")),
      model.toast,
      (batchId) => this.openHistoryBatch(batchId),
    );
    if (scrolling) scrolling.scrollTop = scrollTop;
  }

  private mountShell(): void {
    this.root.innerHTML = `
      <div class="app-shell">
        <header class="topbar">
          <div class="brand"><span class="brand-mark" aria-hidden="true">К</span><div><h1>Календарь маркировки</h1><span class="brand-sub">Честный Знак</span></div></div>
          <nav class="view-tabs" aria-label="Разделы">
            <button type="button" class="view-tab is-active" data-view="calendar" aria-current="page">Календарь</button>
            <button type="button" class="view-tab" data-view="changes" data-guide="changes">Изменения<span class="history-badge"></span></button>
          </nav>
          <div class="theme-control"><span>Тема</span><div class="theme-menu-control"><button type="button" class="theme-current" data-theme-current aria-label="Выбрать тему" aria-haspopup="listbox" aria-expanded="false"></button><div class="theme-menu" role="listbox" aria-label="Темы оформления" hidden><button type="button" role="option" data-theme="auto">Авто</button><button type="button" role="option" data-theme="light">Светлая</button><button type="button" role="option" data-theme="dark">Тёмная</button></div></div></div>
          <button class="status" type="button" data-action="refresh"><span class="status-dot"></span><span class="status-copy"><strong></strong><small></small></span></button>
          <div class="help-control" data-guide="settings">
            <button class="help-button" type="button" aria-label="Справка" aria-haspopup="menu" aria-controls="help-menu" aria-expanded="false" data-action="help">?</button>
            <div class="help-menu" id="help-menu" role="menu" aria-label="Справка" hidden>
              <button type="button" role="menuitem" data-action="guide">Краткий обзор</button>
              <button type="button" role="menuitem" data-action="profile">Настроить профиль</button>
              <button type="button" role="menuitem" data-action="support">Поддержать разработку</button>
              <button type="button" role="menuitem" data-action="about">О программе</button>
            </div>
          </div>
        </header>
        <div class="layout">
          <aside class="sidebar" aria-label="Фильтры календаря" data-guide="filters">
            <section class="sidebar-section" data-section="groups">
              <h2>Товарные группы</h2>
              <div class="group-mode" aria-label="Режим товарных групп"><button type="button" data-group-mode="mine" aria-pressed="false">Только мои</button><button type="button" data-group-mode="all" aria-pressed="true">Все</button></div>
              <input class="filter-field" type="search" data-filter="group-query" placeholder="найти группу">
              <div class="group-list"></div>
            </section>
            <section class="sidebar-section" data-section="categories"><h2>Категории</h2><div class="category-list"></div></section>
            <section class="sidebar-section" data-section="past"><label class="toggle"><input type="checkbox" data-filter="changed"> Только с изменениями</label><label class="toggle"><input type="checkbox" data-filter="past"> Показать прошедшие</label></section>
          </aside>
          <main class="content">
            <section class="calendar-view" aria-label="Календарь">
              <section class="group-suggestions" aria-label="Новые товарные группы" hidden></section>
              <section class="upcoming" aria-label="Ближайшие события" hidden></section>
              <div class="calendar-controls">
                <input class="filter-field feed-search-field" type="search" data-filter="query" aria-label="Поиск по событиям" placeholder="группа, этап, событие">
                <nav class="year-jump" aria-label="Переход по годам">
                  <button type="button" data-year-direction="previous" aria-label="Предыдущий год">‹</button>
                  <div class="year-menu-control">
                    <button type="button" class="year-current" data-year-current aria-label="Выбрать год" aria-haspopup="listbox" aria-expanded="false"></button>
                    <div class="year-menu" role="listbox" aria-label="Доступные годы" hidden></div>
                  </div>
                  <button type="button" data-year-direction="next" aria-label="Следующий год">›</button>
                </nav>
              </div>
              <div class="feed-toolbar"><span class="filter-summary"></span><span class="feed-status" aria-live="polite"></span><button type="button" data-action="reset-filters">Сбросить фильтры</button></div>
              <div class="timeline-feed" data-guide="feed"></div>
              <div class="load-more"></div>
            </section>
            <section class="changes-view" aria-label="История изменений" hidden>
              <div class="section-heading"><h2>История изменений</h2><div class="history-mode group-mode" aria-label="Изменения по товарным группам" hidden><button type="button" data-history-mode="mine" aria-pressed="false">Мои группы</button><button type="button" data-history-mode="all" aria-pressed="true">Все</button></div></div>
              <section class="archive-compare" aria-label="Сравнение со снимком"><label for="archive-select">Сравнить с датой</label><div class="archive-controls"><select id="archive-select" data-archive></select><button type="button" class="secondary-button" data-action="compare">Сравнить</button></div><p class="archive-hint"></p></section>
              <section class="comparison-result" hidden></section>
              <div class="history-list"></div>
            </section>
          </main>
        </div>
        <div class="toast" role="status" aria-live="polite" hidden></div>
      </div>
      <div class="modal-layer" hidden></div>`;
  }

  private bindShell(): void {
    required(this.root.querySelector<HTMLButtonElement>('[data-action="refresh"]'))
      .addEventListener("click", () => this.send({ type: "refresh" }));
    this.root.querySelectorAll<HTMLButtonElement>(".view-tab").forEach((tab) => tab.addEventListener("click", () => {
      this.activateView(tab.dataset.view === "changes" ? "changes" : "calendar");
    }));
    required(this.root.querySelector<HTMLInputElement>('[data-filter="query"]')).addEventListener("input", (event) => {
      this.state.query = (event.currentTarget as HTMLInputElement).value;
      this.state.visibleDayLimit = 90;
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
    });
    required(this.root.querySelector<HTMLInputElement>('[data-filter="group-query"]')).addEventListener("input", (event) => {
      this.state.groupQuery = (event.currentTarget as HTMLInputElement).value;
      this.renderGroups();
    });
    required(this.root.querySelector<HTMLInputElement>('[data-filter="past"]')).addEventListener("change", (event) => {
      this.state.showPast = (event.currentTarget as HTMLInputElement).checked;
      this.state.visibleDayLimit = this.state.showPast ? Number.MAX_SAFE_INTEGER : 90;
      const today = this.requireModel().today;
      this.activeYear = Number(today.slice(0, 4));
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
      this.root.ownerDocument.defaultView?.requestAnimationFrame(() => this.scrollToFeedDate(today));
    });
    required(this.root.querySelector<HTMLInputElement>('[data-filter="changed"]')).addEventListener("change", (event) => {
      this.state.onlyChanged = (event.currentTarget as HTMLInputElement).checked;
      this.state.visibleDayLimit = 90;
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
    });
    const themeCurrent = required(this.root.querySelector<HTMLButtonElement>("[data-theme-current]"));
    const themeMenu = required(this.root.querySelector<HTMLElement>(".theme-menu"));
    const themeOptions = Array.from(themeMenu.querySelectorAll<HTMLButtonElement>('[role="option"]'));
    const openThemeMenu = (focusIndex?: number): void => {
      themeMenu.hidden = false;
      themeCurrent.setAttribute("aria-expanded", "true");
      if (focusIndex !== undefined) themeOptions[focusIndex]?.focus();
    };
    themeCurrent.addEventListener("click", () => {
      if (themeMenu.hidden) openThemeMenu();
      else this.closeThemeMenu();
    });
    themeCurrent.addEventListener("keydown", (event) => {
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      openThemeMenu(event.key === "ArrowDown" ? 0 : themeOptions.length - 1);
    });
    themeMenu.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        event.preventDefault();
        this.closeThemeMenu();
        themeCurrent.focus();
        return;
      }
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      const current = themeOptions.indexOf(document.activeElement as HTMLButtonElement);
      const offset = event.key === "ArrowDown" ? 1 : -1;
      themeOptions[(current + offset + themeOptions.length) % themeOptions.length]?.focus();
    });
    themeMenu.addEventListener("click", (event) => {
      const theme = event.target instanceof Element
        ? event.target.closest<HTMLButtonElement>("[data-theme]")?.dataset.theme
        : undefined;
      if (theme !== "auto" && theme !== "light" && theme !== "dark") return;
      this.state.theme = theme;
      this.applyTheme(this.requireModel());
      this.renderThemePicker();
      this.closeThemeMenu();
      this.send({ type: "setTheme", theme });
    });
    required(this.root.querySelector<HTMLElement>(".group-list")).addEventListener("change", (event) => {
      const checkbox = event.target instanceof HTMLInputElement ? event.target.closest<HTMLInputElement>("[data-group]") : null;
      if (!checkbox?.dataset.group) return;
      if (checkbox.checked) this.state.selectedGroups.add(checkbox.dataset.group);
      else this.state.selectedGroups.delete(checkbox.dataset.group);
      if (checkbox.checked && this.state.selectedGroups.size === 1) this.state.historyMode = "mine";
      if (this.state.selectedGroups.size === 0) this.state.historyMode = "all";
      this.state.hasSelectedGroups = this.state.selectedGroups.size > 0;
      this.state.groupMode = this.state.selectedGroups.size > 0 ? "mine" : "all";
      this.state.visibleDayLimit = 90;
      this.sendSelectedGroups();
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
      this.renderHistory();
    });
    this.root.querySelectorAll<HTMLButtonElement>("[data-group-mode]").forEach((button) => button.addEventListener("click", () => {
      this.state.groupMode = button.dataset.groupMode === "mine" ? "mine" : "all";
      this.state.visibleDayLimit = 90;
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
    }));
    const categoryList = required(this.root.querySelector<HTMLElement>(".category-list"));
    categoryList.addEventListener("click", (event) => {
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-category]") : null;
      if (button?.dataset.category) this.toggleCategory(button.dataset.category as CategoryId);
    });
    categoryList.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" && event.key !== " ") return;
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-category]") : null;
      if (!button?.dataset.category) return;
      event.preventDefault();
      this.toggleCategory(button.dataset.category as CategoryId);
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="reset-filters"]')).addEventListener("click", () => this.resetFilters());
    required(this.root.querySelector<HTMLElement>(".load-more")).addEventListener("click", (event) => {
      if (!(event.target instanceof Element) || !event.target.closest('[data-action="load-more"]')) return;
      this.state.visibleDayLimit += 90;
      this.renderCalendar();
    });
    required(this.root.querySelector<HTMLElement>(".timeline-feed")).addEventListener("click", (event) => {
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-card-key]") : null;
      const card = button?.dataset.cardKey ? this.cards.get(button.dataset.cardKey) : null;
      if (card) this.openCard(card, button ?? undefined);
    });
    this.root.querySelectorAll<HTMLButtonElement>("[data-history-mode]").forEach((button) => button.addEventListener("click", () => {
      this.state.historyMode = button.dataset.historyMode === "mine" ? "mine" : "all";
      this.renderHistory();
    }));
    required(this.root.querySelector<HTMLElement>(".history-list")).addEventListener("click", (event) => {
      const copy = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-copy-batch]") : null;
      if (copy?.dataset.copyBatch) {
        this.send({ type: "copyBatch", batchId: copy.dataset.copyBatch });
        return;
      }
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-other-batch]") : null;
      if (!button?.dataset.otherBatch) return;
      this.state.expandedHistoryBatchIds.add(button.dataset.otherBatch);
      this.renderHistory();
    });
    required(this.root.querySelector<HTMLSelectElement>("[data-archive]")).addEventListener("change", (event) => {
      this.state.selectedArchiveId = (event.currentTarget as HTMLSelectElement).value;
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="compare"]')).addEventListener("click", () => {
      if (!this.state.selectedArchiveId) return;
      this.state.dismissedComparisonBase = null;
      this.send({ type: "compareWith", id: this.state.selectedArchiveId });
    });
    required(this.root.querySelector<HTMLElement>(".upcoming")).addEventListener("click", (event) => {
      const button = event.target instanceof Element
        ? event.target.closest<HTMLButtonElement>("[data-upcoming-date], [data-upcoming-more]")
        : null;
      if (!button) return;
      const date = button.dataset.upcomingDate ?? this.firstVisibleFutureDate();
      if (date) this.scrollToFeedDate(date);
    });
    required(this.root.querySelector<HTMLElement>(".group-suggestions")).addEventListener("click", (event) => {
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-add-group], [data-hide-group]") : null;
      const key = button?.dataset.addGroup ?? button?.dataset.hideGroup;
      if (!button || !key) return;
      if (button.dataset.addGroup) {
        this.state.selectedGroups.add(key);
        this.state.hasSelectedGroups = true;
        this.state.groupMode = "mine";
        this.state.historyMode = "mine";
        this.sendSelectedGroups();
        this.renderGroups();
        this.renderCalendar();
      } else {
        this.state.dismissedGroupSuggestions.add(key);
        this.send({ type: "hideGroupSuggestion", key });
        this.renderGroupSuggestions();
      }
    });
    const yearCurrent = required(this.root.querySelector<HTMLButtonElement>("[data-year-current]"));
    const yearMenu = required(this.root.querySelector<HTMLElement>(".year-menu"));
    yearCurrent.addEventListener("click", () => {
      yearMenu.hidden = !yearMenu.hidden;
      yearCurrent.setAttribute("aria-expanded", String(!yearMenu.hidden));
      if (!yearMenu.hidden) {
        yearMenu.querySelector<HTMLElement>('[aria-selected="true"]')?.scrollIntoView({ block: "nearest" });
      }
    });
    yearMenu.addEventListener("click", (event) => {
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-year]") : null;
      if (button?.dataset.year) this.jumpToYear(button.dataset.year);
    });
    this.root.querySelectorAll<HTMLButtonElement>("[data-year-direction]").forEach((button) => button.addEventListener("click", () => {
      const offset = button.dataset.yearDirection === "previous" ? -1 : 1;
      const years = Array.from(yearMenu.querySelectorAll<HTMLButtonElement>("[data-year]"), (item) => Number(item.dataset.year));
      const nextYear = years[years.indexOf(this.activeYear ?? Number.NaN) + offset];
      if (nextYear !== undefined) this.jumpToYear(String(nextYear));
    }));
    this.root.addEventListener("click", (event) => {
      if (!(event.target instanceof Element) || !event.target.closest(".year-jump")) this.closeYearMenu();
      if (!(event.target instanceof Element) || !event.target.closest(".theme-control")) this.closeThemeMenu();
    });
    this.root.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !yearMenu.hidden) {
        this.closeYearMenu();
        yearCurrent.focus();
      }
      if (event.key === "Escape" && !themeMenu.hidden) {
        this.closeThemeMenu();
        themeCurrent.focus();
      }
    });
    this.bindHelpMenu();
  }

  private bindHelpMenu(): void {
    const helpButton = required(this.root.querySelector<HTMLButtonElement>('[data-action="help"]'));
    const helpMenu = required(this.root.querySelector<HTMLElement>(".help-menu"));
    const helpControl = required(this.root.querySelector<HTMLElement>(".help-control"));
    const appShell = required(this.root.querySelector<HTMLElement>(".app-shell"));
    const items = Array.from(helpMenu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
    const open = (focusIndex?: number): void => {
      this.state.helpOpen = true;
      helpMenu.hidden = false;
      helpButton.setAttribute("aria-expanded", "true");
      if (focusIndex !== undefined) items[focusIndex]?.focus();
    };
    const close = (restoreFocus = false): void => {
      this.state.helpOpen = false;
      helpMenu.hidden = true;
      helpButton.setAttribute("aria-expanded", "false");
      if (restoreFocus) helpButton.focus();
    };
    helpButton.addEventListener("click", () => { if (helpMenu.hidden) open(); else close(); });
    helpButton.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !helpMenu.hidden) { event.preventDefault(); close(true); return; }
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      open(event.key === "ArrowDown" ? 0 : items.length - 1);
    });
    helpMenu.addEventListener("keydown", (event) => {
      if (event.key === "Escape") { event.preventDefault(); close(true); return; }
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      const current = items.indexOf(document.activeElement as HTMLButtonElement);
      const offset = event.key === "ArrowDown" ? 1 : -1;
      items[(current + offset + items.length) % items.length]?.focus();
    });
    appShell.addEventListener("click", (event) => {
      if (!helpMenu.hidden && event.target instanceof Node && !helpControl.contains(event.target)) close();
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="guide"]')).addEventListener("click", () => {
      close();
      this.startGuide(helpButton);
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="support"]')).addEventListener("click", () => {
      close();
      if (this.model) this.postponeSupportPrompt(this.model.today);
      this.state.dialog = { kind: "support" };
      this.renderOverlay(helpButton);
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="profile"]')).addEventListener("click", () => {
      close();
      this.state.dialog = { kind: "profile" };
      this.profileDraft = this.createProfileDraft();
      this.renderOverlay(helpButton);
    });
    required(this.root.querySelector<HTMLButtonElement>('[data-action="about"]')).addEventListener("click", () => {
      close();
      this.state.dialog = { kind: "about" };
      this.renderOverlay(helpButton);
    });
  }

  private initializeState(model: AppViewModel): void {
    for (const category of model.categories) {
      this.state.knownCategories.add(category.id);
    }
    if (this.state.initialized) return;
    for (const group of model.selectedGroups) this.state.selectedGroups.add(group);
    this.state.groupMode = this.state.selectedGroups.size > 0 ? "mine" : "all";
    this.state.hasSelectedGroups = model.hasSelectedGroups;
    this.state.historyMode = model.hasSelectedGroups ? "mine" : "all";
    this.state.theme = model.theme;
    for (const category of model.profile.roleCategories) this.state.activeCategories.add(category);
    this.state.initialized = true;
  }

  private applyTheme(model: AppViewModel): void {
    if (this.state.theme === "auto") delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = this.state.theme;
    for (const category of model.categories) {
      document.documentElement.style.setProperty(`--category-light-${category.id}`, category.color);
      document.documentElement.style.setProperty(`--category-dark-${category.id}`, category.colorDark);
    }
  }

  private renderHeader(): void {
    const model = this.requireModel();
    const status = required(this.root.querySelector<HTMLElement>(".status"));
    required(status.querySelector<HTMLElement>("strong")).textContent = model.status.message;
    required(status.querySelector<HTMLElement>("small")).textContent = model.updatedAt;
    required(status.querySelector<HTMLElement>(".status-dot")).dataset.kind = model.status.kind;
    required(this.root.querySelector<HTMLElement>(".history-badge")).textContent = model.history.unreadCount > 0 ? String(model.history.unreadCount) : "";
    this.renderThemePicker();
  }

  private renderThemePicker(): void {
    const current = required(this.root.querySelector<HTMLButtonElement>("[data-theme-current]"));
    current.textContent = THEME_LABELS[this.state.theme];
    current.setAttribute("aria-label", `Выбрать тему, выбрана ${THEME_LABELS[this.state.theme]}`);
    this.root.querySelectorAll<HTMLButtonElement>("[data-theme]").forEach((button) => {
      button.setAttribute("aria-selected", String(button.dataset.theme === this.state.theme));
    });
  }

  private closeThemeMenu(): void {
    const menu = required(this.root.querySelector<HTMLElement>(".theme-menu"));
    const current = required(this.root.querySelector<HTMLButtonElement>("[data-theme-current]"));
    menu.hidden = true;
    current.setAttribute("aria-expanded", "false");
  }

  private renderGroups(): void {
    const model = this.requireModel();
    const list = required(this.root.querySelector<HTMLElement>(".group-list"));
    list.replaceChildren();
    const query = normalize(this.state.groupQuery);
    const countEvents = filterEvents(model.events, {
      query: this.state.query,
      selectedGroups: new Set<string>(),
      groupMode: "all",
      categories: this.selectedCategories(),
      showPast: this.state.showPast,
      onlyChanged: this.state.onlyChanged,
    }, model.today);
    const counts = new Map<string, number>();
    for (const event of countEvents) counts.set(normalize(event.group), (counts.get(normalize(event.group)) ?? 0) + 1);
    const groups = model.groups
      .map((group) => ({ ...group, filteredCount: counts.get(group.key) ?? 0 }))
      .filter((group) => !query || normalize(group.name).includes(query))
      .sort((left, right) => Number(left.filteredCount === 0) - Number(right.filteredCount === 0)
        || RU_COLLATOR.compare(left.name, right.name));
    for (const group of groups) {
      const label = document.createElement("label");
      label.classList.toggle("is-empty", group.filteredCount === 0);
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.group = group.key;
      checkbox.checked = this.state.selectedGroups.has(group.key);
      const name = document.createElement("span");
      name.className = "group-name";
      name.textContent = group.name;
      if (group.isNew) {
        const badge = document.createElement("small");
        badge.className = "group-new-badge";
        badge.textContent = "новая";
        name.append(" ", badge);
      }
      if (group.renamedFrom) {
        const renamed = document.createElement("small");
        renamed.className = "group-renamed-from";
        renamed.textContent = `ранее: ${group.renamedFrom}`;
        name.append(renamed);
      }
      if (group.isCompleted) {
        const completed = document.createElement("small");
        completed.className = "group-completed-badge";
        completed.textContent = "завершено";
        name.append(" ", completed);
      }
      const count = document.createElement("span");
      count.className = "filter-count";
      count.textContent = String(group.filteredCount);
      label.append(checkbox, createProductGroupIcon(group.name), name, count);
      list.append(label);
    }
    this.root.querySelectorAll<HTMLButtonElement>("[data-group-mode]").forEach((button) => {
      const active = button.dataset.groupMode === this.state.groupMode;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
  }

  private renderCategories(): void {
    const model = this.requireModel();
    const list = required(this.root.querySelector<HTMLElement>(".category-list"));
    list.replaceChildren();
    const countEvents = filterEvents(model.events, {
      query: this.state.query,
      selectedGroups: this.state.selectedGroups,
      groupMode: this.state.groupMode,
      categories: new Set(model.categories.map((category) => category.id)),
      showPast: this.state.showPast,
      onlyChanged: this.state.onlyChanged,
    }, model.today);
    for (const category of model.categories) {
      const categoryCount = countEvents.filter((event) => event.category === category.id).length;
      const active = this.isCategoryActive(category.id);
      const button = document.createElement("button");
      button.type = "button";
      button.className = `category-filter legend-chip${active ? " is-active" : ""}`;
      button.dataset.category = category.id;
      button.setAttribute("aria-pressed", String(active));
      const swatch = document.createElement("span");
      swatch.className = "category-swatch legend-swatch";
      swatch.style.backgroundColor = `var(--category-current-${category.id}, ${category.color})`;
      const label = document.createElement("span");
      label.textContent = category.label;
      const number = document.createElement("span");
      number.className = "filter-count";
      number.textContent = String(categoryCount);
      const check = document.createElement("span");
      check.className = "legend-check";
      check.setAttribute("aria-hidden", "true");
      check.textContent = active ? "✓" : "+";
      button.append(swatch, label, number, check);
      list.append(button);
    }
  }

  private renderCalendar(): void {
    const model = this.requireModel();
    const filtered = filterEvents(model.events, {
      query: this.state.query,
      selectedGroups: this.state.selectedGroups,
      groupMode: this.state.groupMode,
      categories: this.selectedCategories(),
      showPast: this.state.showPast,
      onlyChanged: this.state.onlyChanged,
    }, model.today);
    const allMonths = groupFeed(filtered);
    this.renderGroupSuggestions();
    this.renderUpcoming();
    const totalCounts = visibleCounts(allMonths);
    const visibleMonths = takeDays(allMonths, this.state.visibleDayLimit);
    const shownCounts = visibleCounts(visibleMonths);
    this.cards.clear();
    const feed = required(this.root.querySelector<HTMLElement>(".timeline-feed"));
    feed.replaceChildren();
    if (shownCounts.events === 0) {
      const empty = document.createElement("div");
      empty.className = "feed-empty";
      empty.textContent = "По выбранным фильтрам событий нет";
      feed.append(empty);
    } else this.renderMonths(feed, visibleMonths, model.today);
    const statusText = `Показано ${shownCounts.events} из ${model.eventCount}`;
    required(this.root.querySelector<HTMLElement>(".feed-status")).textContent = statusText;
    required(this.root.querySelector<HTMLElement>(".status-copy small")).textContent = `${statusText} · ${model.updatedAt}`;
    const activeParts = [
      this.state.groupMode === "mine" ? selectedGroupsLabel(this.state.selectedGroups.size) : "все группы",
      pluralNoun(this.selectedCategories().size, "категория", "категории", "категорий"),
      this.state.showPast ? "с прошедшими" : "с текущего месяца",
      ...(this.state.onlyChanged ? ["только с изменениями"] : []),
    ];
    required(this.root.querySelector<HTMLElement>(".filter-summary")).textContent = activeParts.join(" · ");
    const more = required(this.root.querySelector<HTMLElement>(".load-more"));
    more.replaceChildren();
    if (totalCounts.days > shownCounts.days) {
      const remaining = totalCounts.days - shownCounts.days;
      const next = Math.min(90, remaining);
      const button = document.createElement("button");
      button.type = "button";
      button.className = "secondary-button";
      button.dataset.action = "load-more";
      button.textContent = `Показать ещё ${next} из ${remaining}`;
      more.append(button);
    }
    const yearMonths = groupFeed(filterEvents(model.events, {
      query: this.state.query,
      selectedGroups: this.state.selectedGroups,
      groupMode: this.state.groupMode,
      categories: this.selectedCategories(),
      showPast: true,
      onlyChanged: this.state.onlyChanged,
    }, model.today));
    this.renderYearNavigation(yearMonths);
  }

  private renderGroupSuggestions(): void {
    const section = required(this.root.querySelector<HTMLElement>(".group-suggestions"));
    const suggestions = this.requireModel().groupSuggestions
      .filter(item => !this.state.dismissedGroupSuggestions.has(item.key) && !this.state.selectedGroups.has(item.key));
    section.replaceChildren();
    section.hidden = suggestions.length === 0;
    for (const suggestion of suggestions) {
      const item = document.createElement("article");
      item.className = "group-suggestion";
      const copy = document.createElement("div");
      const title = document.createElement("strong");
      title.textContent = `${suggestion.message}: ${suggestion.name}`;
      const detail = document.createElement("span");
      const first = suggestion.firstEventDate ? `, первое ${formatDate(suggestion.firstEventDate)}` : "";
      detail.textContent = `${pluralNoun(suggestion.eventCount, "событие", "события", "событий")}${first}`;
      copy.append(title, detail);
      const actions = document.createElement("div");
      const add = actionButton("Добавить в мои", "primary");
      add.dataset.addGroup = suggestion.key;
      const hide = actionButton("Скрыть");
      hide.dataset.hideGroup = suggestion.key;
      actions.append(add, hide);
      item.append(copy, actions);
      section.append(item);
    }
  }

  private renderUpcoming(): void {
    const model = this.requireModel();
    const filtered = filterEvents(model.events, {
      query: "",
      selectedGroups: this.state.selectedGroups,
      groupMode: this.state.groupMode,
      categories: this.selectedCategories(),
      showPast: false,
      onlyChanged: this.state.onlyChanged,
    }, model.today);
    const result = buildUpcoming(filtered, model.today);
    const section = required(this.root.querySelector<HTMLElement>(".upcoming"));
    section.replaceChildren();
    section.hidden = false;

    const heading = document.createElement("header");
    heading.className = "upcoming-heading";
    const title = document.createElement("h2");
    title.textContent = "Ближайшее";
    const windowLabel = document.createElement("span");
    windowLabel.className = "upcoming-window";
    windowLabel.textContent = `в пределах ${result.actualDays} дней`;
    heading.append(title, windowLabel);
    if (result.totalDates > result.tiles.length) {
      const more = document.createElement("button");
      more.type = "button";
      more.className = "upcoming-more";
      more.dataset.upcomingMore = "true";
      more.textContent = `ещё ${result.totalDates - result.tiles.length} в ленте`;
      heading.append(more);
    }
    section.append(heading);

    if (result.tiles.length === 0) {
      const empty = document.createElement("p");
      empty.className = "upcoming-empty";
      empty.textContent = "Ближайших событий по выбранным фильтрам нет";
      section.append(empty);
      return;
    }

    const tiles = document.createElement("div");
    tiles.className = "upcoming-tiles";
    for (const tile of result.tiles) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "upcoming-tile";
      button.dataset.upcomingDate = tile.date;
      const dateRow = document.createElement("span");
      dateRow.className = "upcoming-date-row";
      const date = document.createElement("span");
      date.className = "upcoming-date";
      date.textContent = formatDate(tile.date);
      const relative = document.createElement("span");
      relative.className = "upcoming-relative";
      relative.textContent = relativeDayLabel(model.today, tile.date);
      dateRow.append(date, relative);

      const groups = document.createElement("span");
      groups.className = "upcoming-groups";
      for (const groupName of tile.groups) {
        const group = document.createElement("span");
        group.className = "upcoming-group";
        const name = document.createElement("span");
        name.className = "upcoming-group-name";
        name.textContent = groupName;
        group.append(createProductGroupIcon(groupName), name);
        groups.append(group);
      }

      const meta = document.createElement("span");
      meta.className = "upcoming-meta";
      const count = document.createElement("span");
      count.className = "upcoming-count";
      count.textContent = pluralNoun(tile.eventCount, "событие", "события", "событий");
      meta.append(count);
      const hiddenGroups = tile.groupCount - tile.groups.length;
      if (hiddenGroups > 0) {
        const moreGroups = document.createElement("span");
        moreGroups.className = "upcoming-hidden-groups";
        moreGroups.textContent = `ещё ${pluralNoun(hiddenGroups, "группа", "группы", "групп")}`;
        meta.append(moreGroups);
      }
      button.append(dateRow, groups, meta);
      tiles.append(button);
    }
    section.append(tiles);
  }

  private firstVisibleFutureDate(): string | null {
    const model = this.requireModel();
    const filtered = filterEvents(model.events, {
      query: "",
      selectedGroups: this.state.selectedGroups,
      groupMode: this.state.groupMode,
      categories: this.selectedCategories(),
      showPast: false,
      onlyChanged: this.state.onlyChanged,
    }, model.today);
    return filtered.find((event) => event.displayDate >= model.today)?.displayDate ?? null;
  }

  private scrollToFeedDate(date: string): void {
    let target = this.root.querySelector<HTMLElement>(`[data-date="${date}"]`);
    if (!target) {
      this.state.query = "";
      required(this.root.querySelector<HTMLInputElement>('[data-filter="query"]')).value = "";
      this.state.visibleDayLimit = Number.MAX_SAFE_INTEGER;
      this.renderCalendar();
      target = this.root.querySelector<HTMLElement>(`[data-date="${date}"]`);
    }
    if (target && typeof target.scrollIntoView === "function") {
      target.scrollIntoView({ behavior: "smooth", block: "center" });
    }
  }

  private renderMonths(container: HTMLElement, months: ReadonlyArray<FeedMonth>, today: string): void {
    let todayHandled = false;
    let hasPastDay = false;
    const seenYears = new Set<number>();
    for (const month of months) {
      const firstDay = month.days[0];
      if (!todayHandled && firstDay && firstDay.date > today) {
        container.append(todayDivider(today));
        todayHandled = true;
      }
      const section = document.createElement("section");
      section.className = "feed-month";
      if (!seenYears.has(month.year)) { section.id = `year-${month.year}`; seenYears.add(month.year); }
      const heading = document.createElement("header");
      heading.className = "feed-month-header";
      const title = document.createElement("h2");
      title.textContent = MONTHS[month.month - 1] ?? "";
      const year = document.createElement("span");
      year.textContent = String(month.year);
      const count = document.createElement("span");
      count.className = "month-count";
      count.textContent = pluralEvents(month.eventCount);
      heading.append(title, year, count);
      section.append(heading);
      for (const day of month.days) {
        if (!todayHandled && day.date > today) {
          section.append(todayDivider(today));
          todayHandled = true;
        }
        const row = document.createElement("div");
        row.className = `feed-day${day.date < today ? " is-past" : ""}`;
        row.dataset.date = day.date;
        const date = document.createElement("div");
        date.className = "day-mark";
        const value = document.createElement("strong");
        value.textContent = day.date.slice(8, 10) + "." + day.date.slice(5, 7);
        const weekday = document.createElement("span");
        weekday.textContent = day.date === today ? "сегодня" : weekdayName(day.date);
        date.append(value, weekday);
        const cards = document.createElement("div");
        cards.className = "feed-cards";
        for (const card of day.cards) {
          const key = card.key;
          this.cards.set(key, card);
          cards.append(this.renderCard(card, key));
        }
        row.append(date, cards);
        section.append(row);
        if (day.date < today) hasPastDay = true;
        else if (day.date === today) todayHandled = true;
      }
      container.append(section);
    }
    if (!todayHandled && hasPastDay) container.append(todayDivider(today));
  }

  private renderCard(card: FeedCard, key: string): HTMLElement {
    const model = this.requireModel();
    const article = document.createElement("article");
    article.className = "feed-card";
    const title = document.createElement("h3");
    title.className = "card-title";
    title.append(createProductGroupIcon(card.group));
    appendHighlighted(title, card.group, this.state.query);
    const rows = document.createElement("div");
    rows.className = "card-rows";
    for (const event of card.events) {
      const row = document.createElement("div");
      row.className = "event-row";
      row.dataset.eventId = event.id;
      const category = model.categories.find((item) => item.id === event.category);
      row.style.setProperty("--event-color", `var(--category-current-${event.category}, ${category?.color ?? "#6b7783"})`);
      const tag = document.createElement("span");
      tag.className = "event-tag";
      tag.textContent = event.typeLabel;
      const text = document.createElement("span");
      text.className = "event-stage";
      appendHighlighted(text, event.stage, this.state.query);
      if (event.isContinuing) {
        const continuing = document.createElement("span");
        continuing.className = "event-period";
        continuing.textContent = `идёт${event.end ? ` до ${formatDate(event.end)}` : ""}`;
        text.append(" ", continuing);
      } else if (event.end && event.end !== event.start) {
        const period = document.createElement("span");
        period.className = "event-period";
        period.textContent = `до ${formatDate(event.end)}`;
        text.append(" ", period);
      }
      row.append(tag, text);
      if (event.recentChange) {
        const change = document.createElement("span");
        change.className = `change-badge change-${event.recentChange.kind}`;
        change.textContent = recentChangeLabel(event.recentChange.kind, event.recentChange.previousStart ?? event.recentChange.previousEnd);
        row.append(change);
      }
      if (event.moveCount >= 2) {
        const moves = document.createElement("span");
        moves.className = "move-count-badge";
        moves.textContent = `переносилось ${pluralNoun(event.moveCount, "раз", "раза", "раз")}`;
        row.append(moves);
      }
      rows.append(row);
    }
    const button = document.createElement("button");
    button.type = "button";
    button.className = "card-details";
    button.dataset.cardKey = key;
    button.setAttribute("aria-label", `${card.group}, ${formatDate(card.date)}, ${pluralEvents(card.events.length)}`);
    button.textContent = "Подробнее";
    article.append(title, rows, button);
    return article;
  }

  private renderYearNavigation(months: ReadonlyArray<FeedMonth>): void {
    const current = required(this.root.querySelector<HTMLButtonElement>("[data-year-current]"));
    const menu = required(this.root.querySelector<HTMLElement>(".year-menu"));
    const navigation = required(current.closest<HTMLElement>(".year-jump"));
    const years = [...new Set(months.map((month) => month.year))];
    menu.replaceChildren();
    for (const year of years) {
      const button = document.createElement("button");
      button.type = "button";
      button.dataset.year = String(year);
      button.setAttribute("role", "option");
      button.textContent = String(year);
      menu.append(button);
    }
    navigation.hidden = years.length === 0;
    const todayYear = Number(this.requireModel().today.slice(0, 4));
    const selectedYear = years.includes(this.activeYear ?? Number.NaN)
      ? this.activeYear
      : years.includes(todayYear)
        ? todayYear
        : years.find((year) => year >= todayYear) ?? years.at(-1);
    this.activeYear = selectedYear ?? null;
    current.textContent = selectedYear === undefined ? "—" : String(selectedYear);
    current.setAttribute("aria-label", selectedYear === undefined ? "Выбрать год" : `Выбрать год, выбран ${selectedYear}`);
    menu.querySelectorAll<HTMLButtonElement>("[data-year]").forEach((button) => {
      button.setAttribute("aria-selected", String(Number(button.dataset.year) === selectedYear));
    });
    this.updateYearNavigation();
  }

  private updateYearNavigation(): void {
    const current = required(this.root.querySelector<HTMLButtonElement>("[data-year-current]"));
    const menu = required(this.root.querySelector<HTMLElement>(".year-menu"));
    const previous = required(this.root.querySelector<HTMLButtonElement>('[data-year-direction="previous"]'));
    const next = required(this.root.querySelector<HTMLButtonElement>('[data-year-direction="next"]'));
    const years = Array.from(menu.querySelectorAll<HTMLButtonElement>("[data-year]"), (item) => Number(item.dataset.year));
    const selectedIndex = years.indexOf(this.activeYear ?? Number.NaN);
    current.textContent = this.activeYear === null ? "—" : String(this.activeYear);
    current.setAttribute("aria-label", this.activeYear === null ? "Выбрать год" : `Выбрать год, выбран ${this.activeYear}`);
    previous.disabled = selectedIndex <= 0;
    next.disabled = selectedIndex < 0 || selectedIndex >= years.length - 1;
    menu.querySelectorAll<HTMLButtonElement>("[data-year]").forEach((button) => {
      button.setAttribute("aria-selected", String(Number(button.dataset.year) === this.activeYear));
    });
  }

  private jumpToYear(year: string): void {
    if (!year) return;
    this.activeYear = Number(year);
    this.updateYearNavigation();
    if (!this.root.querySelector(`#year-${year}`)) {
      this.state.showPast = true;
      required(this.root.querySelector<HTMLInputElement>('[data-filter="past"]')).checked = true;
      this.state.visibleDayLimit = Number.MAX_SAFE_INTEGER;
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
    }
    this.closeYearMenu();
    const target = this.root.querySelector<HTMLElement>(`#year-${year}`);
    if (target && typeof target.scrollIntoView === "function") target.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  private closeYearMenu(): void {
    const menu = required(this.root.querySelector<HTMLElement>(".year-menu"));
    const current = required(this.root.querySelector<HTMLButtonElement>("[data-year-current]"));
    menu.hidden = true;
    current.setAttribute("aria-expanded", "false");
  }

  private renderHistory(): void {
    const hasSelectedGroups = this.state.hasSelectedGroups;
    if (!hasSelectedGroups) this.state.historyMode = "all";
    const mode = required(this.root.querySelector<HTMLElement>(".history-mode"));
    mode.hidden = !hasSelectedGroups;
    mode.querySelectorAll<HTMLButtonElement>("[data-history-mode]").forEach((button) => {
      const active = button.dataset.historyMode === this.state.historyMode;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    renderHistory(
      required(this.root.querySelector<HTMLElement>(".history-list")),
      this.requireModel().history.batches,
      this.state.historyMode,
      this.state.expandedHistoryBatchIds,
    );
  }

  private renderArchives(): void {
    const model = this.requireModel();
    const select = required(this.root.querySelector<HTMLSelectElement>("[data-archive]"));
    select.replaceChildren();
    for (const archive of model.archives) {
      const option = document.createElement("option");
      option.value = archive.id;
      option.textContent = archive.id === "bundled"
        ? `Версия из установщика · ${archive.retrievedAt}`
        : archive.retrievedAt;
      select.append(option);
    }
    if (!model.archives.some((archive) => archive.id === this.state.selectedArchiveId)) {
      this.state.selectedArchiveId = model.archives[0]?.id ?? "";
    }
    select.value = this.state.selectedArchiveId;
    required(this.root.querySelector<HTMLButtonElement>('[data-action="compare"]')).disabled = model.archives.length === 0;
    const checks = model.archives.filter((archive) => archive.id !== "bundled").length;
    required(this.root.querySelector<HTMLElement>(".archive-hint")).textContent = checks > 0
      ? `Доступны снимки за последние ${checks} проверок`
      : "Архивных проверок пока нет";
  }

  private renderComparison(): void {
    const comparison = this.requireModel().comparison;
    const container = required(this.root.querySelector<HTMLElement>(".comparison-result"));
    container.replaceChildren();
    container.hidden = comparison === null || comparison.baseRetrievedAt === this.state.dismissedComparisonBase;
    if (container.hidden || comparison === null) return;
    const header = document.createElement("header");
    const title = document.createElement("h3");
    title.textContent = `Изменения с ${comparison.baseRetrievedAt.split(",")[0] ?? comparison.baseRetrievedAt}`;
    const close = document.createElement("button");
    close.type = "button";
    close.dataset.action = "close-comparison";
    close.textContent = "Закрыть";
    close.addEventListener("click", () => {
      this.state.dismissedComparisonBase = comparison.baseRetrievedAt;
      container.hidden = true;
    });
    const copy = document.createElement("button");
    copy.type = "button";
    copy.dataset.action = "copy-comparison";
    copy.textContent = "Скопировать";
    copy.addEventListener("click", () => this.send({ type: "copyComparison" }));
    const actions = document.createElement("span");
    actions.className = "comparison-actions";
    actions.append(copy, close);
    header.append(title, actions);
    container.append(header, renderChangeCounts(comparison.counts, "history"));
    for (const item of comparison.items) container.append(summaryRow(item));
  }

  private renderViews(): void {
    const calendarActive = this.state.activeView === "calendar";
    required(this.root.querySelector<HTMLElement>(".calendar-view")).hidden = !calendarActive;
    required(this.root.querySelector<HTMLElement>(".changes-view")).hidden = calendarActive;
    required(this.root.querySelector<HTMLElement>(".sidebar")).hidden = !calendarActive;
    required(this.root.querySelector<HTMLElement>(".layout")).classList.toggle("is-changes", !calendarActive);
    this.root.querySelectorAll<HTMLButtonElement>(".view-tab").forEach((tab) => {
      const active = tab.dataset.view === this.state.activeView;
      tab.classList.toggle("is-active", active);
      if (active) tab.setAttribute("aria-current", "page"); else tab.removeAttribute("aria-current");
    });
  }

  private activateView(view: ActiveView): void {
    this.state.activeView = view;
    if (view === "changes") this.send({ type: "markHistorySeen" });
    this.renderViews();
  }

  private toggleCategory(category: CategoryId): void {
    if (this.state.activeCategories.size === 0) {
      for (const known of this.state.knownCategories) this.state.activeCategories.add(known);
    }
    if (this.state.activeCategories.has(category)) this.state.activeCategories.delete(category);
    else this.state.activeCategories.add(category);
    if (this.state.activeCategories.size === this.state.knownCategories.size) this.state.activeCategories.clear();
    this.state.visibleDayLimit = 90;
    this.renderGroups();
    this.renderCategories();
    this.renderCalendar();
  }

  private resetFilters(): void {
    const model = this.requireModel();
    this.state.query = "";
    this.state.showPast = false;
    this.state.onlyChanged = false;
    this.state.groupMode = "all";
    this.state.visibleDayLimit = 90;
    this.state.activeCategories.clear();
    for (const category of model.categories) this.state.activeCategories.add(category.id);
    required(this.root.querySelector<HTMLInputElement>('[data-filter="query"]')).value = "";
    required(this.root.querySelector<HTMLInputElement>('[data-filter="past"]')).checked = false;
    required(this.root.querySelector<HTMLInputElement>('[data-filter="changed"]')).checked = false;
    this.renderGroups();
    this.renderCategories();
    this.renderCalendar();
  }

  private sendSelectedGroups(): void {
    const groups = this.requireModel().groups.map((group) => group.key).filter((key) => this.state.selectedGroups.has(key));
    this.send({ type: "setGroups", groups });
  }

  private selectedCategories(): Set<CategoryId> {
    return this.state.activeCategories.size === 0
      ? new Set(this.state.knownCategories)
      : new Set(this.state.activeCategories);
  }

  private isCategoryActive(category: CategoryId): boolean {
    return this.state.activeCategories.size === 0 || this.state.activeCategories.has(category);
  }

  private openCard(card: FeedCard, opener?: HTMLElement): void {
    this.state.dialog = {
      kind: "events",
      cardKey: card.key,
      date: card.date,
      group: card.group,
      eventIds: card.events.map((event) => event.id),
    };
    this.renderOverlay(opener);
  }

  private renderOverlay(opener?: HTMLElement): void {
    const model = this.requireModel();
    if (this.state.dialog?.kind === "support") { this.showSupport(opener); return; }
    if (this.state.dialog?.kind === "about") { this.showAbout(opener); return; }
    if (this.state.dialog?.kind === "profile") { this.showProfile(opener); return; }
    if (this.state.dialog?.kind === "events") {
      const card = this.state.dialog;
      const events = card.eventIds.flatMap((eventId) => {
        const event = model.events.find((item) => item.id === eventId);
        return event ? [event] : [];
      });
      if (events.length > 0) { this.showEvents(card, events, opener); return; }
      this.state.dialog = null;
    }
    if (model.updateNotice && !this.state.dismissedNoticeIds.has(model.updateNotice.batchId)) {
      this.showNotice(model.updateNotice);
      return;
    }
    if (this.state.guideStep !== null) {
      this.showGuide(opener);
      return;
    }
    this.closeOverlay(false);
  }

  private startGuide(opener?: HTMLElement): void {
    this.state.activeView = "calendar";
    this.renderViews();
    this.state.guideStep = 0;
    this.guideOpener = opener ?? this.helpButton();
    this.renderOverlay(this.guideOpener);
  }

  private showGuide(opener?: HTMLElement): void {
    const index = this.state.guideStep ?? 0;
    const step = GUIDE_STEPS[index];
    if (!step) {
      this.finishGuide();
      return;
    }
    const target = required(this.root.querySelector<HTMLElement>(step.selector));
    target.scrollIntoView?.({ block: "nearest", inline: "nearest" });

    const layer = required(this.root.querySelector<HTMLElement>(".modal-layer"));
    layer.classList.add("is-guide");
    const stage = document.createElement("div");
    stage.className = "guide-stage";
    const highlight = document.createElement("div");
    highlight.className = "guide-highlight";
    highlight.dataset.guideTarget = step.target;
    this.positionGuideHighlight(highlight, target);

    const dialog = document.createElement("section");
    dialog.className = "guide-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "guide-title");
    dialog.setAttribute("aria-describedby", "guide-text");
    const heading = document.createElement("div");
    heading.className = "guide-heading";
    const progress = document.createElement("span");
    progress.className = "guide-progress";
    progress.textContent = `${index + 1} из ${GUIDE_STEPS.length}`;
    const skip = actionButton("Пропустить");
    skip.dataset.action = "guide-skip";
    heading.append(progress, skip);
    const title = document.createElement("h2");
    title.id = "guide-title";
    title.textContent = step.title;
    const text = document.createElement("p");
    text.id = "guide-text";
    text.textContent = step.text;
    const actions = document.createElement("div");
    actions.className = "guide-actions";
    const back = actionButton("Назад");
    back.dataset.action = "guide-back";
    back.disabled = index === 0;
    const next = actionButton(index === GUIDE_STEPS.length - 1 ? "Готово" : "Далее", "primary");
    next.dataset.action = "guide-next";
    actions.append(back, next);
    dialog.append(heading, title, text, actions);
    stage.append(highlight, dialog);

    this.guideOpener ??= opener ?? this.helpButton();
    const controller = this.openOverlay(stage, this.guideOpener, next, () => this.completeGuide());
    skip.addEventListener("click", controller.requestClose);
    back.addEventListener("click", () => {
      if (index === 0) return;
      controller.close(false);
      this.state.guideStep = index - 1;
      this.showGuide(this.guideOpener ?? undefined);
    });
    next.addEventListener("click", () => {
      if (index === GUIDE_STEPS.length - 1) {
        controller.requestClose();
        return;
      }
      controller.close(false);
      this.state.guideStep = index + 1;
      this.showGuide(this.guideOpener ?? undefined);
    });
  }

  private positionGuideHighlight(highlight: HTMLElement, target: HTMLElement): void {
    const rect = target.getBoundingClientRect();
    const view = this.root.ownerDocument.defaultView;
    const padding = 6;
    const viewportWidth = view?.innerWidth ?? rect.right + padding;
    const viewportHeight = view?.innerHeight ?? rect.bottom + padding;
    const left = Math.max(6, rect.left - padding);
    const top = Math.max(6, rect.top - padding);
    highlight.style.left = `${left}px`;
    highlight.style.top = `${top}px`;
    highlight.style.width = `${Math.max(0, Math.min(rect.width + padding * 2, viewportWidth - left - 6))}px`;
    highlight.style.height = `${Math.max(0, Math.min(rect.height + padding * 2, viewportHeight - top - 6))}px`;
  }

  private finishGuide(): void {
    if (this.state.guideStep === null) return;
    this.completeGuide();
    this.closeOverlay(true);
  }

  private completeGuide(): void {
    this.state.guideStep = null;
    this.guideCompleted = true;
    this.guideOpener = null;
    this.root.querySelector<HTMLElement>(".modal-layer")?.classList.remove("is-guide");
    try {
      this.root.ownerDocument.defaultView?.localStorage.setItem(GUIDE_STORAGE_KEY, "done");
    } catch {
      // The guide still stays dismissed for the current session when storage is unavailable.
    }
  }

  private readGuideCompletion(): boolean {
    try {
      return this.root.ownerDocument.defaultView?.localStorage.getItem(GUIDE_STORAGE_KEY) === "done";
    } catch {
      return false;
    }
  }

  private showEvents(
    card: Extract<OpenDialog, { kind: "events" }>,
    events: ReadonlyArray<CalendarEventViewModel>,
    opener?: HTMLElement,
  ): void {
    const dialog = document.createElement("section");
    dialog.className = "dialog event-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "event-dialog-title");
    const header = document.createElement("header");
    header.className = "drawer-header";
    const title = document.createElement("h2");
    title.id = "event-dialog-title";
    title.className = "drawer-title";
    title.textContent = `${formatDate(card.date)} · ${card.group}`;
    const close = actionButton("Закрыть");
    close.classList.add("drawer-close");
    header.append(title, close);
    const list = document.createElement("div");
    list.className = "drawer-events";
    for (const event of events) {
      const category = this.requireModel().categories.find((item) => item.id === event.category);
      const article = document.createElement("article");
      article.className = "drawer-event";
      article.style.setProperty("--event-color", `var(--category-current-${event.category}, ${category?.color ?? "#6b7783"})`);
      const badge = document.createElement("span");
      badge.className = "drawer-category";
      badge.textContent = category?.label ?? event.typeLabel;
      const type = document.createElement("h3");
      type.textContent = event.type;
      const stage = document.createElement("p");
      stage.className = "drawer-stage";
      stage.textContent = event.stage;
      article.append(badge, type, stage);
      if (event.description) {
        const description = document.createElement("p");
        description.className = "drawer-description";
        description.textContent = event.description;
        article.append(description);
      }
      if (event.period) {
        const period = document.createElement("p");
        period.className = "drawer-period";
        period.textContent = event.period;
        article.append(period);
      }
      if (event.history.length > 0) article.append(renderEventHistory(event, this.requireModel().updatedAt));
      if (event.url) {
        const source = actionButton("Открыть источник", "primary");
        source.dataset.sourceEventId = event.id;
        source.addEventListener("click", () => this.send({ type: "openExternal", url: event.url ?? "" }));
        article.append(source);
      }
      list.append(article);
    }
    dialog.append(header, list);
    const eventOpener = opener ?? Array.from(this.root.querySelectorAll<HTMLElement>("[data-card-key]"))
      .find((item) => item.dataset.cardKey === card.cardKey);
    const controller = this.openOverlay(dialog, eventOpener, close, () => { this.state.dialog = null; });
    close.addEventListener("click", controller.requestClose);
  }

  private showSupport(opener?: HTMLElement): void {
    const model = this.requireModel();
    const dialog = document.createElement("section");
    dialog.className = "dialog support-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "support-title");
    const kicker = document.createElement("p");
    kicker.className = "dialog-kicker";
    kicker.textContent = "CloudTips · ₽";
    const title = document.createElement("h2");
    title.id = "support-title";
    title.textContent = "Поддержать разработку";
    const qr = document.createElement("img");
    qr.className = "support-qr";
    qr.src = "/support-cloudtips-qr.png";
    qr.alt = "QR-код страницы поддержки CloudTips";
    qr.width = 296;
    qr.height = 296;
    const url = document.createElement("code");
    url.className = "support-url";
    url.textContent = model.about.supportUrl;
    const actions = document.createElement("div");
    actions.className = "dialog-actions";
    const open = actionButton("Открыть страницу", "primary");
    open.dataset.action = "open-support";
    open.addEventListener("click", () => this.send({ type: "openExternal", url: model.about.supportUrl }));
    const copy = actionButton("Скопировать ссылку");
    copy.dataset.action = "copy-support";
    copy.addEventListener("click", () => this.send({ type: "copySupportUrl" }));
    const close = actionButton("Закрыть");
    actions.append(open, copy, close);
    dialog.append(kicker, title, qr, url, actions);
    const controller = this.openOverlay(dialog, opener ?? this.helpButton(), open, () => { this.state.dialog = null; });
    close.addEventListener("click", controller.requestClose);
  }

  private scheduleSupportPrompt(): void {
    const model = this.requireModel();
    if (!this.supportLaunchRecorded) {
      this.supportLaunchRecorded = true;
      this.supportPromptState = this.registerSupportLaunch(model.today);
    }
    const visiblePrompt = this.root.querySelector<HTMLElement>(".support-prompt");
    if (visiblePrompt && !this.canShowSupportPrompt(model)) {
      visiblePrompt.remove();
      return;
    }
    if (this.supportPromptTimer !== null
      || visiblePrompt
      || !this.supportPromptState
      || !isSupportPromptDue(this.supportPromptState, model.today)
      || !this.canShowSupportPrompt(model)) return;

    const view = this.root.ownerDocument.defaultView;
    if (!view) return;
    this.supportPromptTimer = view.setTimeout(() => {
      this.supportPromptTimer = null;
      const currentModel = this.model;
      if (!currentModel
        || !this.supportPromptState
        || !isSupportPromptDue(this.supportPromptState, currentModel.today)
        || !this.canShowSupportPrompt(currentModel)) return;
      this.showSupportPrompt(currentModel);
    }, SUPPORT_PROMPT_DELAY_MS);
  }

  private canShowSupportPrompt(model: AppViewModel): boolean {
    return model.profile.onboardingCompleted
      && this.guideCompleted
      && this.state.guideStep === null
      && this.state.dialog === null
      && model.updateNotice === null
      && model.toast === null
      && model.status.kind !== "checking"
      && model.status.kind !== "error"
      && model.appUpdate.kind === "current";
  }

  private showSupportPrompt(model: AppViewModel): void {
    if (!this.supportPromptState) return;
    this.postponeSupportPrompt(model.today);

    const prompt = document.createElement("aside");
    prompt.className = "support-prompt";
    prompt.setAttribute("role", "dialog");
    prompt.setAttribute("aria-labelledby", "support-prompt-title");
    const title = document.createElement("h2");
    title.id = "support-prompt-title";
    title.textContent = "Поддержать разработку";
    const text = document.createElement("p");
    text.textContent = "Если календарь оказался полезен, можно поддержать его развитие. Это необязательно — приложение останется бесплатным.";
    const actions = document.createElement("div");
    actions.className = "support-prompt-actions";
    const support = actionButton("Поддержать", "primary");
    support.dataset.action = "support-prompt-open";
    const later = actionButton("Не сейчас");
    later.dataset.action = "support-prompt-later";
    const disable = actionButton("Больше не показывать");
    disable.dataset.action = "support-prompt-disable";
    actions.append(support, later, disable);
    prompt.append(title, text, actions);

    const close = (): void => prompt.remove();
    support.addEventListener("click", () => {
      this.send({ type: "openExternal", url: model.about.supportUrl });
      close();
    });
    later.addEventListener("click", close);
    disable.addEventListener("click", () => {
      if (this.supportPromptState) {
        this.supportPromptState = { ...this.supportPromptState, disabled: true };
        this.saveSupportPromptState(this.supportPromptState);
      }
      close();
    });
    required(this.root.querySelector<HTMLElement>(".app-shell")).append(prompt);
  }

  private postponeSupportPrompt(today: string): void {
    if (!this.supportPromptState) return;
    this.supportPromptState = { ...this.supportPromptState, lastShown: today };
    this.saveSupportPromptState(this.supportPromptState);
  }

  private registerSupportLaunch(today: string): SupportPromptState | null {
    try {
      const storage = this.root.ownerDocument.defaultView?.localStorage;
      if (!storage) return null;
      const raw = storage.getItem(SUPPORT_PROMPT_STORAGE_KEY);
      const parsed = raw ? JSON.parse(raw) as Partial<SupportPromptState> : null;
      const previousLaunchCount = typeof parsed?.launchCount === "number" && Number.isInteger(parsed.launchCount)
        ? parsed.launchCount
        : 0;
      const state: SupportPromptState = {
        firstSeen: isIsoDate(parsed?.firstSeen) ? parsed.firstSeen : today,
        launchCount: Math.max(0, previousLaunchCount) + 1,
        lastShown: isIsoDate(parsed?.lastShown) ? parsed.lastShown : null,
        disabled: parsed?.disabled === true,
      };
      storage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify(state));
      return state;
    } catch {
      return null;
    }
  }

  private saveSupportPromptState(state: SupportPromptState): void {
    try {
      this.root.ownerDocument.defaultView?.localStorage.setItem(SUPPORT_PROMPT_STORAGE_KEY, JSON.stringify(state));
    } catch {
      // The reminder stays closed for the current session when storage is unavailable.
    }
  }

  private showAbout(opener?: HTMLElement): void {
    const model = this.requireModel();
    const dialog = document.createElement("section");
    dialog.className = "dialog about-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "about-title");
    const title = document.createElement("h2");
    title.id = "about-title";
    title.textContent = `${model.about.name} ${model.about.version}`;
    const details = document.createElement("dl");
    details.className = "about-details";
    appendDetail(details, "Разработчик", model.about.developer);
    appendDetail(details, "Владелец и издатель", model.about.publisher);
    const disclaimer = document.createElement("p");
    disclaimer.className = "about-disclaimer";
    disclaimer.textContent = model.about.disclaimer;
    const publicHistorySetting = document.createElement("label");
    publicHistorySetting.className = "public-history-setting";
    const publicHistoryToggle = document.createElement("input");
    publicHistoryToggle.type = "checkbox";
    publicHistoryToggle.checked = model.about.publicHistoryEnabled;
    publicHistoryToggle.dataset.action = "public-history";
    const publicHistoryText = document.createElement("span");
    publicHistoryText.textContent = "Загружать общую историю с GitHub";
    publicHistorySetting.append(publicHistoryToggle, publicHistoryText);
    publicHistoryToggle.addEventListener("change", () => this.send({
      type: "setPublicHistory",
      enabled: publicHistoryToggle.checked,
    }));
    const updateStatus = document.createElement("div");
    updateStatus.className = "app-update-status";
    updateStatus.dataset.kind = model.appUpdate.kind;
    const updateMessage = document.createElement("strong");
    updateMessage.textContent = model.appUpdate.message;
    const updateDetail = document.createElement("small");
    updateDetail.textContent = model.appUpdate.version
      ? `Версия ${model.appUpdate.version}${model.appUpdate.progress === null ? "" : ` · ${model.appUpdate.progress}%`}`
      : "Обновление приложения";
    updateStatus.append(updateMessage, updateDetail);
    const actions = document.createElement("div");
    actions.className = "dialog-actions";
    if (model.appUpdate.canRestart) {
      const restart = actionButton("Перезапустить и обновить", "primary");
      restart.dataset.action = "restart-update";
      restart.addEventListener("click", () => this.send({ type: "restartForUpdate" }));
      actions.append(restart);
    }
    const repository = actionButton("GitHub", "primary");
    repository.dataset.action = "open-repository";
    repository.addEventListener("click", () => this.send({ type: "openExternal", url: model.about.repositoryUrl }));
    const publicHistory = actionButton("История на GitHub");
    publicHistory.dataset.action = "open-public-history";
    publicHistory.addEventListener("click", () => this.send({ type: "openExternal", url: model.about.historyUrl }));
    const logs = actionButton("Открыть журнал");
    logs.dataset.action = "open-logs";
    logs.addEventListener("click", () => this.send({ type: "openLogs" }));
    const close = actionButton("Закрыть");
    actions.append(repository, publicHistory, logs, close);
    dialog.append(title, details, disclaimer, publicHistorySetting, updateStatus, actions);
    const controller = this.openOverlay(dialog, opener ?? this.helpButton(), repository, () => { this.state.dialog = null; });
    close.addEventListener("click", controller.requestClose);
  }

  private createProfileDraft(): ProfileDraft {
    const profile = this.requireModel().profile;
    const draft: ProfileDraft = {
      roles: new Set(profile.selectedRoles),
      sectors: new Set(profile.selectedSectors),
      manualGroups: new Map(Object.entries(profile.manualGroups)),
      groups: new Set<string>(),
    };
    draft.groups = this.calculateProfileGroups(draft);
    return draft;
  }

  private calculateProfileGroups(draft: ProfileDraft): Set<string> {
    const selected = new Set<string>();
    for (const sector of this.requireModel().profile.sectors) {
      if (!draft.sectors.has(sector.id)) continue;
      for (const key of sector.groupKeys) selected.add(key);
    }
    for (const [key, included] of draft.manualGroups) {
      if (included) selected.add(key);
      else selected.delete(key);
    }
    return selected;
  }

  private showProfile(opener?: HTMLElement): void {
    const model = this.requireModel();
    const draft = this.profileDraft ?? this.createProfileDraft();
    this.profileDraft = draft;
    const dialog = document.createElement("section");
    dialog.className = "dialog profile-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "profile-title");
    const title = document.createElement("h2");
    title.id = "profile-title";
    title.textContent = "Настройка календаря";
    const intro = document.createElement("p");
    intro.textContent = "Отметьте роли и направления деятельности. На их основе календарь выберет товарные группы и категории событий.";

    const rolesTitle = document.createElement("h3");
    rolesTitle.textContent = "Роль в работе с маркировкой";
    const roles = document.createElement("div");
    roles.className = "profile-chips";
    for (const role of model.profile.roles) {
      const button = actionButton(role.label);
      button.dataset.profileRole = role.id;
      button.setAttribute("aria-pressed", String(draft.roles.has(role.id)));
      button.addEventListener("click", () => {
        if (draft.roles.has(role.id)) draft.roles.delete(role.id); else draft.roles.add(role.id);
        this.showProfile(opener);
      });
      roles.append(button);
    }

    const sectorsTitle = document.createElement("h3");
    sectorsTitle.textContent = "Направления деятельности";
    const sectors = document.createElement("div");
    sectors.className = "profile-chips";
    for (const sector of model.profile.sectors) {
      const button = actionButton(`${sector.label} · ${sector.activeGroupCount}`);
      button.dataset.profileSector = sector.id;
      button.setAttribute("aria-pressed", String(draft.sectors.has(sector.id)));
      button.addEventListener("click", () => {
        if (draft.sectors.has(sector.id)) draft.sectors.delete(sector.id); else draft.sectors.add(sector.id);
        draft.groups = this.calculateProfileGroups(draft);
        this.showProfile(opener);
      });
      sectors.append(button);
    }

    const details = document.createElement("details");
    details.className = "profile-groups";
    const summary = document.createElement("summary");
    summary.textContent = "Настроить товарные группы вручную";
    const list = document.createElement("div");
    const keys = Array.from(new Set(model.profile.sectors.flatMap(sector => sector.groupKeys)));
    for (const key of keys) {
      const group = model.groups.find(item => item.key === key);
      const label = document.createElement("label");
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.dataset.profileGroup = key;
      checkbox.checked = draft.groups.has(key);
      checkbox.addEventListener("change", () => {
        const defaults = new Set(model.profile.sectors
          .filter(sector => draft.sectors.has(sector.id))
          .flatMap(sector => sector.groupKeys));
        if (checkbox.checked === defaults.has(key)) draft.manualGroups.delete(key);
        else draft.manualGroups.set(key, checkbox.checked);
        draft.groups = this.calculateProfileGroups(draft);
      });
      label.append(checkbox, document.createTextNode(group?.name ?? key));
      list.append(label);
    }
    details.append(summary, list);

    const actions = document.createElement("div");
    actions.className = "dialog-actions";
    const skip = actionButton("Настроить позже");
    skip.dataset.action = "profile-skip";
    const save = actionButton("Сохранить", "primary");
    save.dataset.action = "profile-save";
    actions.append(skip, save);
    dialog.append(title, intro, rolesTitle, roles, sectorsTitle, sectors, details, actions);
    const skipProfile = (): void => {
      this.state.dialog = null;
      this.profileDraft = null;
      this.send({ type: "skipProfile" });
    };
    const controller = this.openOverlay(dialog, opener ?? this.helpButton(), roles.querySelector("button") ?? save, skipProfile);
    skip.addEventListener("click", controller.requestClose);
    save.addEventListener("click", () => {
      const roleCategories = categoriesForRoles(draft.roles);
      if (roleCategories.size > 0) {
        this.state.activeCategories.clear();
        for (const category of roleCategories) this.state.activeCategories.add(category);
      }
      this.state.selectedGroups.clear();
      for (const key of draft.groups) this.state.selectedGroups.add(key);
      this.state.hasSelectedGroups = draft.groups.size > 0;
      this.state.groupMode = draft.groups.size > 0 ? "mine" : "all";
      this.send({
        type: "saveProfile",
        roles: model.profile.roles.map(role => role.id).filter(id => draft.roles.has(id)),
        sectors: model.profile.sectors.map(sector => sector.id).filter(id => draft.sectors.has(id)),
        groups: Array.from(draft.groups),
      });
      this.state.dialog = null;
      this.profileDraft = null;
      controller.close();
      this.renderGroups();
      this.renderCategories();
      this.renderCalendar();
    });
  }

  private showNotice(notice: NonNullable<AppViewModel["updateNotice"]>): void {
    const { batchId, counts, items } = notice;
    const dialog = document.createElement("section");
    dialog.className = "dialog update-dialog";
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-labelledby", "update-title");
    const title = document.createElement("h2");
    title.id = "update-title";
    title.textContent = notice.mineCount > 0
      ? `Календарь обновлён: ${pluralNoun(notice.mineCount, "изменение", "изменения", "изменений")} по вашим группам, ещё ${notice.othersCount} по остальным`
      : "Календарь обновлён";
    const list = document.createElement("div");
    list.className = "notice-list";
    for (const item of items.slice(0, 8)) { const row = summaryRow(item); row.classList.add("notice-item"); list.append(row); }
    const actions = document.createElement("div");
    actions.className = "dialog-actions";
    const all = actionButton("Все изменения", "primary");
    all.dataset.action = "all-changes";
    const close = actionButton("Закрыть");
    close.dataset.action = "close-notice";
    const copy = actionButton("Скопировать");
    copy.dataset.action = "copy-notice";
    copy.addEventListener("click", () => this.send({ type: "copyNotice", batchId }));
    actions.append(copy, all, close);
    dialog.append(title, renderChangeCounts(counts, "notice"), list, actions);
    const dismiss = (): void => {
      this.state.dismissedNoticeIds.add(batchId);
      this.send({ type: "dismissNotice", batchId });
    };
    const controller = this.openOverlay(dialog, null, all, dismiss);
    all.addEventListener("click", () => {
      controller.close(false);
      this.state.dismissedNoticeIds.add(batchId);
      this.openHistoryBatch(batchId, notice.relatedBatchIds ?? [batchId]);
    });
    close.addEventListener("click", controller.requestClose);
  }

  private openHistoryBatch(batchId: string, relatedBatchIds: ReadonlyArray<string> = [batchId]): void {
    this.activateView("changes");
    const related = new Set(relatedBatchIds);
    const targets = Array.from(this.root.querySelectorAll<HTMLElement>("[data-batch-id]"))
      .filter((item) => item.dataset.batchId !== undefined && related.has(item.dataset.batchId));
    if (targets.length > 0) {
      for (const target of targets) target.classList.add("is-highlighted");
      targets[0]?.scrollIntoView?.({ block: "center" });
      window.setTimeout(() => {
        for (const target of targets) target.classList.remove("is-highlighted");
      }, 2_000);
    }
    this.send({ type: "openChanges", batchId });
  }

  private openOverlay(dialog: HTMLElement, opener: HTMLElement | null | undefined, initialFocus: HTMLElement, onRequestClose: () => void): DialogController {
    this.dialogController?.close(false);
    const layer = required(this.root.querySelector<HTMLElement>(".modal-layer"));
    this.dialogController = openDialog(layer, dialog, { opener, initialFocus, onRequestClose });
    return this.dialogController;
  }

  private closeOverlay(restoreFocus: boolean): void {
    this.dialogController?.close(restoreFocus);
    this.dialogController = null;
    const layer = required(this.root.querySelector<HTMLElement>(".modal-layer"));
    layer.classList.remove("is-guide");
    layer.hidden = true;
    layer.replaceChildren();
  }

  private helpButton(): HTMLButtonElement {
    return required(this.root.querySelector<HTMLButtonElement>('[data-action="help"]'));
  }

  private requireModel(): AppViewModel {
    if (!this.model) throw new Error("Модель приложения ещё не получена.");
    return this.model;
  }
}

interface DialogController {
  close(restoreFocus?: boolean): void;
  requestClose(): void;
}

interface DialogOptions {
  readonly opener?: HTMLElement | null;
  readonly initialFocus?: HTMLElement | null;
  readonly onRequestClose: () => void;
}

function openDialog(layer: HTMLElement, dialog: HTMLElement, options: DialogOptions): DialogController {
  const opener = options.opener ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
  let closed = false;
  const close = (restoreFocus = true): void => {
    if (closed) return;
    closed = true;
    layer.onclick = null;
    layer.onkeydown = null;
    layer.hidden = true;
    layer.replaceChildren();
    if (restoreFocus && opener?.isConnected) opener.focus();
  };
  const requestClose = (): void => { options.onRequestClose(); close(); };
  const focusable = (): HTMLElement[] => Array.from(dialog.querySelectorAll<HTMLElement>(
    'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  ));
  layer.replaceChildren(dialog);
  layer.hidden = false;
  layer.onclick = (event) => { if (event.target === layer) requestClose(); };
  layer.onkeydown = (event) => {
    if (event.key === "Escape") { event.preventDefault(); requestClose(); return; }
    if (event.key !== "Tab") return;
    const items = focusable();
    if (items.length === 0) { event.preventDefault(); dialog.focus(); return; }
    const first = items[0];
    const last = items.at(-1);
    if (event.shiftKey && (document.activeElement === first || !dialog.contains(document.activeElement))) {
      event.preventDefault(); last?.focus();
    } else if (!event.shiftKey && (document.activeElement === last || !dialog.contains(document.activeElement))) {
      event.preventDefault(); first?.focus();
    }
  };
  dialog.tabIndex = -1;
  (options.initialFocus ?? focusable()[0] ?? dialog).focus();
  return { close, requestClose };
}

function takeDays(months: ReadonlyArray<FeedMonth>, limit: number): FeedMonth[] {
  let remaining = limit;
  const result: FeedMonth[] = [];
  for (const month of months) {
    if (remaining <= 0) break;
    const days = month.days.slice(0, remaining);
    if (days.length > 0) result.push({ ...month, days, eventCount: days.reduce((sum, day) => sum + day.eventCount, 0) });
    remaining -= days.length;
  }
  return result;
}

function renderHistory(
  container: HTMLElement,
  batches: ReadonlyArray<ChangeBatchViewModel>,
  mode: "mine" | "all",
  expandedBatchIds: ReadonlySet<string>,
): void {
  container.replaceChildren();
  if (batches.length === 0) {
    const empty = document.createElement("p");
    empty.className = "history-empty";
    empty.textContent = "Изменений пока нет";
    container.append(empty);
    return;
  }
  for (const batch of batches) {
    const article = document.createElement("article");
    article.className = `history-batch${batch.isUnread ? " is-unread" : ""}`;
    article.dataset.batchId = batch.id;
    const heading = document.createElement("h3");
    heading.textContent = batch.checkedAt;
    const total = document.createElement("p");
    total.textContent = pluralNoun(batch.counts.total, "изменение", "изменения", "изменений");
    article.append(heading, total, renderChangeCounts(batch.counts, "history"));
    const showAll = mode === "all" || expandedBatchIds.has(batch.id);
    for (const item of showAll ? batch.items : batch.items.filter((entry) => entry.mine)) article.append(summaryRow(item));
    if (mode === "mine" && batch.othersCount > 0 && !showAll) {
      const others = document.createElement("button");
      others.type = "button";
      others.className = "other-changes";
      others.dataset.otherBatch = batch.id;
      others.textContent = `ещё ${batch.othersCount} по другим группам`;
      article.append(others);
    }
    const copy = document.createElement("button");
    copy.type = "button";
    copy.className = "copy-summary";
    copy.dataset.copyBatch = batch.id;
    copy.textContent = "Скопировать";
    article.append(copy);
    container.append(article);
  }
}

function renderChangeCounts(counts: ChangeCountsViewModel, context: "history" | "notice"): HTMLElement {
  const grid = document.createElement("div");
  grid.className = `change-counts ${context}-counts`;
  for (const [kind, value] of [["moved", counts.moved], ["added", counts.added], ["changed", counts.changed], ["removed", counts.removed]] as const) {
    const count = document.createElement("div");
    count.className = `change-count ${context}-count change-${kind}`;
    const number = document.createElement("strong");
    number.textContent = String(value);
    const label = document.createElement("span");
    label.textContent = CHANGE_LABELS[kind];
    count.append(number, label);
    grid.append(count);
  }
  for (const [label, value] of [["Новые группы", counts.groupsAdded ?? 0], ["Переименовано групп", counts.groupsRenamed ?? 0]] as const) {
    if (value === 0) continue;
    const count = document.createElement("div");
    count.className = `change-count ${context}-count change-group`;
    const number = document.createElement("strong");
    number.textContent = String(value);
    const caption = document.createElement("span");
    caption.textContent = label;
    count.append(number, caption);
    grid.append(count);
  }
  return grid;
}

function summaryRow(item: ChangeSummaryViewModel): HTMLElement {
  const row = document.createElement("div");
  row.className = `change-row change-${item.kind}`;
  const marker = document.createElement("span");
  marker.className = "change-marker";
  marker.textContent = item.kind === "moved" ? "→" : item.kind === "added" ? "+" : item.kind === "removed" ? "−" : "•";
  const copy = document.createElement("span");
  const title = document.createElement("strong");
  title.textContent = item.title;
  const detail = document.createElement("small");
  detail.textContent = item.detail;
  copy.append(title, detail);
  if (item.changedFields.length > 0) appendDiffDisclosure(copy, item.changedFields);
  row.append(marker, copy);
  return row;
}

function appendHighlighted(container: HTMLElement, value: string, query: string): void {
  for (const segment of highlightSegments(value, query)) {
    if (segment.match) {
      const mark = document.createElement("mark");
      mark.textContent = segment.text;
      container.append(mark);
    } else container.append(segment.text);
  }
}

function showToast(
  toast: HTMLElement,
  model: NonNullable<AppViewModel["toast"]>,
  openChanges: (batchId: string) => void,
): void {
  toast.dataset.kind = model.kind;
  toast.replaceChildren();
  const message = document.createElement("span");
  message.textContent = model.message;
  toast.append(message);
  if (model.action === "openChanges" && model.batchId) {
    const action = document.createElement("button");
    action.type = "button";
    action.className = "toast-action";
    action.textContent = "Посмотреть";
    action.addEventListener("click", () => {
      toast.hidden = true;
      openChanges(model.batchId ?? "");
    });
    toast.append(action);
  }
  toast.hidden = false;
  window.setTimeout(() => { toast.hidden = true; toast.replaceChildren(); }, 5_000);
}

function appendDetail(list: HTMLDListElement, label: string, value: string): void {
  const term = document.createElement("dt");
  term.textContent = `${label}:`;
  const description = document.createElement("dd");
  description.textContent = value;
  list.append(term, description);
}

function actionButton(label: string, kind = "secondary"): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `dialog-button ${kind}`;
  button.textContent = label;
  return button;
}

function weekdayName(value: string): string {
  const timestamp = Date.parse(`${value}T12:00:00Z`);
  return WEEKDAYS[new Date(timestamp).getUTCDay()] ?? "";
}

function recentChangeLabel(kind: CalendarEventViewModel["history"][number]["kind"], previousDate: string | null): string {
  if (kind === "added") return "новое";
  if (kind === "moved") return previousDate ? `перенесено с ${formatDate(previousDate)}` : "перенесено";
  if (kind === "changed") return "изменено";
  return "изменено";
}

function renderEventHistory(event: CalendarEventViewModel, updatedAt: string): HTMLElement {
  const section = document.createElement("section");
  section.className = "event-history";
  const title = document.createElement("h4");
  title.textContent = "История события";
  const checked = document.createElement("span");
  checked.className = "event-history-checked";
  checked.textContent = `Проверено: ${updatedAt}`;
  const list = document.createElement("ol");
  list.className = "event-history-list";
  let currentDate = event.start ?? event.end;
  for (const entry of [...event.history].sort((left, right) => right.checkedAt.localeCompare(left.checkedAt))) {
    const item = document.createElement("li");
    item.className = `event-history-item change-${entry.kind}`;
    const checkedDate = formatDate(entry.checkedAt.slice(0, 10));
    let summary: string;
    if (entry.kind === "moved") {
      const previousDate = entry.previousStart ?? entry.previousEnd;
      summary = previousDate && currentDate
        ? `${checkedDate} — перенесено с ${formatDate(previousDate)} на ${formatDate(currentDate)}`
        : `${checkedDate} — перенесено`;
      currentDate = previousDate ?? currentDate;
    } else if (entry.kind === "added") summary = `${checkedDate} — добавлено`;
    else if (entry.kind === "changed") summary = `${checkedDate} — изменена формулировка`;
    else summary = `${checkedDate} — удалено`;
    if (entry.changedFields.length === 0) item.textContent = summary;
    else {
      const label = document.createElement("span");
      label.className = "event-history-summary";
      label.textContent = summary;
      item.append(label);
      appendDiffDisclosure(item, entry.changedFields);
    }
    list.append(item);
  }
  section.append(title, checked, list);
  return section;
}

function appendDiffDisclosure(container: HTMLElement, fields: ReadonlyArray<ChangedFieldViewModel>): void {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "diff-toggle";
  button.textContent = "Показать отличия";
  button.setAttribute("aria-expanded", "false");
  const panel = renderChangedFields(fields);
  panel.hidden = true;
  button.addEventListener("click", () => {
    panel.hidden = !panel.hidden;
    button.setAttribute("aria-expanded", String(!panel.hidden));
    button.textContent = panel.hidden ? "Показать отличия" : "Скрыть отличия";
  });
  container.append(button, panel);
}

function renderChangedFields(fields: ReadonlyArray<ChangedFieldViewModel>): HTMLElement {
  const panel = document.createElement("div");
  panel.className = "text-diff";
  for (const field of fields) {
    const section = document.createElement("section");
    section.className = "text-diff-field";
    const title = document.createElement("h5");
    title.textContent = changedFieldLabel(field.field);
    const difference = wordDiff(field.previous, field.current);
    section.append(title, diffSide("Было", difference.previous), diffSide("Стало", difference.current));
    panel.append(section);
  }
  return panel;
}

function diffSide(label: string, segments: ReadonlyArray<DiffSegment>): HTMLElement {
  const row = document.createElement("div");
  row.className = "text-diff-side";
  const heading = document.createElement("strong");
  heading.textContent = label;
  const text = document.createElement("span");
  segments.forEach((segment, index) => {
    if (index > 0) text.append(" ");
    const part = segment.kind === "delete"
      ? document.createElement("del")
      : segment.kind === "insert" ? document.createElement("ins") : document.createElement("span");
    part.textContent = segment.text;
    text.append(part);
  });
  row.append(heading, text);
  return row;
}

function changedFieldLabel(field: ChangedFieldViewModel["field"]): string {
  if (field === "stage") return "Этап";
  if (field === "description") return "Описание";
  if (field === "period") return "Период";
  return "Ссылка";
}

function formatDate(value: string): string {
  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}.${month}.${year}` : value;
}

function todayDivider(today: string): HTMLElement {
  const marker = document.createElement("div");
  marker.className = "today-line";
  marker.dataset.date = today;
  marker.textContent = `Сегодня · ${formatDate(today)}`;
  return marker;
}

function relativeDayLabel(today: string, date: string): string {
  const days = Math.round((Date.parse(`${date}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000);
  if (days === 0) return "сегодня";
  return `через ${pluralNoun(days, "день", "дня", "дней")}`;
}

function isSupportPromptDue(state: SupportPromptState, today: string): boolean {
  if (state.disabled || state.launchCount < SUPPORT_PROMPT_MIN_LAUNCHES) return false;
  const todayTime = isoDateTime(today);
  const firstSeenTime = isoDateTime(state.firstSeen);
  if (todayTime === null || firstSeenTime === null
    || todayTime - firstSeenTime < SUPPORT_PROMPT_MIN_AGE_DAYS * 86_400_000) return false;
  if (!state.lastShown) return true;
  const lastShownTime = isoDateTime(state.lastShown);
  if (lastShownTime === null) return true;
  const nextDate = new Date(lastShownTime);
  nextDate.setUTCMonth(nextDate.getUTCMonth() + SUPPORT_PROMPT_REPEAT_MONTHS);
  return todayTime >= nextDate.getTime();
}

function isIsoDate(value: unknown): value is string {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) && isoDateTime(value) !== null;
}

function isoDateTime(value: string): number | null {
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  return Number.isNaN(timestamp) ? null : timestamp;
}

function pluralNoun(count: number, one: string, few: string, many: string): string {
  const lastTwo = count % 100;
  const last = count % 10;
  const noun = lastTwo >= 11 && lastTwo <= 14 ? many : last === 1 ? one : last >= 2 && last <= 4 ? few : many;
  return `${count} ${noun}`;
}

function selectedGroupsLabel(count: number): string {
  const lastTwo = count % 100;
  const last = count % 10;
  if (lastTwo < 11 || lastTwo > 14) {
    if (last === 1) return `${count} моя группа`;
    if (last >= 2 && last <= 4) return `${count} мои группы`;
  }
  return `${count} моих групп`;
}

const pluralEvents = (count: number): string => pluralNoun(count, "событие", "события", "событий");

const normalize = (value: string): string => value.trim().toLocaleLowerCase("ru-RU").replace(/ё/g, "е");

function categoriesForRoles(roles: ReadonlySet<string>): Set<CategoryId> {
  const selected = new Set<CategoryId>();
  const add = (...categories: CategoryId[]): void => { for (const category of categories) selected.add(category); };
  if (roles.has("retail")) add("retail", "permit", "ban", "edo", "registration");
  if (roles.has("producer")) add("marking", "registration", "edo", "ban");
  if (roles.has("wholesale")) add("edo", "ban", "registration");
  return selected;
}

function required<T>(value: T | null): T {
  if (value === null) throw new Error("Не найден обязательный элемент интерфейса.");
  return value;
}

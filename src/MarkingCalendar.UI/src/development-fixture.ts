import type { AppViewModel } from "./contracts";

export const developmentFixture: AppViewModel = {
  updatedAt: "02.09.2026, 10:45",
  eventCount: 3,
  today: "2026-09-02",
  groups: [
    { key: "детские игрушки", name: "Детские игрушки", eventCount: 1 },
    { key: "молочная продукция", name: "Молочная продукция", eventCount: 2 },
  ],
  groupSuggestions: [],
  profile: { roles: [], sectors: [], selectedRoles: [], selectedSectors: [], manualGroups: {}, roleCategories: [], onboardingCompleted: true },
  selectedGroups: [],
  hasSelectedGroups: false,
  theme: "auto",
  categories: [
    { id: "retail", label: "Розничная продажа", color: "#1f93bb", colorDark: "#3fbde4" },
    { id: "edo", label: "ЭДО и учёт", color: "#7b4fd0", colorDark: "#a583f0" },
    { id: "marking", label: "Маркировка", color: "#1e9a63", colorDark: "#3fc98a" },
    { id: "ban", label: "Запрет оборота", color: "#cf4842", colorDark: "#ec7069" },
  ],
  events: [
    { id: "demo-1", start: "2026-09-01", end: null, period: "с 1 сентября 2026", group: "Молочная продукция", type: "Обязательная маркировка", typeLabel: "Маркировка", stage: "Маркировка становится обязательной для новых категорий", description: "Демонстрационная запись интерфейса.", url: "https://честныйзнак.рф/", category: "marking", recentChange: null, moveCount: 0, history: [] },
    { id: "demo-2", start: "2026-10-01", end: null, period: "с 1 октября 2026", group: "Детские игрушки", type: "Розничная продажа", typeLabel: "Розничная продажа", stage: "Старт передачи сведений через кассу", description: "Демонстрационная запись интерфейса.", url: null, category: "retail", recentChange: { kind: "moved", checkedAt: "2026-09-02T10:45:00+03:00", previousStart: "2026-09-15", previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] }, moveCount: 2, history: [{ kind: "moved", checkedAt: "2026-09-02T10:45:00+03:00", previousStart: "2026-09-15", previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] }] },
    { id: "demo-3", start: "2026-11-01", end: null, period: "с 1 ноября 2026", group: "Молочная продукция", type: "Партионный учёт по ЭДО", typeLabel: "Партионный учёт", stage: "Вводится партионный учёт", description: "Демонстрационная запись интерфейса.", url: null, category: "edo", recentChange: { kind: "added", checkedAt: "2026-09-02T10:45:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] }, moveCount: 0, history: [{ kind: "added", checkedAt: "2026-09-02T10:45:00+03:00", previousStart: null, previousEnd: null, previousStage: null, previousDescription: null, changedFields: [] }] },
  ],
  archives: [
    { id: "20260801-070000-demo.json", retrievedAt: "01.08.2026, 10:00" },
    { id: "bundled", retrievedAt: "01.07.2026, 00:00" },
  ],
  comparison: null,
  history: {
    unreadCount: 1,
    batches: [{
      id: "demo-batch",
      checkedAt: "02.09.2026, 10:45",
      isUnread: true,
      counts: { moved: 1, added: 1, changed: 1, removed: 0, total: 3 },
      mineCount: 0,
      othersCount: 3,
      items: [
        { kind: "moved", title: "Детские игрушки", detail: "Старт перенесён на 01.10.2026", stage: "Розничная продажа", changedFields: [], mine: false },
        { kind: "added", title: "Молочная продукция", detail: "Добавлен партионный учёт", stage: "ЭДО и учёт", changedFields: [], mine: false },
        { kind: "changed", title: "Молочная продукция", detail: "Уточнено описание этапа", stage: "Маркировка", changedFields: [{ field: "stage", previous: "Передача сведений", current: "Передача сведений через ККТ" }], mine: false },
      ],
    }],
  },
  status: { kind: "ready", message: "Данные актуальны" },
  toast: null,
  updateNotice: null,
  appUpdate: { kind: "current", message: "Установлена последняя версия", progress: null, version: null, canRestart: false },
  about: {
    name: "Календарь маркировки",
    version: "0.1.4",
    developer: "Руслан Керусов",
    publisher: "KRS",
    repositoryUrl: "https://github.com/jadieify-hub/marking-calendar",
    historyUrl: "https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md",
    supportUrl: "https://pay.cloudtips.ru/p/a18da555",
    disclaimer: "Независимый проект, не являющийся официальным приложением оператора системы маркировки.",
    publicHistoryEnabled: true,
  },
};

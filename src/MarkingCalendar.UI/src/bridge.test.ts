import { afterEach, describe, expect, it, vi } from "vitest";
import { connectBridge } from "./bridge";
import type { AppViewModel } from "./contracts";

const validModel: AppViewModel = {
  updatedAt: "02.09.2026, 10:00",
  eventCount: 0,
  today: "2026-09-02",
  groups: [],
  groupSuggestions: [],
  profile: { roles: [], sectors: [], selectedRoles: [], selectedSectors: [], manualGroups: {}, roleCategories: [], onboardingCompleted: true },
  selectedGroups: [],
  hasSelectedGroups: false,
  theme: "auto",
  categories: [],
  events: [],
  archives: [],
  comparison: null,
  history: { unreadCount: 0, batches: [] },
  status: { kind: "ready", message: "Данные актуальны" },
  toast: null,
  updateNotice: null,
  appUpdate: { kind: "current", message: "Установлена последняя версия", progress: null, version: null, canRestart: false },
  about: { name: "Календарь маркировки", version: "0.1.3", developer: "Руслан Керусов", publisher: "KRS", repositoryUrl: "https://github.com/jadieify-hub/marking-calendar", historyUrl: "https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md", supportUrl: "https://pay.cloudtips.ru/p/a18da555", disclaimer: "Независимый проект", publicHistoryEnabled: true },
};

describe("connectBridge", () => {
  afterEach(() => {
    Reflect.deleteProperty(window, "chrome");
  });

  it("sends ready and routes host state only after shape validation", () => {
    const postMessage = vi.fn();
    let listener: ((event: { data: unknown }) => void) | undefined;
    Object.defineProperty(window, "chrome", { configurable: true, value: { webview: {
      postMessage,
      addEventListener: (_: "message", handler: (event: { data: unknown }) => void) => { listener = handler; },
    } } });
    const receive = vi.fn();

    const bridge = connectBridge(receive);
    listener?.({ data: { type: "state", model: validModel } });
    listener?.({ data: { type: "state", model: { events: "not-an-array" } } });
    bridge.send({ type: "refresh" });

    expect(postMessage).toHaveBeenNthCalledWith(1, { type: "ready" });
    expect(postMessage).toHaveBeenNthCalledWith(2, { type: "refresh" });
    expect(receive).toHaveBeenCalledOnce();
    expect(receive).toHaveBeenCalledWith(validModel);
  });

  it("uses an explicit development fixture when WebView2 is absent", async () => {
    const receive = vi.fn();

    connectBridge(receive, validModel);
    await Promise.resolve();

    expect(receive).toHaveBeenCalledWith(validModel);
  });

  it("rejects change summaries without the before-and-after field contract", () => {
    let listener: ((event: { data: unknown }) => void) | undefined;
    Object.defineProperty(window, "chrome", { configurable: true, value: { webview: {
      postMessage: vi.fn(),
      addEventListener: (_: "message", handler: (event: { data: unknown }) => void) => { listener = handler; },
    } } });
    const receive = vi.fn();
    connectBridge(receive);
    const invalidModel = {
      ...validModel,
      history: { unreadCount: 1, batches: [{
        id: "batch",
        checkedAt: "02.09.2026, 10:00",
        isUnread: true,
        counts: { moved: 0, added: 0, changed: 1, removed: 0, total: 1 },
        items: [{ kind: "changed", title: "Игрушки", detail: "изменено", stage: "Старт" }],
      }] },
    };

    listener?.({ data: { type: "state", model: invalidModel } });

    expect(receive).not.toHaveBeenCalled();
  });
});

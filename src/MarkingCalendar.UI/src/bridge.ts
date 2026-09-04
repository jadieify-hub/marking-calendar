import type { AppViewModel, CommandSink, UiCommand } from "./contracts";

interface HostMessageEvent {
  readonly data: unknown;
}

interface HostWebView {
  postMessage(message: UiCommand): void;
  addEventListener(type: "message", listener: (event: HostMessageEvent) => void): void;
}

interface HostWindow extends Window {
  readonly chrome?: { readonly webview?: HostWebView };
}

export interface UiBridge {
  readonly send: CommandSink;
}

export function connectBridge(
  receive: (model: AppViewModel) => void,
  developmentFixture?: AppViewModel,
): UiBridge {
  const host = (window as HostWindow).chrome?.webview;
  if (!host) {
    if (developmentFixture) queueMicrotask(() => receive(developmentFixture));
    return { send: () => undefined };
  }

  host.addEventListener("message", (event) => {
    if (!isRecord(event.data) || event.data.type !== "state" || !isAppViewModel(event.data.model)) return;
    receive(event.data.model);
  });
  const send: CommandSink = (command) => {
    if (command.type === "exportCalendar"
      && (!Array.isArray(command.eventIds) || command.eventIds.some((id) => typeof id !== "string" || id.length === 0))) return;
    host.postMessage(command);
  };
  send({ type: "ready" });
  return { send };
}

function isAppViewModel(value: unknown): value is AppViewModel {
  if (!isRecord(value)) return false;
  return typeof value.updatedAt === "string"
    && typeof value.eventCount === "number"
    && typeof value.today === "string"
    && Array.isArray(value.groups)
    && value.groups.every(isGroup)
    && Array.isArray(value.groupSuggestions)
    && value.groupSuggestions.every(isGroupSuggestion)
    && isProfile(value.profile)
    && Array.isArray(value.selectedGroups)
    && value.selectedGroups.every((group) => typeof group === "string")
    && typeof value.hasSelectedGroups === "boolean"
    && (value.theme === "auto" || value.theme === "light" || value.theme === "dark")
    && Array.isArray(value.categories)
    && value.categories.every(isCategory)
    && Array.isArray(value.events)
    && value.events.every(isEvent)
    && Array.isArray(value.archives)
    && value.archives.every(isArchive)
    && (value.comparison === null || isComparison(value.comparison))
    && isRecord(value.history)
    && Array.isArray(value.history.batches)
    && value.history.batches.every(isChangeBatch)
    && typeof value.history.unreadCount === "number"
    && isRecord(value.status)
    && typeof value.status.kind === "string"
    && typeof value.status.message === "string"
    && (value.updateNotice === null || isUpdateNotice(value.updateNotice))
    && (value.toast === null || isToast(value.toast))
    && isAppUpdate(value.appUpdate)
    && isProduct(value.about);
}

function isProfile(value: unknown): boolean {
  return isRecord(value)
    && Array.isArray(value.roles)
    && value.roles.every((item) => isRecord(item) && typeof item.id === "string" && typeof item.label === "string")
    && Array.isArray(value.sectors)
    && value.sectors.every((item) => isRecord(item)
      && typeof item.id === "string"
      && typeof item.label === "string"
      && typeof item.activeGroupCount === "number"
      && Array.isArray(item.groupKeys)
      && item.groupKeys.every((key) => typeof key === "string"))
    && Array.isArray(value.selectedRoles)
    && value.selectedRoles.every((item) => typeof item === "string")
    && Array.isArray(value.selectedSectors)
    && value.selectedSectors.every((item) => typeof item === "string")
    && isRecord(value.manualGroups)
    && Object.values(value.manualGroups).every((item) => typeof item === "boolean")
    && Array.isArray(value.roleCategories)
    && value.roleCategories.every((item) => typeof item === "string")
    && typeof value.onboardingCompleted === "boolean";
}

function isAppUpdate(value: unknown): boolean {
  return isRecord(value)
    && typeof value.kind === "string"
    && typeof value.message === "string"
    && (typeof value.progress === "number" || value.progress === null)
    && (typeof value.version === "string" || value.version === null)
    && typeof value.canRestart === "boolean";
}

function isArchive(value: unknown): boolean {
  return isRecord(value) && typeof value.id === "string" && typeof value.retrievedAt === "string";
}

function isComparison(value: unknown): boolean {
  return isRecord(value)
    && typeof value.baseRetrievedAt === "string"
    && isRecord(value.counts)
    && typeof value.counts.total === "number"
    && typeof value.mineCount === "number"
    && typeof value.othersCount === "number"
    && Array.isArray(value.items)
    && value.items.every(isChangeSummary);
}

function isCategory(value: unknown): boolean {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.label === "string"
    && typeof value.color === "string"
    && typeof value.colorDark === "string";
}

function isGroup(value: unknown): boolean {
  return isRecord(value)
    && typeof value.key === "string"
    && typeof value.name === "string"
    && typeof value.eventCount === "number"
    && (value.isCompleted === undefined || typeof value.isCompleted === "boolean")
    && (value.hasGoodsPage === undefined || typeof value.hasGoodsPage === "boolean");
}

function isGroupSuggestion(value: unknown): boolean {
  return isRecord(value)
    && typeof value.key === "string"
    && typeof value.name === "string"
    && typeof value.eventCount === "number"
    && (value.firstEventDate === null || typeof value.firstEventDate === "string")
    && typeof value.message === "string";
}

function isEvent(value: unknown): boolean {
  return isRecord(value)
    && typeof value.id === "string"
    && (typeof value.start === "string" || value.start === null)
    && (typeof value.end === "string" || value.end === null)
    && typeof value.group === "string"
    && typeof value.type === "string"
    && typeof value.typeLabel === "string"
    && typeof value.stage === "string"
    && typeof value.category === "string"
    && (value.recentChange === null || isLineageEntry(value.recentChange))
    && typeof value.moveCount === "number"
    && Array.isArray(value.history)
    && value.history.every(isLineageEntry);
}

function isLineageEntry(value: unknown): boolean {
  return isRecord(value)
    && (value.kind === "added" || value.kind === "removed" || value.kind === "moved" || value.kind === "changed")
    && typeof value.checkedAt === "string"
    && (value.previousStart === null || typeof value.previousStart === "string")
    && (value.previousEnd === null || typeof value.previousEnd === "string")
    && (value.previousStage === null || typeof value.previousStage === "string")
    && (value.previousDescription === null || typeof value.previousDescription === "string")
    && Array.isArray(value.changedFields)
    && value.changedFields.every(isChangedField);
}

function isChangedField(value: unknown): boolean {
  return isRecord(value)
    && (value.field === "stage" || value.field === "description" || value.field === "period" || value.field === "url")
    && typeof value.previous === "string"
    && typeof value.current === "string";
}

function isUpdateNotice(value: unknown): boolean {
  return isRecord(value)
    && typeof value.batchId === "string"
    && (value.relatedBatchIds === undefined
      || (Array.isArray(value.relatedBatchIds) && value.relatedBatchIds.every((item) => typeof item === "string")))
    && isRecord(value.counts)
    && typeof value.counts.total === "number"
    && typeof value.mineCount === "number"
    && typeof value.othersCount === "number"
    && Array.isArray(value.items)
    && value.items.every(isChangeSummary);
}

function isChangeBatch(value: unknown): boolean {
  return isRecord(value)
    && typeof value.id === "string"
    && typeof value.checkedAt === "string"
    && typeof value.isUnread === "boolean"
    && isRecord(value.counts)
    && typeof value.counts.total === "number"
    && typeof value.mineCount === "number"
    && typeof value.othersCount === "number"
    && Array.isArray(value.items)
    && value.items.every(isChangeSummary);
}

function isChangeSummary(value: unknown): boolean {
  return isRecord(value)
    && (value.kind === "added" || value.kind === "removed" || value.kind === "moved" || value.kind === "changed")
    && typeof value.title === "string"
    && typeof value.detail === "string"
    && typeof value.stage === "string"
    && Array.isArray(value.changedFields)
    && value.changedFields.every(isChangedField)
    && typeof value.mine === "boolean";
}

function isToast(value: unknown): boolean {
  return isRecord(value)
    && (value.kind === "error" || value.kind === "success")
    && typeof value.message === "string"
    && (value.action === null || value.action === "openChanges")
    && (value.batchId === null || typeof value.batchId === "string");
}

function isProduct(value: unknown): boolean {
  return isRecord(value)
    && typeof value.name === "string"
    && typeof value.version === "string"
    && typeof value.developer === "string"
    && typeof value.publisher === "string"
    && typeof value.repositoryUrl === "string"
    && typeof value.historyUrl === "string"
    && typeof value.supportUrl === "string"
    && typeof value.disclaimer === "string"
    && typeof value.publicHistoryEnabled === "boolean";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

export type CategoryId = "retail" | "edo" | "ban" | "permit" | "marking" | "registration" | "other";
export type ChangeKind = "added" | "removed" | "moved" | "changed";
export type ThemePreference = "auto" | "light" | "dark";

export interface CategoryViewModel {
  readonly id: CategoryId;
  readonly label: string;
  readonly color: string;
  readonly colorDark: string;
}

export interface ProductGroupViewModel {
  readonly key: string;
  readonly name: string;
  readonly eventCount: number;
  readonly firstSeen?: string | null;
  readonly firstEventDate?: string | null;
  readonly isNew?: boolean;
  readonly renamedFrom?: string | null;
  readonly isCompleted?: boolean;
  readonly hasGoodsPage?: boolean;
}

export interface GroupSuggestionViewModel {
  readonly key: string;
  readonly name: string;
  readonly eventCount: number;
  readonly firstEventDate: string | null;
  readonly message: string;
}

export interface UserProfileViewModel {
  readonly roles: ReadonlyArray<{ readonly id: string; readonly label: string }>;
  readonly sectors: ReadonlyArray<{ readonly id: string; readonly label: string; readonly activeGroupCount: number; readonly groupKeys: ReadonlyArray<string> }>;
  readonly selectedRoles: ReadonlyArray<string>;
  readonly selectedSectors: ReadonlyArray<string>;
  readonly manualGroups: Readonly<Record<string, boolean>>;
  readonly roleCategories: ReadonlyArray<CategoryId>;
  readonly onboardingCompleted: boolean;
}

export interface ArchiveViewModel {
  readonly id: string;
  readonly retrievedAt: string;
}

export interface ChangedFieldViewModel {
  readonly field: "stage" | "description" | "period" | "url";
  readonly previous: string;
  readonly current: string;
}

export interface EventLineageEntryViewModel {
  readonly kind: ChangeKind;
  readonly checkedAt: string;
  readonly previousStart: string | null;
  readonly previousEnd: string | null;
  readonly previousStage: string | null;
  readonly previousDescription: string | null;
  readonly changedFields: ReadonlyArray<ChangedFieldViewModel>;
}

export interface CalendarEventViewModel {
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

export interface ChangeSummaryViewModel {
  readonly kind: ChangeKind;
  readonly title: string;
  readonly detail: string;
  readonly stage: string;
  readonly changedFields: ReadonlyArray<ChangedFieldViewModel>;
  readonly mine: boolean;
}

export interface ChangeCountsViewModel {
  readonly moved: number;
  readonly added: number;
  readonly changed: number;
  readonly removed: number;
  readonly total: number;
  readonly groupsAdded?: number;
  readonly groupsRenamed?: number;
}

export interface ChangeBatchViewModel {
  readonly id: string;
  readonly checkedAt: string;
  readonly isUnread: boolean;
  readonly counts: ChangeCountsViewModel;
  readonly mineCount: number;
  readonly othersCount: number;
  readonly items: ReadonlyArray<ChangeSummaryViewModel>;
}

export interface UpdateNoticeViewModel {
  readonly batchId: string;
  readonly relatedBatchIds?: ReadonlyArray<string>;
  readonly counts: ChangeCountsViewModel;
  readonly mineCount: number;
  readonly othersCount: number;
  readonly items: ReadonlyArray<ChangeSummaryViewModel>;
}

export interface ComparisonViewModel {
  readonly baseRetrievedAt: string;
  readonly counts: ChangeCountsViewModel;
  readonly mineCount: number;
  readonly othersCount: number;
  readonly items: ReadonlyArray<ChangeSummaryViewModel>;
}

export interface ProductViewModel {
  readonly name: string;
  readonly version: string;
  readonly developer: string;
  readonly publisher: string;
  readonly repositoryUrl: string;
  readonly historyUrl: string;
  readonly supportUrl: string;
  readonly disclaimer: string;
  readonly publicHistoryEnabled: boolean;
}

export interface AppUpdateViewModel {
  readonly kind: "idle" | "checking" | "current" | "downloading" | "ready" | "error" | "unavailable";
  readonly message: string;
  readonly progress: number | null;
  readonly version: string | null;
  readonly canRestart: boolean;
}

export interface AppViewModel {
  readonly updatedAt: string;
  readonly eventCount: number;
  readonly today: string;
  readonly groups: ReadonlyArray<ProductGroupViewModel>;
  readonly selectedGroups: ReadonlyArray<string>;
  readonly hasSelectedGroups: boolean;
  readonly theme: ThemePreference;
  readonly categories: ReadonlyArray<CategoryViewModel>;
  readonly events: ReadonlyArray<CalendarEventViewModel>;
  readonly archives: ReadonlyArray<ArchiveViewModel>;
  readonly comparison: ComparisonViewModel | null;
  readonly history: { readonly unreadCount: number; readonly batches: ReadonlyArray<ChangeBatchViewModel> };
  readonly status: { readonly kind: "checking" | "ready" | "updated" | "error"; readonly message: string };
  readonly updateNotice: UpdateNoticeViewModel | null;
  readonly toast: {
    readonly kind: "error" | "success";
    readonly message: string;
    readonly action: "openChanges" | null;
    readonly batchId: string | null;
  } | null;
  readonly appUpdate: AppUpdateViewModel;
  readonly about: ProductViewModel;
  readonly groupSuggestions: ReadonlyArray<GroupSuggestionViewModel>;
  readonly profile: UserProfileViewModel;
}

export type UiCommand =
  | { readonly type: "ready" }
  | { readonly type: "refresh" }
  | { readonly type: "openChanges"; readonly batchId: string }
  | { readonly type: "dismissNotice"; readonly batchId: string }
  | { readonly type: "markHistorySeen" }
  | { readonly type: "setGroups"; readonly groups: ReadonlyArray<string> }
  | { readonly type: "setTheme"; readonly theme: ThemePreference }
  | { readonly type: "setPublicHistory"; readonly enabled: boolean }
  | { readonly type: "hideGroupSuggestion"; readonly key: string }
  | { readonly type: "saveProfile"; readonly roles: ReadonlyArray<string>; readonly sectors: ReadonlyArray<string>; readonly groups: ReadonlyArray<string> }
  | { readonly type: "skipProfile" }
  | { readonly type: "compareWith"; readonly id: string }
  | { readonly type: "copyBatch"; readonly batchId: string }
  | { readonly type: "copyNotice"; readonly batchId: string }
  | { readonly type: "copyComparison" }
  | { readonly type: "openExternal"; readonly url: string }
  | { readonly type: "copySupportUrl" }
  | { readonly type: "openLogs" }
  | { readonly type: "restartForUpdate" };

export type CommandSink = (command: UiCommand) => void;

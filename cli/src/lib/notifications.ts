export type NotificationTone = "info" | "success" | "warning" | "error"

export interface HarnessNotification {
  readonly id: string
  readonly tone: NotificationTone
  readonly title: string
  readonly detail?: string
  readonly createdAt: number
  readonly ttlMs?: number
  readonly action?: { readonly label: string; readonly command: string }
}

export interface NotificationState {
  readonly items: readonly HarnessNotification[]
}

export type NotificationAction =
  | { readonly type: "add"; readonly notification: HarnessNotification }
  | { readonly type: "dismiss"; readonly id: string }
  | { readonly type: "expire"; readonly now: number }

export const MAX_VISIBLE_NOTIFICATIONS = 3

export function notificationReducer(
  state: NotificationState,
  action: NotificationAction
): NotificationState {
  if (action.type === "add") {
    return {
      items: [
        ...state.items.filter((item) => item.id !== action.notification.id),
        action.notification,
      ],
    }
  }
  if (action.type === "dismiss") {
    return { items: state.items.filter((item) => item.id !== action.id) }
  }
  return {
    items: state.items.filter(
      (item) =>
        item.tone === "error" ||
        item.ttlMs === undefined ||
        item.createdAt + item.ttlMs > action.now
    ),
  }
}

export function visibleNotifications(
  state: NotificationState
): readonly HarnessNotification[] {
  return state.items.slice(-MAX_VISIBLE_NOTIFICATIONS)
}

export type NotificationClickTarget =
  | { readonly kind: "action"; readonly command: string }
  | { readonly kind: "dismiss"; readonly id: string }
  | { readonly kind: "none" }

export function resolveNotificationClick(
  notification: HarnessNotification,
  x: number,
  width: number
): NotificationClickTarget {
  const dismissStart = Math.max(1, width - 3)
  if (x >= dismissStart) {
    return { kind: "dismiss", id: notification.id }
  }
  if (notification.action) {
    const token = `[${notification.action.label}]`
    const actionStart = Math.max(1, width - token.length - 5)
    if (x >= actionStart && x < dismissStart) {
      return { kind: "action", command: notification.action.command }
    }
  }
  return { kind: "none" }
}

import { describe, expect, test } from "bun:test"
import {
  notificationReducer,
  resolveNotificationClick,
  visibleNotifications,
  type HarnessNotification,
} from "./notifications"

const base: HarnessNotification = {
  id: "one",
  tone: "info",
  title: "indexed",
  createdAt: 1_000,
  ttlMs: 500,
}

describe("notificationReducer", () => {
  test("expires TTL notifications with an injected time", () => {
    const state = notificationReducer(
      { items: [base] },
      { type: "expire", now: 1_500 }
    )

    expect(state.items).toEqual([])
  })

  test("keeps errors sticky until explicit dismissal", () => {
    const error = { ...base, id: "error", tone: "error" as const }
    const expired = notificationReducer(
      { items: [error] },
      { type: "expire", now: 50_000 }
    )

    expect(expired.items).toEqual([error])
    expect(
      notificationReducer(expired, { type: "dismiss", id: "error" }).items
    ).toEqual([])
  })

  test("shows at most three with newest closest to the composer", () => {
    const items = ["1", "2", "3", "4"].map((id, index) => ({
      ...base,
      id,
      createdAt: index,
    }))

    expect(visibleNotifications({ items }).map((item) => item.id)).toEqual([
      "2",
      "3",
      "4",
    ])
  })
})

describe("resolveNotificationClick", () => {
  test("resolves action and dismiss cells", () => {
    const notification = {
      ...base,
      action: { label: "retry", command: "/retry" },
    }

    expect(resolveNotificationClick(notification, 34, 40)).toEqual({
      kind: "action",
      command: "/retry",
    })
    expect(resolveNotificationClick(notification, 39, 40)).toEqual({
      kind: "dismiss",
      id: "one",
    })
  })
})

import { Box, useInput } from "ink"
import React from "react"
import { useMouse } from "../hooks/useMouse"
import {
  resolveNotificationClick,
  visibleNotifications,
  type HarnessNotification,
  type NotificationState,
} from "../lib/notifications"
import { isSgrMouseChunk } from "../lib/mouse"
import { palette } from "../theme"
import { SurfaceLine, type SurfaceTextSegment } from "./SurfaceLine"

export interface NotificationCenterProps {
  readonly width: number
  readonly terminalTop: number
  readonly state: NotificationState
  readonly onDismiss: (id: string) => void
  readonly onAction: (command: string) => void
  readonly active?: boolean
}

const marks: Readonly<Record<HarnessNotification["tone"], string>> = {
  info: "[i]",
  success: "[ok]",
  warning: "[!]",
  error: "[x]",
}

const tones: Readonly<Record<HarnessNotification["tone"], string>> = {
  info: palette.brand,
  success: palette.ok,
  warning: palette.waiting,
  error: palette.error,
}

export function NotificationCenter({
  width,
  terminalTop,
  state,
  onDismiss,
  onAction,
  active = true,
}: NotificationCenterProps) {
  const visible = visibleNotifications(state)

  useInput(
    (input, key) => {
      if (isSgrMouseChunk(input)) {
        return
      }
      if (key.ctrl && input.toLowerCase() === "d") {
        const newest = visible.at(-1)
        if (newest) {
          onDismiss(newest.id)
        }
      }
    },
    { isActive: active && visible.length > 0 }
  )

  useMouse(
    (click) => {
      const index = click.y - terminalTop
      const notification = visible[index]
      if (!notification) {
        return
      }
      const target = resolveNotificationClick(notification, click.x, width)
      if (target.kind === "action") {
        onAction(target.command)
      } else if (target.kind === "dismiss") {
        onDismiss(target.id)
      }
    },
    active && visible.length > 0
  )

  return (
    <Box flexDirection="column" width={width}>
      {visible.map((notification) => {
        const segments: SurfaceTextSegment[] = [
          { text: `${marks[notification.tone]} `, color: tones[notification.tone], bold: true },
          { text: notification.title, bold: true },
          ...(notification.detail ? [{ text: ` · ${notification.detail}`, dim: true }] : []),
          ...(notification.action ? [{ text: `  [${notification.action.label}]`, color: palette.brand }] : []),
          { text: "  [x]", dim: true },
        ]
        return (
          <SurfaceLine
            key={notification.id}
            width={width}
            background={palette.raised}
            segments={segments}
          />
        )
      })}
    </Box>
  )
}

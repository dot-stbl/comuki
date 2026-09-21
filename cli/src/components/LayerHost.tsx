import { Box } from "ink"
import React from "react"
import { palette } from "../theme"
import { Fill } from "./Fill"

export type OverlayPlacement = "center" | "bottom"

export interface LayerHostProps {
  readonly width: number
  readonly height: number
  readonly base: React.ReactNode
  readonly overlay?: React.ReactNode
  /**
   * Notification plane — paints as an absolute layer above the base.
   * Used for the "toast over the chat" feel when the terminal is wide
   * enough that the prompt is far from the viewport edge. In narrow
   * terminals the chat wiring routes notifications into the `base`
   * column instead (see `commands/chat.tsx`) so the footer stack and
   * the activity stream share one budget and never overflow the prompt.
   */
  readonly notifications?: React.ReactNode
  readonly placement?: OverlayPlacement
  readonly notificationBottomRows?: number
}

export function LayerHost({
  width,
  height,
  base,
  overlay,
  notifications,
  placement = "center",
  notificationBottomRows = 1,
}: LayerHostProps) {
  return (
    <Box width={width} height={height} flexDirection="column">
      {base}
      {overlay ? (
        // Ink has no offsets or z-index. Absolute boxes overlap at the
        // origin, so paint order is the layer contract: base, overlay,
        // then notifications. OpenTUI can map these names to real planes.
        <Box position="absolute" width={width} height={height}>
          <Fill width={width} height={height} color={palette.rail}>
            <Box
              width={width}
              height={height}
              alignItems="center"
              justifyContent={placement === "bottom" ? "flex-end" : "center"}
            >
              {overlay}
            </Box>
          </Fill>
        </Box>
      ) : null}
      {notifications ? (
        <Box
          position="absolute"
          width={width}
          height={height}
          flexDirection="column"
          justifyContent="flex-end"
          paddingBottom={notificationBottomRows}
        >
          {notifications}
        </Box>
      ) : null}
    </Box>
  )
}

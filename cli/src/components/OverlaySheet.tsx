/** One predictable modal/sheet language for overview and inspectors. */
import { Box, Text } from "ink"
import React from "react"
import { stripAnsi } from "../theme"
import { palette } from "../theme"
import { Fill } from "./Fill"

export interface OverlaySheetProps {
  readonly title: string
  readonly hint: string
  readonly width: number
  readonly height: number
  readonly children: React.ReactNode
}

export function OverlaySheet({
  title,
  hint,
  width,
  height,
  children,
}: OverlaySheetProps) {
  const sheetWidth = Math.max(24, width)
  const sheetHeight = Math.max(4, height)
  const titleText = fit(title, Math.max(1, sheetWidth - 4))
  const hintText = fit(hint, Math.max(1, sheetWidth - 4))
  return (
    <Fill width={sheetWidth} height={sheetHeight} color={palette.rail}>
      <Box width={sheetWidth} height={sheetHeight} flexDirection="column">
        <Text bold color={palette.text} backgroundColor={palette.raised}>
          {`  ${titleText}`.padEnd(sheetWidth)}
        </Text>
        <Box flexDirection="column" flexGrow={1} paddingX={2} paddingY={1}>
          {children}
        </Box>
        <Text dimColor backgroundColor={palette.raised}>
          {`  ${hintText}`.padEnd(sheetWidth)}
        </Text>
      </Box>
    </Fill>
  )
}

/** Inspector for line-oriented panels already produced by pure formatters. */
export function LineInspector({
  title,
  lines,
  width,
  height,
}: {
  readonly title: string
  readonly lines: readonly string[]
  readonly width: number
  readonly height: number
}) {
  const bodyRows = Math.max(1, height - 4)
  return (
    <OverlaySheet
      title={title}
      hint="esc close / arrows remain in transcript"
      width={width}
      height={height}
    >
      {lines.slice(0, bodyRows).map((line, index) => (
        <Text key={index}>{fitAnsi(line, Math.max(1, width - 4))}</Text>
      ))}
    </OverlaySheet>
  )
}

function fit(value: string, width: number): string {
  return value.length <= width ? value : `${value.slice(0, Math.max(1, width - 3))}...`
}

function fitAnsi(value: string, width: number): string {
  return stripAnsi(value).length <= width
    ? value
    : `${stripAnsi(value).slice(0, Math.max(1, width - 3))}...`
}

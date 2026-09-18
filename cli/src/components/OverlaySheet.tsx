/** One predictable modal/sheet language for overview and inspectors. */
import { Box, Text } from "ink"
import React from "react"
import stringWidth from "string-width"
import { stripAnsi, palette } from "../theme"
import { Fill } from "./Fill"
import { SurfaceLine } from "./SurfaceLine"

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
        <SurfaceLine
          width={sheetWidth}
          background={palette.raised}
          segments={[{ text: `  ${titleText}`, color: palette.text, bold: true }]}
        />
        <Box flexDirection="column" flexGrow={1} paddingX={2} paddingY={1}>
          {children}
        </Box>
        <SurfaceLine
          width={sheetWidth}
          background={palette.raised}
          segments={[{ text: `  ${hintText}`, dim: true }]}
        />
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
  if (stringWidth(value) <= width) {
    return value
  }
  let text = ""
  for (const char of value) {
    if (stringWidth(text + char) > Math.max(1, width - 3)) {
      break
    }
    text += char
  }
  return `${text}...`
}

function fitAnsi(value: string, width: number): string {
  return stringWidth(stripAnsi(value)) <= width
    ? value
    : fit(stripAnsi(value), width)
}

import { Text } from "ink"
import React from "react"
import stringWidth from "string-width"

export interface SurfaceTextSegment {
  readonly text: string
  readonly color?: string
  readonly bold?: boolean
  readonly dim?: boolean
}

export interface SurfaceTextProps {
  readonly background: string
  readonly segments: readonly SurfaceTextSegment[]
}

export interface SurfaceLineProps extends SurfaceTextProps {
  readonly width: number
}

const graphemes = new Intl.Segmenter(undefined, { granularity: "grapheme" })

export function padSegmentsToWidth(
  segments: readonly SurfaceTextSegment[],
  width: number
): readonly SurfaceTextSegment[] {
  const target = Math.max(0, width)
  const fitted: SurfaceTextSegment[] = []
  let used = 0

  for (const segment of segments) {
    if (used >= target) {
      break
    }
    let text = ""
    for (const part of graphemes.segment(segment.text)) {
      const columns = stringWidth(part.segment)
      if (used + columns > target) {
        break
      }
      text += part.segment
      used += columns
    }
    if (text.length > 0) {
      fitted.push({ ...segment, text })
    }
  }

  if (used < target) {
    fitted.push({ text: " ".repeat(target - used) })
  }
  return fitted
}

export function SurfaceText({ background, segments }: SurfaceTextProps) {
  return (
    <Text backgroundColor={background} wrap="truncate">
      {segments.map((segment, index) => (
        <Text
          key={index}
          color={segment.color}
          bold={segment.bold}
          dimColor={segment.dim}
          backgroundColor={background}
        >
          {segment.text}
        </Text>
      ))}
    </Text>
  )
}

export function SurfaceLine({ width, background, segments }: SurfaceLineProps) {
  return (
    <Text backgroundColor={background} wrap="truncate">
      {padSegmentsToWidth(segments, width).map((segment, index) => (
        <Text
          key={index}
          color={segment.color}
          bold={segment.bold}
          dimColor={segment.dim}
          backgroundColor={background}
        >
          {segment.text}
        </Text>
      ))}
    </Text>
  )
}

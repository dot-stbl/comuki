import { Box, Text, useInput } from "ink"
import React, { useState } from "react"
import { isSgrMouseChunk } from "../lib/mouse"
import { filterSlashCommands, SLASH_COMMANDS } from "../lib/slash"
import { palette } from "../theme"
import { OverlaySheet } from "./OverlaySheet"

export interface CommandPaletteProps {
  readonly width: number
  readonly onSelect: (command: string) => void
  readonly onClose: () => void
}

export function CommandPalette({
  width,
  onSelect,
  onClose,
}: CommandPaletteProps) {
  const [query, setQuery] = useState("")
  const [selectedIndex, setSelectedIndex] = useState(0)
  const matches = filterSlashCommands(SLASH_COMMANDS, query).slice(0, 10)
  const activeIndex =
    matches.length === 0 ? 0 : Math.min(selectedIndex, matches.length - 1)

  useInput((input, key) => {
    if (isSgrMouseChunk(input)) {
      return
    }
    if (key.escape) {
      onClose()
      return
    }
    if (key.upArrow) {
      setSelectedIndex(
        matches.length === 0
          ? 0
          : (activeIndex - 1 + matches.length) % matches.length
      )
      return
    }
    if (key.downArrow) {
      setSelectedIndex(
        matches.length === 0 ? 0 : (activeIndex + 1) % matches.length
      )
      return
    }
    if (key.return) {
      const selected = matches[activeIndex]
      if (selected) {
        onSelect(`/${selected.name}`)
      }
      return
    }
    if (key.backspace || key.delete) {
      setQuery((current) => current.slice(0, -1))
      setSelectedIndex(0)
      return
    }
    if (!key.ctrl && !key.meta && /^[A-Za-z]+$/.test(input)) {
      setQuery((current) => current + input.toLowerCase())
      setSelectedIndex(0)
    }
  })

  const sheetWidth = Math.max(32, Math.min(72, width))
  return (
    <OverlaySheet
      title={query.length > 0 ? `actions / ${query}` : "actions"}
      hint="type to filter / arrows select / enter run / esc close"
      width={sheetWidth}
      height={Math.max(7, matches.length + 4)}
    >
      <Box flexDirection="column">
        {matches.length === 0 ? (
          <Text dimColor>no matching actions</Text>
        ) : null}
        {matches.map((command, index) => (
          <Text
            key={command.name}
            color={index === activeIndex ? palette.brand : undefined}
            dimColor={index !== activeIndex}
          >
            {`${index === activeIndex ? ">" : " "} /${command.name.padEnd(12)} ${command.description}`}
          </Text>
        ))}
      </Box>
    </OverlaySheet>
  )
}

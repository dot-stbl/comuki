/**
 * The `›` prompt with input history. Line editing and the cursor come
 * from ink-text-input; ArrowUp/ArrowDown walk the history here (the
 * editor ignores vertical arrows, both handlers hear every key).
 */
import { Text, useInput } from "ink"
import TextInput from "ink-text-input"
import React, { useCallback, useState } from "react"

export interface PromptInputProps {
  readonly onSubmit: (value: string) => void
  readonly label?: string
  readonly placeholder?: string
  readonly history?: readonly string[]
}

export function PromptInput({
  onSubmit,
  label = "you  ",
  placeholder = "ask comuki… (help for commands, ctrl+c to exit)",
  history = [],
}: PromptInputProps) {
  const [value, setValue] = useState("")
  const [historyIndex, setHistoryIndex] = useState<number | null>(null)

  const setValueFromHistory = useCallback(
    (nextIndex: number | null) => {
      if (nextIndex === null || history.length === 0) {
        setHistoryIndex(null)
        setValue("")
        return
      }
      const clamped = Math.min(Math.max(0, nextIndex), history.length - 1)
      setHistoryIndex(clamped)
      setValue(history[clamped] ?? "")
    },
    [history]
  )

  useInput((_input, key) => {
    if (key.upArrow) {
      setValueFromHistory(historyIndex === null ? history.length - 1 : Math.max(0, historyIndex - 1))
    }
    if (key.downArrow && historyIndex !== null) {
      setValueFromHistory(historyIndex + 1 >= history.length ? null : historyIndex + 1)
    }
  })

  const handleSubmit = useCallback(
    (submitted: string) => {
      setValue("")
      setHistoryIndex(null)
      onSubmit(submitted)
    },
    [onSubmit]
  )

  return (
    <Text>
      <Text color="#8787f3">{label}› </Text>
      <TextInput
        value={value}
        onChange={setValue}
        onSubmit={handleSubmit}
        placeholder={placeholder}
      />
    </Text>
  )
}

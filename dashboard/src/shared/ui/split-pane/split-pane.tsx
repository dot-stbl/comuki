import { useCallback, useState } from "react"
import type { ReactNode, Ref } from "react"
import {
  Group,
  Panel,
  Separator,
  type Layout,
  type PanelImperativeHandle,
  type PanelSize,
} from "react-resizable-panels"

import { cn } from "@/shared/lib/utils"

import styles from "./split-pane.module.css"

/**
 * Split pane — a resizable, collapsible board over a working surface.
 *
 * `react-resizable-panels` v4 dropped the old `autoSaveId`, so persistence is
 * the consumer's job: this owns it, behind `storageKey`. Reads are guarded —
 * a private window or blocked site data throws on access, and a duty console
 * must not fail to render because it could not remember a divider position.
 */

export type SplitLayout = Layout

function readLayout(storageKey: string | undefined): SplitLayout | undefined {
  if (!storageKey) {
    return undefined
  }
  try {
    const raw = window.localStorage.getItem(storageKey)
    if (!raw) {
      return undefined
    }
    const parsed: unknown = JSON.parse(raw)
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as SplitLayout
    }
    return undefined
  } catch {
    return undefined
  }
}

function writeLayout(storageKey: string, layout: SplitLayout): void {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(layout))
  } catch {
    // Storage is unavailable; the session keeps working, it just forgets.
  }
}

export interface SplitPaneProps {
  children: ReactNode
  orientation?: "horizontal" | "vertical"
  /** localStorage key. Omit to make the layout session-only. */
  storageKey?: string
  /**
   * Asked before every write, so a pane group can decline to remember a layout
   * that is not a decision anybody made.
   *
   * The rail is the case it exists for. A window under the narrow breakpoint
   * collapses the rail for reasons that have nothing to do with what the
   * operator wants at their desk — and because the layout is one key, that
   * collapse used to be written straight over the width they had chosen. A
   * session that ended narrow then opened *collapsed on the big board*, with
   * nothing in the product to explain why. Nothing that happens while the
   * environment is driving the layout is written down.
   *
   * Called at write time rather than read as a flag, so it always answers about
   * the window as it is now, not as it was when the handler was built. Omit to
   * remember everything, which is what every other split on the board wants.
   */
  shouldPersist?: () => boolean
  className?: string
  id?: string
}

export function SplitPane({
  children,
  orientation = "vertical",
  storageKey,
  shouldPersist,
  className,
  id,
}: SplitPaneProps) {
  // Read once, before first paint, so the board does not jump into place.
  // A lazy state initializer rather than a ref: refs must not be read during
  // render, and this value is genuinely part of the first render's output.
  const [initialLayout] = useState<SplitLayout>(
    () => readLayout(storageKey) ?? {}
  )

  const onLayoutChanged = useCallback(
    (layout: SplitLayout) => {
      // Called, not read: the predicate answers about the window as it is at
      // the moment of the write. A boolean prop would answer about the render
      // that produced this handler, which is the render *before* the resize
      // that is being reported.
      if (storageKey && (shouldPersist?.() ?? true)) {
        writeLayout(storageKey, layout)
      }
    },
    [storageKey, shouldPersist]
  )

  return (
    <Group
      id={id}
      orientation={orientation}
      defaultLayout={initialLayout}
      onLayoutChanged={onLayoutChanged}
      className={cn(styles.group, className)}
      data-test="split-pane"
    >
      {children}
    </Group>
  )
}

export interface SplitPanelProps {
  children: ReactNode
  id: string
  defaultSize?: number | string
  minSize?: number | string
  maxSize?: number | string
  collapsible?: boolean
  collapsedSize?: number | string
  onResize?: (size: PanelSize) => void
  panelRef?: Ref<PanelImperativeHandle | null>
  className?: string
}

export function SplitPanel({
  children,
  id,
  defaultSize,
  minSize,
  maxSize,
  collapsible,
  collapsedSize,
  onResize,
  panelRef,
  className,
}: SplitPanelProps) {
  return (
    <Panel
      id={id}
      defaultSize={defaultSize}
      minSize={minSize}
      maxSize={maxSize}
      collapsible={collapsible}
      collapsedSize={collapsedSize}
      onResize={onResize}
      panelRef={panelRef}
      className={cn(styles.panel, className)}
    >
      {children}
    </Panel>
  )
}

export interface SplitSeparatorProps {
  orientation?: "horizontal" | "vertical"
  disabled?: boolean
  className?: string
  "aria-label"?: string
}

export function SplitSeparator({
  orientation = "vertical",
  disabled,
  className,
  "aria-label": ariaLabel = "Resize",
}: SplitSeparatorProps) {
  return (
    <Separator
      disabled={disabled}
      aria-label={ariaLabel}
      data-test="split-separator"
      className={cn(
        styles.separator,
        orientation === "vertical" ? styles.vertical : styles.horizontal,
        className
      )}
    >
      <span className={styles.grip} aria-hidden="true">
        <span />
        <span />
        <span />
      </span>
    </Separator>
  )
}

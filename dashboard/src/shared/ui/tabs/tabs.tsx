import type { ReactNode } from "react"
import type { Key } from "react-aria-components"
import {
  Tab as AriaTab,
  TabList as AriaTabList,
  TabPanel as AriaTabPanel,
  Tabs as AriaTabs,
} from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import styles from "./tabs.module.css"

/**
 * The kit's tabs. A horizontal strip of section names and the content
 * each one reveals.
 *
 * React Aria owns the keyboard grammar (arrow keys, Home, End, activation
 * on `Enter`) and the aria semantics (`role="tablist"`, `role="tab"`,
 * `aria-selected`, `aria-controls`); the kit owns the look — a hairline
 * strip, each tab a short word in the data voice, the selected one
 * wearing a two-pixel rule under it in the brand colour. The same voice
 * the rail uses for its active item.
 *
 * Tabs in the product come in two shapes today: a horizontal strip (the
 * three pages that use one) and the occasional vertical strip (none in
 * production yet, but the kit is wired for both — pass `orientation`
 * and the chrome follows).
 *
 * This is a re-export, not a re-implementation: call sites swap their
 * `from "react-aria-components"` import for the kit one and gain the
 * styled chrome without changing their component tree. The migration of
 * the three pages to use this primitive is a follow-up.
 */
export interface TabsProps {
  /** The id of the showing tab. The same id the matching `<Tab>` was given. */
  selectedKey: Key
  /** What the address bar — or the route, or the page — says to show. */
  onSelectionChange: (next: Key) => void
  /**
   * `horizontal` is the strip on top; `vertical` is a column on the
   * side. The pages use horizontal today; vertical is wired for the
   * future but not in production.
   */
  orientation?: "horizontal" | "vertical"
  className?: string
  children: ReactNode
}

/**
 * The tabs root. A labelled container that holds a {@link TabList} and
 * one {@link TabPanel} per section.
 */
export function Tabs({
  selectedKey,
  onSelectionChange,
  orientation = "horizontal",
  className,
  children,
}: TabsProps) {
  return (
    <AriaTabs
      className={cn(styles.root, className)}
      selectedKey={selectedKey}
      onSelectionChange={onSelectionChange}
      orientation={orientation}
    >
      {children}
    </AriaTabs>
  )
}

export interface TabListProps {
  /** An accessible name for the tab list. */
  "aria-label": string
  className?: string
  children: ReactNode
}

/**
 * The strip of section names. Sits inside the {@link Tabs} root.
 */
export function TabList({
  "aria-label": ariaLabel,
  className,
  children,
}: TabListProps) {
  return (
    <AriaTabList className={cn(styles.list, className)} aria-label={ariaLabel}>
      {children}
    </AriaTabList>
  )
}

export interface TabProps {
  id: Key
  children: ReactNode
  isDisabled?: boolean
  "data-test"?: string
}

/**
 * One section in the strip. A short word in the data voice, the
 * selected one wearing the brand-coloured rule beneath it.
 */
export function Tab({
  id,
  children,
  isDisabled,
  "data-test": dataTest,
}: TabProps) {
  return (
    <AriaTab
      id={id}
      isDisabled={isDisabled}
      data-test={dataTest}
      className={styles.tab}
    >
      {children}
    </AriaTab>
  )
}

export interface TabPanelProps {
  id: Key
  children: ReactNode
  className?: string
}

/**
 * The content for one section. Mounted only when its tab is the
 * selected one, so a heavy panel pays only for the section the operator
 * is looking at.
 */
export function TabPanel({ id, children, className }: TabPanelProps) {
  return (
    <AriaTabPanel id={id} className={cn(styles.panel, className)}>
      {children}
    </AriaTabPanel>
  )
}

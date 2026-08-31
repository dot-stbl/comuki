import type { Meta, StoryObj } from "@storybook/react"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

import { KnowledgeDetailSheet } from "./knowledge-detail-sheet"

const PINNED_RULE: KnowledgeEntry = {
  id: "api-errors",
  kind: "rule",
  title: "api-errors",
  scope: "global",
  ruleKind: "hard",
  revision: "a1b9e0",
  pinned: true,
  summary: "Ошибки API типизированы, с кодом и retry-hint",
  body: "Все HTTP-ошибки возвращают ProblemDetails с стабильным `code`, `title` и retry-hint. Без ad-hoc JSON envelope.",
  updated: "2h ago",
}

const UNPINNED_DOC: KnowledgeEntry = {
  id: "architecture-overview",
  kind: "doc",
  title: "Architecture overview",
  scope: "docs",
  revision: "2.4.1",
  pinned: false,
  summary: "Ведущая модель + рой эфемерных воркеров",
  body: "Comuki декомпозирует задачу ведущей моделью и дирижирует воркерами в контейнерах. Общая база знаний по MCP.",
  updated: "1w ago",
}

const meta: Meta<typeof KnowledgeDetailSheet> = {
  title: "Knowledge/Detail sheet",
  component: KnowledgeDetailSheet,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof KnowledgeDetailSheet>

/**
 * The kit has no sheet, so this is built in the domain: `FormDialog`'s
 * construction — React Aria for the behaviour, a CSS Module for the look —
 * docked to the inline-end edge instead of centred, so the list it was opened
 * from stays visible behind it.
 */
export const PinnedRule: Story = {
  render: () => (
    <KnowledgeDetailSheet entry={PINNED_RULE} open onOpenChange={() => {}} />
  ),
}

/**
 * An entry no run pins itself to says its revision plainly instead. The pinned
 * mark is the one accent on this screen and it has to mean something.
 */
export const UnpinnedDoc: Story = {
  render: () => (
    <KnowledgeDetailSheet entry={UNPINNED_DOC} open onOpenChange={() => {}} />
  ),
}

/** Closed: the overlay renders nothing at all rather than a hidden box. */
export const Closed: Story = {
  render: () => (
    <KnowledgeDetailSheet
      entry={PINNED_RULE}
      open={false}
      onOpenChange={() => {}}
    />
  ),
}

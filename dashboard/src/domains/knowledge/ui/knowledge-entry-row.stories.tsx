import type { Meta, StoryObj } from "@storybook/react"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

import { KnowledgeEntryRow } from "./knowledge-entry-row"

const HARD: KnowledgeEntry = {
  id: "no-secrets",
  kind: "rule",
  title: "no-secrets",
  scope: "global",
  ruleKind: "hard",
  revision: "9f2c1a",
  pinned: true,
  summary: "Секреты только из vault, не в коде и логах",
  body: "Токены и пароли читаются из env / secret store.",
  updated: "1d ago",
}

const SOFT: KnowledgeEntry = {
  id: "test-cov",
  kind: "rule",
  title: "test-cov",
  scope: "profile:tester",
  ruleKind: "soft",
  revision: "7b3d10",
  pinned: false,
  summary: "Покрытие изменённых строк ≥ 80%",
  body: "Гейт coverage считает только diff.",
  updated: "3d ago",
}

const DOC: KnowledgeEntry = {
  id: "architecture-overview",
  kind: "doc",
  title: "Architecture overview",
  scope: "docs",
  revision: "2.4.1",
  pinned: false,
  summary: "Ведущая модель + рой эфемерных воркеров",
  body: "Comuki декомпозирует задачу ведущей моделью.",
  updated: "1w ago",
}

const SKILL: KnowledgeEntry = {
  id: "add-crud-endpoint",
  kind: "skill",
  title: "add-crud-endpoint-aspnet",
  scope: "skills",
  revision: "2.4.1",
  pinned: true,
  summary: "Рецепт CRUD endpoint для ASP.NET",
  body: "Контроллер → DTO → handler → EF config → unit test.",
  updated: "5d ago",
}

const meta: Meta<typeof KnowledgeEntryRow> = {
  title: "Knowledge/Entry row",
  component: KnowledgeEntryRow,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: "var(--s2)",
          padding: "var(--s6)",
          maxInlineSize: "52rem",
        }}
      >
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof KnowledgeEntryRow>

/**
 * The four shapes an entry comes in. A rule carries its own kind beside the
 * shelf it sits on; a doc and a skill carry only the shelf.
 *
 * The summaries are Russian because they are content — written by a person for
 * a person and served by the backend. The interface around them is English.
 */
export const Kinds: Story = {
  render: () => (
    <>
      {[HARD, SOFT, DOC, SKILL].map((entry) => (
        <KnowledgeEntryRow
          key={entry.id}
          entry={entry}
          selected={false}
          onSelect={() => {}}
        />
      ))}
    </>
  ),
}

/**
 * Selection is a border and a wash of the one accent — the way the rail marks
 * its active item, and never a shadow: this list is flat on the floor.
 */
export const Selected: Story = {
  render: () => (
    <>
      <KnowledgeEntryRow entry={HARD} selected onSelect={() => {}} />
      <KnowledgeEntryRow entry={SOFT} selected={false} onSelect={() => {}} />
    </>
  ),
}

/**
 * A summary long enough to want a third line stops at two. The row stays
 * scannable; the rest of the prose is the detail sheet's job.
 */
export const LongSummary: Story = {
  render: () => (
    <KnowledgeEntryRow
      entry={{
        ...DOC,
        summary:
          "Ведущая модель декомпозирует задачу и дирижирует роем эфемерных воркеров в контейнерах, каждый из которых получает пиннованный набор правил и SDK, а общая база знаний раздаётся по MCP — так что повтор того же тикета через неделю даёт тот же план.",
      }}
      selected={false}
      onSelect={() => {}}
    />
  ),
}

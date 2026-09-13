import type { Meta, StoryObj } from "@storybook/react"

import { TopProjects } from "./top-projects"

const meta: Meta<typeof TopProjects> = {
  title: "Cost/Top projects",
  component: TopProjects,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "28rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof TopProjects>

/** A realistic day — prometheus is the runaway, atlas and comuki close behind. */
export const Day: Story = {
  args: {
    rows: [
      { projectId: "p_prometheus", projectKey: "prometheus", projectName: "Prometheus", spend: 31.1, runs: 11, cap: 90 },
      { projectId: "p_comuki", projectKey: "comuki", projectName: "Comuki platform", spend: 26.7, runs: 23, cap: 1100 },
      { projectId: "p_atlas", projectKey: "atlas", projectName: "Atlas", spend: 19.3, runs: 15, cap: 480 },
      { projectId: "p_kafka", projectKey: "kafka", projectName: "Kafka", spend: 13.4, runs: 8, cap: 320 },
      { projectId: "p_meridian", projectKey: "meridian", projectName: "Meridian", spend: 10.4, runs: 6, cap: 260 },
      { projectId: "p_helios", projectKey: "helios", projectName: "Helios", spend: 8.9, runs: 5, cap: 180 },
      { projectId: "p_plexor", projectKey: "plexor", projectName: "Plexor", spend: 5.9, runs: 5, cap: 220 },
    ],
  },
}

/** No project spend yet — the empty state, not a blank table. */
export const Empty: Story = {
  args: { rows: [] },
}

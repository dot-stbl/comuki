import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Tabs, TabList, Tab, TabPanel } from "./tabs"

const meta: Meta<typeof Tabs> = {
  title: "UI Kit/Navigation/Tabs",
  component: Tabs,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Tabs>

function Column({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        inlineSize: "44rem",
        padding: "var(--s8)",
      }}
    >
      {children}
    </div>
  )
}

function Three() {
  const [selected, setSelected] = useState<string>("library")
  return (
    <Column>
      <Tabs
        selectedKey={selected}
        onSelectionChange={(next) => setSelected(String(next))}
      >
        <TabList aria-label="Knowledge sections">
          <Tab id="library">library</Tab>
          <Tab id="disabled" isDisabled>
            coming soon
          </Tab>
          <Tab id="gate">gate</Tab>
        </TabList>
        <TabPanel id="library">
          <h3 style={{ margin: 0 }}>library</h3>
          <p style={{ margin: 0 }}>
            The rule set, revisions, and the eval harness. The default reading a
            knowledge screen opens to.
          </p>
        </TabPanel>
        <TabPanel id="gate">
          <h3 style={{ margin: 0 }}>gate</h3>
          <p style={{ margin: 0 }}>
            The per-project verification gate. Folded in from the retired
            /verify route, kept under domains/verify.
          </p>
        </TabPanel>
      </Tabs>
    </Column>
  )
}

/** The voice the three pages share — a hairline strip, each tab a short
 *  word in the data voice, the selected one wearing a two-pixel rule
 *  under it in the brand colour. */
export const Default: Story = {
  name: "Three tabs",
  render: () => <Three />,
}

function UrlBound() {
  // The same shape the three pages bind to: the URL says which tab is
  // showing. The screen owns the routing; this primitive owns the look.
  const [url, setUrl] = useState("library")
  const [selected, setSelected] = useState(url)
  return (
    <Column>
      <p style={{ margin: 0, fontFamily: "var(--font-data)" }}>
        URL: ?tab={selected}
      </p>
      <Tabs
        selectedKey={selected}
        onSelectionChange={(next) => {
          setSelected(String(next))
          setUrl(String(next))
        }}
      >
        <TabList aria-label="Identity sections">
          <Tab id="users">users</Tab>
          <Tab id="grants">grants</Tab>
          <Tab id="keys">keys</Tab>
        </TabList>
        <TabPanel id="users">
          <p style={{ margin: 0 }}>
            Users are the subjects of every grant and every key. The screen
            reads them first.
          </p>
        </TabPanel>
        <TabPanel id="grants">
          <p style={{ margin: 0 }}>
            A grant is a subject, a role and a scope. Read alongside the user it
            belongs to, never alone.
          </p>
        </TabPanel>
        <TabPanel id="keys">
          <p style={{ margin: 0 }}>
            API keys open the doors their role defines. A key is not safe to
            leave alone until you know what it opens.
          </p>
        </TabPanel>
      </Tabs>
    </Column>
  )
}

/** URL-bound selection — the showing tab is a `?tab=` param, and the
 *  router owns the change. The strip reads "selected" because the
 *  caller said so, not because the strip owns the truth. */
export const UrlDriven: Story = {
  name: "URL-driven",
  render: () => <UrlBound />,
}

function DisabledTab() {
  const [selected, setSelected] = useState<string>("apps")
  return (
    <Column>
      <Tabs
        selectedKey={selected}
        onSelectionChange={(next) => setSelected(String(next))}
      >
        <TabList aria-label="Settings sections">
          <Tab id="apps">apps</Tab>
          <Tab id="rules" isDisabled>
            rules
          </Tab>
          <Tab id="routing">routing</Tab>
        </TabList>
        <TabPanel id="apps">
          <p style={{ margin: 0 }}>Apps — the connectors and providers.</p>
        </TabPanel>
        <TabPanel id="routing">
          <p style={{ margin: 0 }}>Routing — which model fills which role.</p>
        </TabPanel>
      </Tabs>
    </Column>
  )
}

/** A disabled tab — the disabled tab is skipped by the arrow keys, and
 *  cannot be activated by press. The selected state still works for the
 *  rest of the strip. */
export const WithDisabled: Story = {
  name: "Disabled tab",
  render: () => <DisabledTab />,
}

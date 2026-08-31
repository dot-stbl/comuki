import type { Meta, StoryObj } from "@storybook/react"

import { CreateTicketPage } from "@/domains/sources/pages/create-ticket-page"
import {
  SeededSources,
  SourcesStoryFrame,
} from "@/domains/sources/ui/sources-story-frame"

/**
 * Filing a ticket into native intake, at `/sources/<id>/ticket/new`.
 *
 * The one form in this section that did *not* fold into a source's own page.
 * The watch and the connection details are configuration of a connection, so
 * they belong on the connection; a ticket is a different entity with a
 * different lifetime, gated on `inbox.take` rather than `sources.edit` and
 * taken by a member rather than by an administrator.
 */
const meta: Meta<typeof CreateTicketPage> = {
  title: "Sources/File a ticket",
  component: CreateTicketPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CreateTicketPage>

/**
 * The live reading: native intake on `comuki`. Title, body, labels — and one
 * switch that is not a field about the ticket at all, because "create" and
 * "create and start" differ by exactly one bit and a pair of buttons would make
 * the operator infer which one also wrote the ticket down.
 */
export const NewTicket: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_native_comuki/ticket/new">
      <SeededSources>
        <CreateTicketPage sourceId="src_native_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * A member who may put work into intake and could not touch the connection it
 * lands in. The form is live, which is the point of gating on the act rather
 * than on the section: requiring a project administrator to write down a bug
 * would be the wrong shape even on an administrator's screen.
 */
export const AsAMember: Story = {
  render: () => (
    <SourcesStoryFrame
      at="/sources/src_native_comuki/ticket/new"
      roles={["viewer"]}
      projectRoles={{ p_comuki: ["member"] }}
    >
      <SeededSources>
        <CreateTicketPage sourceId="src_native_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * A viewer on the same project. The create keeps its place and says what it
 * needs — and the sentence names `member` first, which is how the screen shows
 * that it answers to `inbox.take` and not to `sources.edit`.
 */
export const Denied: Story = {
  render: () => (
    <SourcesStoryFrame
      at="/sources/src_native_comuki/ticket/new"
      roles={["viewer"]}
      projectRoles={{ p_comuki: ["viewer"] }}
    >
      <SeededSources>
        <CreateTicketPage sourceId="src_native_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * The empty reading: an address whose source is no longer connected. It says so
 * by name rather than offering a form whose submit would land nowhere.
 */
export const SourceGone: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_vanished/ticket/new">
      <SeededSources>
        <CreateTicketPage sourceId="src_vanished" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

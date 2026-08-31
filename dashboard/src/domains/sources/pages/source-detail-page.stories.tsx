import { useEffect } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { SourceDetailPage } from "@/domains/sources/pages/source-detail-page"
import {
  SeededSources,
  SourcesStoryFrame,
} from "@/domains/sources/ui/sources-story-frame"
import {
  disconnectSeedSource,
  resetSeedSources,
} from "@/shared/api/mock/sources.store"

/**
 * One connection, and the two dialogs that folded into it.
 *
 * The watch region is where `watch-dialog` landed; the connection region is
 * where the edit half of `connect-source-dialog` did. The badge, the probe and
 * the disconnect ride in the header rather than in either footer, because they
 * act on the record and a footer says what the *form* does.
 */
const meta: Meta<typeof SourceDetailPage> = {
  title: "Sources/A source's page",
  component: SourceDetailPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SourceDetailPage>

/**
 * A source that is gone — reached the way an operator actually reaches it,
 * by the connection being disconnected out from under an address that was
 * already open.
 */
function Vanished({ sourceId }: { sourceId: string }) {
  useEffect(() => {
    resetSeedSources()
    disconnectSeedSource(sourceId)
    return () => {
      resetSeedSources()
    }
  }, [sourceId])
  return <SourceDetailPage sourceId={sourceId} />
}

/**
 * A GitHub app install admitting into both the swarm and the catalog: the
 * live reading. Its facts, its watch, its connection details and the two
 * hand-offs it has instead of a table.
 */
export const Watching: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_gh_comuki">
      <SeededSources>
        <SourceDetailPage sourceId="src_gh_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * A self-hosted GitLab whose watch is on, healthy and admitting nothing — the
 * label was renamed in the tracker and the expression here was never updated.
 * It is also the only shape that shows a base url, because it is the only one
 * with an instance to name.
 */
export const SelfHosted: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_gl_plexor">
      <SeededSources>
        <SourceDetailPage sourceId="src_gl_plexor" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * Jira with a revoked token. The badge says *that* it is broken; the notice at
 * the top is the only place that says why, in the provider's own words — and
 * what it says to do next is reconnect rather than edit.
 */
export const Broken: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_jira_atlas">
      <SeededSources>
        <SourceDetailPage sourceId="src_jira_atlas" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * Native intake: no watch, no remote end, and a disconnect that refuses. The
 * refusal is the product's rather than a role's, so it wears `denied` with the
 * product's own sentence — the same spelling the row uses. It is also the one
 * shape that offers a ticket.
 */
export const NativeIntake: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_native_comuki">
      <SeededSources>
        <SourceDetailPage sourceId="src_native_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * The same page to somebody who only watches `comuki`. Every act is still
 * there, at the same size, in the same place, each carrying the sentence that
 * would open it — a shorter screen teaches nobody what to ask for.
 */
export const Denied: Story = {
  render: () => (
    <SourcesStoryFrame
      at="/sources/src_gh_comuki"
      roles={["viewer"]}
      projectRoles={{ p_comuki: ["viewer"] }}
    >
      <SeededSources>
        <SourceDetailPage sourceId="src_gh_comuki" />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * The empty reading: the address resolved to nothing. It names the id it was
 * given, says that arriving here is ordinary rather than a fault, and hands
 * back the list.
 */
export const NotFound: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/src_gh_comuki">
      <Vanished sourceId="src_gh_comuki" />
    </SourcesStoryFrame>
  ),
}

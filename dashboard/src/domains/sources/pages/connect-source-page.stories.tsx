import type { Meta, StoryObj } from "@storybook/react"

import { ConnectSourcePage } from "@/domains/sources/pages/connect-source-page"
import {
  SeededSources,
  SourcesStoryFrame,
} from "@/domains/sources/ui/sources-story-frame"

/**
 * Connecting a source, on its own screen at its own address.
 *
 * Four decisions in the order they constrain each other, a credential list that
 * is closed per provider, and a probe that has to answer before the save opens.
 * All of that was true in the dialog; what is new is the room to say it and a
 * way back that is not a button in a footer.
 */
const meta: Meta<typeof ConnectSourcePage> = {
  title: "Sources/Connect a source",
  component: ConnectSourcePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ConnectSourcePage>

/**
 * The empty reading: as it opens. Nothing typed, the save `disabled` because
 * untested is *invalid* rather than forbidden, and the secret's rule stated
 * above the box it is typed into rather than under the button that would have
 * already taken it.
 */
export const Untested: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/new">
      <SeededSources>
        <ConnectSourcePage />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * The live reading: pick `gitlab` in the first select to watch the base url
 * appear, then press the probe. A self-hosted instance has to be named before
 * anything can reach it, and the form says so in the provider's own answer
 * rather than in a validation message it invented.
 */
export const SelfHostedProvider: Story = {
  render: () => (
    <SourcesStoryFrame at="/sources/new">
      <SeededSources>
        <ConnectSourcePage />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

/**
 * The same form to somebody who administers `atlas` and only watches the rest.
 * The save keeps its place and carries the sentence naming the role and the
 * project that would open it — change the project select to `atlas` and the
 * refusal goes away, which is the whole shape of a project permission.
 */
export const DeniedOnThisProject: Story = {
  render: () => (
    <SourcesStoryFrame
      at="/sources/new"
      roles={["viewer"]}
      projectRoles={{ p_atlas: ["project-admin"] }}
    >
      <SeededSources>
        <ConnectSourcePage />
      </SeededSources>
    </SourcesStoryFrame>
  ),
}

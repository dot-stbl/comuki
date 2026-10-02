import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { InitWizardPage } from "./init-wizard-page"

/* The onboarding wizard at `/chat/init`. Five steps, each its own
   screen-sized card: the repository (remote + branch), the compute
   choice (provider + ceiling), the model endpoints, the knowledge opt-in,
   and the confirm review before the stream runs.

   The step is a search parameter in the real route, which is why the page
   takes `step` as a prop rather than reading it off a router context —
   the story hands the step directly so each frame can show one card.
   `project` is the project the console scoped `/init` to when it
   launched from chat; the stories here open with `p_comuki` already
   chosen so the form starts from a real draft rather than the empty one
   that the route would land on when nobody had selected anything yet.

   The fifth step is the review, where the operator reads the whole draft
   back before pressing Start. The stream itself is a moving target that
   the harness cannot freeze, so it is left out of scope here — the
   story per step is the one that needs to be readable. */

const ROUTES = [
  "/",
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
  "/sources",
  "/knowledge",
  "/verify",
  "/settings",
  "/identity",
  "/projects",
  "/compute",
  "/models",
  "/observability",
  "/chat",
  "/chat/init",
] as const

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof InitWizardPage> = {
  title: "Pages/InitWizard",
  component: InitWizardPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof InitWizardPage>

/**
 * The first card — the project, the git remote, the branch, the
 * write-access toggle. The Continue button advances to the compute
 * step; the wizard's own crumb sits on the chrome above.
 */
export const RepoStep: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/chat/init"
      projects={PROJECTS}
    >
      <InitWizardPage step="repo" project="p_comuki" />
    </PageStoryFrame>
  ),
}

/**
 * The fifth card — the review, where every field the operator typed is
 * read back to them before Start. Notice above, definition list below;
 * the words carry the same prose the form collected on the way in.
 */
export const ConfirmStep: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/chat/init"
      projects={PROJECTS}
    >
      <InitWizardPage step="confirm" project="p_comuki" />
    </PageStoryFrame>
  ),
}
import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react-vite"
import { expect, userEvent } from "storybook/test"

import { SessionProvider, type Role } from "@/shared/session"

import { CreateProjectForm } from "./create-project-form"

/* The form reads the session for one thing — whether this role may create a
   project at all — so a story of it stands inside a session. Everything else
   it owns itself: no router, no shell, no mutation. */

/** The seeded registry's handles — what "taken" means on this form. */
const TAKEN_SLUGS = ["comuki", "plexor", "atlas", "vega"]

/** Every project the seeded registry holds, for the session's project list. */
const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_vega", key: "vega", name: "Vega" },
]

function Shift({ roles, children }: { roles: Role[]; children: ReactNode }) {
  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: roles,
        projectRoles: {},
      }}
      projects={PROJECTS}
    >
      <div style={{ padding: "var(--s6)", inlineSize: "44rem" }}>
        {children}
      </div>
    </SessionProvider>
  )
}

/** This repo's fields key on stable ids, not testing-library defaults. */
function field(root: HTMLElement, id: string): HTMLElement {
  const found = root.querySelector<HTMLElement>(`#${id}`)
  if (!found) {
    throw new Error(`#${id} not found in story canvas`)
  }
  return found
}

/** The form itself, wired to do nothing — a story reads, it does not create. */
function Form() {
  return (
    <CreateProjectForm
      takenSlugs={TAKEN_SLUGS}
      onCreate={() => {}}
      onCancel={() => {}}
    />
  )
}

const meta: Meta<typeof CreateProjectForm> = {
  title: "Projects/Create project form",
  component: CreateProjectForm,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CreateProjectForm>

/**
 * The form as it opens: six fields, the submit waiting on a name, and the
 * slug proposing itself from the name the moment one is typed.
 */
export const Empty: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Form />
    </Shift>
  ),
}

/**
 * Every field answering: type a name and the slug proposes itself, the
 * repository and the icon say where the project's marks live, and the tags
 * commit as chips on Enter or comma.
 *
 * The colour is the one field this story cannot fill — a native colour
 * picker answers to the OS dialog, not the keyboard — so it stays in its
 * unset reading here; `Color field`'s own stories carry it.
 */
export const Filled: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Form />
    </Shift>
  ),
  play: async ({ canvasElement }) => {
    await userEvent.type(field(canvasElement, "project-name"), "Atlas Two")
    await userEvent.type(
      field(canvasElement, "project-repo"),
      "git@github.com:atlas/worker-profiles.git"
    )
    await userEvent.type(field(canvasElement, "project-icon"), "🛰️")
    await userEvent.type(
      field(canvasElement, "project-tags"),
      "billing{enter}web{enter}"
    )
    await expect(field(canvasElement, "project-slug")).toHaveValue("atlas-two")
  },
}

/**
 * The slug refused, and why: typing a space into it leaves the sentence
 * under the field rather than a red border around it, and the submit stays
 * reachable because a control that cannot be pressed cannot explain itself.
 * The slug stays exactly as typed — sanitising it quietly would produce a
 * handle nobody chose.
 */
export const SlugError: Story = {
  render: () => (
    <Shift roles={["platform-admin"]}>
      <Form />
    </Shift>
  ),
  play: async ({ canvasElement }) => {
    await userEvent.type(field(canvasElement, "project-slug"), "atlas two")
  },
}

/**
 * The same form to a role that may not create projects. The submit keeps
 * its place and carries the sentence naming the roles that would open the
 * act — denied, not disabled, so the explanation stays reachable — and
 * typing a name first shows the refusal alone rather than beside an
 * empty-form disable.
 */
export const Denied: Story = {
  render: () => (
    <Shift roles={["member"]}>
      <Form />
    </Shift>
  ),
  play: async ({ canvasElement }) => {
    await userEvent.type(field(canvasElement, "project-name"), "Vega clone")
  },
}

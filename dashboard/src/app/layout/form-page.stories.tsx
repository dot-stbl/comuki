import { createContext, useContext, useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { SessionProvider } from "@/shared/session"
import { Button, SelectField, TextField } from "@/shared/ui"

import {
  FormActions,
  FormFields,
  FormLayout,
  FormPage,
  FormRow,
} from "./form-page"

/* The crumbs are real `<Link>`s and the shell renders the whole rail, so this
   screen only exists inside a router, a session and a query client — the same
   three the app hands it. A memory router carrying the product's own paths
   gives the story working crumbs without dragging in the route tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  [
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
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

function Frame({ children }: { children: ReactNode }) {
  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: ["platform-admin"],
        projectRoles: {},
      }}
      projects={[
        { id: "p_comuki", key: "comuki", name: "Comuki platform" },
        { id: "p_atlas", key: "atlas", name: "Atlas" },
      ]}
    >
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <SlotContext value={children}>
          <RouterProvider router={router} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

/**
 * The frame a form gets now that it is a screen rather than a modal — shell,
 * header, and a column measured for reading rather than for a modal's width.
 *
 * The furniture beside it (`FormLayout`, `FormFields`, `FormRow`,
 * `FormActions`) is what a form standing on the page is built from, and it is
 * shown here rather than in its own story because the page and the form on it
 * are one gesture.
 */
const meta: Meta<typeof FormPage> = {
  title: "Shell/FormPage",
  component: FormPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [(Story) => <Frame>{<Story />}</Frame>],
}

export default meta
type Story = StoryObj<typeof FormPage>

const SCOPES = [
  { value: "platform", label: "platform" },
  { value: "project", label: "project" },
]

const ROLES = [
  { value: "viewer", label: "viewer" },
  { value: "member", label: "member" },
  { value: "approver", label: "approver" },
  { value: "operator", label: "operator" },
  { value: "project-admin", label: "project-admin" },
  { value: "platform-admin", label: "platform-admin" },
]

function ProjectForm() {
  const [name, setName] = useState("")
  const [slug, setSlug] = useState("")
  const [repo, setRepo] = useState("")

  return (
    <FormLayout
      data-test="story-project"
      onSubmit={(event) => event.preventDefault()}
    >
      <FormFields>
        <TextField
          id="story-project-name"
          label="name"
          value={name}
          placeholder="what this project is, in a few words"
          onValueChange={setName}
        />
        <TextField
          id="story-project-slug"
          label="slug"
          value={slug}
          placeholder="lowercase, hyphens, no spaces"
          hint="Shown as a column in every list in the product. Lowercase letters, digits and hyphens."
          onValueChange={setSlug}
        />
        <TextField
          id="story-project-repo"
          label="git profile repository"
          value={repo}
          placeholder="git@github.com:org/worker-profiles.git"
          hint="Optional. Where this project's worker profiles are authored — leave it empty to run on the platform defaults."
          onValueChange={setRepo}
        />
      </FormFields>

      <FormActions>
        <Button type="submit">Create project</Button>
        <Button variant="secondary">Cancel</Button>
      </FormActions>
    </FormLayout>
  )
}

/** One column of questions and the two acts under it. */
export const Default: Story = {
  args: {
    title: "New project",
    crumbs: [
      { label: "platform" },
      { label: "projects", to: "/projects" },
      { label: "new" },
    ],
    summary:
      "A project owns its applications, its runs and its budget. The slug is the handle it is known by everywhere else.",
    children: <ProjectForm />,
  },
}

function GrantForm() {
  const [scope, setScope] = useState("platform")
  const [project, setProject] = useState("p_comuki")
  const [role, setRole] = useState("viewer")
  const [subject, setSubject] = useState("u_rhea")

  return (
    <FormLayout
      data-test="story-grant"
      onSubmit={(event) => event.preventDefault()}
    >
      <FormFields>
        <FormRow>
          <SelectField
            id="story-grant-kind"
            label="subject kind"
            value="user"
            options={[
              { value: "user", label: "user" },
              { value: "api-key", label: "api key" },
            ]}
            onValueChange={() => {}}
          />
          <SelectField
            id="story-grant-subject"
            label="subject"
            value={subject}
            options={[
              { value: "u_rhea", label: "Rhea Okafor" },
              { value: "u_tomas", label: "Tomas Lindqvist" },
            ]}
            onValueChange={setSubject}
          />
        </FormRow>

        <SelectField
          id="story-grant-role"
          label="role"
          value={role}
          options={ROLES}
          hint="Roles live in code — these six are the whole set, and there is no way to add one."
          onValueChange={setRole}
        />

        <FormRow>
          <SelectField
            id="story-grant-scope"
            label="scope"
            value={scope}
            options={SCOPES}
            hint="A platform grant holds everywhere. A project grant holds on one project and nowhere else."
            onValueChange={setScope}
          />
          {scope === "project" ? (
            <SelectField
              id="story-grant-project"
              label="project"
              value={project}
              options={[
                { value: "p_comuki", label: "comuki" },
                { value: "p_atlas", label: "atlas" },
              ]}
              onValueChange={setProject}
            />
          ) : null}
        </FormRow>
      </FormFields>

      <FormActions>
        <Button type="submit">Grant</Button>
        <Button variant="secondary">Cancel</Button>
      </FormActions>
    </FormLayout>
  )
}

/**
 * Two fields that are one decision share a line, and a conditional second
 * field takes no track until it exists — pick *project* as the scope to watch
 * the row grow a second column rather than reflow the one it had.
 */
export const PairedFields: Story = {
  args: {
    title: "Grant a role",
    crumbs: [
      { label: "platform" },
      { label: "identity", to: "/identity" },
      { label: "new grant" },
    ],
    summary:
      "A grant is three things: who, which role, and where it holds. Roles are the six the code has.",
    children: <GrantForm />,
  },
}

/** The least a form page can be: a title, a crumb path and one field. */
export const OneField: Story = {
  args: {
    title: "Link an oidc subject",
    crumbs: [
      { label: "platform" },
      { label: "identity", to: "/identity" },
      { label: "link" },
    ],
    children: (
      <FormLayout
        data-test="story-link"
        onSubmit={(event) => event.preventDefault()}
      >
        <FormFields>
          <TextField
            id="story-oidc-subject"
            label="subject"
            value=""
            placeholder="oidc|provider|00000000"
            hint="The `sub` claim the provider issues for this person."
            onValueChange={() => {}}
          />
        </FormFields>
        <FormActions>
          <Button type="submit">Link subject</Button>
          <Button variant="secondary">Cancel</Button>
        </FormActions>
      </FormLayout>
    ),
  },
}

import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { InitWizardPage, stepFrom, type InitStep } from "@/domains/chat"

export interface InitSearch {
  /**
   * Which step of the wizard is showing.
   *
   * In the URL because a wizard without an address is a wizard you cannot send
   * anybody to, cannot reload, and cannot press back inside. Absent means the
   * first step, so `/chat/init` is a link anybody can hand over.
   *
   * What is deliberately *not* here is anything typed. A git remote and a
   * secret reference in the address bar are a git remote and a secret
   * reference in browser history and in every proxy log on the way.
   */
  step?: InitStep
  /**
   * The project the console scoped `/init` to, when the wizard was reached by
   * typing rather than by clicking. It seeds the first step's own field and is
   * then the operator's — and it is validated against the projects they may
   * connect a source on, so an id pasted into the address bar opens the picker
   * rather than the project.
   */
  project?: string
}

export const Route = createFileRoute("/chat/init")({
  validateSearch: (search: Record<string, unknown>): InitSearch => {
    const parsed: InitSearch = {}
    if (typeof search.step === "string") {
      parsed.step = stepFrom(search.step)
    }
    const project =
      typeof search.project === "string" ? search.project.trim() : ""
    if (project) {
      parsed.project = project
    }
    return parsed
  },
  component: RouteComponent,
})

/* Gated on the act rather than on the section it is reached from: `chat.use`
   opens the console and `sources.edit` opens this, so a member who guesses the
   URL meets the forbidden state with the roles that would work written on it —
   rather than a five-step form whose last button refuses. */
function RouteComponent() {
  const { step = "repo", project } = Route.useSearch()

  return (
    <RequirePermission
      permission="sources.edit"
      title="Onboard a repository"
      crumbs={[
        { label: "console", to: "/chat" },
        { label: "onboard a repository" },
      ]}
    >
      <InitWizardPage step={step} project={project} />
    </RequirePermission>
  )
}

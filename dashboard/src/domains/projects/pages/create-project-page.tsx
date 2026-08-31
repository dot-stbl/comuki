import { useMemo, useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useCreateProjectMutation,
  useProjectsQuery,
} from "@/domains/projects/api/queries"
import type { CreateProjectInput } from "@/domains/projects/model/types"
import { CreateProjectForm } from "@/domains/projects/ui/create-project-form"
import { ConfirmDialog } from "@/shared/ui"

/**
 * Adding a project, on its own screen at its own address.
 *
 * The three things a modal never had to answer for, answered here:
 *
 * - **A way back.** The crumb path is `platform / projects / new`, and
 *   `projects` is a real link. Somebody who arrived by typing the URL still
 *   has the registry one click away.
 * - **A cancel that returns.** Back through the router's own history when
 *   there is history — the operator lands wherever they pressed *new project*,
 *   filters and scroll position included — and on the registry when there is
 *   not, which is what a bookmarked or pasted URL gets.
 * - **A submit that lands on the thing it made.** The registry has no per
 *   project screen to send anyone to, so the next best true thing: the list,
 *   filtered to the new slug, with the filter *visible* in the toolbar and one
 *   click from being cleared. Coupling two surfaces invisibly is the one thing
 *   this product's tables are not allowed to do; a filter the operator can see
 *   and reset is the sanctioned way to say "this one".
 *
 * The success navigation replaces rather than pushes: a form that has already
 * been submitted is not somewhere the back button should be able to return to.
 */
export function CreateProjectPage() {
  const navigate = useNavigate()
  const router = useRouter()
  const { data = [] } = useProjectsQuery()
  const createProject = useCreateProjectMutation()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const takenSlugs = useMemo(() => data.map((project) => project.slug), [data])

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/projects", search: {} })
    })
  }

  const onCreate = (input: CreateProjectInput) => {
    createProject.mutate(input, {
      onSuccess: () => {
        toast.success("Project created", { description: input.slug })
        guard.leave(() => {
          void navigate({
            to: "/projects",
            search: { q: input.slug },
            replace: true,
          })
        })
      },
    })
  }

  return (
    <FormPage
      title="New project"
      crumbs={[
        { label: "platform" },
        { label: "projects", to: "/projects" },
        { label: "new" },
      ]}
      summary="A project owns its applications, its runs and its budget. The slug is the handle it is known by everywhere else."
    >
      <CreateProjectForm
        takenSlugs={takenSlugs}
        busy={createProject.isPending}
        onCreate={onCreate}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      {/* Leaving a half-filled form is a decision in one sentence, which is
          exactly what a confirm is for — and unlike the form it replaced, it
          asks about something the operator did by accident. */}
      <ConfirmDialog
        open={guard.asking}
        title="Leave without creating the project?"
        body="The name, slug and repository you typed are not saved anywhere yet. Leaving this page drops them."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}

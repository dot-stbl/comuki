import { useMemo, useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useCreateTaskMutation,
  useTasksQuery,
} from "@/domains/tasks/api/queries"
import { uniqueTaskApps } from "@/domains/tasks/model/filter-tasks"
import type { CreateTaskInput } from "@/domains/tasks/model/types"
import { CreateTaskForm } from "@/domains/tasks/ui/create-task-form"
import { TASK_APPS } from "@/shared/api/mock/tasks.seed"
import { can, useSession } from "@/shared/session"
import { ConfirmDialog, Notice } from "@/shared/ui"

/**
 * Creating a task, on its own screen at `/tasks/new`.
 *
 * It was a modal over the backlog, and the argument for the modal was honest
 * while it lasted: five short answers taken while reading a list, where a
 * page would lose the place. But creating an entity is a page in this
 * product — the chat wizard, connect-source, every identity create — because
 * a modal has no address to paste into a ticket, no path back except its own
 * footer, and `--modal-w`'s 26rem for a form whose first question is a row of
 * five provider cards. The last of the offenders is this one.
 *
 * The three things a modal never had to answer for, answered here:
 *
 * - **A way back.** The crumb path is `tasks / new`, and `tasks` is a real
 *   link — the list, filters and all, one click away.
 * - **A cancel that returns.** Back through the router's own history when
 *   there is history, and on `/tasks` when there is not, which is what a
 *   bookmarked or pasted URL gets.
 * - **A submit that lands on what it made.** `/tasks?q=<title>`, narrowed to
 *   the ticket's own title — the same hand-off the identity creates use, so
 *   the operator sees the row they made and can clear the narrowing in the
 *   toolbar it arrives seeded into. `replace: true`, because a form that has
 *   already been submitted is not somewhere back should be able to return.
 */
export function CreateTaskPage() {
  const navigate = useNavigate()
  const router = useRouter()
  const session = useSession()

  const { data = [] } = useTasksQuery()
  const createTask = useCreateTaskMutation()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  // The app list the form offers, derived the way the backlog's own filter
  // derives it: what the backlog holds, falling back to the platform's known
  // apps when it holds nothing yet — an empty backlog is exactly when intake
  // is used, and a form that offered no apps then would be refusing by
  // accident.
  const apps = useMemo(() => {
    const fromData = uniqueTaskApps(data)
    return fromData.length > 0 ? fromData : [...TASK_APPS]
  }, [data])

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/tasks", search: {} })
    })
  }

  const onCreate = (input: CreateTaskInput) => {
    // The button already refuses a denied click, and the handler asks the same
    // question again on the way in: the gate is the permission, not the
    // control that happens to be carrying it today.
    if (!can(session, "inbox.take", input.projectId)) {
      return
    }
    createTask.mutate(input, {
      onSuccess: () => {
        toast.success("Task created", { description: input.title })
        guard.leave(() => {
          void navigate({
            to: "/tasks",
            search: { q: input.title },
            replace: true,
          })
        })
      },
    })
  }

  return (
    <FormPage
      title="New task"
      crumbs={[{ label: "tasks", to: "/tasks" }, { label: "new" }]}
      summary="One ticket in front of the swarm: where it came from, which project's backlog it lands in, and how urgent it is."
    >
      {createTask.error ? (
        <Notice tone="bad" data-test="create-failure">
          {createTask.error.message} Nothing was queued — what you typed is
          still exactly as you left it.
        </Notice>
      ) : null}

      <CreateTaskForm
        apps={apps}
        busy={createTask.isPending}
        onCreate={onCreate}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      {/* Leaving a half-filled form is a decision in one sentence, which is
          exactly what a confirm is for — and unlike the form it replaced, it
          asks about something the operator did by accident. */}
      <ConfirmDialog
        open={guard.asking}
        title="Leave without creating the task?"
        body="The title and brief you typed are not saved anywhere yet. Leaving this page drops them, and nothing reaches intake."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}

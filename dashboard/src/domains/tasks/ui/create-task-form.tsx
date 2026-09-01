import { useEffect, useMemo, useState } from "react"
import type { FormEvent } from "react"

import {
  FormActions,
  FormCard,
  FormLayout,
  FormMeasure,
  FormRow,
} from "@/app/layout/form-page"
import { DEFAULT_TASK_SOURCE } from "@/domains/tasks/model/task-sources"
import type { CreateTaskInput, TaskPriority } from "@/domains/tasks/model/types"
import { TaskPriorityField } from "@/domains/tasks/ui/task-priority-field"
import { TaskSourceCards } from "@/domains/tasks/ui/task-source-cards"
import { can, useCan, useSession } from "@/shared/session"
import {
  Button,
  SelectField,
  TextField,
  TextareaField,
} from "@/shared/ui"

export interface CreateTaskFormProps {
  apps: string[]
  busy?: boolean
  onCreate: (input: CreateTaskInput) => void
  onCancel: () => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Manual intake: one ticket, typed by a person rather than pulled off a board.
 *
 * This was the `CreateTaskDialog` and is now the fields half of a page — the
 * arguments are unchanged, because none of them were about being in a modal.
 * The one thing a page has that a modal did not is room, and the first thing
 * that room bought is the question a modal's width had been quietly skipping:
 * where the task comes from. The card row above the fields asks it, and the
 * stamp it produces is what the backlog's source column reads.
 *
 * Priority is a segmented row rather than the select it used to be: the
 * backlog reads the value as a coloured, shaped badge, and a form that asked
 * the same question in plain text was teaching two vocabularies for one
 * idea. Each segment wears the badge's own icon and hue, so picking `high`
 * here is already seeing it there — see `task-priority-field.tsx`.
 *
 * No router, no shell, no mutation — the page above owns all three, which is
 * what lets the fields and the rules about them be tested on their own.
 */
export function CreateTaskForm({
  apps,
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: CreateTaskFormProps) {
  const session = useSession()

  /**
   * The projects this shift may put work into — the only ones the form offers.
   *
   * Filtered rather than shown-and-refused: a select is a list of things that
   * can happen, so a project this person cannot take work in has no business
   * being in it. The *acts* stay visible and explain themselves; the choices
   * behind an act do not. If the list comes back empty the submit carries the
   * sentence instead, which is the honest single denial for the whole form.
   */
  const projects = useMemo(
    () =>
      session.projects.filter((entry) => can(session, "inbox.take", entry.id)),
    [session]
  )

  const [source, setSource] = useState(DEFAULT_TASK_SOURCE)
  const [title, setTitle] = useState("")
  const [projectId, setProjectId] = useState(projects[0]?.id ?? "")
  const [app, setApp] = useState(apps[0] ?? "")
  const [priority, setPriority] = useState<TaskPriority>("normal")
  const [brief, setBrief] = useState("")

  // The chosen project is the one the act happens in, so it is the one the
  // check names. With no project to choose, this degrades to the session-wide
  // question — false exactly when the list above came back empty — and the
  // sentence loses the project rather than naming the wrong one.
  const create = useCan("inbox.take", projectId || undefined)

  // Coarse the way `useUnsavedGuard` asks for it: any field off its default,
  // the source stamp included — choosing a provider is as much a decision the
  // operator would lose as a typed line is.
  const dirty =
    source !== DEFAULT_TASK_SOURCE ||
    title !== "" ||
    brief !== "" ||
    projectId !== (projects[0]?.id ?? "") ||
    app !== (apps[0] ?? "") ||
    priority !== "normal"

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const trimmed = title.trim()
    // `disabled` for *invalid* — a missing title, an app that never loaded —
    // and `denied` for the role, and the two must not look alike.
    if (create.denial || busy || !trimmed || !app || !projectId) {
      return
    }
    onCreate({
      projectId,
      source,
      title: trimmed,
      app,
      priority,
      brief: brief.trim() || undefined,
    })
  }

  return (
    <FormLayout data-test="create-task" onSubmit={submit}>
      {/* The page is full width, so the form is grouped rather than stacked:
          three named decisions, each a card, rather than one long column of
          full-width fields. The cards spend the width where it buys something
          — the source row spreads its five providers, the ticket row puts
          project, app and priority on one line — and `FormMeasure` keeps the
          one-line fields readable. */}
      <FormCard
        label="source"
        note="where the ticket came from. every card but manual expects a connection watching that provider — writing a ticket here records where it came from, it does not file one there."
      >
        <TaskSourceCards
          value={source}
          disabled={busy}
          data-test="task-source"
          onValueChange={setSource}
        />
      </FormCard>

      <FormCard label="the ticket" note="what to do, where it lands, how urgent it is.">
        <FormMeasure>
          <TextField
            id="task-title"
            label="title"
            autoFocus
            value={title}
            disabled={busy}
            placeholder="what to do, in one line"
            data-test="task-title"
            onValueChange={setTitle}
          />
        </FormMeasure>

        <FormRow>
          <SelectField
            id="task-project"
            label="project"
            value={projectId}
            disabled={busy || projects.length === 0}
            options={projects.map((entry) => ({
              value: entry.id,
              label: entry.key,
            }))}
            data-test="task-project"
            onValueChange={setProjectId}
          />

          <SelectField
            id="task-app"
            label="app"
            value={app}
            disabled={busy || apps.length === 0}
            options={apps.map((item) => ({ value: item, label: item }))}
            data-test="task-app"
            onValueChange={setApp}
          />

          <TaskPriorityField
            value={priority}
            disabled={busy}
            data-test="task-priority"
            onValueChange={setPriority}
          />
        </FormRow>
      </FormCard>

      <FormCard
        label="brief"
        note="context, acceptance criteria, links — for the person who picks this up, not for the form."
      >
        <FormMeasure>
          <TextareaField
            id="task-brief"
            label="brief"
            // A brief is something a person wrote for another person to read, so it
            // takes the interface voice rather than the data one.
            voice="prose"
            value={brief}
            disabled={busy}
            placeholder="context, acceptance criteria, links…"
            data-test="task-brief"
            onValueChange={setBrief}
          />
        </FormMeasure>
      </FormCard>

      <FormActions>
        {/* `disabled` stays for *busy* and *invalid*. Having no project to
            choose is neither — it is the denial itself, and `denied` is what
            keeps the sentence reachable: a disabled control fires no pointer
            events, so its explanation never arrives at a pointer and leaves
            the tab order too. */}
        <Button
          type="submit"
          data-test="form-submit"
          denied={create.denial}
          disabled={busy || !title.trim() || !app}
          aria-busy={busy || undefined}
        >
          Create &amp; queue
        </Button>
        <Button
          variant="secondary"
          data-test="form-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          Cancel
        </Button>
      </FormActions>
    </FormLayout>
  )
}

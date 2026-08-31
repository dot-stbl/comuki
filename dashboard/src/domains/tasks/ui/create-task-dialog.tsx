import { useMemo, useState } from "react"

import type {
  CreateTaskInput,
  TaskPriority,
} from "@/domains/tasks/model/types"
import { can, useCan, useSession } from "@/shared/session"
import {
  FormDialog,
  SelectField,
  TextField,
  TextareaField,
} from "@/shared/ui"

const PRIORITIES: { value: TaskPriority; label: string }[] = [
  { value: "low", label: "low" },
  { value: "normal", label: "normal" },
  { value: "high", label: "high" },
]

export interface CreateTaskDialogProps {
  open: boolean
  apps: string[]
  busy?: boolean
  onOpenChange: (open: boolean) => void
  onCreate: (input: CreateTaskInput) => void
}

/**
 * Manual intake: one ticket, typed by a person rather than pulled off a branch.
 *
 * A dialog rather than a page, unlike the platform registries' forms: this is
 * five short answers taken while the operator is already reading the backlog,
 * and sending them to `/tasks/new` and back would lose the place in a list they
 * are working down. The forms that became pages are the ones that *edit*
 * something that already exists.
 *
 * Priority is a select and not the three-button segmented row it used to be.
 * The kit has no segmented control, and inventing a fifth idiom for a closed
 * list of three values — beside two other closed lists in the same form — would
 * have been a new thing to learn for nothing gained. A `SelectField` is exactly
 * what a closed list is for, and the words are unchanged.
 */
export function CreateTaskDialog({
  open,
  apps,
  busy = false,
  onOpenChange,
  onCreate,
}: CreateTaskDialogProps) {
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

  const reset = () => {
    setTitle("")
    setProjectId(projects[0]?.id ?? "")
    setApp(apps[0] ?? "")
    setPriority("normal")
    setBrief("")
  }

  const close = () => {
    reset()
    onOpenChange(false)
  }

  const submit = () => {
    const trimmed = title.trim()
    if (!create.allowed || !trimmed || !app || !projectId) {
      return
    }
    onCreate({
      projectId,
      title: trimmed,
      app,
      priority,
      brief: brief.trim() || undefined,
    })
    reset()
  }

  return (
    <FormDialog
      open={open}
      title="New task"
      description="Create a manual intake item and queue it for the orchestrator."
      submitLabel="Create & queue"
      busy={busy}
      // `disabled` stays for *busy* and *invalid*. Having no project to choose
      // is neither — it is the denial itself, and `denied` is what keeps the
      // sentence reachable: a disabled control fires no pointer events, so its
      // explanation never arrives at a pointer and leaves the tab order too.
      submitDisabled={!title.trim() || !app}
      denied={create.denial}
      onSubmit={submit}
      onCancel={close}
    >
      <TextField
        id="task-title"
        label="Title"
        autoFocus
        value={title}
        onValueChange={setTitle}
        placeholder="what to do, in one line"
        disabled={busy}
      />

      <SelectField
        id="task-project"
        label="Project"
        value={projectId}
        onValueChange={setProjectId}
        options={projects.map((entry) => ({
          value: entry.id,
          label: entry.key,
        }))}
        disabled={busy || projects.length === 0}
      />

      <SelectField
        id="task-app"
        label="App"
        value={app}
        onValueChange={setApp}
        options={apps.map((item) => ({ value: item, label: item }))}
        disabled={busy || apps.length === 0}
      />

      <SelectField
        id="task-priority"
        label="Priority"
        value={priority}
        onValueChange={(next) => setPriority(next as TaskPriority)}
        options={PRIORITIES}
        disabled={busy}
      />

      <TextareaField
        id="task-brief"
        label="Brief"
        // A brief is something a person wrote for another person to read, so it
        // takes the interface voice rather than the data one.
        voice="prose"
        value={brief}
        onValueChange={setBrief}
        placeholder="context, acceptance criteria, links…"
        disabled={busy}
      />
    </FormDialog>
  )
}

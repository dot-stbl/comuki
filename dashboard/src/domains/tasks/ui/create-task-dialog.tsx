import { useState, type FormEvent } from "react"
import { Plus } from "lucide-react"

import type {
  CreateTaskInput,
  TaskPriority,
} from "@/domains/tasks/model/types"
import { Button } from "@/shared/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/dialog"
import { Input } from "@/shared/ui/input"
import { Label } from "@/shared/ui/label"
import {
  NativeSelect,
  NativeSelectOption,
} from "@/shared/ui/native-select"
import { Textarea } from "@/shared/ui/textarea"

const PRIORITIES: TaskPriority[] = ["low", "normal", "high"]

export interface CreateTaskDialogProps {
  open: boolean
  apps: string[]
  busy?: boolean
  onOpenChange: (open: boolean) => void
  onCreate: (input: CreateTaskInput) => void
}

export function CreateTaskDialog({
  open,
  apps,
  busy = false,
  onOpenChange,
  onCreate,
}: CreateTaskDialogProps) {
  const [title, setTitle] = useState("")
  const [app, setApp] = useState(apps[0] ?? "")
  const [priority, setPriority] = useState<TaskPriority>("normal")
  const [brief, setBrief] = useState("")

  const reset = () => {
    setTitle("")
    setApp(apps[0] ?? "")
    setPriority("normal")
    setBrief("")
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const trimmed = title.trim()
    if (!trimmed || !app) {
      return
    }
    onCreate({
      title: trimmed,
      app,
      priority,
      brief: brief.trim() || undefined,
    })
    reset()
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          reset()
        }
        onOpenChange(next)
      }}
    >
      <DialogContent className="sm:max-w-md" showCloseButton>
        <form onSubmit={submit} className="grid gap-4">
          <DialogHeader>
            <DialogTitle>New task</DialogTitle>
            <DialogDescription>
              Create a manual intake item and queue it for the orchestrator.
            </DialogDescription>
          </DialogHeader>

          <div className="grid gap-3">
            <div className="grid gap-1.5">
              <Label htmlFor="task-title">Title</Label>
              <Input
                id="task-title"
                autoFocus
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                placeholder="Что сделать — кратко"
                disabled={busy}
              />
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="grid gap-1.5">
                <Label htmlFor="task-app">App</Label>
                <NativeSelect
                  id="task-app"
                  className="w-full"
                  value={app}
                  onChange={(event) => setApp(event.target.value)}
                  disabled={busy || apps.length === 0}
                >
                  {apps.map((item) => (
                    <NativeSelectOption key={item} value={item}>
                      {item}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
              </div>

              <div className="grid gap-1.5">
                <Label>Priority</Label>
                <div className="inline-flex rounded-md border border-border p-0.5">
                  {PRIORITIES.map((item) => (
                    <Button
                      key={item}
                      type="button"
                      size="sm"
                      variant={priority === item ? "secondary" : "ghost"}
                      aria-pressed={priority === item}
                      disabled={busy}
                      className="flex-1 capitalize"
                      onClick={() => setPriority(item)}
                    >
                      {item}
                    </Button>
                  ))}
                </div>
              </div>
            </div>

            <div className="grid gap-1.5">
              <Label htmlFor="task-brief">Brief</Label>
              <Textarea
                id="task-brief"
                value={brief}
                onChange={(event) => setBrief(event.target.value)}
                placeholder="Контекст, критерии приёмки, ссылки…"
                disabled={busy}
              />
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={busy}
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              size="sm"
              disabled={busy || !title.trim() || !app}
            >
              <Plus />
              Create &amp; queue
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

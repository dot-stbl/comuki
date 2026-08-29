import { Info, Plus, RotateCcw } from "lucide-react"
import { toast } from "sonner"

import type { TrackerProvider } from "@/domains/settings/model/types"
import { cn } from "@/shared/lib/utils"
import { Alert, AlertDescription, AlertTitle } from "@/shared/ui/alert"
import { Button } from "@/shared/ui/button"

export interface TrackerPanelProps {
  trackers: TrackerProvider[]
}

export function TrackerPanel({ trackers }: TrackerPanelProps) {
  return (
    <div className="flex flex-col gap-3">
      <Alert>
        <Info />
        <AlertTitle>Intake sources</AlertTitle>
        <AlertDescription>
          Подключённый трекер синкает тикеты в backlog · ручной ввод доступен
          всегда.
        </AlertDescription>
      </Alert>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {trackers.map((provider) => (
          <div
            key={provider.id}
            className={cn(
              "flex flex-col gap-3 rounded-lg border border-border bg-card p-3",
              provider.connected && "ring-1 ring-primary/30"
            )}
          >
            <div className="flex items-center gap-2">
              <span className="font-medium text-sm">{provider.name}</span>
              {provider.connected ? (
                <span
                  className="size-1.5 rounded-full bg-st-success"
                  title="connected"
                />
              ) : null}
            </div>
            <p className="font-mono text-xs text-muted-foreground">
              {provider.meta}
            </p>
            <div className="mt-auto flex items-center justify-between gap-2">
              {provider.connected ? (
                <>
                  <span className="font-mono text-[10px] text-muted-foreground">
                    synced {provider.last}
                  </span>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() =>
                      toast.message(`Synced ${provider.name}`, {
                        description: "imported new issues",
                      })
                    }
                  >
                    <RotateCcw />
                    Sync
                  </Button>
                </>
              ) : (
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  onClick={() =>
                    toast.message(`Connect ${provider.name}`, {
                      description: "OAuth flow…",
                    })
                  }
                >
                  <Plus />
                  Connect
                </Button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

import type { ReactNode } from "react"
import {
  BarChart3,
  Cpu,
  DollarSign,
  FileCode2,
  Server,
} from "lucide-react"

import type {
  RunStage,
  StageInspector as StageInspectorModel,
} from "@/domains/runs/model/types"
import { cn } from "@/shared/lib/utils"
import { Badge } from "@/shared/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card"
import { Separator } from "@/shared/ui/separator"
import { StatusBadge } from "@/shared/ui/status-badge"

export interface StageInspectorProps {
  stage: RunStage
  index: number
  total: number
  info: StageInspectorModel
}

export function StageInspectorPanel({
  stage,
  index,
  total,
  info,
}: StageInspectorProps) {
  return (
    <div className="grid gap-3 lg:grid-cols-2">
      <Card size="sm">
        <CardHeader className="border-b">
          <CardTitle className="flex items-center justify-between gap-2">
            <span>Stage log · {stage.label}</span>
            <span className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
              append-only
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 pt-3">
          {info.events.map((event, eventIndex) => (
            <div
              key={`${event.time}-${eventIndex}`}
              className="grid grid-cols-[3.5rem_1fr] gap-2 font-mono text-xs"
            >
              <span className="text-muted-foreground tabular-nums">
                {event.time}
              </span>
              <span
                className={cn(
                  "text-foreground",
                  event.status === "running" && "text-st-running",
                  event.status === "failed" && "text-st-failed",
                  event.status === "waiting" && "text-st-waiting"
                )}
              >
                {event.text}
              </span>
            </div>
          ))}
        </CardContent>
      </Card>

      <Card size="sm">
        <CardHeader className="border-b">
          <CardTitle className="flex items-center justify-between gap-2">
            <span>
              {stage.label} · stage {index}/{total}
            </span>
            <StatusBadge status={stage.status} size="sm" />
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 pt-3">
          <div className="flex flex-wrap gap-2">
            <MetaChip icon={Cpu} label="model" value={info.role} />
            <MetaChip icon={BarChart3} label="tok" value={info.tokens} />
            <MetaChip icon={DollarSign} label="cost" value={`$${info.cost}`} />
            <MetaChip icon={Server} label="env" value={info.env} />
          </div>

          <Section title="Input — what fed it">
            <div className="flex flex-wrap gap-1.5">
              {info.inputs.map((item) => (
                <Badge key={`${item.label}-${item.detail ?? ""}`} variant="outline">
                  {item.label}
                  {item.detail ? (
                    <span className="text-muted-foreground"> · {item.detail}</span>
                  ) : null}
                </Badge>
              ))}
            </div>
          </Section>

          {info.gate ? (
            <Section title="Verification gate">
              <div className="flex flex-wrap gap-1.5">
                {info.gate.map((check) => (
                  <StatusBadge key={check.name} status={check.status} size="sm">
                    {check.name}
                  </StatusBadge>
                ))}
              </div>
            </Section>
          ) : null}

          <Section title="Output — what it produced">
            {info.files ? (
              <div className="flex flex-col gap-2">
                {info.files.map((file) => (
                  <div
                    key={file.path}
                    className="rounded-md border border-border bg-muted/20"
                  >
                    <div className="flex items-center gap-2 border-b border-border px-2 py-1.5 font-mono text-xs">
                      <FileCode2 className="size-3.5 text-muted-foreground" />
                      <span className="flex-1 truncate">{file.path}</span>
                      <span className="text-st-success">+{file.added}</span>
                      <span className="text-st-failed">−{file.deleted}</span>
                    </div>
                    <pre className="max-h-48 overflow-auto p-2 font-mono text-[11px] leading-relaxed">
                      {file.lines.map((line, lineIndex) => (
                        <div
                          key={`${file.path}-${lineIndex}`}
                          className={cn(
                            "grid grid-cols-[2rem_1rem_1fr] gap-2",
                            line.kind === "add" && "bg-st-success/10 text-st-success",
                            line.kind === "del" && "bg-st-failed/10 text-st-failed"
                          )}
                        >
                          <span className="text-muted-foreground tabular-nums">
                            {line.line}
                          </span>
                          <span>
                            {line.kind === "add"
                              ? "+"
                              : line.kind === "del"
                                ? "−"
                                : " "}
                          </span>
                          <span>{line.text}</span>
                        </div>
                      ))}
                    </pre>
                  </div>
                ))}
              </div>
            ) : (
              <div className="flex flex-col gap-1.5">
                {info.outputs.map((item) => (
                  <div
                    key={`${item.label}-${item.detail ?? ""}`}
                    className="flex items-center gap-2 rounded-md border border-border px-2 py-1.5 text-xs"
                  >
                    <span className="font-medium">{item.label}</span>
                    {item.detail ? (
                      <span className="text-muted-foreground">{item.detail}</span>
                    ) : null}
                  </div>
                ))}
              </div>
            )}
          </Section>
        </CardContent>
      </Card>
    </div>
  )
}

function MetaChip({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof Cpu
  label: string
  value: string
}) {
  return (
    <span className="inline-flex items-center gap-1 rounded-md border border-border bg-muted/30 px-2 py-1 font-mono text-xs">
      <Icon className="size-3 text-muted-foreground" />
      <span className="text-muted-foreground">{label}</span>
      <b className="font-semibold text-foreground">{value}</b>
    </span>
  )
}

function Section({
  title,
  children,
}: {
  title: string
  children: ReactNode
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
        {title}
      </div>
      <Separator />
      {children}
    </div>
  )
}

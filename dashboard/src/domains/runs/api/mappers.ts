import type {
  DiffFile,
  GateCheck,
  RunDetail,
  RunSummary,
  TraceEvent,
  WorkItem,
  WorkItemInspector,
} from "@/domains/runs/model/types"
import {
  PROFILE_META,
  TRACE_SEED,
  type SeedDiffFile,
  type SeedProfile,
  type SeedRun,
  type SeedStatus,
  type SeedTrace,
  type SeedWorkItem,
} from "@/shared/api/mock"

function mapStatus(status: SeedStatus) {
  return status
}

function mapWorkItem(entry: SeedWorkItem): WorkItem {
  return {
    id: entry.id,
    profile: entry.profile,
    label: entry.label,
    status: mapStatus(entry.status),
    dependsOn: entry.deps,
    cost: entry.cost,
    tokens: entry.tokens,
    startedAt: entry.startedAt,
  }
}

function mapDiff(files: SeedDiffFile[]): DiffFile[] {
  return files.map((file) => ({
    path: file.file,
    added: file.add,
    deleted: file.del,
    lines: file.lines.map((line) => ({
      kind: line.ty,
      line: line.n,
      text: line.text,
    })),
  }))
}

function genericTrace(run: SeedRun): SeedTrace {
  const events: SeedTrace["events"] = []
  run.items.forEach((entry, index) => {
    if (entry.status === "queued") {
      return
    }
    const minutes = String(Math.floor(index * 0.9)).padStart(2, "0")
    const seconds = String((index * 17) % 60).padStart(2, "0")
    events.push({
      t: entry.startedAt ?? `${minutes}:${seconds}`,
      st: entry.status,
      text: `«${entry.label}» — ${entry.status}`,
    })
  })

  return {
    brief: `${run.title}. Worker brief for the current work item.`,
    rules: ["api-errors", "db-tx"],
    revision: { rules: "rules@a1b9e0", sdk: "sdk@2.4.1" },
    events,
    diff: [
      {
        file: "src/changes.ts",
        add: 8,
        del: 2,
        lines: [
          { ty: "ctx", n: "1", text: `// ${run.title}` },
          { ty: "add", n: "2", text: "+ implementation" },
          { ty: "del", n: "3", text: "- old code" },
        ],
      },
    ],
    tests: [
      { name: "types", st: "success", detail: "ok" },
      { name: "lint", st: "success", detail: "ok" },
      {
        name: "unit",
        st: run.status === "failed" ? "failed" : "success",
        detail: run.status === "failed" ? "2 failed" : "ok",
      },
      { name: "e2e", st: "queued", detail: "waiting" },
      { name: "visual", st: "queued", detail: "waiting" },
    ],
  }
}

function mapEvents(events: SeedTrace["events"]): TraceEvent[] {
  return events.map((event) => ({
    time: event.t,
    status: mapStatus(event.st),
    text: event.text,
  }))
}

export function toRunSummary(seed: SeedRun): RunSummary {
  return {
    id: seed.id,
    projectId: seed.projectId,
    app: seed.app,
    title: seed.title,
    status: mapStatus(seed.status),
    current: seed.current,
    model: seed.model,
    cost: seed.cost,
    tokens: seed.tokens,
    durationSec: seed.startSec,
    done: seed.done ?? false,
    workItems: seed.items.map(mapWorkItem),
  }
}

export function toRunDetail(seed: SeedRun): RunDetail {
  const trace = TRACE_SEED[seed.id] ?? genericTrace(seed)
  return {
    ...toRunSummary(seed),
    brief: trace.brief,
    rules: trace.rules,
    revision: trace.revision,
    events: mapEvents(trace.events),
  }
}

/**
 * The inspector for one work item. What it is handed and what it leaves behind
 * comes from its **profile** — that is the part of a step that is declared and
 * knowable. The step's name is the brain's, and never keys anything here.
 */
export function toWorkItemInspector(
  seed: SeedRun,
  itemId: string
): WorkItemInspector {
  const entry = seed.items.find((candidate) => candidate.id === itemId)
  const profile: SeedProfile = entry?.profile ?? "implementer"
  const meta = PROFILE_META[profile]
  const trace = TRACE_SEED[seed.id] ?? genericTrace(seed)
  const status = entry?.status ?? "queued"
  const active = status === "running" || status === "escalated"
  const env =
    status === "queued"
      ? "—"
      : active
        ? `env_${seed.id.slice(0, 4)}`
        : "env (recycled)"
  const tokens =
    status === "queued"
      ? "0"
      : `${((entry?.tokens ?? seed.tokens) / 1000).toFixed(1)}k`
  const cost =
    status === "queued" ? "0.00" : (entry?.cost ?? seed.cost).toFixed(2)

  let gate: GateCheck[] | null = null
  if (meta.gate) {
    if (status === "queued") {
      gate = ["types", "lint", "unit", "build"].map((name) => ({
        name,
        status: "queued" as const,
      }))
    } else {
      const base = meta.gate === "full" ? trace.tests : trace.tests.slice(0, 2)
      gate = base.map((test) => ({
        name: test.name,
        status: status === "success" ? "success" : mapStatus(test.st),
      }))
    }
  }

  const events: TraceEvent[] = []
  if (status === "queued") {
    events.push({ time: "—", text: "queued — not started", status: "queued" })
  } else {
    events.push({
      time: entry?.startedAt ?? "00:00",
      text: `container up · ${env}`,
      status: "success",
    })
    events.push({
      time: "00:04",
      text: `pinned ${trace.revision.rules} · ${trace.revision.sdk}`,
      status: "success",
    })
    meta.ev.forEach((line, index) => {
      events.push({
        time: `00:${String(12 + index * 9).padStart(2, "0")}`,
        text: line,
        status: "success",
      })
    })
    if (active) {
      events.push({
        time: "01:00",
        text: meta.live ?? "running…",
        status: "running",
      })
    } else if (status === "success") {
      events.push({
        time: "01:00",
        text: "work item complete",
        status: "success",
      })
    } else if (status === "failed") {
      events.push({
        time: "01:00",
        text: "gate failed — escalated to debug profile",
        status: "failed",
      })
    } else if (status === "waiting") {
      events.push({
        time: "—",
        text: "waiting for human gate",
        status: "waiting",
      })
    }
  }

  const outDiff = meta.out === "diff"
  return {
    role: meta.role,
    env,
    tokens,
    cost,
    inputs: meta.in.map(([icon, label, detail]) => ({ icon, label, detail })),
    outputs: outDiff
      ? []
      : (meta.out as Array<[string, string, string?]>).map(
          ([icon, label, detail]) => ({ icon, label, detail })
        ),
    files: outDiff ? mapDiff(trace.diff) : null,
    gate,
    events,
  }
}

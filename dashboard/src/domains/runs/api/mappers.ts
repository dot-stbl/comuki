import type {
  DiffFile,
  GateCheck,
  RunDetail,
  RunStage,
  RunSummary,
  StageInspector,
  TraceEvent,
} from "@/domains/runs/model/types"
import {
  STAGE_META,
  TRACE_SEED,
  type SeedDiffFile,
  type SeedRun,
  type SeedStage,
  type SeedStatus,
  type SeedTrace,
} from "@/shared/api/mock"

function mapStatus(status: SeedStatus) {
  return status
}

function mapStage(stage: SeedStage): RunStage {
  return {
    key: stage.key,
    label: stage.label,
    status: mapStatus(stage.status),
    lane: stage.lane,
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
  run.stages.forEach((stage, index) => {
    if (stage.status === "queued") {
      return
    }
    const minutes = String(Math.floor(index * 0.9)).padStart(2, "0")
    const seconds = String((index * 17) % 60).padStart(2, "0")
    events.push({
      t: `${minutes}:${seconds}`,
      st: stage.status,
      text: `Stage «${stage.label}» — ${stage.status}`,
    })
  })

  return {
    brief: `${run.title}. Worker brief for the current stage.`,
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

function mapEvents(
  events: SeedTrace["events"]
): TraceEvent[] {
  return events.map((event) => ({
    time: event.t,
    status: mapStatus(event.st),
    text: event.text,
  }))
}

export function toRunSummary(seed: SeedRun): RunSummary {
  return {
    id: seed.id,
    app: seed.app,
    title: seed.title,
    status: mapStatus(seed.status),
    current: seed.current,
    model: seed.model,
    cost: seed.cost,
    tokens: seed.tokens,
    durationSec: seed.startSec,
    done: seed.done ?? false,
    stages: seed.stages.map(mapStage),
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

export function toStageInspector(
  seed: SeedRun,
  stageKey: string
): StageInspector {
  const stage =
    seed.stages.find((item) => item.key === stageKey) ??
    ({ key: stageKey, label: stageKey, status: "queued" } as SeedStage)
  const meta = STAGE_META[stageKey] ?? {
    role: "worker" as const,
    in: [["box", "upstream output"]],
    out: [["box", "output"]] as Array<[string, string, string?]>,
    ev: ["stage work"],
  }
  const trace = TRACE_SEED[seed.id] ?? genericTrace(seed)
  const status = stage.status
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
      : active
        ? `${(seed.tokens / 1000).toFixed(1)}k`
        : `${3 + stageKey.length}.0k`
  const cost =
    status === "queued"
      ? "0.00"
      : active
        ? seed.cost.toFixed(2)
        : (0.05 + stageKey.length * 0.02).toFixed(2)

  let gate: GateCheck[] | null = null
  if (meta.gate) {
    if (status === "queued") {
      gate = ["types", "lint", "unit", "build"].map((name) => ({
        name,
        status: "queued" as const,
      }))
    } else {
      const base =
        meta.gate === "full" ? trace.tests : trace.tests.slice(0, 2)
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
      time: "00:00",
      text: `container up · ${env}`,
      status: "success",
    })
    events.push({
      time: "00:04",
      text: `pinned ${trace.revision.rules} · ${trace.revision.sdk}`,
      status: "success",
    })
    ;(meta.ev ?? ["stage work"]).forEach((line, index) => {
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
      events.push({ time: "01:00", text: "stage complete", status: "success" })
    } else if (status === "failed") {
      events.push({
        time: "01:00",
        text: "gate failed — escalated to debug-agent",
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
    inputs: (meta.in ?? []).map(([icon, label, detail]) => ({
      icon,
      label,
      detail,
    })),
    outputs: outDiff
      ? []
      : ((meta.out as Array<[string, string, string?]>) ?? []).map(
          ([icon, label, detail]) => ({ icon, label, detail })
        ),
    files: outDiff ? mapDiff(trace.diff) : null,
    gate,
    events,
  }
}

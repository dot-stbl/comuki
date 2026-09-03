import type {
  ArtifactPointer,
  DiffFile,
  GateCheck,
  RunArtifacts,
  RunDetail,
  RunSummary,
  TraceEvent,
  WorkItem,
  WorkItemInspector,
} from "@/domains/runs/model/types"
import type { RunArtifactsPage as RunArtifactsPageDto } from "@/shared/api/_generated/types/RunArtifactsPage"
import type { RunView } from "@/shared/api/_generated/types/RunView"
import type {
  ArtifactPointer as ArtifactPointerDto,
} from "@/shared/api/_generated/types/ArtifactPointer"
import type { RunsPage } from "@/shared/api/_generated/types/RunsPage"
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

// ---------------------------------------------------------------------------
// kubb wire → domain mappers.
//
// The host returns a sparse row (`RunView`: id, projectId, status, createdAt,
// updatedAt) — not the full SeedRun fixture the mock-first mappers above
// produce. The real screen will still show many of the same fields (current,
// app, cost, tokens, plan); we cannot fabricate them from the wire, so the
// mapping fills in only what RunView actually carries and leaves the rest to
// defaults the screen can render without crashing.
//
// Defaults are picked to keep an empty card legible, not to be plausible:
// - `status` carries over as-is (`queued`, `running`, …).
// - `app`, `title`, `model` are unknown until a detail endpoint lands — empty
//   string. The screen's header falls back to the run id.
// - `current` is the empty work item id `""`; downstream readers short-circuit
//   on the empty string (`currentItem`, `planGraph`).
// - `done` is `false` while the run is in flight; only terminal runs set it.
// - `cost` / `tokens` are 0; formatting helpers already render zero cleanly.
// - `durationSec` is the up-to-now delta on `updatedAt - createdAt`. The real
//   list doesn't expose elapsed time otherwise; this is the closest the wire
//   gets us without the detail endpoint.
// - `workItems` is `[]`. The graph would otherwise paint a phantom plan.
// ---------------------------------------------------------------------------

const EMPTY_WORK_ITEMS: WorkItem[] = []

/**
 * Wire row → domain summary.
 *
 * The screen's render of `done` and the live "duration" sits between two
 * timestamps the host already gives us (`createdAt` / `updatedAt`); for the
 * list view, "duration so far" is that delta, in seconds. A detail endpoint
 * (out of scope this slice) would replace these defaults with planner output.
 */
export function mapRunViewToSummary(view: RunView): RunSummary {
  const durationSec = Math.max(
    0,
    Math.round((Date.parse(view.updatedAt) - Date.parse(view.createdAt)) / 1000),
  )
  return {
    id: view.id,
    projectId: view.projectId,
    app: "",
    title: "",
    status: view.status as RunSummary["status"],
    current: "",
    model: "worker",
    cost: 0,
    tokens: 0,
    durationSec,
    done: view.status === "succeeded" || view.status === "failed" || view.status === "cancelled",
    workItems: EMPTY_WORK_ITEMS,
  }
}

/**
 * Wire page → list of domain summaries. The wire carries `page` / `pageSize`
 * / `total`, which the domain shape drops — pagination lives on the screen
 * (TanStack) and the totals are a derived header string.
 */
export function mapRunsPageToSummaries(page: RunsPage): RunSummary[] {
  return page.items.map(mapRunViewToSummary)
}

/**
 * Wire row → domain detail.
 *
 * RunView does not carry the bits a RunDetail needs (`brief`, `rules`,
 * `revision`, `events`). The screen already renders a sparse summary;
 * rendering an empty detail is no worse than not loading one at all.
 */
export function mapRunViewToDetail(view: RunView): RunDetail {
  return {
    ...mapRunViewToSummary(view),
    brief: "",
    rules: [],
    revision: { rules: "", sdk: "" },
    events: [],
  }
}

/**
 * Wire row → `ArtifactPointer` domain item.
 *
 * The kubb `ArtifactPointer.uri` is `string`; the domain type carries `URL`
 * so callers can `.href` it. If the URI is malformed (it never should be —
 * the host writes canonical signed URLs from MinIO), the row is dropped and a
 * console warning is logged; the screen keeps a partial list rather than
 * throwing on bad wire data.
 */
function mapArtifactPointer(entry: ArtifactPointerDto): ArtifactPointer | null {
  try {
    return {
      name: entry.name,
      uri: new URL(entry.uri),
      size: typeof entry.size === "string" ? Number.parseInt(entry.size, 10) : entry.size,
      contentType: entry.contentType,
    }
  } catch (error) {
    if (typeof console !== "undefined") {
      console.warn("[runs] dropping artifact with malformed URI", entry.name, error)
    }
    return null
  }
}

/**
 * Wire page → domain run-artifacts page. Empty list when the run has not
 * been packaged yet — exactly what the host returns.
 */
export function mapRunArtifactsPageToArtifacts(
  page: RunArtifactsPageDto,
): RunArtifacts {
  const items = page.items
    .map(mapArtifactPointer)
    .filter((entry): entry is ArtifactPointer => entry !== null)
  return {
    projectId: page.projectId,
    runId: page.runId,
    items,
  }
}

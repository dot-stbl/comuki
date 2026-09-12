import type {
  ArtifactPointer,
  DiffFile,
  GateCheck,
  RunArtifacts,
  RunDetail,
  RunStatus,
  RunSummary,
  TraceEvent,
  WorkItem,
  WorkItemInspector,
} from "@/domains/runs/model/types"
import type { RunArtifactsPage as RunArtifactsPageDto } from "@/shared/api/_generated/types/RunArtifactsPage"
import type { RunDetail as RunDetailDto } from "@/shared/api/_generated/types/RunDetail"
import type { RunDetailEvent as RunDetailEventDto } from "@/shared/api/_generated/types/RunDetailEvent"
import type { RunDetailWorkItem as RunDetailWorkItemDto } from "@/shared/api/_generated/types/RunDetailWorkItem"
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

/* ---------------------------------------------------------------------------
 * Wire status → domain status.
 *
 * `RunStatus` is the design system's seven words and nothing else (see
 * `shared/ui/status-badge.tsx` and the Real Words Rule in `DESIGN.md`). The
 * host's vocabulary still differs in one place — `succeeded` is a real value
 * on `RunView.status` / `RunDetail.status`, both of which are a plain `string`
 * on the wire — so the two vocabularies have to be mapped, not asserted.
 *
 * A cast did not map them, it only silenced the compiler, and the lie landed
 * at runtime in two places: `statusIcons[status]` in `StatusBadge` came back
 * `undefined` and React threw "Element type is invalid" on the first completed
 * run in real mode, and `TRIAGE_RANK[status]` came back `NaN`, which quietly
 * unsorted the duty list instead of crashing it.
 * ------------------------------------------------------------------------- */

/** Closed set of the seven words the design system has; see <see cref="RunStatus"/>. */
const KNOWN_RUN_STATUSES: ReadonlySet<string> = new Set<RunStatus>([
  "running",
  "success",
  "failed",
  "waiting",
  "queued",
  "escalated",
  "cancelled",
])

/**
 * Narrow a wire word to the domain union. A predicate rather than a cast, so
 * the set above is the only place the six words are written down.
 */
function isRunStatus(value: string): value is RunStatus {
  return KNOWN_RUN_STATUSES.has(value)
}

/**
 * The word an unmapped wire status degrades to.
 *
 * `failed` is the least misleading of the seven for a status the screen cannot
 * read. It is the only remaining word that is both **terminal** and **not a
 * success**, which is the pair of facts every unreadable status shares: the
 * three live words (`running`, `queued`, `waiting`) would promise the operator
 * that a finished run is still moving, `escalated` would put phantom work at
 * the top of the duty list claiming a human is blocking it, `success` would
 * tell them work landed that did not, and `cancelled` would claim somebody
 * chose this. `failed` overstates *why* and understates nothing — the
 * conservative direction for a duty screen.
 */
const UNKNOWN_RUN_STATUS: RunStatus = "failed"

/**
 * Normalise the wire `status` string to the domain union.
 *
 * One known wire word has no design-system counterpart: `succeeded` →
 * `success`, the same fact in the product's own spelling.
 *
 * `cancelled` used to be a second one. It was folded onto `failed` because the
 * design system had no word for "an operator stopped this on purpose", and
 * inventing a seventh status is a `DESIGN.md` decision (a hue *and* a hatch —
 * the Two-Channel Status Rule), not a mapper's. That decision has since been
 * made: `cancelled` is a status of its own, with its own rung on every
 * palette's ladder and the sparsest hatch in the set, and it arrives here
 * unchanged. A board that cancels runs routinely no longer paints itself red
 * for work nobody failed at.
 *
 * Anything else falls through to the fallback above rather than throwing — the
 * host may have rolled out a status the FE has not been taught. A partial
 * backend rollout should degrade the row, not take down the screen.
 */
export function normalizeRunStatus(value: string): RunStatus {
  if (value === "succeeded") {
    return "success"
  }
  if (isRunStatus(value)) {
    return value
  }
  return UNKNOWN_RUN_STATUS
}

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
    status: normalizeRunStatus(view.status),
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
 * Wire RunDetail → domain RunDetail.
 *
 * The detail endpoint (issue #S7) carries the work-item graph, the recent
 * journal strip, the pinned revisions, and the brief — the four pieces
 * the detail page was built around and that the list endpoint could not
 * supply. Cost / tokens / `title` populate from the wire when the host
 * has them; `current` is the empty work-item id (the screen's plan readers
 * short-circuit on `""` when there is no standing item, e.g. terminal runs).
 */
export function mapRunDetailToDetail(detail: RunDetailDto): RunDetail {
  return {
    ...mapRunDetailToSummary(detail),
    brief: detail.brief,
    rules: detail.rules,
    revision: {
      rules: detail.revision.rules,
      sdk: detail.revision.sdk,
    },
    events: detail.events.map(mapRunDetailEventToTraceEvent),
  };
}

/**
 * Wire detail row → domain summary. The summary needs the same sparse shape
 * as the list row's mapper, but the detail wire actually carries
 * `title` / `app` / `model` / `costUsd` / `tokens`, so we read them.
 */
function mapRunDetailToSummary(detail: RunDetailDto): RunSummary {
  const durationSec = Math.max(
    0,
    Math.round((Date.parse(detail.updatedAt) - Date.parse(detail.createdAt)) / 1000),
  );

  return {
    id: detail.id,
    projectId: detail.projectId,
    app: detail.app,
    title: detail.title,
    status: normalizeRunStatus(detail.status),
    current: detail.workItems[0]?.id ?? "",
    model: detail.model === "lead" ? "lead" : "worker",
    cost: toNumber(detail.costUsd),
    tokens: toNumber(detail.tokens),
    durationSec,
    done:
      detail.status === "succeeded" ||
      detail.status === "failed" ||
      detail.status === "cancelled",
    workItems: detail.workItems.map(mapRunDetailWorkItemToDomain),
  };
}

/**
 * Wire RunDetailWorkItem → domain WorkItem. The wire shape carries the
 * same field set as the domain (id / profile / label / status /
 * dependsOn / cost / tokens / startedAt); the only coercion is
 * `cost` / `tokens` from the wire's loose `number | string` to a real
 * `number`, and `startedAt` from the wire's `string | null` to the
 * domain's optional string.
 */
function mapRunDetailWorkItemToDomain(
  entry: RunDetailWorkItemDto,
): WorkItem {
  return {
    id: entry.id,
    profile: entry.profile,
    label: entry.label,
    status: normalizeRunStatus(entry.status),
    dependsOn: entry.dependsOn,
    cost: toNumber(entry.cost),
    tokens: toNumber(entry.tokens),
    startedAt: entry.startedAt ?? undefined,
  };
}

/**
 * Wire RunDetailEvent → domain TraceEvent.
 *
 * The wire carries the journal's raw shape — `type` (`run.status_changed`,
 * `work_item.status_changed`, …), `occurredAt` (ISO) and `payloadJson` (raw
 * JSON whose shape is per-type). The domain TraceEvent is what the screen
 * renders as a timeline row: `time` (`HH:MM`), `status` (a RunStatus),
 * `text` (a one-line summary).
 *
 * The wire is read-only: the screen never asked for a JSON-tree view of
 * the payload. We parse the minimum we can render — a status hint from
 * the payload when one is present, the type as the textual label, and the
 * time formatted as `HH:MM` so the timeline reads like the rest of the
 * page. When the payload is missing or malformed we fall back to a
 * neutral reading; never throw on bad wire data.
 */
function mapRunDetailEventToTraceEvent(
  entry: RunDetailEventDto,
): TraceEvent {
  const occurredAt = new Date(entry.occurredAt);
  const time = isNaN(occurredAt.getTime())
    ? "—"
    : `${String(occurredAt.getUTCHours()).padStart(2, "0")}:${String(occurredAt.getUTCMinutes()).padStart(2, "0")}`;

  const parsedStatus = readStatusFromPayload(entry.payloadJson);
  const text = parsedStatus.summary ?? entry.type;

  return {
    time,
    status: parsedStatus.status ?? "running",
    text,
  };
}

/**
 * kubb emits JSON-stringified numbers as `number | string` so they can
 * round-trip `int64` / `double` without losing precision on big values.
 * The domain types carry plain `number`; this helper normalises both
 * branches to a finite number (or 0 when the wire sent nothing).
 */
function toNumber(value: number | string): number {
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : 0;
  }
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Minimal payload-reader for status / summary hints.
 *
 * The journal payload is open-ended per type; we extract the bits the
 * screen already knows how to render — `to` / `from` for status, the
 * event type as a textual fallback. Anything we cannot parse is dropped
 * silently: the screen's TraceEvent readers treat the default ("running")
 * status and a free-text `text` as valid.
 */
function readStatusFromPayload(
  payloadJson: string | null,
): { status: TraceEvent["status"] | null; summary: string | null } {
  if (!payloadJson) {
    return { status: null, summary: null };
  }
  try {
    const parsed: unknown = JSON.parse(payloadJson);
    if (parsed === null || typeof parsed !== "object") {
      return { status: null, summary: null };
    }
    const record = parsed as Record<string, unknown>;
    const candidate = typeof record["to"] === "string"
      ? (record["to"] as string).toLowerCase()
      : null;
    const status =
      candidate !== null && isWireRunStatus(candidate)
        ? normalizeRunStatus(candidate)
        : null;
    const summary = typeof record["type"] === "string"
      ? (record["type"] as string)
      : null;
    return { status, summary };
  } catch {
    return { status: null, summary: null };
  }
}

/**
 * The host's run lifecycle, verbatim — **seven** words, not the domain's six.
 *
 * This used to be spelled `value is TraceEvent["status"]`, and the signature
 * was a lie: it admitted `succeeded` and `cancelled`, which are not members of
 * that union, and handed them to a `TraceEvent` whose badge then had no icon
 * to draw. The predicate asks the only question it can honestly answer — "is
 * this a word the wire's lifecycle uses?" — and `normalizeRunStatus` is what
 * turns the answer into one of the product's six. A payload word that is in
 * neither vocabulary stays `null`, so the event keeps its neutral default
 * rather than being painted as a failure by the unknown-status fallback.
 */
const WIRE_RUN_STATUSES: ReadonlySet<string> = new Set([
  "queued",
  "running",
  "waiting",
  "escalated",
  "succeeded",
  "failed",
  "cancelled",
]);

function isWireRunStatus(value: string): boolean {
  return WIRE_RUN_STATUSES.has(value);
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

import { NATIVE_PROVIDER } from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import type { Task, TaskStatus } from "@/domains/tasks/model/types"
import type { IntakeTicketView } from "@/shared/api/_generated/types/IntakeTicketView"
import type { SeedTask } from "@/shared/api/mock/tasks.seed"

// ---------------------------------------------------------------------------
// Mock-first mappers (unchanged from pre-wire behaviour).
//
// The operator's `dev:mock` flow and the Storybook need the same shape
// `useTasksQuery` returns, so the mock path maps a `SeedTask` directly. No
// domain shape drift between mock and real modes — the screen branches on
// `env.useMock`, not on the return type.
// ---------------------------------------------------------------------------

/**
 * The mock host's word for the product's own intake is `manual`; the domain's
 * — and the real wire's — is `native`.
 *
 * Translating it here is what a seam is for, and it is the same job
 * `sourceConnectionViewToConnection` does on the other domain's wire: the
 * store on the far side speaks its own vocabulary, and exactly one function
 * knows both. Every other provider word passes through untouched, including
 * one the registry has never seen — the badge spells it.
 *
 * This entry is the mock seed's legacy spelling and nothing deeper. When
 * `tasks.seed.ts` is next touched it should say `native` like everything
 * else, and this map should go with it.
 */
const SEED_SOURCE_ALIASES: Record<string, ProviderKey> = {
  manual: NATIVE_PROVIDER,
}

export function toTask(seed: SeedTask): Task {
  return {
    id: seed.id,
    projectId: seed.projectId,
    source: SEED_SOURCE_ALIASES[seed.source] ?? seed.source,
    title: seed.title,
    app: seed.app,
    priority: seed.priority,
    status: seed.status,
    age: seed.age,
  }
}

// ---------------------------------------------------------------------------
// Wire → domain mappers (real-backend path).
//
// The kubb-generated response type is `IntakeTicketView`, with a flat shape:
// id / projectId / source (kebab-case provider key) / externalId / title /
// url / status / runId / createdAt. The dashboard's `Task` is richer (carries
// an `app` and a pre-formatted `age` string), so two facts are derived:
//
// - `source` — the wire's kebab-case key (`github` | `gitlab` | `yandex-tracker`
//   | `jira` | `native`) is already the dashboard's `ProviderKey`, which is a
//   `string`. The wire value passes through untouched, including any unknown
//   provider the host grows before the dashboard does — the badge spells it.
// - `app` — the wire carries no sub-project / area name. We default to the
//   `source` so the column and the filter chip still render something honest
//   ("github" reads as "the GitHub feed", "jira" as "the Jira board") rather
//   than blanks. When the wire grows a project→area dimension, this default
//   is the seam to widen.
// - `age` — the dashboard renders a pre-formatted "8 min" / "2 h" string. The
//   wire gives us an ISO instant; the same age-formatting helper that the
//   sessions mapper uses (relative to now) does the conversion.
// ---------------------------------------------------------------------------

/**
 * A wire `IntakeTicketStatus` → dashboard `TaskStatus`. The wire's enum is
 * `Pending | Claimed | Done | Dismissed`; only `Pending` arrives through
 * `/api/v1/inbox` (the host's `ListPendingAsync` filters the others out), so
 * the mapping is one entry today. `Claimed` is the only other live value,
 * reached on the optimistic claim transition; it reads as `"queued"` in the
 * dashboard so the row's chip turns from "new" to "queued" without a new
 * screen. `Done` / `Dismissed` are unreachable here — kept for symmetry.
 */
function wireStatusToTaskStatus(wire: string): TaskStatus {
  switch (wire) {
    case "Pending":
      return "new"
    case "Claimed":
      return "queued"
    case "Done":
    case "Dismissed":
    default:
      return "new"
  }
}

/**
 * `createdAt` (ISO) → "8 min" / "2 h" / "just now" — the dashboard's
 * pre-formatted vocabulary. Mirrors the helper in
 * `domains/chat/api/mappers.ts`; a shared helper is the natural follow-up
 * but out of scope here.
 */
function formatAge(iso: string, now: number = Date.now()): string {
  const created = new Date(iso).getTime()
  if (Number.isNaN(created)) {
    return ""
  }
  const delta = now - created
  if (delta < 60_000) {
    return "just now"
  }
  const minutes = Math.floor(delta / 60_000)
  if (minutes < 60) {
    return `${minutes} min`
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours} h`
  }
  const days = Math.floor(hours / 24)
  return `${days} d`
}

/**
 * Wire `IntakeTicketView` → dashboard `Task`.
 *
 * The dashboard has no `internalId` notion — every Task's id comes from
 * the wire. `app` defaults to the (verbatim) source; `priority` defaults to
 * `"normal"`; the wire does not yet carry per-ticket priority, and the badge
 * renders "normal" without highlighting — a future wire shape carries
 * `priority`, this mapper widens to read it.
 */
export function intakeTicketViewToTask(view: IntakeTicketView): Task {
  return {
    id: view.id,
    projectId: view.projectId,
    source: view.source,
    title: view.title,
    app: view.source,
    priority: "normal",
    status: wireStatusToTaskStatus(view.status),
    age: formatAge(view.createdAt),
  }
}

export function intakeTicketViewsToTasks(views: IntakeTicketView[]): Task[] {
  return views.map(intakeTicketViewToTask)
}

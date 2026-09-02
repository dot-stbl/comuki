import { PROFILE_CATALOG, seedDayAxis, type SeedProfile } from "./runs.seed"

/**
 * The claim queue and the worker pool — the two halves of the runtime.
 *
 * Fictional, like every other seed in this folder: no id, age, lease, digest or
 * provider handle below came from a real container. The figures are chosen to
 * make the *interesting* states reachable without a backend, not to look like
 * an average shift.
 *
 * The shape follows the v1 scope draft (§4 Runtime). The orchestrator puts work
 * items into a queue; a free worker claims one by profile, takes a lease and
 * heartbeats. So the two lists are one mechanism seen from its two ends, and
 * the seed keeps them paired rather than inventing them independently:
 *
 *   - every `running` item is claimed by a worker that exists, and
 *   - every `busy` or `draining` worker holds exactly that item.
 *
 * `seed-shapes.test.ts` in the queue domain asserts both, because a mock that
 * drifts out of that pairing teaches the screen a failure the product cannot
 * actually produce.
 */

/**
 * Work-item statuses, from the scope draft's orchestration table.
 *
 * Deliberately **no `stalled`**. A stall — a lease that expired without a
 * heartbeat — is an *event*: the orchestrator turns it into `failed` or puts
 * the item back to `queued`. Giving it a status would invent a resting state
 * the database never holds, and the screen would then carry a row nobody can
 * act on.
 */
export const WORK_ITEM_STATUSES = [
  "blocked",
  "queued",
  "running",
  "succeeded",
  "failed",
  "cancelled",
] as const

export type SeedWorkItemStatus = (typeof WORK_ITEM_STATUSES)[number]

/** A worker is idle, holding one item, or finishing up and refusing new claims. */
export const WORKER_STATES = ["idle", "busy", "draining"] as const

export type SeedWorkerState = (typeof WORKER_STATES)[number]

/** The `IComputeProvider` implementations that are must-have in v1.0. */
export const COMPUTE_PROVIDERS = ["docker", "kubernetes"] as const

export type SeedComputeProvider = (typeof COMPUTE_PROVIDERS)[number]

/** Project ids — the same three the session seed hands out roles on. */
export const QUEUE_PROJECT_IDS = ["p_comuki", "p_plexor", "p_atlas"] as const

export type SeedProjectId = (typeof QUEUE_PROJECT_IDS)[number]

export interface SeedQueueItem {
  id: string
  /** The run this item belongs to. Points at a run in `RUNS_SEED`. */
  runId: string
  /** Permission is resolved per row, and this is the row's answer. */
  projectId: SeedProjectId
  /** Catalog key of the profile that may claim it. The matching axis. */
  profile: SeedProfile
  /**
   * The brain's own name for the step. Prose, never a key.
   *
   * Russian, and the one field here that is: interface copy is English, but a
   * step name is *content* — a language model wrote it for a human to read, the
   * same as a ticket title. The run seed holds the same kind of string in the
   * same language, and the two lists are read side by side.
   */
  label: string
  status: SeedWorkItemStatus
  /**
   * Seconds in the *current* status — not since the item was created.
   *
   * For a queued item that is how long it has gone unclaimed, which is the one
   * number this screen exists to make visible: eight seconds is normal, eleven
   * minutes with idle workers means no profile matches it.
   */
  ageSec: number
  /**
   * The worker holding the lease, or `null`.
   *
   * Non-null exactly while the item is `running`: the lease is released the
   * moment an item leaves that status, so a finished item has no claimant here
   * — who did the work lives in the run's own journal.
   */
  claimedBy: string | null
  /** Items in the same run this one waits on. Only `blocked` has any. */
  blockedOn: string[]
}

export interface SeedWorker {
  id: string
  /** The pool this container was raised in. Gates the admin acts on its row. */
  projectId: SeedProjectId
  profile: SeedProfile
  state: SeedWorkerState
  /** The work item it holds a lease on; `null` while idle. */
  itemId: string | null
  provider: SeedComputeProvider
  /** The provider's own handle for the container. A value, not prose. */
  handle: string
  /**
   * Seconds since the last heartbeat landed. A worker that holds a lease and
   * stopped heartbeating is the failure this screen is built to spot.
   */
  heartbeatAgeSec: number
  /** Seconds until the lease expires; `null` when the worker holds none. */
  leaseSec: number | null
  /** Seconds since the container came up. */
  upSec: number
  /** Image it was started from — the short digest the worker labels carry. */
  digest: string
}

/**
 * Scale knobs the project turns, per the scope draft: the core owns pool and
 * scale, the project sets min/max idle. `minIdle: 0` is create-per-task, and it
 * is why an empty pool is usually the configured resting state rather than an
 * outage — which the screen has to be able to say out loud.
 */
export interface SeedWorkerPool {
  projectId: SeedProjectId
  minIdle: number
  maxIdle: number
}

/* ---------------------------------------------------------------------------
 * The pool. Eleven containers across two projects — and none at all on atlas,
 * on purpose: an empty pool is a state the screen must explain rather than a
 * gap in the mock. Atlas keeps `minIdle: 0`, so its emptiness is the configured
 * resting state, and the four queued atlas items below are what a scale-up
 * would answer.
 * ------------------------------------------------------------------------- */

const IMAGE = "sha256:9c41ab"
/** One container is a release behind, which is why it is draining. */
const IMAGE_PREV = "sha256:41b7de"

export const WORKERS_SEED: SeedWorker[] = [
  {
    id: "wk_2f8a",
    projectId: "p_comuki",
    profile: "implementer",
    state: "busy",
    itemId: "wi_0101",
    provider: "docker",
    handle: "docker/comuki-dev/2f8a91c4",
    heartbeatAgeSec: 3,
    leaseSec: 214,
    upSec: 1840,
    digest: IMAGE,
  },
  {
    id: "wk_5d13",
    projectId: "p_comuki",
    profile: "reviewer",
    state: "busy",
    itemId: "wi_0102",
    provider: "kubernetes",
    handle: "k8s/comuki-prod/worker-reviewer-5d13",
    heartbeatAgeSec: 2,
    leaseSec: 176,
    upSec: 620,
    digest: IMAGE,
  },
  {
    // A release behind: the digest no longer matches, so nothing new is claimed
    // on it and it goes away once this item lands.
    id: "wk_c412",
    projectId: "p_comuki",
    profile: "implementer",
    state: "draining",
    itemId: "wi_0103",
    provider: "kubernetes",
    handle: "k8s/comuki-prod/worker-implementer-c412",
    heartbeatAgeSec: 4,
    leaseSec: 96,
    upSec: 10480,
    digest: IMAGE_PREV,
  },
  {
    id: "wk_44de",
    projectId: "p_comuki",
    profile: "verifier",
    state: "busy",
    itemId: "wi_0107",
    provider: "kubernetes",
    handle: "k8s/comuki-prod/worker-verifier-44de",
    heartbeatAgeSec: 6,
    leaseSec: 118,
    upSec: 903,
    digest: IMAGE,
  },
  {
    id: "wk_a07e",
    projectId: "p_comuki",
    profile: "explorer",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: "docker/comuki-dev/a07e4411",
    heartbeatAgeSec: 1,
    leaseSec: null,
    upSec: 5400,
    digest: IMAGE,
  },
  {
    id: "wk_9b60",
    projectId: "p_comuki",
    profile: "tester",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: "docker/comuki-dev/9b60f2a7",
    heartbeatAgeSec: 2,
    leaseSec: null,
    upSec: 240,
    digest: IMAGE,
  },
  {
    // The failure the screen exists for: a lease seconds from expiry on a
    // worker that stopped heartbeating over a minute ago. Nobody may claim the
    // item until the lease lapses, and the run has stood still since.
    id: "wk_e34d",
    projectId: "p_plexor",
    profile: "implementer",
    state: "busy",
    itemId: "wi_0104",
    provider: "kubernetes",
    handle: "k8s/plexor-prod/worker-implementer-e34d",
    heartbeatAgeSec: 74,
    leaseSec: 6,
    upSec: 1512,
    digest: IMAGE,
  },
  {
    id: "wk_1c95",
    projectId: "p_plexor",
    profile: "planner",
    state: "busy",
    itemId: "wi_0105",
    provider: "kubernetes",
    handle: "k8s/plexor-prod/worker-planner-1c95",
    heartbeatAgeSec: 3,
    leaseSec: 233,
    upSec: 388,
    digest: IMAGE,
  },
  {
    id: "wk_b8f1",
    projectId: "p_plexor",
    profile: "reviewer",
    state: "draining",
    itemId: "wi_0106",
    provider: "docker",
    handle: "docker/plexor-dev/b8f1cc02",
    heartbeatAgeSec: 5,
    leaseSec: 41,
    upSec: 7620,
    digest: IMAGE,
  },
  {
    id: "wk_0f77",
    projectId: "p_plexor",
    profile: "explorer",
    state: "busy",
    itemId: "wi_0108",
    provider: "docker",
    handle: "docker/plexor-dev/0f77ba31",
    heartbeatAgeSec: 9,
    leaseSec: 155,
    upSec: 466,
    digest: IMAGE,
  },
  {
    id: "wk_7a20",
    projectId: "p_plexor",
    profile: "docs",
    state: "idle",
    itemId: null,
    provider: "docker",
    handle: "docker/plexor-dev/7a201de9",
    heartbeatAgeSec: 1,
    leaseSec: null,
    upSec: 90,
    digest: IMAGE,
  },
]

export const WORKER_POOLS_SEED: SeedWorkerPool[] = [
  { projectId: "p_comuki", minIdle: 2, maxIdle: 6 },
  { projectId: "p_plexor", minIdle: 1, maxIdle: 4 },
  // Create-per-task: this pool is meant to sit empty until a backlog appears.
  { projectId: "p_atlas", minIdle: 0, maxIdle: 0 },
]

/* ---------------------------------------------------------------------------
 * The queue. Hand-written first, so the cases the screen is an instrument for
 * are exact rather than sampled:
 *
 *   wi_0001  queued 43 minutes on a profile no worker in that pool runs — the
 *            reading that turns a list into an instrument.
 *   wi_0002  queued 11 minutes into an empty pool.
 *   wi_0003  queued 8 seconds. Same column, and nothing is wrong.
 *   wi_0104  running on the worker that stopped heartbeating.
 *   wi_0020  blocked for hours, and correctly so — it waits on two items that
 *            are still running. Age only alarms where age means something.
 *   wi_0030  a stall that already became a failure, which is the only way a
 *            stall exists in this model.
 * ------------------------------------------------------------------------- */

const HAND_ITEMS: SeedQueueItem[] = [
  {
    id: "wi_0001",
    runId: "2a6f1c33",
    projectId: "p_plexor",
    profile: "verifier",
    label: "проверить метрики выплат после раската",
    status: "queued",
    ageSec: 2612,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0002",
    runId: "78dde6cc",
    projectId: "p_atlas",
    profile: "docs",
    label: "описать новое окно хранения",
    status: "queued",
    ageSec: 664,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0003",
    runId: "1715607d",
    projectId: "p_comuki",
    profile: "implementer",
    label: "добавить колонку ключа идемпотентности",
    status: "queued",
    ageSec: 8,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0004",
    runId: "b54cda2e",
    projectId: "p_comuki",
    profile: "reviewer",
    label: "перечитать границу транзакции",
    status: "queued",
    ageSec: 22,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0005",
    runId: "538453df",
    projectId: "p_plexor",
    profile: "explorer",
    label: "найти всех, кто зовёт джобу экспорта",
    status: "queued",
    ageSec: 47,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0006",
    runId: "f1bbcd90",
    projectId: "p_atlas",
    profile: "implementer",
    label: "разбить рассылку счетов на две джобы",
    status: "queued",
    ageSec: 331,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0007",
    runId: "8ff34741",
    projectId: "p_atlas",
    profile: "reviewer",
    label: "проверить новый путь назначения роли",
    status: "queued",
    ageSec: 152,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0008",
    runId: "2e2ac0f2",
    projectId: "p_atlas",
    profile: "tester",
    label: "прогнать фикстуру дублирующего вебхука",
    status: "queued",
    ageSec: 74,
    claimedBy: null,
    blockedOn: [],
  },

  {
    id: "wi_0101",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "implementer",
    label: "переписать обработчик вокруг ключа идемпотентности",
    status: "running",
    ageSec: 412,
    claimedBy: "wk_2f8a",
    blockedOn: [],
  },
  {
    id: "wi_0102",
    runId: "b3d8a402",
    projectId: "p_comuki",
    profile: "reviewer",
    label: "снять тайминги скелетона",
    status: "running",
    ageSec: 96,
    claimedBy: "wk_5d13",
    blockedOn: [],
  },
  {
    id: "wi_0103",
    runId: "9d72b5f0",
    projectId: "p_comuki",
    profile: "implementer",
    label: "вынести бюджет ретраев в конфиг",
    status: "running",
    ageSec: 271,
    claimedBy: "wk_c412",
    blockedOn: [],
  },
  {
    id: "wi_0104",
    runId: "2a6f1c33",
    projectId: "p_plexor",
    profile: "implementer",
    label: "добэкфилить реестр выплат",
    status: "running",
    ageSec: 1186,
    claimedBy: "wk_e34d",
    blockedOn: [],
  },
  {
    id: "wi_0105",
    runId: "5b1d7e40",
    projectId: "p_plexor",
    profile: "planner",
    label: "решить, что делает частичный возврат",
    status: "running",
    ageSec: 143,
    claimedBy: "wk_1c95",
    blockedOn: [],
  },
  {
    id: "wi_0106",
    runId: "3c6ef36a",
    projectId: "p_plexor",
    profile: "reviewer",
    label: "проверить обратимость миграции",
    status: "running",
    ageSec: 388,
    claimedBy: "wk_b8f1",
    blockedOn: [],
  },
  {
    id: "wi_0107",
    runId: "c40aa2e1",
    projectId: "p_comuki",
    profile: "verifier",
    label: "сравнить долю ошибок с прошлой неделей",
    status: "running",
    ageSec: 58,
    claimedBy: "wk_44de",
    blockedOn: [],
  },
  {
    id: "wi_0108",
    runId: "daa66d1b",
    projectId: "p_plexor",
    profile: "explorer",
    label: "снять карту веера уведомлений",
    status: "running",
    ageSec: 77,
    claimedBy: "wk_0f77",
    blockedOn: [],
  },

  {
    id: "wi_0020",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "tester",
    label: "прогнать повторную доставку того же события",
    status: "blocked",
    ageSec: 13260,
    claimedBy: null,
    blockedOn: ["wi_0101", "wi_0102"],
  },
  {
    id: "wi_0021",
    runId: "b3d8a402",
    projectId: "p_comuki",
    profile: "docs",
    label: "записать контракт загрузки в гайд",
    status: "blocked",
    ageSec: 31,
    claimedBy: null,
    blockedOn: ["wi_0102"],
  },
  {
    id: "wi_0022",
    runId: "3c6ef36a",
    projectId: "p_plexor",
    profile: "verifier",
    label: "проследить миграцию на реплике",
    status: "blocked",
    ageSec: 902,
    claimedBy: null,
    blockedOn: ["wi_0106"],
  },

  {
    // What a stall actually becomes. The lease lapsed with no heartbeat, the
    // orchestrator raised the event, and the item is failed — not "stalled".
    id: "wi_0030",
    runId: "9d72b5f0",
    projectId: "p_comuki",
    profile: "implementer",
    label: "расширить индекс реестра выплат",
    status: "failed",
    ageSec: 252,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0031",
    runId: "5b1d7e40",
    projectId: "p_plexor",
    profile: "docs",
    label: "описать состояния возврата",
    status: "cancelled",
    ageSec: 590,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0040",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "explorer",
    label: "прочитать обработчик вебхуков",
    status: "succeeded",
    ageSec: 1940,
    claimedBy: null,
    blockedOn: [],
  },
  {
    id: "wi_0041",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "planner",
    label: "согласовать контракт идемпотентности",
    status: "succeeded",
    ageSec: 1705,
    claimedBy: null,
    blockedOn: [],
  },
]

/* ---------------------------------------------------------------------------
 * Synthetic bulk — NOT real work items.
 *
 * The queue is virtualized and filterable, and neither is exercised by twenty
 * rows. These take the list to forty-eight. Deterministic (fixed LCG seed) so
 * tests and Storybook see the same table twice.
 *
 * Two constraints the generator keeps, because the screen reads them as facts:
 * nothing here is `running` — a running item needs a real worker and every
 * worker in the pool above already holds one — and nothing here lands on
 * `p_atlas`, so that pool's backlog stays exactly the four hand-written items
 * and its empty state stays legible profile by profile.
 * ------------------------------------------------------------------------- */

const BULK_STATUSES: SeedWorkItemStatus[] = [
  "succeeded",
  "queued",
  "blocked",
  "succeeded",
  "failed",
  "queued",
  "succeeded",
  "blocked",
  "cancelled",
  "succeeded",
  "queued",
  "failed",
  "succeeded",
  "blocked",
  "queued",
  "succeeded",
  "cancelled",
  "failed",
  "succeeded",
  "queued",
  "blocked",
  "succeeded",
  "failed",
  "succeeded",
  "queued",
]

const BULK_RUNS = [
  "1715607d",
  "b54cda2e",
  "538453df",
  "f1bbcd90",
  "8ff34741",
  "2e2ac0f2",
  "cc623aa3",
  "6a99b454",
  "08d12e05",
  "a708a7b6",
  "45402167",
  "e3779b18",
  "81af14c9",
  "1fe68e7a",
  "be1e082b",
  "5c5581dc",
  "fa8cfb8d",
  "98c4753e",
]

/** Step names, per profile. Prose the brain invents, never a key. */
const LABELS: Record<SeedProfile, string[]> = {
  explorer: [
    "trace the callers of the ledger writer",
    "read the current retry path",
    "find where the token is minted",
  ],
  planner: [
    "settle what a partial failure means",
    "pick the migration order",
    "decide the rollback story",
  ],
  implementer: [
    "move the cursor into the query",
    "add the backfill command",
    "split the mailer job in two",
  ],
  reviewer: [
    "check the transaction boundary",
    "read the new error mapping",
    "look for a leaked secret",
  ],
  tester: [
    "replay the duplicate delivery",
    "run the ledger fixtures",
    "cover the empty-cursor case",
  ],
  verifier: [
    "compare the error rate after deploy",
    "watch the queue depth for an hour",
    "confirm the backfill is complete",
  ],
  docs: [
    "update the webhook page",
    "record the decision in knowledge",
    "add an example to the guide",
  ],
}

function lcg(seed: number): () => number {
  let state = seed
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296
    return state / 4294967296
  }
}

function bulkItems(): SeedQueueItem[] {
  const random = lcg(20260830)
  // Atlas is excluded on purpose — see the block comment above.
  const projects: SeedProjectId[] = ["p_comuki", "p_plexor"]

  return BULK_STATUSES.map((status, index) => {
    const profile =
      PROFILE_CATALOG[Math.floor(random() * PROFILE_CATALOG.length)]
    const bank = LABELS[profile]
    // Queued rows stay under four minutes: the long waits are the hand-written
    // ones, so "how many have waited too long" is a number the seed decides
    // rather than one the random walk happens to produce.
    const ageSec =
      status === "queued"
        ? Math.floor(random() * 220) + 4
        : Math.floor(random() * 2400) + 30

    return {
      id: `wi_2${String(index + 1).padStart(3, "0")}`,
      runId: BULK_RUNS[index % BULK_RUNS.length],
      projectId: projects[Math.floor(random() * projects.length)],
      profile,
      label: bank[index % bank.length],
      status,
      ageSec,
      claimedBy: null,
      blockedOn: status === "blocked" ? ["wi_0101"] : [],
    }
  })
}

export const QUEUE_SEED: SeedQueueItem[] = [...HAND_ITEMS, ...bulkItems()]

/* ---------------------------------------------------------------------------
 * Queue depth per day — the time half of the reading the snapshot above is.
 *
 * The list answers "what is waiting now"; the series answers whether that is
 * getting better. Six authored days in single digits — an ordinary week for a
 * pool that keeps up — and today *derived*: the series' last column is counted
 * off `QUEUE_SEED` itself, so the chart and the header can never disagree about
 * how deep the queue is on the day you are looking at both of them.
 *
 * The story the shape continues: today holds the week's depth because of the
 * two faults the snapshot already tells — `wi_0104`, stuck on the lease
 * `wk_e34d` stopped defending, and the four atlas items waiting on a pool that
 * is `minIdle: 0` and has not scaled up yet.
 * ------------------------------------------------------------------------- */

export interface SeedQueueDepth {
  daysAgo: number
  weekday: string
  /** Work items waiting for a claim — the same thing the header's count says. */
  depth: number
}

const PAST_DEPTHS = [4, 6, 5, 8, 6, 9] as const

export const QUEUE_DEPTH_SEED: SeedQueueDepth[] = [
  ...seedDayAxis()
    .filter((day) => day.daysAgo > 0)
    .map((day, index) => ({
      daysAgo: day.daysAgo,
      weekday: day.weekday,
      depth: PAST_DEPTHS[index] ?? 6,
    })),
  {
    daysAgo: 0,
    weekday: "today",
    // Derived, not authored: the band and the header are one reading.
    depth: QUEUE_SEED.filter((item) => item.status === "queued").length,
  },
]

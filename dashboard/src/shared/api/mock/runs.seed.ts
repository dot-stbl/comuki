export type SeedStatus =
  "running" | "success" | "failed" | "waiting" | "queued" | "escalated"

/**
 * The worker profiles this client has declared.
 *
 * A closed catalog: profiles live in the client's git as prompt + skills +
 * tools, and the brain can only pick from what exists there. That is what makes
 * the profile the run graph's one stable identity, and the only axis the duty
 * board is allowed to aggregate on.
 *
 * The step *names* on the work items below are the opposite: the brain invents
 * them per ticket, in the product's own language. Two runs share profiles and
 * share no step name at all — which is exactly what the seed has to make vivid.
 */
export const PROFILE_CATALOG = [
  "explorer",
  "planner",
  "implementer",
  "reviewer",
  "tester",
  "verifier",
  "docs",
] as const

export type SeedProfile = (typeof PROFILE_CATALOG)[number]

export interface SeedWorkItem {
  /** Unique inside its run. */
  id: string
  profile: SeedProfile
  /** The brain's own name for this step. Prose, never a key. */
  label: string
  status: SeedStatus
  /** Ids of items in the same run this one waits on. */
  deps: string[]
  cost?: number
  tokens?: number
  /** Run-relative clock, `MM:SS`. Absent while the item is queued. */
  startedAt?: string
}

export interface SeedRun {
  id: string
  /**
   * The project this run belongs to, by id — an attribute of the row, not a
   * mode the screen is in. The duty engineer watches the whole swarm at once,
   * so every list mixes projects and every gated act on a row answers to *this*
   * project rather than to the session.
   */
  projectId: string
  app: string
  title: string
  status: SeedStatus
  /** Id of the work item the run is standing on. */
  current: string
  model: "worker" | "lead"
  cost: number
  tokens: number
  startSec: number
  done?: boolean
  items: SeedWorkItem[]
}

/** A run before the seed stamps its project on it — see `PROJECT_BY_APP`. */
type SeedRunDraft = Omit<SeedRun, "projectId">

/**
 * Which project an app belongs to.
 *
 * One app, one project: an application is built inside a project and does not
 * move between them, so a run and a backlog item that name the same app can
 * never disagree about whose project they are in. Both seeds read this — it is
 * the whole mapping, written once.
 *
 * The ids are the ones `session.seed.ts` hands the shift, and the split is what
 * makes the duty list worth looking at: the seeded user approves on `p_comuki`,
 * administers `p_atlas` and can only watch `p_plexor`, so the same list carries
 * rows whose Approve works directly above rows whose Approve explains itself.
 */
export const PROJECT_BY_APP: Record<string, string> = {
  // The vendor's own platform.
  "web-app": "p_comuki",
  "worker-pool": "p_comuki",
  "search-idx": "p_comuki",
  // Plexor — identity and messaging. The session only watches this one.
  "auth-svc": "p_plexor",
  "identity-svc": "p_plexor",
  "notify-svc": "p_plexor",
  "admin-portal": "p_plexor",
  // Atlas — payments, storefront and the guide site that documents them.
  "billing-api": "p_atlas",
  "ledger-core": "p_atlas",
  "checkout-web": "p_atlas",
  "docs-site": "p_atlas",
}

function withProject(run: SeedRunDraft): SeedRun {
  return { ...run, projectId: PROJECT_BY_APP[run.app] }
}

/* ---------------------------------------------------------------------------
 * The day axis every time series in the mock reads.
 *
 * The seeds keep times relative on purpose — a stamped date is a mock that
 * starts failing on a Tuesday six months from now — and a day series is still
 * relative: `daysAgo` counts back from today, the weekday label is derived from
 * the clock at seed init, and `weekend` says which columns are Saturday and
 * Sunday *this* week. The stories the series tell (weekend tickets are
 * lighter, the incident was three days ago) therefore hold on whichever day
 * the app is opened, which a fixed list of dates never could.
 * ------------------------------------------------------------------------- */

const WEEKDAY_LABELS = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"] as const

export interface SeedDay {
  daysAgo: number
  /** Derived weekday ("mon"), or "today" for the column the shift is standing on. */
  weekday: string
  /** Saturday or Sunday this week — the days the spend story expects to be lighter. */
  weekend: boolean
}

export function seedDayAxis(count = 7): SeedDay[] {
  const today = new Date().getDay()
  return Array.from({ length: count }, (_, index) => {
    const daysAgo = count - 1 - index
    const weekday = (today - daysAgo + 7 * Math.ceil(daysAgo / 7) + 7) % 7
    return {
      daysAgo,
      weekday: daysAgo === 0 ? "today" : WEEKDAY_LABELS[weekday],
      weekend: weekday === 0 || weekday === 6,
    }
  })
}

/* ---------------------------------------------------------------------------
 * Run outcomes per day — the shift's history, which the snapshot above is not.
 *
 * A run the live list shows is a run that happened *today*: ages are minutes,
 * leases are minutes, and the list is the current shift rather than an archive.
 * So the day a run finishes never appears in `RUNS_SEED` as a field — it
 * appears here, as the finished counts per day, and the last column is bounded
 * below by what the live list is already showing (a seed that finished fewer
 * runs today than the list holds would be describing a different day).
 *
 * The statuses are the three a run can *rest in* overnight: success, failed,
 * escalated. The other three — running, waiting, queued — are mid-flight
 * states, and a bar that stacked them would be counting unfinished work as an
 * outcome of the day it sits in.
 *
 * The story the shape continues: three days ago the auth-svc identity
 * migration ran (the app list still carries its `+21%` trend), and that day
 * broke the week's runs — the failed spike sits there, on every screen that
 * asks the time question, so the cost spike, the outcome spike and the
 * `+21%` trend all name one incident rather than three coincidences.
 * ------------------------------------------------------------------------- */

export interface SeedOutcomeDay {
  daysAgo: number
  weekday: string
  /** Only the statuses a finished run can rest in, worst last in the stack. */
  byStatus: Array<{ status: SeedStatus; count: number }>
}

/** Finishes on a weekday: what the swarm clears on an uneventful day. */
const WEEKDAY_SUCCESSES: Record<number, number> = {
  0: 11, // sun
  1: 34, // mon
  2: 33, // tue
  3: 35, // wed
  4: 32, // thu
  5: 30, // fri
  6: 14, // sat
}

/** Failures and escalations per days-ago, for every day but today. */
const PAST_DAYS: Record<number, { failed: number; escalated: number }> = {
  6: { failed: 4, escalated: 1 },
  5: { failed: 3, escalated: 2 },
  4: { failed: 5, escalated: 1 },
  3: { failed: 14, escalated: 3 },
  2: { failed: 6, escalated: 2 },
  1: { failed: 5, escalated: 1 },
}

/** The incident day — the auth-svc migration — cleared less than a normal day. */
const INCIDENT_DAYS_AGO = 3
const INCIDENT_SUCCESS_FACTOR = 0.8

function outcomeDays(): SeedOutcomeDay[] {
  const axis = seedDayAxis()

  const past = axis
    .filter((day) => day.daysAgo > 0)
    .map((day) => {
      const weekday = WEEKDAY_LABELS.indexOf(
        day.weekday as (typeof WEEKDAY_LABELS)[number]
      )
      const base = PAST_DAYS[day.daysAgo] ?? { failed: 0, escalated: 0 }
      const success = Math.round(
        WEEKDAY_SUCCESSES[weekday] *
          (day.daysAgo === INCIDENT_DAYS_AGO ? INCIDENT_SUCCESS_FACTOR : 1)
      )
      return {
        daysAgo: day.daysAgo,
        weekday: day.weekday,
        byStatus: [
          { status: "success" as const, count: success },
          { status: "failed" as const, count: base.failed },
          { status: "escalated" as const, count: base.escalated },
        ],
      }
    })

  return [
    ...past,
    {
      daysAgo: 0,
      weekday: "today",
      // Bounded below by the live list: every run RUNS_SEED shows in a
      // finished state happened on today's shift, so today's column cannot
      // be smaller than the list that is drawing beside it.
      byStatus: [
        { status: "success", count: 26 },
        { status: "failed", count: 12 },
        { status: "escalated", count: 9 },
      ],
    },
  ]
}

export const OUTCOMES_SEED: SeedOutcomeDay[] = outcomeDays()

export interface SeedDiffLine {
  ty: "ctx" | "add" | "del"
  n: string
  text: string
}

export interface SeedDiffFile {
  file: string
  add: number
  del: number
  lines: SeedDiffLine[]
}

export interface SeedTraceEvent {
  t: string
  st: SeedStatus
  text: string
}

export interface SeedTrace {
  brief: string
  rules: string[]
  revision: { rules: string; sdk: string }
  events: SeedTraceEvent[]
  diff: SeedDiffFile[]
  tests: Array<{ name: string; st: SeedStatus; detail: string }>
}

function item(
  id: string,
  profile: SeedProfile,
  label: string,
  status: SeedStatus,
  deps: string[] = []
): SeedWorkItem {
  return { id, profile, label, status, deps }
}

/* ---------------------------------------------------------------------------
 * Hand-written runs. Six plans, six different shapes: a three-item "just close
 * it", a branch of two, a four-wide branch, a run that died at the second step.
 * No two share a step name.
 * ------------------------------------------------------------------------- */

const HAND_RUNS: SeedRunDraft[] = [
  {
    id: "8f3c2a91",
    app: "billing-api",
    title: "Идемпотентность в обработчике webhook'ов Stripe",
    status: "running",
    current: "w4",
    model: "worker",
    cost: 0.42,
    tokens: 18400,
    startSec: 252,
    items: [
      item("w1", "explorer", "прочитать обработчик вебхуков", "success"),
      item("w2", "planner", "контракт на идемпотентность", "success", ["w1"]),
      item("w3", "implementer", "завести таблицу idem_keys", "success", ["w2"]),
      item(
        "w4",
        "implementer",
        "переписать обработчик под ключ идемпотентности",
        "running",
        ["w2"]
      ),
      item("w5", "reviewer", "проверить границы транзакции", "queued", [
        "w3",
        "w4",
      ]),
      item("w6", "tester", "проверить повторную доставку события", "queued", [
        "w5",
      ]),
      item("w7", "verifier", "дождаться аппрува на раскатку", "queued", ["w6"]),
      item("w8", "docs", "обновить страницу про вебхуки", "queued", ["w7"]),
    ],
  },
  {
    id: "b3d8a402",
    app: "web-app",
    title: "Скелетоны загрузки на дашборде прогонов",
    status: "running",
    current: "w2",
    model: "worker",
    cost: 0.18,
    tokens: 7200,
    startSec: 96,
    // Mode A: the brain decided this one does not need a plan at all.
    items: [
      item(
        "w1",
        "explorer",
        "посмотреть, где сейчас пусто на экране",
        "success"
      ),
      item(
        "w2",
        "implementer",
        "нарисовать скелетоны вместо спиннера",
        "running",
        ["w1"]
      ),
      item("w3", "verifier", "снять визуальный снапшот", "queued", ["w2"]),
    ],
  },
  {
    id: "5b1d7e40",
    app: "web-app",
    title: "Виртуализация таблицы прогонов (react-window)",
    status: "waiting",
    current: "w9",
    model: "worker",
    cost: 1.08,
    tokens: 42100,
    startSec: 775,
    items: [
      item("w1", "explorer", "снять список затронутых таблиц", "success"),
      item("w2", "planner", "решить, что делаем в этой итерации", "success", [
        "w1",
      ]),
      item(
        "w3",
        "implementer",
        "вынести строку в отдельный компонент",
        "success",
        ["w2"]
      ),
      item("w4", "implementer", "подключить виртуализатор к телу", "success", [
        "w3",
      ]),
      item("w5", "implementer", "пересчитать высоту скролл-порта", "success", [
        "w4",
      ]),
      item("w6", "reviewer", "сверить с контрактом таблицы", "success", ["w5"]),
      item("w7", "tester", "прогнать смоук по длинным спискам", "success", [
        "w6",
      ]),
      item("w8", "tester", "снять визуальный снапшот", "success", ["w6"]),
      item("w9", "verifier", "дождаться аппрува на раскатку", "waiting", [
        "w7",
        "w8",
      ]),
      item("w10", "docs", "записать решение в базу знаний", "queued", ["w9"]),
    ],
  },
  {
    id: "2a6f1c33",
    app: "auth-svc",
    title: "Ротация JWT-ключей без даунтайма",
    status: "escalated",
    current: "w6",
    model: "lead",
    cost: 2.31,
    tokens: 96800,
    startSec: 540,
    // Four lanes off one plan — the widest branch the brain writes by hand.
    items: [
      item("w1", "explorer", "выяснить, где живёт проверка подписи", "success"),
      item("w2", "planner", "план ротации без даунтайма", "success", ["w1"]),
      item("w3", "implementer", "завести второй активный ключ", "success", [
        "w2",
      ]),
      item("w4", "implementer", "научить верификатор двум ключам", "success", [
        "w2",
      ]),
      item("w5", "implementer", "обновить JWKS-эндпоинт", "success", ["w2"]),
      item(
        "w6",
        "implementer",
        "починить гонку при выкате ключа",
        "escalated",
        ["w2"]
      ),
      item("w7", "reviewer", "проверить обратную совместимость", "queued", [
        "w3",
        "w4",
        "w5",
        "w6",
      ]),
      item("w8", "tester", "прогнать сценарий старого токена", "queued", [
        "w7",
      ]),
      item("w9", "verifier", "подтвердить, что откат готов", "queued", ["w8"]),
      item("w10", "docs", "описать новый формат ключей", "queued", ["w9"]),
    ],
  },
  {
    id: "9d72b5f0",
    app: "docs-site",
    title: "Миграция на новый theme API",
    status: "failed",
    current: "w3",
    model: "worker",
    cost: 0.19,
    tokens: 8300,
    startSec: 121,
    items: [
      item("w1", "explorer", "собрать карту использований темы", "success"),
      item("w2", "planner", "решить, что переносим сейчас", "success", ["w1"]),
      item("w3", "implementer", "переехать на новый theme API", "failed", [
        "w2",
      ]),
      item("w4", "reviewer", "вычитать диф по правилам ui-tokens", "queued", [
        "w3",
      ]),
      item("w5", "verifier", "прогнать гейт проверок клиента", "queued", [
        "w4",
      ]),
    ],
  },
  {
    id: "c40aa2e1",
    app: "worker-pool",
    title: "Ретраи с экспоненциальным бэкоффом",
    status: "success",
    current: "w9",
    model: "worker",
    cost: 0.67,
    tokens: 25600,
    startSec: 510,
    done: true,
    items: [
      item("w1", "explorer", "найти все места с ретраями", "success"),
      item("w2", "planner", "выбрать единый бэкофф", "success", ["w1"]),
      item("w3", "implementer", "вынести ретраи в отдельный слой", "success", [
        "w2",
      ]),
      item("w4", "implementer", "прокинуть тайм-аут до провайдера", "success", [
        "w2",
      ]),
      item("w5", "reviewer", "проверить, что нет утечки секретов", "success", [
        "w3",
        "w4",
      ]),
      item("w6", "tester", "прогнать юнит-тесты по очереди", "success", ["w5"]),
      item("w7", "verifier", "сверить метрики после выката", "success", ["w6"]),
      item("w8", "docs", "записать решение в базу знаний", "success", ["w7"]),
      item("w9", "docs", "добавить пример в гайд", "success", ["w8"]),
    ],
  },
]

/* ---------------------------------------------------------------------------
 * Synthetic bulk — NOT real runs.
 *
 * The duty screen is designed for 50–200 concurrent runs; the hand-written set
 * above is six, which never exercises the board. These fill the swarm to a
 * realistic shift so density, the pinch marker and the stuck list are seen
 * under the load they were built for. Deterministic (fixed LCG seed) so tests
 * and Storybook stay stable. Every field is invented: no real cost, token or
 * timing figure from a production run appears here.
 *
 * The graphs are deliberately unlike each other — three-item runs the brain
 * closed without planning, ordinary eight-to-thirteen item plans, and a handful
 * of forty-plus item runs branching four lanes wide. Nothing in the product may
 * assume a shape, so the mock refuses to give it one.
 * ------------------------------------------------------------------------- */

const SYNTHETIC_APPS = [
  "billing-api",
  "web-app",
  "ledger-core",
  "notify-svc",
  "search-idx",
  "admin-portal",
  "checkout-web",
  "identity-svc",
]

const SYNTHETIC_TITLES = [
  "Ретраи с экспоненциальной паузой в очереди выплат",
  "Пагинация в списке инвойсов",
  "Импорт словаря синонимов для поиска",
  "Массовое назначение ролей",
  "Гостевой чекаут: форма адреса",
  "Переезд диалогов на React Aria",
  "Бэкфилл курсов валют",
  "Отказ от legacy SMS-провайдера",
  "Экспорт журнала аудита в CSV",
  "Переиндексация при переименовании тенанта",
  "Кэш прайс-листа на стороне edge",
  "Разбор вебхуков в фоновой очереди",
  "Ограничение частоты для публичного API",
  "Мягкое удаление в справочнике клиентов",
  "Перенос миграций на idempotent-скрипты",
  "Правки текстов в письмах о просрочке",
  "Виртуализация длинных таблиц",
  "Единый формат ошибок в ответах API",
  "Точечная инвалидация кэша каталога",
  "Двухфакторка через TOTP",
  "Сжатие артефактов прогонов в MinIO",
  "Трассировка запросов через прокси",
  "Пересчёт агрегатов стоимости за сутки",
  "Фильтр по приложению в очереди аппрувов",
]

/**
 * Step names, per profile — the brain's vocabulary, not a catalog. Nothing
 * reads these as keys; they exist so a row and a graph node say something a
 * person recognises, and so no two runs look like they ran the same steps.
 */
const LABELS: Record<SeedProfile, string[]> = {
  explorer: [
    "прочитать обработчик вебхуков",
    "найти все места с ретраями",
    "собрать карту зависимостей модуля",
    "выяснить, где живёт валидация",
    "посмотреть, как устроен текущий кэш",
    "снять список затронутых эндпоинтов",
    "разобраться со схемой миграций",
    "найти похожий случай в соседнем сервисе",
  ],
  planner: [
    "контракт на идемпотентность",
    "разбить задачу на два лейна",
    "решить, что делаем в этой итерации",
    "план миграции без даунтайма",
    "согласовать формат ошибки",
    "расписать шаги отката",
    "выбрать порядок раскатки",
    "уточнить границы правки",
  ],
  implementer: [
    "переписать обработчик под ключ идемпотентности",
    "завести таблицу под ключи",
    "вынести ретраи в отдельный слой",
    "починить гонку при параллельных вебхуках",
    "прокинуть тайм-аут до провайдера",
    "переехать на новый клиент",
    "добавить пагинацию в список",
    "закрыть дыру в валидации входа",
    "подключить кэш прайс-листа",
    "обновить тексты в письмах",
    "разнести миграцию на два шага",
    "убрать дубли в очереди",
  ],
  reviewer: [
    "вычитать диф по правилам db-tx",
    "проверить, что нет утечки секретов",
    "сверить с контрактом",
    "посмотреть на границы транзакции",
    "оценить риск раскатки",
    "проверить обратную совместимость",
  ],
  tester: [
    "прогнать юнит-тесты по обработчику",
    "проверить повторную доставку события",
    "нагрузить эндпоинт логина",
    "прогнать смоук по критичным маршрутам",
    "проверить миграцию на копии базы",
    "снять визуальный снапшот",
  ],
  verifier: [
    "дождаться аппрува на раскатку",
    "прогнать гейт проверок клиента",
    "сверить метрики после выката",
    "проверить бюджет прогона",
    "подтвердить, что откат готов",
  ],
  docs: [
    "записать решение в базу знаний",
    "обновить страницу про вебхуки",
    "добавить пример в гайд",
    "описать новый формат ошибки",
  ],
}

/**
 * Where the shift is sitting, per profile. The graphs are arbitrary but the
 * board's reading is authored: the approve gate is where work piles up waiting
 * on a person, and the implementer lane is where it is busiest.
 */
const SYNTHETIC_POOL: Array<[SeedProfile, Array<[SeedStatus, number]>]> = [
  [
    "explorer",
    [
      ["running", 6],
      ["queued", 8],
    ],
  ],
  [
    "planner",
    [
      ["running", 7],
      ["waiting", 4],
    ],
  ],
  [
    "implementer",
    [
      ["running", 28],
      ["failed", 4],
      ["escalated", 3],
    ],
  ],
  [
    "reviewer",
    [
      ["running", 11],
      ["waiting", 3],
    ],
  ],
  [
    "tester",
    [
      ["running", 9],
      ["failed", 7],
      ["escalated", 3],
    ],
  ],
  [
    "verifier",
    [
      ["running", 5],
      ["waiting", 14],
      ["escalated", 2],
    ],
  ],
  [
    "docs",
    [
      ["running", 5],
      ["success", 9],
    ],
  ],
]

function lcg(seed: number): () => number {
  let state = seed
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296
    return state / 4294967296
  }
}

function hexId(index: number): string {
  return (index * 2654435761 + 0x9e3779b9)
    .toString(16)
    .padStart(8, "0")
    .slice(-8)
}

/** A plan under construction: profile plus the ids it waits on. */
interface PlanNode {
  id: string
  profile: SeedProfile
  deps: string[]
}

interface Plan {
  nodes: PlanNode[]
}

type ShapeKind = "close" | "standard" | "wide"

function plan(
  build: (add: (profile: SeedProfile, deps: string[]) => string) => void
): Plan {
  const nodes: PlanNode[] = []
  const add = (profile: SeedProfile, deps: string[]): string => {
    const id = `w${nodes.length + 1}`
    nodes.push({ id, profile, deps })
    return id
  }
  build(add)
  return { nodes }
}

/** Three items, no plan step: the brain decided to just close the ticket. */
function closePlan(): Plan {
  return plan((add) => {
    const explore = add("explorer", [])
    const work = add("implementer", [explore])
    add("verifier", [work])
  })
}

/** The ordinary shape: eight to thirteen items, two to four lanes of work. */
function standardPlan(random: () => number): Plan {
  return plan((add) => {
    const explore = add("explorer", [])
    const planning = add("planner", [explore])

    const lanes = 2 + Math.floor(random() * 3)
    const first: string[] = []
    for (let lane = 0; lane < lanes; lane += 1) {
      first.push(add("implementer", [planning]))
    }

    const second: string[] = []
    const extra = Math.floor(random() * 4)
    for (let n = 0; n < extra; n += 1) {
      second.push(add("implementer", [first[n % first.length]]))
    }

    const review = add("reviewer", [...first, ...second])
    const test = add("tester", [review])
    const verify = add("verifier", [test])
    add("docs", [verify])
  })
}

/**
 * Forty-plus items, four lanes wide and eight deep. Nothing in the product may
 * assume a run is small, so one shape in the seed insists otherwise.
 */
function widePlan(): Plan {
  return plan((add) => {
    const explore = add("explorer", [])
    const planning = add("planner", [explore])

    const laneReviews: string[] = []
    for (let lane = 0; lane < 4; lane += 1) {
      let previous = planning
      for (let step = 0; step < 8; step += 1) {
        previous = add("implementer", [previous])
      }
      laneReviews.push(add("reviewer", [previous]))
    }

    const review = add("reviewer", laneReviews)
    const test = add("tester", [review])
    const verify = add("verifier", [test])
    add("docs", [verify])
  })
}

function buildPlan(kind: ShapeKind, random: () => number): Plan {
  if (kind === "close") {
    return closePlan()
  }
  if (kind === "wide") {
    return widePlan()
  }
  return standardPlan(random)
}

/** Longest-path depth, the same rule the domain model reads graphs with. */
function planDepths(nodes: PlanNode[]): Map<string, number> {
  const depths = new Map<string, number>()
  for (const node of nodes) {
    const depth = node.deps.reduce(
      (max, dependency) => Math.max(max, (depths.get(dependency) ?? 0) + 1),
      0
    )
    depths.set(node.id, depth)
  }
  return depths
}

/**
 * A shape that can actually host the pool slot being filled. `close` has no
 * planner and no docs, so a run parked on either has to be a bigger plan.
 */
function shapeFor(profile: SeedProfile, random: () => number): ShapeKind {
  const roll = random()
  if (profile === "implementer" || profile === "reviewer") {
    return roll > 0.94 ? "wide" : "standard"
  }
  if (profile === "explorer" || profile === "verifier") {
    return roll > 0.72 ? "close" : "standard"
  }
  return "standard"
}

/**
 * Statuses over a graph: everything upstream of the front has cleared, the
 * front carries the run's own status, everything downstream is still queued.
 * Siblings on the front's own depth are a coin toss between cleared and queued
 * — which is what makes a lane look like a lane rather than a wavefront.
 */
function assignStatuses(
  nodes: PlanNode[],
  frontId: string,
  status: SeedStatus,
  random: () => number
): SeedWorkItem[] {
  const depths = planDepths(nodes)
  const frontDepth = depths.get(frontId) ?? 0
  const labels = new Map<SeedProfile, number>()

  return nodes.map((node) => {
    const depth = depths.get(node.id) ?? 0
    let itemStatus: SeedStatus = "queued"
    if (node.id === frontId) {
      itemStatus = status
    } else if (depth < frontDepth) {
      itemStatus = "success"
    } else if (depth === frontDepth) {
      itemStatus = random() > 0.5 ? "success" : "queued"
    }

    // Walk each profile's vocabulary rather than sampling it, so one run never
    // repeats a step name while two runs rarely share one.
    const cursor = labels.get(node.profile) ?? Math.floor(random() * 12)
    labels.set(node.profile, cursor + 1)
    const bank = LABELS[node.profile]

    return {
      id: node.id,
      profile: node.profile,
      label: bank[cursor % bank.length],
      status: itemStatus,
      deps: node.deps,
    }
  })
}

function syntheticRuns(): SeedRunDraft[] {
  const random = lcg(20260830)
  const runs: SeedRunDraft[] = []
  let index = 0

  for (const [profile, statuses] of SYNTHETIC_POOL) {
    for (const [status, count] of statuses) {
      for (let n = 0; n < count; n += 1) {
        index += 1
        const done = status === "success"
        const kind = shapeFor(profile, random)
        let nodes = buildPlan(kind, random).nodes
        if (!nodes.some((node) => node.profile === profile)) {
          nodes = buildPlan("standard", random).nodes
        }

        const hosts = nodes.filter((node) => node.profile === profile)
        const front = hosts[Math.floor(random() * hosts.length)] ?? nodes[0]

        const items = done
          ? assignStatuses(nodes, nodes[nodes.length - 1].id, "success", random)
          : assignStatuses(nodes, front.id, status, random)

        runs.push({
          id: hexId(index),
          app: SYNTHETIC_APPS[index % SYNTHETIC_APPS.length],
          title: SYNTHETIC_TITLES[index % SYNTHETIC_TITLES.length],
          status,
          current: done ? nodes[nodes.length - 1].id : front.id,
          model: random() > 0.78 ? "lead" : "worker",
          cost: Math.round(random() * 210) / 100,
          tokens: Math.round(random() * 46000) + 900,
          startSec: status === "queued" ? 0 : Math.round(random() * 2400) + 40,
          done,
          items,
        })
      }
    }
  }

  return runs
}

/** Stable per-run seed, so an item's figures never move between reloads. */
function hash(text: string): number {
  let value = 2166136261
  for (let index = 0; index < text.length; index += 1) {
    value = (value ^ text.charCodeAt(index)) * 16777619
    value >>>= 0
  }
  return value
}

function clock(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  return `${String(minutes).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`
}

/**
 * Per-item cost, tokens and start clock. Filled here rather than written into
 * every plan above: they are figures, not decisions, and an item that has not
 * started has none of them.
 */
function withItemMetrics(run: SeedRun): SeedRun {
  const random = lcg(hash(run.id))
  let elapsed = 0

  return {
    ...run,
    items: run.items.map((entry) => {
      if (entry.status === "queued") {
        return entry
      }
      elapsed += 20 + Math.floor(random() * 160)
      return {
        ...entry,
        cost: Math.round(random() * 90) / 100,
        tokens: Math.round(random() * 21000) + 400,
        startedAt: clock(elapsed),
      }
    }),
  }
}

export const RUNS_SEED: SeedRun[] = [...HAND_RUNS, ...syntheticRuns()]
  .map(withProject)
  .map(withItemMetrics)

export const TRACE_SEED: Record<string, SeedTrace> = {
  "8f3c2a91": {
    brief:
      "Сделать обработчик `POST /webhooks/stripe` идемпотентным. Ключ идемпотентности — заголовок `Idempotency-Key`; при повторе вернуть сохранённый ответ, не пере-выполняя side-effects. Ретраи Stripe (до 3 сут) должны быть безопасны.",
    rules: ["api-errors", "db-tx", "no-secrets"],
    revision: { rules: "rules@a1b9e0", sdk: "sdk@2.4.1" },
    events: [
      {
        t: "00:00",
        st: "success",
        text: "Запрос принят · план мозга построен",
      },
      {
        t: "00:04",
        st: "success",
        text: "«прочитать обработчик вебхуков» завершён — 4 файла в контексте",
      },
      {
        t: "00:31",
        st: "success",
        text: "«контракт на идемпотентность» завершён · граф из 8 задач",
      },
      {
        t: "01:02",
        st: "success",
        text: "«завести таблицу idem_keys» завершён · применено 3 правила",
      },
      {
        t: "01:48",
        st: "running",
        text: "«переписать обработчик под ключ идемпотентности» в работе",
      },
      { t: "03:10", st: "waiting", text: "Retry юнит-теста test_replay (1/3)" },
      {
        t: "04:12",
        st: "running",
        text: "worker · gpt-class · 18.4k токенов накоплено",
      },
    ],
    diff: [
      {
        file: "src/webhooks/stripe.ts",
        add: 14,
        del: 3,
        lines: [
          {
            ty: "ctx",
            n: "42",
            text: "export async function handleStripe(req: Req) {",
          },
          {
            ty: "add",
            n: "43",
            text: "  const key = req.headers['idempotency-key'];",
          },
          { ty: "add", n: "44", text: "  if (key) {" },
          {
            ty: "add",
            n: "45",
            text: "    const cached = await store.get(key);",
          },
          {
            ty: "add",
            n: "46",
            text: "    if (cached) return cached.response;",
          },
          { ty: "add", n: "47", text: "  }" },
          { ty: "del", n: "43", text: "  // TODO: idempotency" },
          {
            ty: "ctx",
            n: "48",
            text: "  const event = verify(req.body, req.sig);",
          },
          {
            ty: "add",
            n: "49",
            text: "  const res = await processEvent(event);",
          },
          {
            ty: "add",
            n: "50",
            text: "  if (key) await store.put(key, res, { ttl: 259200 });",
          },
          { ty: "ctx", n: "51", text: "  return res;" },
        ],
      },
      {
        file: "migrations/0042_idem_keys.sql",
        add: 6,
        del: 0,
        lines: [
          { ty: "add", n: "1", text: "CREATE TABLE idem_keys (" },
          { ty: "add", n: "2", text: "  key text PRIMARY KEY," },
          { ty: "add", n: "3", text: "  response jsonb NOT NULL," },
          {
            ty: "add",
            n: "4",
            text: "  created_at timestamptz DEFAULT now()",
          },
          { ty: "add", n: "5", text: ");" },
        ],
      },
    ],
    tests: [
      { name: "types", st: "success", detail: "tsc — 0 errors" },
      { name: "lint", st: "success", detail: "eslint — clean" },
      { name: "unit", st: "running", detail: "42/44 · retry test_replay" },
      { name: "e2e", st: "queued", detail: "Playwright — waiting" },
      { name: "visual", st: "queued", detail: "Storybook diff — waiting" },
    ],
  },
}

/**
 * What a profile is, said once — its role, what it is handed, what it leaves
 * behind. Keyed on the profile because that is the identity: the same seven
 * entries answer for every run, however the brain named the step.
 */
export const PROFILE_META: Record<
  SeedProfile,
  {
    role: "worker" | "lead" | "judge"
    in: Array<[string, string, string?]>
    out: "diff" | Array<[string, string, string?]>
    gate?: "lite" | "full"
    ev: string[]
    live?: string
  }
> = {
  explorer: {
    role: "worker",
    in: [
      ["book", "comuki-mcp · docs"],
      ["terminal", "grep worktree"],
      ["file", "ticket brief"],
    ],
    out: [["file", "findings.md", "context map · risk points"]],
    ev: [
      "read docs: webhooks, idempotency",
      "grep worktree: handlers/stripe_*",
    ],
  },
  planner: {
    role: "lead",
    in: [
      ["file", "findings.md"],
      ["book", "ruleset"],
    ],
    out: [
      ["box", "work item graph", "profiles + dependencies"],
      ["file", "worker brief"],
    ],
    ev: ["emit plan under task", "judge: plan approved"],
  },
  implementer: {
    role: "worker",
    in: [
      ["file", "brief"],
      ["lock", "db-tx @a1b9e0"],
      ["lock", "api-errors @a1b9e0"],
      ["server", "prod snapshot"],
    ],
    out: "diff",
    gate: "full",
    ev: [
      "fetch profile ref implementer@a1b9e0",
      "apply rules: db-tx, api-errors",
      "write handlers/stripe_webhook.ts",
      "ran tsc → 0 errors",
    ],
    live: "ran eslint → running…",
  },
  reviewer: {
    role: "lead",
    in: [
      ["box", "upstream diff"],
      ["book", "ruleset"],
    ],
    out: [["box", "review notes", "blocking · non-blocking"]],
    gate: "lite",
    ev: ["read diff", "check rules: db-tx, no-secrets"],
  },
  tester: {
    role: "judge",
    in: [["box", "feature build"]],
    out: [["flask", "verification gate"]],
    gate: "full",
    ev: ["deterministic layer: types → lint → unit → build"],
  },
  verifier: {
    role: "judge",
    in: [
      ["box", "green gate"],
      ["file", "autonomy policy"],
    ],
    out: [["server", "prod / staging"]],
    gate: "full",
    ev: ["run client checks", "await approval gate"],
  },
  docs: {
    role: "worker",
    in: [["box", "event: shipped to prod"]],
    out: [["book", "knowledge base update"]],
    ev: ["docs profile upserts knowledge"],
  },
}

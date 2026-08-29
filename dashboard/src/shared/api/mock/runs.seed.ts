export type SeedStatus =
  | "running"
  | "success"
  | "failed"
  | "waiting"
  | "queued"
  | "escalated"

export interface SeedStageTemplate {
  key: string
  label: string
  lane?: "a" | "b"
}

export interface SeedStage extends SeedStageTemplate {
  status: SeedStatus
}

export interface SeedRun {
  id: string
  app: string
  title: string
  status: SeedStatus
  current: string
  model: "worker" | "lead"
  cost: number
  tokens: number
  startSec: number
  done?: boolean
  stages: SeedStage[]
}

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

export const STAGE_TEMPLATE: SeedStageTemplate[] = [
  { key: "explore", label: "explore" },
  { key: "plan", label: "plan" },
  { key: "contract", label: "contract" },
  { key: "back", label: "backend", lane: "a" },
  { key: "front", label: "frontend", lane: "b" },
  { key: "sync", label: "sync" },
  { key: "tests", label: "tests" },
  { key: "deploy", label: "deploy" },
  { key: "doc", label: "doc" },
]

function stages(map: Partial<Record<string, SeedStatus>>): SeedStage[] {
  return STAGE_TEMPLATE.map((stage) => ({
    ...stage,
    status: map[stage.key] ?? "queued",
  }))
}

export const RUNS_SEED: SeedRun[] = [
  {
    id: "8f3c2a91",
    app: "billing-api",
    title: "Идемпотентность в обработчике webhook'ов Stripe",
    status: "running",
    current: "back",
    model: "worker",
    cost: 0.42,
    tokens: 18400,
    startSec: 252,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "running",
      front: "success",
    }),
  },
  {
    id: "b3d8a402",
    app: "web-app",
    title: "Скелетоны загрузки на дашборде прогонов",
    status: "running",
    current: "front",
    model: "worker",
    cost: 0.18,
    tokens: 7200,
    startSec: 96,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "running",
    }),
  },
  {
    id: "5b1d7e40",
    app: "web-app",
    title: "Виртуализация таблицы прогонов (react-window)",
    status: "waiting",
    current: "deploy",
    model: "worker",
    cost: 1.08,
    tokens: 42100,
    startSec: 775,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "waiting",
    }),
  },
  {
    id: "2a6f1c33",
    app: "auth-svc",
    title: "Ротация JWT-ключей без даунтайма",
    status: "escalated",
    current: "back",
    model: "lead",
    cost: 2.31,
    tokens: 96800,
    startSec: 540,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "escalated",
      front: "success",
    }),
  },
  {
    id: "9d72b5f0",
    app: "docs-site",
    title: "Миграция на новый theme API",
    status: "failed",
    current: "contract",
    model: "worker",
    cost: 0.19,
    tokens: 8300,
    startSec: 121,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "failed",
    }),
  },
  {
    id: "c40aa2e1",
    app: "worker-pool",
    title: "Ретраи с экспоненциальным бэкоффом",
    status: "success",
    current: "doc",
    model: "worker",
    cost: 0.67,
    tokens: 25600,
    startSec: 510,
    done: true,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "success",
      doc: "success",
    }),
  },
  {
    id: "7e0b9d12",
    app: "billing-api",
    title: "Кэш идемпотентных ответов в Redis",
    status: "queued",
    current: "explore",
    model: "worker",
    cost: 0,
    tokens: 0,
    startSec: 0,
    stages: stages({}),
  },
  {
    id: "a1f4c8d2",
    app: "web-app",
    title: "Тёмная тема для экрана настроек",
    status: "running",
    current: "front",
    model: "worker",
    cost: 0.24,
    tokens: 9800,
    startSec: 140,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "running",
    }),
  },
  {
    id: "4c91e6a7",
    app: "worker-pool",
    title: "Грейсфул-шатдаун при rolling deploy",
    status: "success",
    current: "doc",
    model: "worker",
    cost: 0.53,
    tokens: 21300,
    startSec: 430,
    done: true,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "success",
      doc: "success",
    }),
  },
  {
    id: "d8b2705f",
    app: "auth-svc",
    title: "Rate-limit на эндпоинт логина",
    status: "waiting",
    current: "deploy",
    model: "worker",
    cost: 0.71,
    tokens: 28900,
    startSec: 610,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "waiting",
    }),
  },
  {
    id: "f3e0a91b",
    app: "billing-api",
    title: "Вебхук-ретраи с дедупликацией по event_id",
    status: "running",
    current: "back",
    model: "worker",
    cost: 0.39,
    tokens: 16700,
    startSec: 205,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "running",
      front: "success",
    }),
  },
  {
    id: "6a7d4e22",
    app: "docs-site",
    title: "Поиск по документации (Pagefind)",
    status: "queued",
    current: "explore",
    model: "worker",
    cost: 0,
    tokens: 0,
    startSec: 0,
    stages: stages({}),
  },
  {
    id: "b5c89f01",
    app: "web-app",
    title: "Оптимистичные апдейты в очереди аппрувов",
    status: "escalated",
    current: "sync",
    model: "lead",
    cost: 1.84,
    tokens: 74200,
    startSec: 495,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "escalated",
    }),
  },
  {
    id: "2f6b1a90",
    app: "worker-pool",
    title: "Метрики heartbeat воркеров в Prometheus",
    status: "success",
    current: "doc",
    model: "worker",
    cost: 0.44,
    tokens: 18100,
    startSec: 380,
    done: true,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "success",
      doc: "success",
    }),
  },
  {
    id: "9c3e7b44",
    app: "auth-svc",
    title: "Миграция сессий на Redis-кластер",
    status: "failed",
    current: "tests",
    model: "worker",
    cost: 0.92,
    tokens: 36400,
    startSec: 520,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "failed",
    }),
  },
  {
    id: "e7a05c18",
    app: "billing-api",
    title: "Экспорт инвойсов в CSV/PDF",
    status: "running",
    current: "front",
    model: "worker",
    cost: 0.28,
    tokens: 11200,
    startSec: 165,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "running",
    }),
  },
  {
    id: "1b8f3d67",
    app: "docs-site",
    title: "OG-картинки для страниц гайдов",
    status: "queued",
    current: "explore",
    model: "worker",
    cost: 0,
    tokens: 0,
    startSec: 0,
    stages: stages({}),
  },
  {
    id: "5d24a0f9",
    app: "web-app",
    title: "Виртуализация ленты событий трейса",
    status: "success",
    current: "doc",
    model: "worker",
    cost: 0.61,
    tokens: 24800,
    startSec: 455,
    done: true,
    stages: stages({
      explore: "success",
      plan: "success",
      contract: "success",
      back: "success",
      front: "success",
      sync: "success",
      tests: "success",
      deploy: "success",
      doc: "success",
    }),
  },
]

export const TRACE_SEED: Record<string, SeedTrace> = {
  "8f3c2a91": {
    brief:
      "Сделать обработчик `POST /webhooks/stripe` идемпотентным. Ключ идемпотентности — заголовок `Idempotency-Key`; при повторе вернуть сохранённый ответ, не пере-выполняя side-effects. Ретраи Stripe (до 3 сут) должны быть безопасны.",
    rules: ["api-errors", "db-tx", "no-secrets"],
    revision: { rules: "rules@a1b9e0", sdk: "sdk@2.4.1" },
    events: [
      { t: "00:00", st: "success", text: "Запрос принят · план мозга построен" },
      {
        t: "00:04",
        st: "success",
        text: "Стадия «изучатор» завершена — 4 файла в контексте",
      },
      { t: "00:31", st: "success", text: "Стадия «план» завершена · DAG 8 стадий" },
      {
        t: "01:02",
        st: "success",
        text: "Контракт согласован · применено 3 правила",
      },
      {
        t: "01:05",
        st: "success",
        text: "Лейн «фронт» завершён (no-op, только бек)",
      },
      {
        t: "01:48",
        st: "running",
        text: "Лейн «бек» в работе · правка handler + миграция",
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
          { ty: "add", n: "45", text: "    const cached = await store.get(key);" },
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

export const STAGE_META: Record<
  string,
  {
    role: "worker" | "lead" | "judge"
    in: Array<[string, string, string?]>
    out: "diff" | Array<[string, string, string?]>
    gate?: "lite" | "full"
    ev: string[]
    live?: string
  }
> = {
  explore: {
    role: "worker",
    in: [
      ["book", "comuki-mcp · docs"],
      ["terminal", "grep worktree"],
      ["file", "ticket brief"],
    ],
    out: [["file", "findings.md", "context map · risk points"]],
    ev: ["read docs: webhooks, idempotency", "grep worktree: handlers/stripe_*"],
  },
  plan: {
    role: "lead",
    in: [
      ["file", "findings.md"],
      ["book", "ruleset"],
    ],
    out: [
      ["box", "stage DAG", "parallel lanes: back ∥ front"],
      ["file", "worker brief"],
    ],
    ev: ["build DAG under task", "judge: plan approved"],
  },
  contract: {
    role: "worker",
    in: [
      ["file", "brief"],
      ["box", "DAG"],
    ],
    out: [
      ["file", "openapi.yaml", "committed @c1a2e0"],
      ["git-branch", "feat branch"],
    ],
    gate: "lite",
    ev: ["generate OpenAPI", "commit openapi.yaml @c1a2e0"],
  },
  back: {
    role: "worker",
    in: [
      ["file", "openapi.yaml @c1a2"],
      ["lock", "db-tx @a1b9e0"],
      ["lock", "api-errors @a1b9e0"],
      ["server", "prod snapshot"],
    ],
    out: "diff",
    gate: "full",
    ev: [
      "read contract openapi.yaml@c1a2",
      "apply rules: db-tx, api-errors",
      "write handlers/stripe_webhook.ts",
      "ran tsc → 0 errors",
    ],
    live: "ran eslint → running…",
  },
  front: {
    role: "worker",
    in: [
      ["file", "openapi.yaml @c1a2"],
      ["lock", "ui-tokens @a1b9e0"],
    ],
    out: [["image", "visual baseline", "snapshot accepted"]],
    gate: "full",
    ev: ["read contract openapi.yaml@c1a2", "front lane completed"],
  },
  sync: {
    role: "lead",
    in: [["box", "back + front outputs"]],
    out: [["box", "contract reconcile"]],
    ev: ["reconcile parallel lanes"],
  },
  tests: {
    role: "judge",
    in: [["box", "feature build"]],
    out: [["flask", "verification gate"]],
    gate: "full",
    ev: ["deterministic layer: types → lint → unit → build"],
  },
  deploy: {
    role: "worker",
    in: [
      ["box", "green gate"],
      ["file", "autonomy policy"],
    ],
    out: [["server", "prod / staging"]],
    ev: ["await approval gate"],
  },
  doc: {
    role: "worker",
    in: [["box", "event: shipped to prod"]],
    out: [["book", "knowledge base update"]],
    ev: ["doc-agent updates KB"],
  },
}

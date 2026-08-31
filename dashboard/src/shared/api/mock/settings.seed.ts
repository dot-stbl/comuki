export type SeedAutonomyMode = "auto" | "human"
export type SeedKeyStatus = "ok" | "warn"

export interface SeedApp {
  name: string
  repo: string
  stack: string
  envs: string[]
  deploy: string
}

export interface SeedSwarmRule {
  id: string
  scope: string
  kind: "hard" | "soft"
  ver: string
  desc: string
  body: string
}

export interface SeedAutonomyRow {
  cls: string
  mode: SeedAutonomyMode
}

export interface SeedModelRoute {
  role: "lead" | "worker" | "judge"
  model: string
  use: string
}

export interface SeedProviderKey {
  provider: string
  scope: string
  rotation: string
  status: SeedKeyStatus
  statusLabel: string
}

export interface SeedTrackerProvider {
  id: string
  name: string
  connected: boolean
  meta: string
  last?: string
}

export interface SeedBudgets {
  perTaskUsd: number
  perAppUsd: number
  globalUsd: number
  usedUsd: number
  killSwitch: boolean
  pauseSwarm: boolean
}

export interface SeedSettingsSnapshot {
  apps: SeedApp[]
  rules: SeedSwarmRule[]
  autonomy: SeedAutonomyRow[]
  routing: SeedModelRoute[]
  keys: SeedProviderKey[]
  trackers: SeedTrackerProvider[]
  budgets: SeedBudgets
}

export const SETTINGS_SEED: SeedSettingsSnapshot = {
  apps: [
    {
      name: "billing-api",
      repo: "comuki/billing-api",
      stack: "Node · Fastify · PG",
      envs: ["prod", "staging"],
      deploy: "Fly.io",
    },
    {
      name: "web-app",
      repo: "comuki/web-app",
      stack: "React · Vite · TS",
      envs: ["prod", "staging", "preview"],
      deploy: "Vercel",
    },
    {
      name: "worker-pool",
      repo: "comuki/worker-pool",
      stack: "Go · gRPC",
      envs: ["prod"],
      deploy: "k8s",
    },
    {
      name: "auth-svc",
      repo: "comuki/auth-svc",
      stack: "Node · PG",
      envs: ["prod", "staging"],
      deploy: "Fly.io",
    },
    {
      name: "docs-site",
      repo: "comuki/docs",
      stack: "Astro · MDX",
      envs: ["prod"],
      deploy: "Cloudflare",
    },
  ],
  rules: [
    {
      id: "api-errors",
      scope: "global",
      kind: "hard",
      ver: "a1b9e0",
      desc: "Ошибки API типизированы, с кодом и retry-hint",
      body: "ProblemDetails only. Stable `code` for clients.",
    },
    {
      id: "db-tx",
      scope: "profile:implementer",
      kind: "hard",
      ver: "a1b9e0",
      desc: "Мутации БД — в транзакции, идемпотентны",
      body: "Explicit transaction around every multi-statement write.",
    },
    {
      id: "no-secrets",
      scope: "global",
      kind: "hard",
      ver: "9f2c1a",
      desc: "Секреты только из vault, не в коде и логах",
      body: "Env / secret store only. Never echo credentials.",
    },
    {
      id: "ui-tokens",
      scope: "app:web-app",
      kind: "soft",
      ver: "a1b9e0",
      desc: "Только токены дизайн-системы, без хардкода цветов",
      body: "Comuki CSS tokens; no raw hex in components.",
    },
    {
      id: "test-cov",
      scope: "profile:tester",
      kind: "soft",
      ver: "7b3d10",
      desc: "Покрытие изменённых строк ≥ 80%",
      body: "Diff coverage gate for product repos.",
    },
  ],
  autonomy: [
    { cls: "Documentation / comments", mode: "auto" },
    { cls: "Tests", mode: "auto" },
    { cls: "UI components", mode: "auto" },
    { cls: "Business logic (backend)", mode: "human" },
    { cls: "DB schema / migrations", mode: "human" },
    { cls: "Dependency updates", mode: "human" },
    { cls: "Deploy to production", mode: "human" },
    { cls: "Visual baseline update", mode: "human" },
  ],
  routing: [
    {
      role: "lead",
      model: "primary (large)",
      use: "planning, escalation, repair",
    },
    {
      role: "worker",
      model: "worker",
      use: "routine steps, small edits",
    },
    {
      role: "judge",
      model: "mid-size",
      use: "gates, diff review",
    },
  ],
  keys: [
    {
      provider: "provider-A",
      scope: "lead + worker",
      rotation: "30 days",
      status: "ok",
      statusLabel: "ok",
    },
    {
      provider: "provider-B",
      scope: "judge",
      rotation: "30 days",
      status: "ok",
      statusLabel: "ok",
    },
    {
      provider: "proxy",
      scope: "all roles",
      rotation: "—",
      status: "warn",
      statusLabel: "budget 67%",
    },
  ],
  trackers: [
    {
      id: "jira",
      name: "Jira",
      connected: true,
      meta: "project COMUKI · 14 issues",
      last: "2 min ago",
    },
    {
      id: "github",
      name: "GitHub Issues",
      connected: false,
      meta: "connect to import issues",
    },
    {
      id: "linear",
      name: "Linear",
      connected: false,
      meta: "connect to import issues",
    },
  ],
  budgets: {
    perTaskUsd: 2,
    perAppUsd: 40,
    globalUsd: 220,
    usedUsd: 148.2,
    killSwitch: false,
    pauseSwarm: false,
  },
}

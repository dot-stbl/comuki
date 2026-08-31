import { PROJECT_BY_APP } from "./runs.seed"

export type SeedTaskSource = "jira" | "manual"
export type SeedTaskPriority = "low" | "normal" | "high"
export type SeedTaskStatus = "new" | "queued" | "planning"

export interface SeedTask {
  id: string
  /**
   * The project this ticket belongs to, by id. A backlog item is intake for
   * one project, so taking it is a decision made inside that project — the
   * dispatch on its row answers to this id, not to the shift.
   */
  projectId: string
  source: SeedTaskSource
  title: string
  app: string
  priority: SeedTaskPriority
  status: SeedTaskStatus
  age: string
}

/** A ticket before the seed stamps its project on it. */
type SeedTaskDraft = Omit<SeedTask, "projectId">

export const TASK_APPS = [
  "billing-api",
  "web-app",
  "auth-svc",
  "docs-site",
  "worker-pool",
] as const

/**
 * The backlog, fictional like every other seed here.
 *
 * The project is not written on the rows: it is read off the app through the
 * one mapping both seeds share, so a ticket and the run it becomes can never
 * end up in different projects.
 */
const BACKLOG: SeedTaskDraft[] = [
  {
    id: "COMUKI-128",
    source: "jira",
    title: "Кэш идемпотентных ответов в Redis",
    app: "billing-api",
    priority: "high",
    status: "new",
    age: "8 min",
  },
  {
    id: "COMUKI-127",
    source: "jira",
    title: "Поиск по документации (Pagefind)",
    app: "docs-site",
    priority: "normal",
    status: "new",
    age: "22 min",
  },
  {
    id: "m-3041",
    source: "manual",
    title: "Тёмная тема для экрана настроек",
    app: "web-app",
    priority: "normal",
    status: "queued",
    age: "2 h",
  },
  {
    id: "COMUKI-125",
    source: "jira",
    title: "OG-картинки для страниц гайдов",
    app: "docs-site",
    priority: "low",
    status: "queued",
    age: "1 h",
  },
  {
    id: "COMUKI-124",
    source: "jira",
    title: "Rate-limit на эндпоинт логина",
    app: "auth-svc",
    priority: "high",
    status: "planning",
    age: "3 h",
  },
  {
    id: "m-3039",
    source: "manual",
    title: "Метрики heartbeat воркеров в Prometheus",
    app: "worker-pool",
    priority: "low",
    status: "queued",
    age: "5 h",
  },
]

export const TASKS_SEED: SeedTask[] = BACKLOG.map((task) => ({
  ...task,
  projectId: PROJECT_BY_APP[task.app],
}))

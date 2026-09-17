export interface SeedLearningCandidate {
  id: string
  projectId: string
  topic: string
  observation: string
  proposedRule: string
  sourceRef: string
  repeatCount: number
  status: string
  createdAt: string
}

/* Mock-mode learning queue — the rule suggestions workers queued through
   learning.suggest. projectIds are the seed projects (PROJECT_BY_APP's
   values in runs.seed) so the per-project permission checks answer the way
   they do for plans. */
export const LEARNING_SEED: SeedLearningCandidate[] = [
  {
    id: "lc-01",
    projectId: "p_comuki",
    topic: "build.dotnet",
    observation:
      "Три прогона подряд упали на одном тесте с холодным кешем — тест зелёный после первого прогона.",
    proposedRule:
      "Прогревай кеш (warmup-скрипт) перед dotnet test в свежих контейнерах.",
    sourceRef: "worker:9f21",
    repeatCount: 3,
    status: "pending",
    createdAt: "2026-09-13T11:20:00Z",
  },
  {
    id: "lc-02",
    projectId: "p_atlas",
    topic: "testing.xunit",
    observation:
      "xUnit v3 запускается через dotnet run, а не dotnet test — discovery не работает.",
    proposedRule:
      "Для xUnit v3 используй dotnet run --project, не dotnet test.",
    sourceRef: "worker:2c04",
    repeatCount: 1,
    status: "pending",
    createdAt: "2026-09-13T11:44:00Z",
  },
]

export type SeedKnowledgeKind = "rule" | "doc" | "skill"
export type SeedRuleKind = "hard" | "soft"
export type SeedEvalResult = "pass" | "fail"
export type SeedEvalDelta = "+" | "-" | "="

export interface SeedKnowledgeEntry {
  id: string
  kind: SeedKnowledgeKind
  title: string
  scope: string
  ruleKind?: SeedRuleKind
  revision: string
  pinned: boolean
  summary: string
  body: string
  updated: string
}

export interface SeedEvalCase {
  task: string
  before: SeedEvalResult
  after: SeedEvalResult
  delta: SeedEvalDelta
}

export interface SeedKnowledgeRevision {
  rules: string
  sdk: string
  updated: string
}

export interface SeedKnowledgeSnapshot {
  revision: SeedKnowledgeRevision
  rulesActive: number
  rulesHard: number
  rulesSoft: number
  entries: SeedKnowledgeEntry[]
  eval: SeedEvalCase[]
}

export const KNOWLEDGE_SEED: SeedKnowledgeSnapshot = {
  revision: {
    rules: "rules@a1b9e0",
    sdk: "sdk@2.4.1",
    updated: "2h ago",
  },
  rulesActive: 5,
  rulesHard: 3,
  rulesSoft: 2,
  entries: [
    {
      id: "api-errors",
      kind: "rule",
      title: "api-errors",
      scope: "global",
      ruleKind: "hard",
      revision: "a1b9e0",
      pinned: true,
      summary: "Ошибки API типизированы, с кодом и retry-hint",
      body: "Все HTTP-ошибки возвращают ProblemDetails с стабильным `code`, `title` и retry-hint. Без ad-hoc JSON envelope.",
      updated: "2h ago",
    },
    {
      id: "db-tx",
      kind: "rule",
      title: "db-tx",
      scope: "stage:backend",
      ruleKind: "hard",
      revision: "a1b9e0",
      pinned: true,
      summary: "Мутации БД — в транзакции, идемпотентны",
      body: "Любая write-стадия оборачивает SaveChanges в явную транзакцию. Повторный прогон с тем же ключом идемпотентности не создаёт дублей.",
      updated: "2h ago",
    },
    {
      id: "no-secrets",
      kind: "rule",
      title: "no-secrets",
      scope: "global",
      ruleKind: "hard",
      revision: "9f2c1a",
      pinned: true,
      summary: "Секреты только из vault, не в коде и логах",
      body: "Токены и пароли читаются из env / secret store. Логи и commit message не содержат credential значений.",
      updated: "1d ago",
    },
    {
      id: "ui-tokens",
      kind: "rule",
      title: "ui-tokens",
      scope: "app:web-app",
      ruleKind: "soft",
      revision: "a1b9e0",
      pinned: false,
      summary: "Только токены дизайн-системы, без хардкода цветов",
      body: "Цвета и spacing через CSS-переменные Comuki. Hex/oklch литералы в компонентах запрещены.",
      updated: "2h ago",
    },
    {
      id: "test-cov",
      kind: "rule",
      title: "test-cov",
      scope: "stage:tests",
      ruleKind: "soft",
      revision: "7b3d10",
      pinned: false,
      summary: "Покрытие изменённых строк ≥ 80%",
      body: "Гейт coverage считает только diff. Floor для платформы — 70% line; для изменённых строк в продукте — 80%.",
      updated: "3d ago",
    },
    {
      id: "add-crud-endpoint",
      kind: "skill",
      title: "add-crud-endpoint-aspnet",
      scope: "skills",
      revision: "2.4.1",
      pinned: true,
      summary: "Рецепт CRUD endpoint для ASP.NET",
      body: "Контроллер → DTO → handler → EF config → unit test. Маршруты через ApiRoutes constants.",
      updated: "5d ago",
    },
    {
      id: "architecture-overview",
      kind: "doc",
      title: "Architecture overview",
      scope: "docs",
      revision: "2.4.1",
      pinned: false,
      summary: "Ведущая модель + рой эфемерных воркеров",
      body: "Comuki декомпозирует задачу ведущей моделью и дирижирует воркерами в контейнерах. Общая база знаний по MCP.",
      updated: "1w ago",
    },
  ],
  eval: [
    {
      task: "idempotent-webhook",
      before: "fail",
      after: "pass",
      delta: "+",
    },
    {
      task: "jwt-rotation",
      before: "pass",
      after: "pass",
      delta: "=",
    },
    {
      task: "table-virtualize",
      before: "fail",
      after: "pass",
      delta: "+",
    },
    {
      task: "theme-migrate",
      before: "pass",
      after: "fail",
      delta: "-",
    },
  ],
}

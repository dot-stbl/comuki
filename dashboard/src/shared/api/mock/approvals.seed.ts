export type SeedApprovalType = "plan" | "deploy" | "baseline"
export type SeedRisk = "low" | "medium" | "high"

export interface SeedApproval {
  id: string
  type: SeedApprovalType
  app: string
  run: string
  age: string
  risk: SeedRisk
  summary: string
  assumptions: string[]
}

export const APPROVALS_SEED: SeedApproval[] = [
  {
    id: "ap-01",
    type: "plan",
    app: "billing-api",
    run: "7e0b9d12",
    age: "4 min",
    risk: "medium",
    summary:
      "Кэш идемпотентных ответов в Redis. DAG 7 стадий, 1 лейн. Оценка ~$0.30, ~5 мин.",
    assumptions: [
      "TTL ключей — 72ч, как в Stripe",
      "Redis уже в стеке, новых зависимостей нет",
      "Фронт не затронут",
    ],
  },
  {
    id: "ap-02",
    type: "deploy",
    app: "web-app",
    run: "5b1d7e40",
    age: "12 min",
    risk: "high",
    summary:
      "Виртуализация таблицы прогонов. Зелёный гейт, 0 регрессий. Деплой в production.",
    assumptions: [
      "Изменение только клиентское",
      "Фичефлаг table_virtualized=on",
      "Откат — мгновенный",
    ],
  },
  {
    id: "ap-03",
    type: "baseline",
    app: "docs-site",
    run: "c40aa2e1",
    age: "1 h",
    risk: "medium",
    summary:
      "Обновление visual baseline для компонента Button (новый theme API).",
    assumptions: [
      "Сдвиг радиуса 6px→3px ожидаем",
      "Контраст в норме (AA)",
      "Diff только в Button.stories",
    ],
  },
]

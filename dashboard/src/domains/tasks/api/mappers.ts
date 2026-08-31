import type { Task } from "@/domains/tasks/model/types"
import type { SeedTask } from "@/shared/api/mock/tasks.seed"

export function toTask(seed: SeedTask): Task {
  return {
    id: seed.id,
    projectId: seed.projectId,
    source: seed.source,
    title: seed.title,
    app: seed.app,
    priority: seed.priority,
    status: seed.status,
    age: seed.age,
  }
}

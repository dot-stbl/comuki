import { NATIVE_PROVIDER } from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import type { Task } from "@/domains/tasks/model/types"
import type { SeedTask } from "@/shared/api/mock/tasks.seed"

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

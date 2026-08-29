import type {
  AppRegistryItem,
  AutonomyRow,
  Budgets,
  ModelRoute,
  ProviderKey,
  SettingsSnapshot,
  SwarmRule,
  TrackerProvider,
} from "@/domains/settings/model/types"
import type {
  SeedApp,
  SeedAutonomyRow,
  SeedBudgets,
  SeedModelRoute,
  SeedProviderKey,
  SeedSettingsSnapshot,
  SeedSwarmRule,
  SeedTrackerProvider,
} from "@/shared/api/mock/settings.seed"

export function toApp(seed: SeedApp): AppRegistryItem {
  return {
    name: seed.name,
    repo: seed.repo,
    stack: seed.stack,
    envs: [...seed.envs],
    deploy: seed.deploy,
  }
}

export function toSwarmRule(seed: SeedSwarmRule): SwarmRule {
  return {
    id: seed.id,
    scope: seed.scope,
    kind: seed.kind,
    ver: seed.ver,
    desc: seed.desc,
    body: seed.body,
  }
}

export function toAutonomyRow(seed: SeedAutonomyRow): AutonomyRow {
  return { cls: seed.cls, mode: seed.mode }
}

export function toModelRoute(seed: SeedModelRoute): ModelRoute {
  return { role: seed.role, model: seed.model, use: seed.use }
}

export function toProviderKey(seed: SeedProviderKey): ProviderKey {
  return {
    provider: seed.provider,
    scope: seed.scope,
    rotation: seed.rotation,
    status: seed.status,
    statusLabel: seed.statusLabel,
  }
}

export function toTracker(seed: SeedTrackerProvider): TrackerProvider {
  return {
    id: seed.id,
    name: seed.name,
    connected: seed.connected,
    meta: seed.meta,
    last: seed.last,
  }
}

export function toBudgets(seed: SeedBudgets): Budgets {
  return { ...seed }
}

export function toSettingsSnapshot(
  seed: SeedSettingsSnapshot
): SettingsSnapshot {
  return {
    apps: seed.apps.map(toApp),
    rules: seed.rules.map(toSwarmRule),
    autonomy: seed.autonomy.map(toAutonomyRow),
    routing: seed.routing.map(toModelRoute),
    keys: seed.keys.map(toProviderKey),
    trackers: seed.trackers.map(toTracker),
    budgets: toBudgets(seed.budgets),
  }
}

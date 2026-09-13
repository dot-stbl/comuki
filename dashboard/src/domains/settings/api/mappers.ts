import type {
  AppRegistryItem,
  AutonomyRow,
  Budgets,
  ModelRoute,
  PlatformSettings,
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

/* ------------------------------------------------------------------ *
 * The wire — `GET /api/v1/settings` (`SettingsView`, camelCased by the
 * serializer). The kubb client answers `any` (no response schema in the
 * spec), so the typed claim lives here beside its mapper. The view is
 * already the domain shape — the mapper exists so the wire never reaches a
 * page unclaimed, the same discipline every other domain keeps.
 * ------------------------------------------------------------------ */

/** The host's `SettingsView`. */
export interface PlatformSettingsWire {
  readonly orchestration: {
    readonly lease: {
      readonly leaseTtlSeconds: number
      readonly reapIntervalSeconds: number
      readonly reapGraceSeconds: number
      readonly maxAttempts: number
    }
    readonly escalationTimeout: {
      readonly enabled: boolean
      readonly timeoutSeconds: number
      readonly sweepIntervalSeconds: number
    }
  }
  readonly compute: {
    readonly provider: string
    readonly scale: {
      readonly workerImage: string
      readonly profilesGitRef: string
      readonly minIdle: number
      readonly maxConcurrent: number
      readonly idleTtlSeconds: number
      readonly pollIntervalSeconds: number
    }
  }
  readonly proxy: {
    readonly enabled: boolean
  }
}

/** A settings view onto the snapshot the page renders. */
export function platformSettingsWireToSettings(
  wire: PlatformSettingsWire
): PlatformSettings {
  return {
    orchestration: {
      lease: { ...wire.orchestration.lease },
      escalationTimeout: { ...wire.orchestration.escalationTimeout },
    },
    compute: {
      provider: wire.compute.provider,
      scale: { ...wire.compute.scale },
    },
    proxy: { ...wire.proxy },
  }
}

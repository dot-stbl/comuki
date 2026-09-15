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
import type { SettingsView } from "@/shared/api/_generated/types/SettingsView"

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
 * serializer). The spec declares the response schema now, so the typed
 * claim is the kubb-generated `SettingsView`; its counters arrive as
 * `number | string` (the serializer may read numbers from strings) and
 * this edge normalises them into the plain numbers the snapshot carries.
 * The view is already the domain shape — the mapper exists so the wire
 * never reaches a page unclaimed, the same discipline every other domain
 * keeps.
 * ------------------------------------------------------------------ */

/** A settings view onto the snapshot the page renders. */
export function platformSettingsWireToSettings(
  wire: SettingsView
): PlatformSettings {
  return {
    orchestration: {
      lease: {
        leaseTtlSeconds: Number(wire.orchestration.lease.leaseTtlSeconds),
        reapIntervalSeconds: Number(
          wire.orchestration.lease.reapIntervalSeconds
        ),
        reapGraceSeconds: Number(wire.orchestration.lease.reapGraceSeconds),
        maxAttempts: Number(wire.orchestration.lease.maxAttempts),
      },
      escalationTimeout: {
        enabled: wire.orchestration.escalationTimeout.enabled,
        timeoutSeconds: Number(
          wire.orchestration.escalationTimeout.timeoutSeconds
        ),
        sweepIntervalSeconds: Number(
          wire.orchestration.escalationTimeout.sweepIntervalSeconds
        ),
      },
    },
    compute: {
      provider: wire.compute.provider,
      scale: {
        workerImage: wire.compute.scale.workerImage,
        profilesGitRef: wire.compute.scale.profilesGitRef,
        minIdle: Number(wire.compute.scale.minIdle),
        maxConcurrent: Number(wire.compute.scale.maxConcurrent),
        idleTtlSeconds: Number(wire.compute.scale.idleTtlSeconds),
        pollIntervalSeconds: Number(wire.compute.scale.pollIntervalSeconds),
      },
    },
    proxy: { enabled: wire.proxy.enabled },
  }
}

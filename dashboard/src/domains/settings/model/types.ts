export type AutonomyMode = "auto" | "human"
export type KeyStatus = "ok" | "warn"
export type ModelRole = "lead" | "worker" | "judge"
/**
 * Binding or advisory. Named rather than left inline on `SwarmRule`, because
 * the mark that draws it and the filter that offers it both have to speak the
 * closed set — and a union spelled out in three places is a union that drifts.
 */
export type RuleKind = "hard" | "soft"

/* ------------------------------------------------------------------ *
 * The platform snapshot — `GET /api/v1/settings`, real mode.
 *
 * Every value is `IOptions`-backed: fixed at boot, changed by configuration
 * and a restart, and served back verbatim. There is deliberately no editor
 * shape here — the host has no PUT, and a form over a read-only surface is
 * a phantom save waiting to happen.
 * ------------------------------------------------------------------ */

/** Claim/lease policy from `Orchestration:Lease`. */
export interface LeaseSettings {
  leaseTtlSeconds: number
  reapIntervalSeconds: number
  reapGraceSeconds: number
  maxAttempts: number
}

/** The escalation ratchet, including its ops kill-switch. */
export interface EscalationTimeoutSettings {
  enabled: boolean
  timeoutSeconds: number
  sweepIntervalSeconds: number
}

/** Scale supervisor defaults from `Compute:Scale`. */
export interface ComputeScaleSettings {
  workerImage: string
  profilesGitRef: string
  minIdle: number
  maxConcurrent: number
  idleTtlSeconds: number
  pollIntervalSeconds: number
}

/** The whole read-only snapshot the host serves. */
export interface PlatformSettings {
  orchestration: {
    lease: LeaseSettings
    escalationTimeout: EscalationTimeoutSettings
  }
  compute: {
    provider: string
    scale: ComputeScaleSettings
  }
  proxy: {
    enabled: boolean
  }
}

export interface AppRegistryItem {
  name: string
  repo: string
  stack: string
  envs: string[]
  deploy: string
}

export interface SwarmRule {
  id: string
  scope: string
  kind: RuleKind
  ver: string
  desc: string
  body: string
}

export interface AutonomyRow {
  cls: string
  mode: AutonomyMode
}

export interface ModelRoute {
  role: ModelRole
  model: string
  use: string
}

export interface ProviderKey {
  provider: string
  scope: string
  rotation: string
  status: KeyStatus
  statusLabel: string
}

export interface TrackerProvider {
  id: string
  name: string
  connected: boolean
  meta: string
  last?: string
}

export interface Budgets {
  perTaskUsd: number
  perAppUsd: number
  globalUsd: number
  usedUsd: number
  killSwitch: boolean
  pauseSwarm: boolean
}

export interface SettingsSnapshot {
  apps: AppRegistryItem[]
  rules: SwarmRule[]
  autonomy: AutonomyRow[]
  routing: ModelRoute[]
  keys: ProviderKey[]
  trackers: TrackerProvider[]
  budgets: Budgets
}

export interface SettingsSaveInput {
  budgets: Budgets
  routing: ModelRoute[]
}

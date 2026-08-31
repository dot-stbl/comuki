export type AutonomyMode = "auto" | "human"
export type KeyStatus = "ok" | "warn"
export type ModelRole = "lead" | "worker" | "judge"
/**
 * Binding or advisory. Named rather than left inline on `SwarmRule`, because
 * the mark that draws it and the filter that offers it both have to speak the
 * closed set — and a union spelled out in three places is a union that drifts.
 */
export type RuleKind = "hard" | "soft"

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

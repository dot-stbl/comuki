import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  platformSettingsWireToSettings,
  toSettingsSnapshot,
} from "@/domains/settings/api/mappers"
import type {
  AutonomyMode,
  PlatformSettings,
  SettingsSaveInput,
  SettingsSnapshot,
} from "@/domains/settings/model/types"
import { getApiV1Settings } from "@/shared/api/_generated/clients/getApiV1Settings"
import { SETTINGS_SEED } from "@/shared/api/mock/settings.seed"
import { env } from "@/shared/config/env"

/** The real-mode platform snapshot — the read the host actually serves. */
export const platformSettingsQueryKey = ["settings"] as const

/** The mock control plane — a different shape under a namespaced key. */
export const settingsQueryKey = ["settings", "control-plane"] as const

let mockSettings: SettingsSnapshot | null = null

function ensureSettings(): SettingsSnapshot {
  if (!mockSettings) {
    mockSettings = toSettingsSnapshot(SETTINGS_SEED)
  }
  return mockSettings
}

async function getSettings(): Promise<SettingsSnapshot> {
  if (!env.useMock) {
    throw new Error("settings API not implemented — set VITE_USE_MOCK=true")
  }
  return {
    ...ensureSettings(),
    apps: [...ensureSettings().apps],
    rules: [...ensureSettings().rules],
    autonomy: [...ensureSettings().autonomy],
    routing: ensureSettings().routing.map((route) => ({ ...route })),
    keys: [...ensureSettings().keys],
    trackers: [...ensureSettings().trackers],
    budgets: { ...ensureSettings().budgets },
  }
}

async function saveSettings(
  input: SettingsSaveInput
): Promise<SettingsSnapshot> {
  if (!env.useMock) {
    throw new Error("settings API not implemented — set VITE_USE_MOCK=true")
  }
  const current = ensureSettings()
  mockSettings = {
    ...current,
    budgets: { ...input.budgets },
    routing: input.routing.map((route) => ({ ...route })),
  }
  return getSettings()
}

/** The two stops on the proxy budget, addressed one at a time. */
export type SettingsStopKind = "killSwitch" | "pauseSwarm"

/**
 * Throwing one stop, or standing it down.
 *
 * Deliberately not folded into `saveSettings`: a stop is an act with a sentence
 * of its own, performed the moment it is pressed, and routing it through "edit
 * the budgets form, then save" would make the emergency brake a form field —
 * which is the shape this screen just lost on purpose. The real control plane
 * will expose these as their own endpoints; this is that shape minus the wire.
 */
async function toggleSettingsStop(
  kind: SettingsStopKind,
  on: boolean
): Promise<SettingsSnapshot> {
  if (!env.useMock) {
    throw new Error("settings API not implemented — set VITE_USE_MOCK=true")
  }
  mockSettings = {
    ...ensureSettings(),
    budgets: { ...ensureSettings().budgets, [kind]: on },
  }
  return getSettings()
}

/**
 * Who decides one class of change — flipped live, one class at a time.
 *
 * The same focused-act shape as the stops: the row's control writes exactly the
 * row it names, not a whole-settings save that would have to round-trip every
 * other section on the screen to change one word.
 */
async function setAutonomyMode(
  cls: string,
  mode: AutonomyMode
): Promise<SettingsSnapshot> {
  if (!env.useMock) {
    throw new Error("settings API not implemented — set VITE_USE_MOCK=true")
  }
  mockSettings = {
    ...ensureSettings(),
    autonomy: ensureSettings().autonomy.map((row) =>
      row.cls === cls ? { ...row, mode } : row
    ),
  }
  return getSettings()
}

export function useSettingsQuery() {
  return useQuery({
    queryKey: settingsQueryKey,
    queryFn: getSettings,
    // The control plane is the mock's own description; real mode renders the
    // platform snapshot instead and never asks this query to run (its
    // queryFn is an honest throw outside mock).
    enabled: env.useMock,
  })
}

/**
 * The platform snapshot, `GET /api/v1/settings` — the one settings read the
 * host serves in real mode. Every value is boot-fixed; there is no PUT and
 * the page says so rather than offering a save that cannot land.
 */
export function usePlatformSettingsQuery() {
  return useQuery({
    queryKey: platformSettingsQueryKey,
    queryFn: async (): Promise<PlatformSettings> => {
      const wire = await getApiV1Settings()
      return platformSettingsWireToSettings(wire)
    },
    enabled: !env.useMock,
  })
}

export function useSettingsSaveMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: saveSettings,
    onSuccess: (next) => {
      queryClient.setQueryData(settingsQueryKey, next)
    },
  })
}

export function useSettingsStopMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      kind,
      on,
    }: {
      kind: SettingsStopKind
      on: boolean
    }) => toggleSettingsStop(kind, on),
    onSuccess: (next) => {
      queryClient.setQueryData(settingsQueryKey, next)
    },
  })
}

export function useSettingsAutonomyMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ cls, mode }: { cls: string; mode: AutonomyMode }) =>
      setAutonomyMode(cls, mode),
    onSuccess: (next) => {
      queryClient.setQueryData(settingsQueryKey, next)
    },
  })
}

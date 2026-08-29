import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { toSettingsSnapshot } from "@/domains/settings/api/mappers"
import type {
  SettingsSaveInput,
  SettingsSnapshot,
} from "@/domains/settings/model/types"
import { SETTINGS_SEED } from "@/shared/api/mock/settings.seed"
import { env } from "@/shared/config/env"

export const settingsQueryKey = ["settings"] as const

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

export function useSettingsQuery() {
  return useQuery({
    queryKey: settingsQueryKey,
    queryFn: getSettings,
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

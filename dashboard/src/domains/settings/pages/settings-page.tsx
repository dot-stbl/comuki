import {
  Box,
  CheckCheck,
  Cpu,
  Database,
  GitBranch,
  ShieldAlert,
  SlidersHorizontal,
} from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import {
  useSettingsQuery,
  useSettingsSaveMutation,
} from "@/domains/settings/api/queries"
import type { BudgetFormValues } from "@/domains/settings/model/budget-form"
import type { RoutingFormValues } from "@/domains/settings/model/routing-form"
import type { ModelRoute, SettingsSnapshot } from "@/domains/settings/model/types"
import { AppsPanel } from "@/domains/settings/ui/apps-panel"
import { AutonomyPanel } from "@/domains/settings/ui/autonomy-panel"
import { BudgetsPanel } from "@/domains/settings/ui/budgets-panel"
import { KeysPanel } from "@/domains/settings/ui/keys-panel"
import { RoutingPanel } from "@/domains/settings/ui/routing-panel"
import { RulesPanel } from "@/domains/settings/ui/rules-panel"
import { TrackerPanel } from "@/domains/settings/ui/tracker-panel"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Skeleton } from "@/shared/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/tabs"

const SECTIONS = [
  { id: "apps", label: "Apps", icon: Box },
  { id: "rules", label: "Rules", icon: CheckCheck },
  { id: "autonomy", label: "Autonomy", icon: SlidersHorizontal },
  { id: "routing", label: "Routing", icon: Cpu },
  { id: "budgets", label: "Budgets", icon: ShieldAlert },
  { id: "keys", label: "Keys", icon: Database },
  { id: "tracker", label: "Tracker", icon: GitBranch },
] as const

function applyRouting(
  current: ModelRoute[],
  values: RoutingFormValues
): ModelRoute[] {
  return current.map((route) => {
    if (route.role === "lead") {
      return { ...route, model: values.leadModel }
    }
    if (route.role === "worker") {
      return { ...route, model: values.workerModel }
    }
    return { ...route, model: values.judgeModel }
  })
}

function applyBudgets(
  snapshot: SettingsSnapshot,
  values: BudgetFormValues
): SettingsSnapshot["budgets"] {
  return {
    ...snapshot.budgets,
    perTaskUsd: values.perTaskUsd,
    perAppUsd: values.perAppUsd,
    globalUsd: values.globalUsd,
    killSwitch: values.killSwitch,
    pauseSwarm: values.pauseSwarm,
  }
}

export function SettingsPage() {
  const { data, isLoading, isError, error } = useSettingsQuery()
  const save = useSettingsSaveMutation()

  const onSaveRouting = (values: RoutingFormValues) => {
    if (!data) {
      return
    }
    save.mutate(
      {
        budgets: data.budgets,
        routing: applyRouting(data.routing, values),
      },
      {
        onSuccess: () => {
          toast.success("Routing saved", {
            description: "role → model map updated (mock)",
          })
        },
      }
    )
  }

  const onSaveBudgets = (values: BudgetFormValues) => {
    if (!data) {
      return
    }
    save.mutate(
      {
        budgets: applyBudgets(data, values),
        routing: data.routing,
      },
      {
        onSuccess: () => {
          toast.success("Budgets saved", {
            description: values.killSwitch
              ? "kill-switch ON · swarm claims blocked"
              : values.pauseSwarm
                ? "swarm paused"
                : "caps updated (mock)",
          })
        },
      }
    )
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
            configure / settings
          </div>
          <h1 className="text-lg font-semibold tracking-tight">Settings</h1>
          <p className="font-mono text-xs text-muted-foreground">
            control plane configuration
          </p>
        </header>

        {isLoading ? (
          <div className="flex flex-col gap-3">
            <Skeleton className="h-8 w-full max-w-xl rounded-lg" />
            <Skeleton className="h-64 rounded-lg" />
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load settings</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {data ? (
          <Tabs defaultValue="apps">
            <TabsList variant="line" className="flex h-auto w-full flex-wrap">
              {SECTIONS.map((section) => {
                const Icon = section.icon
                return (
                  <TabsTrigger key={section.id} value={section.id}>
                    <Icon />
                    {section.label}
                  </TabsTrigger>
                )
              })}
            </TabsList>

            <TabsContent value="apps" className="mt-4">
              <AppsPanel apps={data.apps} />
            </TabsContent>
            <TabsContent value="rules" className="mt-4">
              <RulesPanel rules={data.rules} />
            </TabsContent>
            <TabsContent value="autonomy" className="mt-4">
              <AutonomyPanel rows={data.autonomy} />
            </TabsContent>
            <TabsContent value="routing" className="mt-4">
              <RoutingPanel
                routes={data.routing}
                busy={save.isPending}
                onSave={onSaveRouting}
              />
            </TabsContent>
            <TabsContent value="budgets" className="mt-4">
              <BudgetsPanel
                budgets={data.budgets}
                busy={save.isPending}
                onSave={onSaveBudgets}
              />
            </TabsContent>
            <TabsContent value="keys" className="mt-4">
              <KeysPanel keys={data.keys} />
            </TabsContent>
            <TabsContent value="tracker" className="mt-4">
              <TrackerPanel trackers={data.trackers} />
            </TabsContent>
          </Tabs>
        ) : null}
      </div>
    </AppShell>
  )
}

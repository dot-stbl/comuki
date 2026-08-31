import type { ComponentType } from "react"
import {
  Box,
  CheckCheck,
  Cpu,
  Database,
  GitBranch,
  RotateCw,
  ShieldAlert,
  SlidersHorizontal,
} from "lucide-react"
import { Tab, TabList, TabPanel, Tabs } from "react-aria-components"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useSettingsQuery,
  useSettingsSaveMutation,
} from "@/domains/settings/api/queries"
import type { BudgetFormValues } from "@/domains/settings/model/budget-form"
import type { RoutingFormValues } from "@/domains/settings/model/routing-form"
import { isSettingsTab, type SettingsTab } from "@/domains/settings/model/tabs"
import type {
  ModelRoute,
  SettingsSnapshot,
} from "@/domains/settings/model/types"
import { AppsPanel } from "@/domains/settings/ui/apps-panel"
import { AutonomyPanel } from "@/domains/settings/ui/autonomy-panel"
import { BudgetsPanel } from "@/domains/settings/ui/budgets-panel"
import { KeysPanel } from "@/domains/settings/ui/keys-panel"
import { RoutingPanel } from "@/domains/settings/ui/routing-panel"
import { RulesPanel } from "@/domains/settings/ui/rules-panel"
import { TrackerPanel } from "@/domains/settings/ui/tracker-panel"
import { useCan } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./settings-page.module.css"

const SKELETON_WIDTHS = ["48%", "72%", "56%", "84%", "42%"]

interface SectionMeta {
  id: SettingsTab
  label: string
  icon: ComponentType<{ className?: string }>
}

const SECTIONS: SectionMeta[] = [
  { id: "apps", label: "apps", icon: Box },
  { id: "rules", label: "rules", icon: CheckCheck },
  { id: "autonomy", label: "autonomy", icon: SlidersHorizontal },
  { id: "routing", label: "routing", icon: Cpu },
  { id: "budgets", label: "budgets", icon: ShieldAlert },
  { id: "keys", label: "keys", icon: Database },
  { id: "tracker", label: "tracker", icon: GitBranch },
]

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

export interface SettingsPageProps {
  /** Which section is showing. In the URL, so it can be linked and returned to. */
  tab: SettingsTab
  onTabChange: (tab: SettingsTab) => void
}

/**
 * The control plane, in seven sections.
 *
 * Three of them are read-only and say so on their own title line, because their
 * source is somewhere else entirely — the apps registry and the swarm rules
 * live in the client's git and change by commit, and the provider keys come
 * from env with rotation running inside the proxy. A panel that offered no
 * controls and no explanation would read as a screen somebody forgot to finish,
 * which is exactly the wrong reading: those three are complete.
 *
 * Sections rather than seven rail items because none of them is a destination —
 * nobody comes to the platform to look at `keys`, they come to settings and
 * then find keys. The showing section lives in the URL rather than in this
 * component, so a cap or a routing map is a thing one operator can send another
 * and so a save that refetches cannot drop somebody back on `apps`.
 *
 * Every panel here that writes writes a *live* setting — one the control plane
 * reloads without a git round-trip — so one `settings.live` answer serves all of
 * them. The panels whose source is git have nothing to gate.
 */
export function SettingsPage({ tab, onTabChange }: SettingsPageProps) {
  const { data, isLoading, isError, error, refetch } = useSettingsQuery()
  const save = useSettingsSaveMutation()

  const mayEdit = useCan("settings.live")

  const onSaveRouting = (values: RoutingFormValues) => {
    if (!data || !mayEdit.allowed) {
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
    if (!data || !mayEdit.allowed) {
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
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "settings" }]}
          title="Settings"
          summary="control plane configuration"
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="settings-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Settings did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="settings-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {data ? (
          <Tabs
            className={styles.tabs}
            selectedKey={tab}
            onSelectionChange={(key) => {
              if (isSettingsTab(key)) {
                onTabChange(key)
              }
            }}
          >
            <TabList aria-label="Settings sections" className={styles.tabList}>
              {SECTIONS.map((section) => {
                const Icon = section.icon
                return (
                  <Tab
                    key={section.id}
                    id={section.id}
                    className={styles.tab}
                    data-test={`tab-${section.id}`}
                  >
                    {/* The glyph rides the word rather than replacing it: seven
                        sections of bare icons is a rebus, and this strip is
                        navigation rather than a row of acts. */}
                    <Icon className={styles.tabIcon} aria-hidden="true" />
                    {section.label}
                  </Tab>
                )
              })}
            </TabList>

            <TabPanel id="apps" className={styles.tabPanel}>
              <AppsPanel apps={data.apps} />
            </TabPanel>
            <TabPanel id="rules" className={styles.tabPanel}>
              <RulesPanel rules={data.rules} />
            </TabPanel>
            <TabPanel id="autonomy" className={styles.tabPanel}>
              <AutonomyPanel rows={data.autonomy} />
            </TabPanel>
            <TabPanel id="routing" className={styles.tabPanel}>
              <RoutingPanel
                routes={data.routing}
                busy={save.isPending}
                save={mayEdit}
                onSave={onSaveRouting}
              />
            </TabPanel>
            <TabPanel id="budgets" className={styles.tabPanel}>
              <BudgetsPanel
                budgets={data.budgets}
                busy={save.isPending}
                save={mayEdit}
                onSave={onSaveBudgets}
              />
            </TabPanel>
            <TabPanel id="keys" className={styles.tabPanel}>
              <KeysPanel keys={data.keys} />
            </TabPanel>
            <TabPanel id="tracker" className={styles.tabPanel}>
              <TrackerPanel trackers={data.trackers} edit={mayEdit} />
            </TabPanel>
          </Tabs>
        ) : null}
      </div>
    </AppShell>
  )
}

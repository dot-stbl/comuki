import { useMemo } from "react"
import { useNavigate, useSearch } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"

import { ProcedureWorkbench } from "@/domains/procedures/procedure-workbench"
import {
  LIVE_RUN_NEEDS_DECISION,
  REPLAY_OBSERVED_DRIFT,
  STANDARD_FEATURE_PROCEDURE,
  STUDIO_DEFAULT,
} from "@/domains/procedures/model/procedure-fixtures"
import type { ProcedureMode } from "@/domains/procedures/model/types"

/**
 * Procedures page — the operator's view of a procedure in three
 * panels (Studio / Live run / Replay).
 *
 * One route, three panels: the page reads the workbench mode from a
 * URL search param (`?mode=studio|live|replay`) so the three readings
 * share one decision vocabulary and one chrome. The same procedure is
 * rendered three ways — authoring is the closed-form definition,
 * live is an admitted run, replay is the planned-vs-observed timeline.
 *
 * Mock-first: under `VITE_USE_MOCK=true` the workbench reads the
 * canonical fixtures in `model/procedure-fixtures.ts`. Real mode wires
 * the kubb-generated procedures endpoints (PHASE 2 follow-up).
 *
 * The procedure key comes from the URL path (`/procedures/$procedureKey`);
 * the page falls back to the standard-feature fixture when the key is
 * missing or unknown.
 */
export function ProceduresPage() {
  const { t: tShell } = useTranslation("shell")
  const { t } = useTranslation("procedures")
  const navigate = useNavigate()
  const routeParams = useSearch({ strict: false }) as Record<string, string | undefined>

  // Mode from URL — defaults to studio when missing or unrecognised.
  const mode = normalizeMode(routeParams["mode"])

  // The fixtures' graph lives in `model/procedure-fixtures.ts`; this is
  // the contract the storybook stories already validate. We render the
  // standard-feature flow as the default Studio fixture and switch to
  // the live-run / replay fixtures when the URL asks for those panels.
  const fixture = useMemo(() => {
    if (mode === "live") return LIVE_RUN_NEEDS_DECISION
    if (mode === "replay") return REPLAY_OBSERVED_DRIFT
    return STUDIO_DEFAULT
  }, [mode])

  // Tab click — moves the mode in the URL without a route change.
  const onModeChange = (next: ProcedureMode) => {
    void navigate({
      to: "/procedures/$procedureKey",
      params: { procedureKey: routeParams["procedureKey"] ?? "standard-feature" },
      search: { mode: next },
    })
  }

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: tShell("crumb.procedures"), to: "/procedures" },
            { label: routeParams["procedureKey"] ?? "standard-feature" },
          ]}
          title={t("title", {
            procedureKey: routeParams["procedureKey"] ?? "standard-feature",
          })}
          summary={t("description")}
        />
      }
    >
      <ProcedureWorkbench
        fixture={fixture}
        flow={mode === "studio" ? STANDARD_FEATURE_PROCEDURE : undefined}
        initialMode={mode}
        // The tab strip inside the workbench calls this back to move the
        // URL without a route change. Same shape the runs detail page
        // uses for its tab strip — the page owns the chrome, the
        // workbench owns the panel selection.
        onModeChange={onModeChange}
      />
    </AppShell>
  )
}

function normalizeMode(input: string | undefined): ProcedureMode {
  if (input === "live" || input === "replay") return input
  return "studio"
}
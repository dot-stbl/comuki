import { useTranslation } from "react-i18next"
import { RotateCw, ArrowLeft } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { FormPage } from "@/app/layout/form-page"
import { useWorkTaskQuery } from "@/domains/work/api/queries"
import { isWorkTaskSummary } from "@/domains/work/api/mappers"
import {
  type WorkTaskResolutionOutcome,
  type WorkTaskSourceRefView,
  type WorkTaskStatus,
  type WorkTaskSummary,
} from "@/domains/work/model/types"
import { projectOf, useSession } from "@/shared/session"
import {
  Button,
  Fact,
  FactList,
  Notice,
  ScreenState,
  Skeleton,
  Tooltip,
  buttonClass,
} from "@/shared/ui"
import { requestFailureMessage } from "@/shared/api/problem"

import styles from "./work-task-detail-page.module.css"

const SKELETON_WIDTHS = ["58%", "82%", "44%", "70%", "36%"]

export interface WorkTaskDetailPageProps {
  /** From the path. A work task is a thing, so looking at one has an address. */
  workTaskId: string
}

/**
 * Status → surface tone.
 *
 * The four tones this product distinguishes, and the ones WorkTask statuses
 * actually mean: Draft and Ready are quiet (idle), Active and Blocked are
 * present (warn for Blocked because the task is stuck, default for Active
 * because the task is in motion), Resolved is done (ok), Cancelled is
 * stopped without closure (bad). The two readings that are not on the wire
 * today — `Replaced` and `Waived` — land as `ok` because they share
 * Resolved's "this task is finished" reading.
 */
function statusTone(
  status: WorkTaskStatus
): "idle" | "ok" | "warn" | "bad" {
  switch (status) {
    case "Draft":
    case "Ready":
      return "idle"
    case "Active":
      return "ok"
    case "Blocked":
      return "warn"
    case "Resolved":
      return "ok"
    case "Cancelled":
      return "bad"
  }
}

/** One work task, and what is decided about it. */
export function WorkTaskDetailPage({ workTaskId }: WorkTaskDetailPageProps) {
  const { t } = useTranslation("work")
  const { t: tShell } = useTranslation("shell")
  const { data, isLoading, isError, error, refetch } =
    useWorkTaskQuery(workTaskId)
  const session = useSession()

  // The query answered and the answer was "no such task" — a different reading
  // from a failed read, and the only one of the two a Retry cannot help with.
  const missing = !isLoading && !isError && !data

  // The query answered with a payload the page cannot render — the
  // id the operator asked for is effectively not in the host. Same
  // notFound reading as `missing`, because a wire without `id` and
  // `status` is the same answer as "no row": the id the operator
  // came in with is not on the screen.
  const malformed = !isLoading && !isError && data !== undefined && !isWorkTaskSummary(data)

  const crumbs = [
    { label: tShell("crumb.liveRuns"), to: "/runs" },
    { label: `task_${workTaskId}` },
  ]

  if (isLoading) {
    return (
      <FormPage
        title={t("detailPage.titleFallback")}
        crumbs={crumbs}
      >
        <Skeleton lines={SKELETON_WIDTHS} data-test="work-loading" />
      </FormPage>
    )
  }

  if (isError) {
    return (
      <FormPage
        title={t("detailPage.titleFallback")}
        crumbs={crumbs}
      >
        <ScreenState
          kind="error"
          title={t("detailPage.errorTitle")}
          description={requestFailureMessage(
            error,
            t("errors.unknown", { ns: "common" })
          )}
          action={
            <Tooltip content={t("actions.retry", { ns: "common" })}>
              <Button
                size="icon-sm"
                data-test="work-retry"
                aria-label={t("actions.retry", { ns: "common" })}
                onClick={() => {
                  void refetch()
                }}
              >
                <RotateCw aria-hidden="true" />
              </Button>
            </Tooltip>
          }
          data-test="work-error"
        />
      </FormPage>
    )
  }

  if (missing || malformed) {
    /* The host answered and this id was not in it. The state names the
     * missing id, because "not found" without it is a screen that cannot be
     * acted on: the operator arrived from a link somebody else wrote, and
     * the id is the only part of it they can take back to whoever wrote it.
     * A way out rather than a retry — asking again would ask the same
     * question. */
    return (
      <FormPage
        title={t("detailPage.titleFallback")}
        crumbs={crumbs}
      >
        <ScreenState
          kind="notFound"
          title={t("detailPage.notFoundTitle")}
          description={t("detailPage.notFoundBody")}
          hint={workTaskId}
          data-test="work-not-found"
          action={
            <Tooltip content={t("detailPage.backToTasks")}>
              <Link
                to="/tasks"
                search={{}}
                data-test="work-not-found-back"
                aria-label={t("detailPage.backToTasks")}
                className={buttonClass({ size: "icon-sm" })}
              >
                <ArrowLeft aria-hidden="true" />
              </Link>
            </Tooltip>
          }
        />
      </FormPage>
    )
  }

  /* `data` is a real WorkTaskSummary past this branch — the three gates above
   * (loading, error, missing-or-malformed) have already returned, and the
   * `isWorkTaskSummary` runtime guard rejected any shape the page cannot
   * render. The cast is a type-narrower for TS only; the guard ran. */
  const task = data as WorkTaskSummary

  /* The project key in the summary is the operator's word for it — exactly
   * what the runs list shows. A row of facts underneath it that named the
   * project's guid would be two screens saying the same thing in two
   * languages. */
  const projectKey =
    projectOf(session, task.projectId)?.key ?? task.projectId

  return (
    <FormPage
      title={task.title}
      crumbs={crumbs}
      summary={
        <span className={styles.summary} data-test="work-summary">
          {projectKey} · {t(`status.${task.status}` as const)}
        </span>
      }
      actions={
        <span
          className={styles.factStatusBadge}
          data-tone={statusTone(task.status)}
          data-test="work-status-badge"
        >
          {t(`status.${task.status}` as const)}
        </span>
      }
    >
      <section data-test="work-facts">
        <FactList framed>
          <Fact name={t("detailPage.factProject")}>{projectKey}</Fact>
          <Fact name={t("detailPage.factStatus")}>
            {t(`status.${task.status}` as const)}
          </Fact>
          <Fact
            name={t("detailPage.factOutcome")}
            absent={task.resolutionOutcome === null}
          >
            {task.resolutionOutcome === null
              ? "—"
              : t(
                  `outcome.${task.resolutionOutcome}` as const,
                  {
                    defaultValue:
                      outcomeFallback[task.resolutionOutcome],
                  }
                )}
          </Fact>
          <Fact name={t("detailPage.factAttempt")}>{task.attemptOrdinal}</Fact>
          <Fact
            name={t("detailPage.factActiveAttempt")}
            absent={task.activeAttemptId === null}
          >
            {task.activeAttemptId ?? t("detailPage.factActiveAttemptEmpty")}
          </Fact>
          <Fact name={t("detailPage.factVisibility")}>
            {t(`visibility.${task.visibility}` as const)}
          </Fact>
          <Fact
            name={t("detailPage.factMission")}
            absent={task.missionId === null}
          >
            {task.missionId ?? t("detailPage.factMissionEmpty")}
          </Fact>
          <Fact name={t("detailPage.factBriefVersion")}>
            {task.briefVersion}
          </Fact>
          <Fact name={t("detailPage.factCreatedAt")}>{task.createdAt}</Fact>
          <Fact name={t("detailPage.factUpdatedAt")}>{task.updatedAt}</Fact>
        </FactList>
      </section>

      <section data-test="work-source-refs">
        <ul className={styles.sourceList}>
          {task.sourceRefs.length === 0 ? (
            <li>
              <Notice data-test="work-no-source-refs">
                {t("detailPage.sourceRefsEmpty")}
              </Notice>
            </li>
          ) : (
            task.sourceRefs.map((source) => (
              <SourceRefRow
                key={source.externalId}
                source={source}
                kindLabel={t(`sourceKind.${source.kind}` as const)}
              />
            ))
          )}
        </ul>
      </section>
    </FormPage>
  )
}

/** Outcomes the host surfaces that the dashboard's vocabulary may not yet
 * know about — the dashboard reads `WorkTaskResolutionOutcome` from the wire
 * and renders the wire word verbatim if the locale is missing it. */
const outcomeFallback: Record<WorkTaskResolutionOutcome, string> = {
  Succeeded: "Succeeded",
  Waived: "Waived",
  Replaced: "Replaced",
  Failed: "Failed",
}

interface SourceRefRowProps {
  source: WorkTaskSourceRefView
  kindLabel: string
}

function SourceRefRow({ source, kindLabel }: SourceRefRowProps) {
  return (
    <li
      className={styles.sourceItem}
      data-test="work-source-ref"
      data-source-kind={source.kind}
    >
      <span className={styles.sourceKind}>{kindLabel}</span>
      <code className={styles.sourceExternalId} data-test="work-source-id">
        {source.externalId}
      </code>
      {source.displayName ? (
        <span className={styles.sourceDisplayName}>{source.displayName}</span>
      ) : null}
    </li>
  )
}
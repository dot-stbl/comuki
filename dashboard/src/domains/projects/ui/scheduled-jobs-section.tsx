import { useState } from "react"
import { Pause, Play, Plus, RotateCw, Trash2 } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

import {
  useCreateScheduledJobMutation,
  useDeleteScheduledJobMutation,
  useScheduledJobsQuery,
  useSetScheduledJobEnabledMutation,
  type ScheduledJob,
} from "@/domains/projects/api/scheduled-jobs"
import { requestFailureMessage } from "@/shared/api/problem"
import {
  Button,
  ConfirmDialog,
  CronField,
  FormDialog,
  ScreenState,
  Section,
  Skeleton,
  StatusBadge,
  TextField,
  TextareaField,
  Tooltip,
} from "@/shared/ui"

import styles from "./scheduled-jobs-section.module.css"

/**
 * The cron surface of one project — what the platform starts on its own.
 *
 * Rendered as a region of the project page beside the record and the roles,
 * because a schedule is a fact *about the project*, not a screen of its
 * own: there is nothing here that outgrows a region until a project has
 * enough schedules to be somebody's whole job.
 *
 * The two acts (pause/resume, delete) and the create control are gated by
 * the page on `sources.edit` — the dashboard's session vocabulary has no
 * scheduler keys yet (the backend speaks `scheduler:read` / `scheduler:
 * write`), and the intake-edit permission is the honest neighbour.
 */

/** `2026-09-13T03:00:00Z` → `2026-09-13 03:00 UTC` — honest to the wire. */
function formatWhen(iso: string): string {
  return `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC`
}

/** One bar per schedule the section is about to draw. */
const SKELETON_WIDTHS = ["62%", "48%", "71%"]

/** Reads `title` out of the brief JSON; the raw document stays a value. */
function briefTitle(briefJson: string): string {
  try {
    const parsed = JSON.parse(briefJson) as { title?: unknown }
    return typeof parsed.title === "string" ? parsed.title : briefJson
  } catch {
    return briefJson
  }
}

export interface ScheduledJobsSectionProps {
  projectId: string
  /** Whether this session may create, pause and delete (`sources.edit`). */
  canEdit: boolean
}

export function ScheduledJobsSection({
  projectId,
  canEdit,
}: ScheduledJobsSectionProps) {
  const { t } = useTranslation("projects")
  const jobs = useScheduledJobsQuery(projectId)
  const createJob = useCreateScheduledJobMutation()
  const setEnabled = useSetScheduledJobEnabledMutation()
  const deleteJob = useDeleteScheduledJobMutation()

  const [createOpen, setCreateOpen] = useState(false)
  const [cron, setCron] = useState("")
  const [profileKey, setProfileKey] = useState("")
  const [briefJson, setBriefJson] = useState('{"title":""}')
  const [pendingDelete, setPendingDelete] = useState<ScheduledJob | null>(null)

  const createInvalid =
    cron.trim().length === 0 || profileKey.trim().length === 0

  function submitCreate(): void {
    createJob.mutate(
      {
        projectId,
        cronExpression: cron.trim(),
        profileKey: profileKey.trim(),
        briefJson,
      },
      {
        onSuccess: () => {
          setCreateOpen(false)
          setCron("")
          setProfileKey("")
          setBriefJson('{"title":""}')
        },
      }
    )
  }

  return (
    <Section
      id="project-jobs"
      title={t("jobs.section")}
      note={
        jobs.data
          ? t("jobs.configured", { count: jobs.data.length })
          : jobs.isError
            ? /* Not "counting": nothing is counting any more. */
              t("jobs.unknown")
            : /* Absent while loading, not zero — see the cost hand-off. */
              t("jobs.counting")
      }
      data-test="project-jobs"
    >
      {/* Three answers, three states, and they are disjoint. The section used
          to ask `jobs.data && jobs.data.length > 0` and nothing else, so an
          unanswered read — loading, or failed — fell straight through into the
          sentence that says no schedule exists. That is the one thing this
          section must not say while it does not yet know. */}
      {jobs.isLoading ? (
        <Skeleton
          lines={SKELETON_WIDTHS}
          inset="none"
          label={t("jobs.loading")}
          data-test="project-jobs-loading"
        />
      ) : null}

      {jobs.isError ? (
        <ScreenState
          kind="error"
          title={t("jobs.errorTitle")}
          description={requestFailureMessage(jobs.error, t("errors.unknown"))}
          inset="none"
          data-test="project-jobs-error"
          action={
            <Tooltip content={t("actions.retry")}>
              <Button
                size="icon-sm"
                data-test="project-jobs-retry"
                aria-label={t("actions.retry")}
                onClick={() => {
                  void jobs.refetch()
                }}
              >
                <RotateCw aria-hidden="true" />
              </Button>
            </Tooltip>
          }
        />
      ) : null}

      {jobs.data && jobs.data.length > 0 ? (
        <ul className={styles.jobs} data-test="project-jobs-list">
          {jobs.data.map((job) => (
            <li key={job.id} className={styles.job}>
              <span className={styles.jobWhat}>
                <span className={styles.jobProfile}>{job.profileKey}</span>
                <span className={styles.jobBrief}>
                  {briefTitle(job.briefJson)}
                </span>
              </span>
              <span className={styles.jobCron}>{job.cronExpression}</span>
              <span className={styles.jobWhen}>
                next {formatWhen(job.nextFireAt)}
              </span>
              <StatusBadge
                status={job.enabled ? "success" : "queued"}
                size="sm"
              >
                {job.enabled ? t("jobs.statusEnabled") : t("jobs.statusPaused")}
              </StatusBadge>
              {canEdit ? (
                <span className={styles.jobActs}>
                  <Tooltip
                    content={job.enabled ? t("jobs.pause") : t("jobs.resume")}
                  >
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      aria-label={
                        job.enabled ? t("jobs.pause") : t("jobs.resume")
                      }
                      data-test={`job-toggle-${job.id}`}
                      disabled={setEnabled.isPending}
                      onClick={() => {
                        setEnabled.mutate({
                          projectId,
                          jobId: job.id,
                          enabled: !job.enabled,
                        })
                      }}
                    >
                      {job.enabled ? (
                        <Pause aria-hidden="true" />
                      ) : (
                        <Play aria-hidden="true" />
                      )}
                    </Button>
                  </Tooltip>
                  <Tooltip content={t("jobs.delete")}>
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      aria-label={t("jobs.delete")}
                      data-test={`job-delete-${job.id}`}
                      onClick={() => {
                        setPendingDelete(job)
                      }}
                    >
                      <Trash2 aria-hidden="true" />
                    </Button>
                  </Tooltip>
                </span>
              ) : (
                <span />
              )}
            </li>
          ))}
        </ul>
      ) : jobs.data ? (
        <ScreenState
          kind="empty"
          title={t("jobs.emptyTitle")}
          description={t("jobs.emptyDescription")}
          inset="none"
          data-test="project-jobs-empty"
        />
      ) : null}

      {canEdit ? (
        <div className={styles.head}>
          <Button
            size="sm"
            data-test="job-create-open"
            onClick={() => {
              setCreateOpen(true)
            }}
          >
            <Plus aria-hidden="true" />
            {t("jobs.create")}
          </Button>
        </div>
      ) : null}

      <FormDialog
        open={createOpen}
        title={t("jobs.create")}
        description={t("jobs.dialogDescription")}
        submitLabel={t("jobs.dialogSubmit")}
        busy={createJob.isPending}
        submitDisabled={createInvalid}
        onSubmit={submitCreate}
        onCancel={() => {
          setCreateOpen(false)
        }}
      >
        <div className={styles.form}>
          <CronField
            id="job-cron"
            label={t("jobs.cronLabel")}
            value={cron}
            onValueChange={setCron}
            hint={t("jobs.cronHint")}
          />
          <TextField
            id="job-profile"
            label={t("jobs.profileLabel")}
            value={profileKey}
            onValueChange={setProfileKey}
            placeholder={t("jobs.profilePlaceholder")}
            hint={t("jobs.profileHint")}
          />
          <TextareaField
            id="job-brief"
            label={t("jobs.briefLabel")}
            voice="code"
            value={briefJson}
            onValueChange={setBriefJson}
            hint={t("jobs.briefHint")}
          />
        </div>
      </FormDialog>

      <ConfirmDialog
        open={pendingDelete !== null}
        title={t("jobs.deleteTitle")}
        body={
          pendingDelete ? (
            /* The cron expression and the profile key are values; the
               sentence around them is copy, so the code spans ride slots
               and the words belong to the locale. */
            <Trans
              ns="projects"
              i18nKey="jobs.deleteBody"
              components={{
                cron: <code>{pendingDelete.cronExpression}</code>,
                profile: <code>{pendingDelete.profileKey}</code>,
              }}
            />
          ) : null
        }
        confirmLabel={t("jobs.delete")}
        cancelLabel={t("jobs.deleteCancel")}
        danger={true}
        onConfirm={() => {
          if (pendingDelete) {
            deleteJob.mutate(
              { projectId, jobId: pendingDelete.id },
              { onSettled: () => setPendingDelete(null) }
            )
          }
        }}
        onCancel={() => {
          setPendingDelete(null)
        }}
      />
    </Section>
  )
}

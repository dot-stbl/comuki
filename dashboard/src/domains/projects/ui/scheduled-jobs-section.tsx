import { useState } from "react"
import { Pause, Play, Plus, Trash2 } from "lucide-react"

import {
  useCreateScheduledJobMutation,
  useDeleteScheduledJobMutation,
  useScheduledJobsQuery,
  useSetScheduledJobEnabledMutation,
  type ScheduledJob,
} from "@/domains/projects/api/scheduled-jobs"
import {
  Button,
  ConfirmDialog,
  FormDialog,
  Section,
  StatusBadge,
  TextareaField,
  TextField,
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
      },
    )
  }

  return (
    <Section
      id="project-jobs"
      title="scheduled jobs"
      note={
        jobs.data
          ? `${jobs.data.length} configured`
          : /* Absent while loading, not zero — see the cost hand-off. */
            "counting"
      }
      data-test="project-jobs"
    >
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
                {job.enabled ? "enabled" : "paused"}
              </StatusBadge>
              {canEdit ? (
                <span className={styles.jobActs}>
                  <Tooltip
                    content={job.enabled ? "Pause schedule" : "Resume schedule"}
                  >
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      aria-label={
                        job.enabled ? "Pause schedule" : "Resume schedule"
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
                  <Tooltip content="Delete schedule">
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      aria-label="Delete schedule"
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
      ) : (
        <p className={styles.quiet} data-test="project-jobs-empty">
          No schedule starts work on this project on its own. Everything that
          runs here was filed or dispatched by a person.
        </p>
      )}

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
            Schedule a job
          </Button>
        </div>
      ) : null}

      <FormDialog
        open={createOpen}
        title="Schedule a job"
        description="A cron entry the platform files on this project by itself — the brief becomes the ticket, the profile becomes the first step."
        submitLabel="Schedule"
        busy={createJob.isPending}
        submitDisabled={createInvalid}
        onSubmit={submitCreate}
        onCancel={() => {
          setCreateOpen(false)
        }}
      >
        <div className={styles.form}>
          <TextField
            id="job-cron"
            label="cron"
            value={cron}
            onValueChange={setCron}
            placeholder="0 3 * * *"
            hint="Five-field cron, in the project's own timezone policy."
          />
          <TextField
            id="job-profile"
            label="profile key"
            value={profileKey}
            onValueChange={setProfileKey}
            placeholder="implementer"
            hint="The work-item profile the run starts from — the catalog lives in the client's git."
          />
          <TextareaField
            id="job-brief"
            label="brief json"
            voice="code"
            value={briefJson}
            onValueChange={setBriefJson}
            hint="Stored verbatim. Nothing between this box and the host touches it."
          />
        </div>
      </FormDialog>

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete this schedule?"
        body={
          pendingDelete ? (
            <>
              <code>{pendingDelete.cronExpression}</code> for{" "}
              <code>{pendingDelete.profileKey}</code> stops firing and is
              removed. Work already running because of it is not touched.
            </>
          ) : null
        }
        confirmLabel="Delete schedule"
        cancelLabel="Keep it"
        danger={true}
        onConfirm={() => {
          if (pendingDelete) {
            deleteJob.mutate(
              { projectId, jobId: pendingDelete.id },
              { onSettled: () => setPendingDelete(null) },
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

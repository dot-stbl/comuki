import { useCallback, useMemo } from "react"
import { RotateCw } from "lucide-react"

import { useSetVerifyEnabled } from "@/domains/verify/api/mutations"
import { useVerifyQuery } from "@/domains/verify/api/queries"
import { commandsFor } from "@/domains/verify/model/gate"
import { VerifyProjectPanel } from "@/domains/verify/ui/verify-project-panel"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./gate-tab.module.css"

const SKELETON_WIDTHS = ["64%", "40%", "78%", "52%"]

/**
 * The verification gate: a feature flag, and a read-mostly list.
 *
 * A section of Knowledge rather than a screen of its own because it is the
 * same chassis — read-only registries sourced from the client's git,
 * project-scoped — answering a different question, and a tab keeps the two
 * questions honestly separated without pretending either is a destination.
 * `/verify` still resolves: it redirects to this tab, so a link pasted into a
 * ticket a year ago lands where the gate lives now.
 *
 * Read-mostly is a decision, not a shortfall. The commands are declared in the
 * client's git, so the only thing that changes one is a commit in their
 * repository — and the section's job is therefore to say *where* that
 * repository is, precisely, and link to it. This project has shipped the
 * alternative before: a panel titled "RulesEditor" over a table nobody could
 * edit, which read as a broken feature rather than as a rule. There is no
 * editor here and no disabled Edit implying one is coming.
 *
 * One section per project, because the gate is per project — there is no
 * current project in this shell, so the tab shows them all, exactly as the
 * duty board shows every project's runs. What is scoped is the **act**: the
 * switch answers to `settings.live` on that project, so a person who
 * administers one and only watches another sees one live switch above one that
 * explains itself.
 */
export function GateTab() {
  const { data, isLoading, isError, error, refetch } = useVerifyQuery()
  const session = useSession()
  const setEnabled = useSetVerifyEnabled()

  const projects = useMemo(() => data?.projects ?? [], [data])
  const commands = useMemo(() => data?.commands ?? [], [data])

  const setEnabledMutate = setEnabled.mutate
  // The switch already refuses a denied change, but the handler answers the
  // same question again on the way in: the gate is the permission, not the
  // control that happens to be carrying it today.
  const onEnabledChange = useCallback(
    (projectId: string, enabled: boolean) => {
      if (!can(session, "settings.live", projectId)) {
        return
      }
      setEnabledMutate({ projectId, enabled })
    },
    [session, setEnabledMutate]
  )

  const ready = !isLoading && !isError

  return (
    <div className={styles.gate}>
      {isLoading ? (
        <div className={styles.skeleton} data-test="verify-loading">
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
          <p className={styles.stateTitle}>Couldn&apos;t load the gate</p>
          <p className={styles.stateBody}>
            {error instanceof Error ? error.message : "Unknown error"}
          </p>
          <span>
            <Tooltip content="Retry">
              <Button
                size="icon-sm"
                data-test="verify-retry"
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

      {setEnabled.error ? (
        <p className={styles.failure} role="alert" data-test="verify-failure">
          {setEnabled.error instanceof Error
            ? setEnabled.error.message
            : "The change failed."}{" "}
          Nothing moved — the gate is back as it was.
        </p>
      ) : null}

      {ready ? (
        <>
          <p className={styles.intro}>
            A run has to clear the client&apos;s own checks before it can
            land. The checks live in their repository — one file, committed
            like anything else — so this section turns the gate on and off and
            shows what each check last said. Editing a command means editing
            the file; every section below says exactly where its file is.
          </p>

          {projects.map((project) => {
            const key = projectOf(session, project.projectId)?.key
            const name =
              projectOf(session, project.projectId)?.name ?? project.projectId
            const denied = can(session, "settings.live", project.projectId)
              ? null
              : needsLabel("settings.live", key)

            return (
              <VerifyProjectPanel
                key={project.projectId}
                project={project}
                projectKey={key ?? project.projectId}
                projectName={name}
                commands={commandsFor(commands, project.projectId)}
                denied={denied}
                saving={
                  setEnabled.isPending &&
                  setEnabled.variables?.projectId === project.projectId
                }
                onEnabledChange={(enabled) =>
                  onEnabledChange(project.projectId, enabled)
                }
              />
            )
          })}
        </>
      ) : null}
    </div>
  )
}

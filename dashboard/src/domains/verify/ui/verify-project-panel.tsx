import { useMemo, useState } from "react"
import { ExternalLink, PowerOff } from "lucide-react"

import { sourceLocation } from "@/domains/verify/model/gate"
import type {
  VerifyCommand,
  VerifyProject,
} from "@/domains/verify/model/types"
import {
  createVerifyColumns,
  getCommandId,
} from "@/domains/verify/ui/verify-columns"
import {
  DataTable,
  DataTableToolbar,
  SwitchField,
  Tooltip,
  applyDataFilters,
  buttonClass,
  hasActiveFilters,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import styles from "./verify-project-panel.module.css"

export interface VerifyProjectPanelProps {
  project: VerifyProject
  /** The word the operator calls the project by. */
  projectKey: string
  projectName: string
  commands: VerifyCommand[]
  /** The sentence naming what would open the switch, or `null`. */
  denied: string | null
  saving: boolean
  onEnabledChange: (enabled: boolean) => void
}

/**
 * One project's verification gate.
 *
 * Three bands, and the middle one is the reason the screen exists. The
 * **switch** is the only thing here anybody can change. The **source band**
 * names the repository, the ref and the path the commands were read from, and
 * links out to it — because editing a command means committing over there, and
 * a screen that knew where "there" was and did not say so would be making the
 * operator go and find out. The **table** is read-only for the same reason, and
 * carries no disabled Edit to imply otherwise.
 *
 * A project whose gate is off still lists its commands: a switch here does not
 * delete a file over there, and hiding the list would suggest it had.
 */
export function VerifyProjectPanel({
  project,
  projectKey,
  projectName,
  commands,
  denied,
  saving,
  onEnabledChange,
}: VerifyProjectPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])

  const columns = useMemo(() => createVerifyColumns(), [])
  const rows = useMemo(
    () => applyDataFilters(commands, filters, columns),
    [commands, filters, columns]
  )

  const emptyLabel = hasActiveFilters(filters)
    ? "No checks match the current filters."
    : "No checks are declared."

  return (
    <section
      className={styles.panel}
      data-test="verify-project"
      data-project={projectKey}
    >
      <header className={styles.head}>
        <h2 className={styles.title}>
          <span className={styles.key}>{projectKey}</span> · {projectName}
        </h2>
        <SwitchField
          id={`verify-enabled-${project.projectId}`}
          label="run the gate on every run"
          checked={project.enabled}
          onCheckedChange={onEnabledChange}
          onLabel="gate on"
          offLabel="gate off"
          disabled={saving}
          denied={denied}
          data-test="verify-enabled"
        />
      </header>

      <div className={styles.source} data-test="verify-source">
        <div className={styles.sourceText}>
          <p className={styles.sourceLead}>
            These commands are declared in this client&apos;s git and are
            read-only here. Changing one is a commit in their repository — that
            is what makes a run reproducible, so there is no editor on this
            screen by design.
          </p>
          <p className={styles.sourcePath} data-test="verify-source-path">
            {sourceLocation(project.source)} · read {project.readAt}
          </p>
        </div>
        {/* Three words became a glyph. The path this opens is printed
            beside it, so the tooltip and the name only have to say what the
            act is. */}
        <Tooltip content="Open in git">
          <a
            className={buttonClass({ variant: "outline", size: "icon-sm" })}
            href={project.source.url}
            target="_blank"
            rel="noreferrer"
            aria-label="Open in git"
            data-test="verify-source-link"
          >
            <ExternalLink aria-hidden="true" />
          </a>
        </Tooltip>
      </div>

      {!project.enabled ? (
        <p className={styles.off} data-test="verify-gate-off">
          <PowerOff className={styles.offIcon} aria-hidden="true" />
          <span>
            The gate is off, so nothing below is run and no run is blocked by
            it. The file is still in git and the checks are still declared —
            turning this back on starts running them again from the next run.
          </span>
        </p>
      ) : null}

      {commands.length === 0 ? (
        <div className={styles.empty} data-test="verify-empty">
          <p className={styles.emptyTitle}>No checks are declared</p>
          <p className={styles.emptyBody}>
            The gate is looking for{" "}
            <span className={styles.code}>{project.source.path}</span> in{" "}
            <span className={styles.code}>{project.source.repo}</span> at{" "}
            <span className={styles.code}>{project.source.ref}</span> and found
            nothing. Commit the file there and it is picked up on the next read
            — there is nowhere here to create one.
          </p>
          <Tooltip content="Open in git">
            <a
              className={buttonClass({ variant: "outline", size: "icon-sm" })}
              href={project.source.url}
              target="_blank"
              rel="noreferrer"
              aria-label="Open in git"
              data-test="verify-empty-link"
            >
              <ExternalLink aria-hidden="true" />
            </a>
          </Tooltip>
        </div>
      ) : (
        <>
          <div className={styles.toolbar}>
            <DataTableToolbar
              columns={columns}
              filters={filters}
              onFiltersChange={setFilters}
              columnVisibility={columnVisibility}
              onColumnVisibilityChange={setColumnVisibility}
              trailing={
                <span className={styles.count}>{rows.length} shown</span>
              }
            />
          </div>
          <div className={styles.tableArea}>
            <DataTable
              columns={columns}
              data={rows}
              getRowId={getCommandId}
              density="compact"
              columnVisibility={columnVisibility}
              onColumnVisibilityChange={setColumnVisibility}
              sorting={sorting}
              onSortingChange={setSorting}
              emptyLabel={emptyLabel}
            />
          </div>
        </>
      )}
    </section>
  )
}

import { useMemo, type ReactNode } from "react"
import { ArrowLeft, ArrowRight, RotateCw } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useIdentityQuery } from "@/domains/identity/api/queries"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import type { ProjectRow } from "@/domains/projects/model/types"
import { useQueueQuery } from "@/domains/queue/api/queries"
import { formatCost } from "@/domains/runs/model/format"
import { useSourcesQuery } from "@/domains/sources/api/queries"
import { can, needsLabel, useCan, useSession } from "@/shared/session"
import {
  Button,
  ForbiddenState,
  Section,
  Tooltip,
  buttonClass,
} from "@/shared/ui"

import styles from "./project-detail-page.module.css"

const SKELETON_WIDTHS = ["38%", "62%", "48%", "70%"]

export interface ProjectDetailPageProps {
  /**
   * From the path. A project is a thing, so looking at one has an address.
   *
   * Taken as a prop rather than read off `getRouteApi` inside, so the screen
   * can be mounted in a story and in a test without the generated route tree
   * standing behind it — the arrangement `LinkOidcPage` already uses, and the
   * better one here for the same reason: the id is the only thing this page
   * needs from the router, and a component that reaches for the router to get
   * one string cannot be looked at anywhere else.
   */
  projectId: string
}

/**
 * One hand-off row: a screen this project's work actually lives on.
 *
 * `count` is a node rather than a number because the reading is two voices —
 * the figures are values and the words between them are prose — and splitting
 * that into two props would let a call site put a number in the wrong one.
 */
interface Handoff {
  id: string
  /** The screen, in the rail's own words. */
  what: string
  /** What this project has over there, right now. */
  count: ReactNode
  /** What pressing it does, said plainly. */
  note: string
  /** The destination, named from the product's own route union. */
  to: "/runs" | "/queue" | "/sources" | "/cost"
  /** The narrowing. `q`, the one parameter every list here already reads. */
  search: { q: string } | Record<string, never>
}

/**
 * The four hand-offs, in the order a duty question is usually asked in.
 *
 * ## Why this page hands off instead of drawing
 *
 * A detail page that renders its own runs table is a second duty screen, and
 * the day the two disagree the operator believes whichever one they are
 * standing on. So this page shows what is only knowable *about the project* —
 * its record, and who holds a role on it — and everything that already has a
 * screen is a count and a link to that screen with the filter applied.
 *
 * ## The parameter is `q`, and it is not a new one
 *
 * Three of the four destinations narrow on `q` — the same parameter `/runs`,
 * `/queue`, `/projects` and `/identity` already read, and the same one
 * `resolve.ts` writes when the palette hands free text off to a screen. A
 * second set of parameters would be a second contract to keep, and the
 * destinations would not read it. Each row names its route rather than a built
 * string, so renaming a screen breaks this build instead of quietly producing
 * a link that lands nowhere.
 *
 * The permission on each row is asked **against this project**: `runs.view`,
 * `queue.view`, `sources.view` and `cost.view` are project permissions, and the
 * same person is a viewer here and a project-admin next door. A row the session
 * cannot reach is not rendered at all — navigation a role cannot use is hidden,
 * and only an *act* it cannot use stays visible and explains itself.
 */
function handoffRows({
  project,
  queueItems,
  queueWorkers,
  connections,
  figure,
  pending,
}: {
  project: ProjectRow
  /** `null` while the queue board is still on its way. */
  queueItems: number | null
  queueWorkers: number | null
  connections: number | null
  figure: (value: ReactNode) => ReactNode
  pending: ReactNode
}): Handoff[] {
  // The handle, not the id: the three lists that narrow on this match the
  // project *key* in their promoted text filter, because that is the value the
  // operator types and the value their project column shows.
  const q = { q: project.slug }

  return [
    {
      id: "runs",
      what: "live runs",
      count: (
        <>
          {figure(project.activeRuns)} in flight of {figure(project.totalRuns)}{" "}
          seen
        </>
      ),
      note: "open the duty list narrowed to this project",
      to: "/runs",
      search: q,
    },
    {
      id: "queue",
      what: "queue & workers",
      count:
        queueItems === null || queueWorkers === null ? (
          pending
        ) : (
          <>
            {figure(queueItems)} work items · {figure(queueWorkers)} workers up
          </>
        ),
      note: "open the claim queue narrowed to this project",
      to: "/queue",
      search: q,
    },
    {
      id: "sources",
      what: "sources",
      count:
        connections === null ? (
          pending
        ) : (
          <>{figure(connections)} connections</>
        ),
      note: "open the intake narrowed to this project",
      to: "/sources",
      search: q,
    },
    {
      id: "cost",
      what: "cost & failures",
      count:
        project.spendToday === null ? (
          // Absent, not zero. A project the cost report has never heard of has
          // not spent nothing — it has not been measured, and `$0.00` would be
          // the screen telling an operator a two-day-old project is already
          // accounted for.
          <>
            <span className={styles.absent}>—</span> nothing attributed yet
          </>
        ) : (
          <>{figure(formatCost(project.spendToday))} today</>
        ),
      // The one row whose link is wider than its figure, and it says so. There
      // is no `?q=` for the cost report today and no honest way to invent one:
      // spend is attributed per application, and the screen behind this is the
      // platform's whole day.
      note: "the figure is this project's — the report behind it is the platform's",
      to: "/cost",
      // No `q`: there is nothing over there that would read one.
      search: {},
    },
  ]
}

/**
 * One project, in full.
 *
 * The registry answers "what exists and what is it costing"; this answers the
 * two questions a row cannot: **what is this project configured as**, and **who
 * holds a role on it**. Neither is visible anywhere else in the product, and
 * both are the reason the page is worth an address.
 *
 * Everything else on it is a hand-off. See `handoffRows` for the argument.
 *
 * THE HEIGHT CHAIN, and what this screen deliberately does not do with it.
 * `AppShell` hands every screen a sized scroll port; a screen that *fills* —
 * the registry, the duty board — declares `height: 100%` all the way down so a
 * table can resolve a depth against it. This does not fill. It declares no
 * height and no `flex: 1` and ends where its last region ends, exactly like
 * `form-page.module.css`. jsdom computes no layout, so a wrong answer here
 * would render as a blank strip and still pass every gate.
 */
export function ProjectDetailPage({ projectId }: ProjectDetailPageProps) {
  const session = useSession()
  const { data = [], isLoading, isError, error, refetch } = useProjectsQuery()

  // Read unconditionally, gated on render. These are hooks, so a `useCan`
  // standing in front of one would make the hook order depend on the session —
  // and the page would then have two shapes for React to reconcile between.
  // What a role does or does not open is a question about what is *drawn*.
  const identity = useIdentityQuery()
  const queue = useQueueQuery()
  const sources = useSourcesQuery()

  // Identity is a platform act: being project-admin of this very project must
  // not open the list of who else holds a role on it.
  const mayManageIdentity = useCan("identity.manage")

  const project = data.find((entry) => entry.id === projectId) ?? null

  const grants = useMemo(
    () =>
      (identity.data?.grants ?? []).filter(
        (grant) =>
          grant.subjectKind === "user" && grant.projectId === projectId
      ),
    [identity.data, projectId]
  )

  const handoffs = useMemo(() => {
    if (!project) {
      return []
    }

    const figure = (value: ReactNode) => (
      <span className={styles.figure}>{value}</span>
    )
    const pending = <span className={styles.absent}>counting</span>

    const rows = handoffRows({
      project,
      queueItems: queue.data
        ? queue.data.items.filter((item) => item.projectId === project.id)
            .length
        : null,
      queueWorkers: queue.data
        ? queue.data.workers.filter(
            (worker) => worker.projectId === project.id
          ).length
        : null,
      connections: sources.data
        ? sources.data.connections.filter(
            (connection) => connection.projectId === project.id
          ).length
        : null,
      figure,
      pending,
    })

    const allowed: Record<string, boolean> = {
      runs: can(session, "runs.view", project.id),
      queue: can(session, "queue.view", project.id),
      sources: can(session, "sources.view", project.id),
      cost: can(session, "cost.view", project.id),
    }

    return rows.filter((row) => allowed[row.id])
  }, [project, queue.data, sources.data, session])

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: "platform" },
            { label: "projects", to: "/projects" },
            // The slug, not the display name: the crumb path is an address,
            // and the slug is the handle this project is known by everywhere
            // else in the product.
            { label: project?.slug ?? "project" },
          ]}
          title={project?.name ?? "Project"}
          summary={
            project ? (
              <>
                <span className={styles.value}>{project.slug}</span>
                {" · created "}
                <span className={styles.value}>{project.createdAt}</span>
              </>
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen} data-test="project-detail">
        {isLoading ? (
          <div className={styles.skeleton} data-test="project-loading">
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
            <p className={styles.stateTitle}>The registry did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="project-retry"
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

        {!isLoading && !isError && !project ? (
          /* The registry answered and this id was not in it. The state names
             the missing thing, because "not found" without the id is a screen
             that cannot be acted on: the operator arrived here from a link
             somebody else wrote, and the id is the only part of it they can
             take back to whoever wrote it. */
          <div className={styles.state} data-test="project-not-found">
            <p className={styles.stateTitle}>No project with that id</p>
            <p className={styles.stateBody}>
              The registry holds nothing under{" "}
              <code className={styles.missing}>{projectId}</code>. A project id
              out of an old link is the ordinary way to arrive here — an address
              outlives the project it named, and the registry is where the ones
              that still exist are.
            </p>
            <span>
              <Tooltip content="Back to projects">
                <Link
                  to="/projects"
                  search={{}}
                  data-test="project-not-found-back"
                  aria-label="Back to projects"
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <ArrowLeft aria-hidden="true" />
                </Link>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {project ? (
          <>
            {/* --- what only this page knows --- */}

            <Section
              id="project-record"
              title="the project itself"
              data-test="project-facts"
            >
              {/* A definition list, hairline-bounded, no fill and no shadow:
                  four facts about a record are a data surface, and a data
                  surface in this product is a boundary and a corner rather
                  than a card. */}
              <dl className={styles.facts}>
                <div className={styles.fact}>
                  <dt className={styles.factName}>slug</dt>
                  <dd className={styles.factValue}>{project.slug}</dd>
                </div>
                <div className={styles.fact}>
                  <dt className={styles.factName}>name</dt>
                  {/* The one field on a project written for a reader. */}
                  <dd className={styles.factProse}>{project.name}</dd>
                </div>
                <div className={styles.fact}>
                  <dt className={styles.factName}>git profile repository</dt>
                  {project.gitProfileRepo ? (
                    /* The one fact on this page somebody copies out of it, so
                       it is selected as a unit rather than as part of a
                       sentence — the same treatment the run id gets. */
                    <dd className={styles.factRepo}>
                      {project.gitProfileRepo}
                    </dd>
                  ) : (
                    /* Not missing — running on the platform's own profiles,
                       which is a legitimate way for a project to be
                       configured. The registry column says it in exactly these
                       words; two spellings of one fact is how the two screens
                       start disagreeing. */
                    <dd className={styles.factAbsent}>platform defaults</dd>
                  )}
                </div>
                <div className={styles.fact}>
                  <dt className={styles.factName}>created</dt>
                  <dd className={styles.factValue}>{project.createdAt}</dd>
                </div>
              </dl>
            </Section>

            {/* --- who holds which role on it --- */}

            <Section
              id="project-roles"
              title="roles on this project"
              note={
                mayManageIdentity.allowed && identity.data
                  ? `${grants.length} held`
                  : undefined
              }
              data-test="project-roles"
            >
              {mayManageIdentity.allowed ? (
                <>
                  {grants.length > 0 ? (
                    <ul className={styles.grants} data-test="project-grants">
                      {grants.map((grant) => (
                        <li key={grant.id} className={styles.grant}>
                          <span className={styles.grantSubject}>
                            {grant.subjectLabel}
                          </span>
                          {/* A role is a value out of a closed set in code,
                              not a label somebody chose, so it reads in the
                              data voice beside the address. */}
                          <span className={styles.grantRole}>{grant.role}</span>
                          <span className={styles.grantWhen}>
                            {grant.grantedAt}
                          </span>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className={styles.quiet} data-test="project-no-grants">
                      Nobody holds a role on this project. Anyone who can reach
                      it reaches it through a platform grant, which holds
                      everywhere and is not listed here.
                    </p>
                  )}

                  <p className={styles.outbound}>
                    {/* The grants list matches on its scope label, and a
                        project's scope label *is* its slug — so this lands
                        narrowed rather than on the whole platform's grants. */}
                    <Link
                      to="/identity"
                      search={{ tab: "grants", q: project.slug }}
                      className={styles.outLink}
                      data-test="project-grants-all"
                    >
                      every assignment on this project
                    </Link>
                    <Link
                      to="/identity/grants/new"
                      className={styles.outLink}
                      data-test="project-grant-new"
                    >
                      grant a role
                    </Link>
                  </p>
                </>
              ) : (
                /* Rendered, not hidden. An administrator reading somebody
                   else's screen has to learn that this region exists and which
                   role opens it; a region that silently disappears teaches
                   that the page is smaller than it is. */
                <ForbiddenState
                  className={styles.forbidden}
                  needs={needsLabel("identity.manage")}
                  subject="Roles on this project"
                />
              )}
            </Section>

            {/* --- everything else, with a count and a link --- */}

            {handoffs.length > 0 ? (
              <Section
                id="project-elsewhere"
                title="where this project's work is"
                data-test="project-handoffs"
              >
                <div className={styles.handoffs}>
                  {handoffs.map((row) => (
                    <Link
                      key={row.id}
                      to={row.to}
                      search={row.search}
                      className={styles.handoff}
                      data-test={`project-handoff-${row.id}`}
                    >
                      <span className={styles.handoffText}>
                        <span className={styles.handoffHead}>
                          <span className={styles.handoffWhat}>{row.what}</span>
                          <span className={styles.handoffCount}>
                            {row.count}
                          </span>
                        </span>
                        <span className={styles.handoffNote}>{row.note}</span>
                      </span>
                      <ArrowRight
                        className={styles.handoffIcon}
                        aria-hidden="true"
                      />
                    </Link>
                  ))}
                </div>
              </Section>
            ) : null}
          </>
        ) : null}
      </div>
    </AppShell>
  )
}

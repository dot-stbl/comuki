import { useMemo, useState, type ReactElement, type ReactNode } from "react"
import { ArrowLeft, ArrowRight, Pencil, RotateCw } from "lucide-react"
import { Link } from "@tanstack/react-router"
import { Trans, useTranslation } from "react-i18next"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useIdentityQuery } from "@/domains/identity/api/queries"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import { useUpdateProjectMutation } from "@/domains/projects/api/mutations"
import type { ProjectRow } from "@/domains/projects/model/types"
import type { ProjectsTranslator } from "@/domains/projects/ui/projects-columns"
import {
  markLabel,
  resolveProjectMark,
} from "@/domains/projects/model/identity"
import {
  ProjectIdentityEditor,
  type ProjectIdentityPatch,
} from "@/domains/projects/ui/project-identity-editor"
import { ProjectMark } from "@/domains/projects/ui/project-mark"
import { projectAccentStyle } from "@/domains/projects/ui/project-accent"
import { useQueueQuery, useWorkersQuery } from "@/domains/queue/api/queries"
import { formatCost } from "@/domains/runs/model/format"
import { useSourcesQuery } from "@/domains/sources/api/queries"
import { ScheduledJobsSection } from "@/domains/projects/ui/scheduled-jobs-section"
import { requestFailureMessage } from "@/shared/api/problem"
import { can, needsLabel, useCan, useSession } from "@/shared/session"
import {
  Button,
  Fact,
  FactList,
  ForbiddenState,
  Notice,
  ScreenState,
  Section,
  Skeleton,
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
  t,
}: {
  project: ProjectRow
  /** `null` while the queue board is still on its way. */
  queueItems: number | null
  queueWorkers: number | null
  connections: number | null
  figure: (value: string | number) => ReactElement
  pending: ReactNode
  t: ProjectsTranslator
}): Handoff[] {
  // The handle, not the id: the three lists that narrow on this match the
  // project *key* in their promoted text filter, because that is the value the
  // operator types and the value their project column shows.
  const q = { q: project.slug }

  return [
    {
      id: "runs",
      what: t("handoff.runsWhat"),
      count: (
        <Trans
          ns="projects"
          i18nKey="handoff.runsCount"
          components={{
            count: figure(project.activeRuns),
            total: figure(project.totalRuns),
          }}
        />
      ),
      note: t("handoff.runsNote"),
      to: "/runs",
      search: q,
    },
    {
      id: "queue",
      what: t("handoff.queueWhat"),
      count:
        queueItems === null || queueWorkers === null ? (
          pending
        ) : (
          <Trans
            ns="projects"
            i18nKey="handoff.queueCount"
            components={{
              items: figure(queueItems),
              workers: figure(queueWorkers),
            }}
          />
        ),
      note: t("handoff.queueNote"),
      to: "/queue",
      search: q,
    },
    {
      id: "sources",
      what: t("handoff.sourcesWhat"),
      count:
        connections === null ? (
          pending
        ) : (
          <Trans
            ns="projects"
            i18nKey="handoff.sourcesCount"
            components={{ count: figure(connections) }}
          />
        ),
      note: t("handoff.sourcesNote"),
      to: "/sources",
      search: q,
    },
    {
      id: "cost",
      what: t("handoff.costWhat"),
      count:
        project.spendToday === null ? (
          // Absent, not zero. A project the cost report has never heard of has
          // not spent nothing — it has not been measured, and `$0.00` would be
          // the screen telling an operator a two-day-old project is already
          // accounted for.
          <Trans
            ns="projects"
            i18nKey="handoff.costNothing"
            components={{ dash: <span className={styles.absent}>—</span> }}
          />
        ) : (
          <Trans
            ns="projects"
            i18nKey="handoff.costToday"
            components={{ spend: figure(formatCost(project.spendToday)) }}
          />
        ),
      // The one row whose link is wider than its figure, and it says so. There
      // is no `?q=` for the cost report today and no honest way to invent one:
      // spend is attributed per application, and the screen behind this is the
      // platform's whole day.
      note: t("handoff.costNote"),
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
  const { t } = useTranslation("projects")
  const { t: tShell } = useTranslation("shell")
  const { data = [], isLoading, isError, error, refetch } = useProjectsQuery()

  // Read unconditionally, gated on render. These are hooks, so a `useCan`
  // standing in front of one would make the hook order depend on the session —
  // and the page would then have two shapes for React to reconcile between.
  // What a role does or does not open is a question about what is *drawn*.
  const identity = useIdentityQuery()
  const queue = useQueueQuery()
  const pool = useWorkersQuery()
  const sources = useSourcesQuery()

  // Identity is a platform act: being project-admin of this very project must
  // not open the list of who else holds a role on it.
  const mayManageIdentity = useCan("identity.manage")

  // Editing the record is a project act — asked against this project, so the
  // same person can be an admin here and a viewer next door. Asked as a hook
  // (not render-time `can`) because the edit affordance is chrome the page
  // always draws, not a row that arrives with the data.
  const mayEditRecord = useCan("projects.edit", projectId)

  // The identity editor writes through the widened PATCH; three optional
  // fields are not a page (design D8), so the editor opens under the facts it
  // edits and closes when the write lands.
  const updateProject = useUpdateProjectMutation()
  const [editingIdentity, setEditingIdentity] = useState(false)

  const project = data.find((entry) => entry.id === projectId) ?? null

  const grants = useMemo(
    () =>
      (identity.data?.grants ?? []).filter(
        (grant) => grant.subjectKind === "user" && grant.projectId === projectId
      ),
    [identity.data, projectId]
  )

  const handoffs = useMemo(() => {
    if (!project) {
      return []
    }

    /* The figures stringify because a Trans slot holding a falsy child
       (a bare `0`) renders empty — react-i18next drops it while walking
       the node tree. A count of zero is a reading, not a blank. */
    const figure = (value: string | number): ReactElement => (
      <span className={styles.figure}>{String(value)}</span>
    )
    const pending = (
      <span className={styles.absent}>{t("handoff.counting")}</span>
    )

    const rows = handoffRows({
      project,
      queueItems: queue.data
        ? queue.data.items.filter((item) => item.projectId === project.id)
            .length
        : null,
      queueWorkers: pool.data
        ? pool.data.filter((worker) => worker.projectId === project.id).length
        : null,
      connections: sources.data
        ? sources.data.connections.filter(
            (connection) => connection.projectId === project.id
          ).length
        : null,
      figure,
      pending,
      t,
    })

    const allowed: Record<string, boolean> = {
      runs: can(session, "runs.view", project.id),
      queue: can(session, "queue.view", project.id),
      sources: can(session, "sources.view", project.id),
      cost: can(session, "cost.view", project.id),
    }

    return rows.filter((row) => allowed[row.id])
  }, [project, queue.data, pool.data, sources.data, session, t])

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: tShell("crumb.platform") },
            { label: tShell("crumb.projects"), to: "/projects" },
            // The slug, not the display name: the crumb path is an address,
            // and the slug is the handle this project is known by everywhere
            // else in the product.
            { label: project?.slug ?? t("detail.crumbFallback") },
          ]}
          title={project?.name ?? t("detail.fallbackTitle")}
          summary={
            project ? (
              <Trans
                ns="projects"
                i18nKey="detail.summary"
                components={{
                  slug: <span className={styles.value}>{project.slug}</span>,
                  created: (
                    <span className={styles.value}>{project.createdAt}</span>
                  ),
                }}
              />
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen} data-test="project-detail">
        {isLoading ? (
          <Skeleton
            lines={SKELETON_WIDTHS}
            inset="none"
            data-test="project-loading"
          />
        ) : null}

        {isError ? (
          <ScreenState
            kind="error"
            title={t("registry.errorTitle")}
            description={requestFailureMessage(
              error,
              t("errors.unknown", { ns: "common" })
            )}
            /* `none`: the screen's own body already pays for its room, the
               way the record and the hand-offs below do. */
            inset="none"
            action={
              <Tooltip content={t("actions.retry", { ns: "common" })}>
                <Button
                  size="icon-sm"
                  data-test="project-retry"
                  aria-label={t("actions.retry", { ns: "common" })}
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            }
          />
        ) : null}

        {!isLoading && !isError && !project ? (
          /* The registry answered and this id was not in it. The state names
             the missing thing, because "not found" without the id is a screen
             that cannot be acted on: the operator arrived here from a link
             somebody else wrote, and the id is the only part of it they can
             take back to whoever wrote it. */
          <ScreenState
            kind="notFound"
            title={t("detail.notFoundTitle")}
            description={
              <Trans
                ns="projects"
                i18nKey="detail.notFoundDescription"
                components={{
                  id: <code className={styles.missing}>{projectId}</code>,
                }}
              />
            }
            inset="none"
            data-test="project-not-found"
            action={
              <Tooltip content={t("detail.notFoundBack")}>
                <Link
                  to="/projects"
                  search={{}}
                  data-test="project-not-found-back"
                  aria-label={t("detail.notFoundBack")}
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <ArrowLeft aria-hidden="true" />
                </Link>
              </Tooltip>
            }
          />
        ) : null}

        {project ? (
          <>
            {/* --- what only this page knows --- */}

            <Section
              id="project-record"
              title={t("detail.factsSection")}
              data-test="project-facts"
            >
              {/* A definition list, hairline-bounded, no fill and no shadow:
                  four facts about a record are a data surface, and a data
                  surface in this product is a boundary and a corner rather
                  than a card. */}
              <FactList framed>
                <Fact name={t("detail.fact.slug")}>{project.slug}</Fact>
                {/* The one field on a project written for a reader. */}
                <Fact name={t("detail.fact.name")} voice="prose">
                  {project.name}
                </Fact>
                {/* The identity block: the mark (resolved, not just stored —
                    the derivation is as much a fact about the project as the
                    override), the accent as a dot and a value, and the
                    vocabulary. The dot reads the same one custom property the
                    registry's row sets; the words carry the meaning, the
                    colour only reinforces it. */}
                <Fact name={t("detail.fact.mark")}>
                  <span
                    className={styles.markFact}
                    style={projectAccentStyle(project.color)}
                  >
                    <ProjectMark project={project} size="md" />
                    <span className={styles.accentDot} aria-hidden="true" />
                    <span>{markLabel(resolveProjectMark(project))}</span>
                  </span>
                </Fact>
                {project.icon ? (
                  <Fact name={t("identity.icon")} selectable>
                    {project.icon}
                  </Fact>
                ) : (
                  <Fact name={t("identity.icon")} absent>
                    {t("detail.fact.iconAbsent")}
                  </Fact>
                )}
                {project.color ? (
                  <Fact name={t("identity.accentColour")} selectable>
                    {project.color}
                  </Fact>
                ) : (
                  <Fact name={t("identity.accentColour")} absent>
                    {t("detail.fact.colourAbsent")}
                  </Fact>
                )}
                {project.tags.length > 0 ? (
                  <Fact name={t("identity.tags")}>
                    <span
                      className={styles.tagFact}
                      style={projectAccentStyle(project.color)}
                    >
                      {project.tags.map((tag) => (
                        <span
                          key={tag}
                          className={styles.tagChip}
                          data-test="project-fact-tag"
                        >
                          {tag}
                        </span>
                      ))}
                    </span>
                  </Fact>
                ) : (
                  <Fact name={t("identity.tags")} absent>
                    {t("detail.fact.tagsAbsent")}
                  </Fact>
                )}
                {project.gitProfileRepo ? (
                  /* The one fact on this page somebody copies out of it, so it
                      is selected as a unit rather than as part of a sentence —
                      the same treatment the run id gets. */
                  <Fact name={t("identity.repo")} selectable>
                    {project.gitProfileRepo}
                  </Fact>
                ) : (
                  /* Not missing — running on the platform's own profiles, which
                      is a legitimate way for a project to be configured. The
                      registry column says it in exactly these words; two
                      spellings of one fact is how the two screens start
                      disagreeing. */
                  <Fact name={t("identity.repo")} absent>
                    {t("identity.platformDefaults")}
                  </Fact>
                )}
                <Fact name={t("detail.fact.created")}>{project.createdAt}</Fact>
              </FactList>

              {/* The edit affordance — an act, so it stays visible and names
                  what it needs rather than disappearing for a role that
                  cannot use it. Three optional fields are not a page (design
                  D8), so the editor opens under the facts it edits. */}
              <div className={styles.identityBar}>
                {editingIdentity ? null : mayEditRecord.allowed ? (
                  <Tooltip content={t("detail.editIdentity")}>
                    <Button
                      size="sm"
                      data-test="identity-edit"
                      aria-label={t("detail.editIdentity")}
                      onClick={() => {
                        setEditingIdentity(true)
                      }}
                    >
                      <Pencil aria-hidden="true" />
                      {t("detail.editIdentity")}
                    </Button>
                  </Tooltip>
                ) : (
                  <Tooltip
                    content={mayEditRecord.denial ?? t("detail.editIdentity")}
                  >
                    <Button
                      size="sm"
                      data-test="identity-edit"
                      denied={mayEditRecord.denial}
                    >
                      <Pencil aria-hidden="true" />
                      {t("detail.editIdentity")}
                    </Button>
                  </Tooltip>
                )}
              </div>

              {editingIdentity ? (
                <>
                  {updateProject.error ? (
                    <Notice tone="bad" data-test="identity-edit-failure">
                      {requestFailureMessage(
                        updateProject.error,
                        t("detail.editIdentityFailure")
                      )}
                    </Notice>
                  ) : null}
                  <ProjectIdentityEditor
                    project={project}
                    busy={updateProject.isPending}
                    onSave={(patch: ProjectIdentityPatch) => {
                      // Only the identity fields ride the PATCH; name and
                      // description go as `null` (untouched), and the tags
                      // list goes exactly as edited — an emptied list is the
                      // one clear this surface can honestly make (D5).
                      updateProject.mutate(
                        {
                          projectId: project.id,
                          patch: {
                            name: null,
                            description: null,
                            icon: patch.icon,
                            color: patch.color,
                            tags: patch.tags,
                          },
                        },
                        {
                          onSuccess: () => {
                            setEditingIdentity(false)
                          },
                        }
                      )
                    }}
                    onCancel={() => {
                      setEditingIdentity(false)
                    }}
                  />
                </>
              ) : null}
            </Section>

            {/* --- who holds which role on it --- */}

            <Section
              id="project-roles"
              title={t("detail.rolesSection")}
              note={
                mayManageIdentity.allowed && identity.data
                  ? t("detail.rolesHeld", { count: grants.length })
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
                      {t("detail.rolesEmpty")}
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
                      {t("detail.rolesAll")}
                    </Link>
                    <Link
                      to="/identity/grants/new"
                      className={styles.outLink}
                      data-test="project-grant-new"
                    >
                      {t("detail.rolesGrant")}
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
                  subject={t("detail.rolesForbiddenSubject")}
                />
              )}
            </Section>

            {/* --- what the platform starts on its own --- */}

            {/* Gated on the intake-view permission: the dashboard's session
                vocabulary has no scheduler keys yet (the backend speaks
                `scheduler:read`/`scheduler:write`), and `sources.view` is the
                honest neighbour — a schedule is an admission source. */}
            {can(session, "sources.view", project.id) ? (
              <ScheduledJobsSection
                projectId={project.id}
                canEdit={can(session, "sources.edit", project.id)}
              />
            ) : null}

            {/* --- everything else, with a count and a link --- */}

            {handoffs.length > 0 ? (
              <Section
                id="project-elsewhere"
                title={t("handoff.section")}
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

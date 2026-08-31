import { RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useObservabilityQuery } from "@/domains/observability/api/queries"
import { BoardsPanel } from "@/domains/observability/ui/boards-panel"
import { ConnectGuide } from "@/domains/observability/ui/connect-guide"
import { useSession } from "@/shared/session"
import { Button, Section, Tooltip } from "@/shared/ui"

import styles from "./observability-page.module.css"

const SKELETON_WIDTHS = ["70%", "48%", "62%"]

/**
 * Where the metrics and the logs live.
 *
 * A page of links, and the links are the design rather than a placeholder for
 * an embed. The requirements rule the iframe out (§15), and the reason is worth
 * saying on the page itself, so it is: infra logs and run timelines are read on
 * different clocks by people asking different questions, and a surface that
 * showed both would teach an operator to look for a run's story inside a
 * metrics board — where half of it is not.
 *
 * That makes this a genuinely small section, and it should read as deliberate
 * rather than thin. What earns its place is the second half: the operator who
 * opens this on a fresh installation finds three boards they cannot reach, and
 * the page tells them exactly whose job that is and where the definitions live.
 *
 * **Nothing inside is gated.** Observability is platform-scoped, so the route's
 * `observability.view` — asked as `can(session, permission)` with no project id
 * — is the whole gate; there is no act on this page to gate a second time.
 * Every control is a link out. The day the platform learns to import a board on
 * the operator's behalf, that button gets a check and this note goes.
 */
export function ObservabilityPage() {
  const { data, isLoading, isError, error, refetch } = useObservabilityQuery()
  const session = useSession()

  const boards = data?.boards ?? []
  const reachable = boards.filter((board) => board.url !== null).length
  const noBoards = boards.length > 0 && reachable === 0
  const ready = !isLoading && !isError && data !== undefined

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "platform" }, { label: "observability" }]}
          title="Observability"
          summary={
            ready ? (
              reachable > 0 ? (
                <>
                  <span className={styles.strong}>{reachable}</span> of{" "}
                  <span className={styles.strong}>{boards.length}</span> boards
                  reachable · {data.grafana?.baseUrl ?? "no grafana configured"}
                </>
              ) : (
                <span className={styles.warn}>no boards are reachable yet</span>
              )
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="observability-loading">
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
            <p className={styles.stateTitle}>Couldn&apos;t load the boards</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="observability-retry"
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

        {ready ? (
          <>
            <Section
              variant="screen"
              data-test="observability-boards"
              title="Boards"
              note={
                <>
                  These open in grafana, in a new tab, and they are not embedded
                  here on purpose. Infra metrics and a run&apos;s own timeline
                  are read on different clocks by people asking different
                  questions — one asks whether the platform is healthy, the
                  other asks what happened to one ticket — and a screen that
                  showed both would teach an operator to look for a run&apos;s
                  story in a metrics board, where only half of it is. A
                  run&apos;s story is on <span className={styles.code}>/runs</span>
                  ; this is everything underneath it.
                </>
              }
            >
              <BoardsPanel boards={boards} />
            </Section>

            <Section
              variant="screen"
              data-test="observability-connect"
              title="How to connect"
              note={
                <>
                  The boards are platform-wide: one board covers all{" "}
                  <span className={styles.strong}>{session.projects.length}</span>{" "}
                  projects, which is why there is no project filter on this page
                  and why the boards themselves carry the project as a variable.
                  Their definitions are versioned with the platform, so a board
                  and the metric it reads change in one commit.
                </>
              }
            >
              <ConnectGuide
                grafana={data.grafana}
                boardsRepo={data.boardsRepo}
                noBoards={noBoards}
              />
            </Section>
          </>
        ) : null}
      </div>
    </AppShell>
  )
}

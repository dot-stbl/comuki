import { useCallback, useMemo, useState } from "react"
import { RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useRetireStaleWorkers,
  useTakeComputeWork,
} from "@/domains/compute/api/mutations"
import { useComputeQuery } from "@/domains/compute/api/queries"
import {
  bindingFirst,
  strandedIdle,
  versionLabel,
} from "@/domains/compute/model/capacity"
import type {
  ComputeProvider,
  WorkerVersion,
} from "@/domains/compute/model/types"
import { CapacityCard } from "@/domains/compute/ui/capacity-card"
import { ProvidersPanel } from "@/domains/compute/ui/providers-panel"
import { VersionsPanel } from "@/domains/compute/ui/versions-panel"
import { useObservabilityQuery } from "@/domains/observability/api/queries"
import { BoardsPanel } from "@/domains/observability/ui/boards-panel"
import { ConnectGuide } from "@/domains/observability/ui/connect-guide"
import { can, projectOf, useSession } from "@/shared/session"
import { Button, ConfirmDialog, Section, Tooltip } from "@/shared/ui"

import styles from "./compute-page.module.css"

const SKELETON_WIDTHS = ["46%", "78%", "62%", "88%", "40%"]

/**
 * Where containers actually run.
 *
 * The lower tier of the rail, and a different clock from the duty screens: this
 * is opened rarely, deliberately, and usually because something will not scale.
 * So it is dense and it is not urgent — no live counts, no pulse, no board. It
 * is a registry, read top to bottom.
 *
 * Four sections, in the order the question is actually asked:
 *
 *   1. **providers** — which `IComputeProvider` instances exist and which one is
 *      taking new starts. Docker and Kubernetes; containerd is not v1 and does
 *      not appear.
 *   2. **pools** — the two ceilings side by side. v1 scaling is quota-aware
 *      *plus* the provider's capacity API, so a scale-up stops at whichever of
 *      two independent limits runs out first, and the screen's whole job is to
 *      say which one that is instead of making somebody subtract.
 *   3. **worker versions** — the label a claim is matched against, image digest
 *      plus profiles git-ref. This is where a full idle pool sitting next to a
 *      growing queue stops being a mystery.
 *   4. **boards** — the grafana boards the platform ships definitions for,
 *      folded in from the observability screen that no longer has a page of
 *      its own. Links out, never an embed, and the guide under the list says
 *      how to connect an installation that has nothing in it yet.
 *
 * Both acts here gate on `compute.manage`, a *platform* permission: it reads
 * platform roles alone, so no `projectId` is ever passed with it. The route
 * already gated `compute.view`; nothing inside re-gates viewing — except the
 * boards section, which is the first *folded* section and the precedent it
 * sets: the screen's permission gates the door, and a section folded in from
 * another screen carries its own permission and *hides* below the door's.
 * A denied act stays in the document and says what is missing; a denied
 * section has no act whose denial could be explained, so it is simply not
 * that session's to see.
 */
export function ComputePage() {
  const { data, isLoading, isError, error, refetch } = useComputeQuery()
  const session = useSession()

  const [retiring, setRetiring] = useState<WorkerVersion | null>(null)

  const takeWork = useTakeComputeWork()
  const retire = useRetireStaleWorkers()

  const providers = useMemo(() => data?.providers ?? [], [data])
  const pools = useMemo(() => data?.pools ?? [], [data])
  const versions = useMemo(() => data?.versions ?? [], [data])

  // The folded-section rule, first spelled here: the route's `compute.view`
  // gates the door, and the boards section carries its own permission —
  // `observability.view`, platform scope like the door's — and hides below
  // it, never greys out. There is no act in it whose denial could be
  // explained, so a session that cannot read it never sees it, and the query
  // is never even asked.
  const boardsVisible = can(session, "observability.view")
  const observability = useObservabilityQuery({ enabled: boardsVisible })

  const boards = observability.data?.boards ?? []
  const noBoards =
    boards.length > 0 && boards.every((board) => board.url === null)

  // Tightest first: the pool about to refuse work is the one somebody came
  // here about, and it should not be third in a list sorted by project name.
  const orderedPools = useMemo(
    () => bindingFirst(pools, providers),
    [pools, providers]
  )

  const active = providers.find((provider) => provider.takingWork)
  const stranded = strandedIdle(versions)
  const workers = pools.reduce((total, pool) => total + pool.workers, 0)

  // The button already refuses a denied click, but the handler answers the same
  // question again on the way in: the gate is the permission, not the control
  // that happens to be carrying it today.
  const takeWorkMutate = takeWork.mutate
  const onTakeWork = useCallback(
    (provider: ComputeProvider) => {
      if (!can(session, "compute.manage")) {
        return
      }
      takeWorkMutate(provider.id)
    },
    [takeWorkMutate, session]
  )

  const onRetire = useCallback(
    (version: WorkerVersion) => {
      if (!can(session, "compute.manage")) {
        return
      }
      setRetiring(version)
    },
    [session]
  )

  const switchingId = takeWork.isPending ? (takeWork.variables ?? null) : null
  const retiringLabel =
    retire.isPending && retire.variables
      ? `${retire.variables.digest}|${retire.variables.profilesRef}`
      : null

  const failure = takeWork.error ?? retire.error
  const ready = !isLoading && !isError

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "platform" }, { label: "compute" }]}
          title="Compute"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{providers.length}</span>{" "}
                providers · <span className={styles.strong}>{workers}</span>{" "}
                workers up ·{" "}
                {active ? (
                  <>
                    <span className={styles.strong}>{active.kind}</span> takes
                    new starts
                  </>
                ) : (
                  <span className={styles.warn}>nothing takes new starts</span>
                )}
                {stranded > 0 ? (
                  <>
                    {" · "}
                    <span className={styles.warn}>{stranded}</span> idle on a
                    label nothing matches
                  </>
                ) : null}
              </>
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="compute-loading">
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
            <p className={styles.stateTitle}>Couldn&apos;t load compute</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="compute-retry"
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

        {failure ? (
          <p className={styles.failure} role="alert">
            {failure instanceof Error ? failure.message : "The change failed."}{" "}
            Nothing moved — the registry is back as it was.
          </p>
        ) : null}

        {ready ? (
          <>
            <Section
              variant="screen"
              data-test="compute-providers"
              title="Providers"
              note={
                <>
                  Docker for dev and compose, Kubernetes for prod — the two
                  <code className={styles.code}>IComputeProvider</code>{" "}
                  implementations v1 has. One takes new starts; the rest hold
                  the leases they already handed out.
                </>
              }
            >
              <ProvidersPanel
                providers={providers}
                pools={pools}
                switchingId={switchingId}
                onTakeWork={onTakeWork}
              />
            </Section>

            <Section
              variant="screen"
              data-test="compute-pools"
              title="Pools"
              note={
                <>
                  Scaling is quota-aware plus the provider&apos;s capacity api,
                  so every pool has two ceilings with two different owners. The
                  one marked <span className={styles.tag}>binding</span> is the
                  one refusing the next container — the other is what it had
                  spare.
                </>
              }
            >
              {orderedPools.length === 0 ? (
                <p className={styles.sectionEmpty}>no pool is configured</p>
              ) : (
                <div className={styles.pools}>
                  {orderedPools.map((pool) => (
                    <CapacityCard
                      key={`${pool.projectId}/${pool.providerId}`}
                      pool={pool}
                      provider={providers.find(
                        (provider) => provider.id === pool.providerId
                      )}
                      projectKey={
                        projectOf(session, pool.projectId)?.key ??
                        pool.projectId
                      }
                    />
                  ))}
                </div>
              )}
            </Section>

            <Section
              variant="screen"
              data-test="compute-versions"
              title="Worker versions"
              note={
                <>
                  A worker is labelled by image digest <em>and</em> profiles
                  git-ref. Changing either only affects a new start — an idle
                  worker on any other label is never matched to an item, which
                  is how a full pool sits beside a growing queue.
                </>
              }
            >
              <VersionsPanel
                versions={versions}
                retiringLabel={retiringLabel}
                onRetire={onRetire}
              />
            </Section>

            {boardsVisible && observability.data ? (
              <Section
                variant="screen"
                data-test="compute-boards"
                title="Boards"
                note={
                  <>
                    The grafana boards, folded in from the screen that used to
                    own them. They open in a new tab and are not embedded here
                    on purpose: infra metrics and a run&apos;s own timeline are
                    read on different clocks by people asking different
                    questions, and a surface that showed both would teach an
                    operator to look for a run&apos;s story in a metrics board,
                    where only half of it is. A run&apos;s story is on{" "}
                    <span className={styles.code}>/runs</span>. One board covers
                    every project at once, and the definitions are versioned
                    with the platform — the guide under the list says how to
                    connect an installation that has nothing in it yet.
                  </>
                }
              >
                <BoardsPanel boards={boards} />
                <ConnectGuide
                  grafana={observability.data.grafana}
                  boardsRepo={observability.data.boardsRepo}
                  noBoards={noBoards}
                />
              </Section>
            ) : null}
          </>
        ) : null}
      </div>

      <ConfirmDialog
        open={retiring !== null}
        danger
        title="Retire the idle workers on this label?"
        body={
          retiring
            ? `${versionLabel(retiring)} — ${retiring.idle} idle containers are torn down. The ${retiring.workers - retiring.idle} still holding a lease keep running until their item lands.`
            : ""
        }
        confirmLabel="Retire idle"
        cancelLabel="Leave them up"
        onConfirm={() => {
          if (retiring && can(session, "compute.manage")) {
            retire.mutate({
              digest: retiring.digest,
              profilesRef: retiring.profilesRef,
            })
          }
          setRetiring(null)
        }}
        onCancel={() => setRetiring(null)}
      />
    </AppShell>
  )
}

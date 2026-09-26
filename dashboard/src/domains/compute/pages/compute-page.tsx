import { useCallback, useMemo, useState } from "react"
import { RotateCw } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

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
import {
  Button,
  ConfirmDialog,
  ScreenState,
  Section,
  Skeleton,
  Tooltip,
} from "@/shared/ui"

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
  const { t } = useTranslation("compute")
  const { t: tShell } = useTranslation("shell")
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
          breadcrumbs={[
            { label: tShell("crumb.platform") },
            { label: tShell("crumb.compute") },
          ]}
          title={t("page.title")}
          summary={
            ready ? (
              <>
                {/* The figures are values in their own voice and the words are
                    the product's; the slots stringify the counts because a
                    Trans slot holding a bare falsy `0` renders empty, and zero
                    is a reading. */}
                <Trans
                  ns="compute"
                  i18nKey="page.summary"
                  components={{
                    providers: (
                      <span className={styles.strong}>
                        {String(providers.length)}
                      </span>
                    ),
                    workers: (
                      <span className={styles.strong}>{String(workers)}</span>
                    ),
                    takes: active ? (
                      <Trans
                        ns="compute"
                        i18nKey="page.summaryTakes"
                        components={{
                          kind: (
                            <span className={styles.strong}>{active.kind}</span>
                          ),
                        }}
                      />
                    ) : (
                      <span className={styles.warn}>
                        {t("page.summaryNothing")}
                      </span>
                    ),
                  }}
                />
                {stranded > 0 ? (
                  <>
                    {" · "}
                    <Trans
                      ns="compute"
                      i18nKey="page.summaryStranded"
                      components={{
                        count: (
                          <span className={styles.warn}>
                            {String(stranded)}
                          </span>
                        ),
                      }}
                    />
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
          <Skeleton
            lines={SKELETON_WIDTHS}
            inset="flush"
            label={t("page.loading")}
            data-test="compute-loading"
          />
        ) : null}

        {isError ? (
          <ScreenState
            kind="error"
            title={t("page.errorTitle")}
            description={
              error instanceof Error ? error.message : t("errors.unknown")
            }
            inset="flush"
            data-test="compute-error"
            action={
              <Tooltip content={t("actions.retry")}>
                <Button
                  size="icon-sm"
                  data-test="compute-retry"
                  aria-label={t("actions.retry")}
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

        {failure ? (
          <p className={styles.failure} role="alert">
            {failure instanceof Error
              ? failure.message
              : t("errors.changeFailed")}{" "}
            {t("page.failureTail")}
          </p>
        ) : null}

        {ready ? (
          <>
            <Section
              variant="screen"
              data-test="compute-providers"
              title={t("providers.section")}
              note={
                <Trans
                  ns="compute"
                  i18nKey="providers.note"
                  components={{
                    code: <code className={styles.code} />,
                  }}
                />
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
              title={t("pools.section")}
              note={
                <Trans
                  ns="compute"
                  i18nKey="pools.note"
                  components={{
                    tag: <span className={styles.tag} />,
                  }}
                />
              }
            >
              {orderedPools.length === 0 ? (
                <p className={styles.sectionEmpty}>{t("pools.empty")}</p>
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
              title={t("versions.section")}
              note={
                <Trans
                  ns="compute"
                  i18nKey="versions.note"
                  components={{
                    em: <em />,
                  }}
                />
              }
            >
              <VersionsPanel
                versions={versions}
                retiringLabel={retiringLabel}
                onRetire={onRetire}
              />
            </Section>

            {/* Rendered on the permission, not on the payload. Keyed off
                `observability.data` this section simply was not there while
                the request was in flight and stayed missing forever if it
                failed — a whole region of the screen disappearing with no
                loading bar, no sentence and no way to ask again. The
                permission decides whether the section exists; the query
                decides which of its three states is showing. */}
            {boardsVisible ? (
              <Section
                variant="screen"
                data-test="compute-boards"
                title={t("boards.section")}
                note={
                  <Trans
                    ns="compute"
                    i18nKey="boards.note"
                    components={{
                      code: <code className={styles.code} />,
                    }}
                  />
                }
              >
                {observability.isLoading ? (
                  <Skeleton
                    lines={3}
                    inset="none"
                    label={t("boards.loading")}
                    data-test="boards-loading"
                  />
                ) : null}

                {observability.isError ? (
                  <ScreenState
                    kind="error"
                    title={t("boards.errorTitle")}
                    description={
                      observability.error instanceof Error
                        ? observability.error.message
                        : t("errors.unknown")
                    }
                    /* The section has already paid for its own room, so the
                       state stands on its edge rather than buying more. */
                    inset="none"
                    data-test="boards-error"
                    action={
                      <Tooltip content={t("actions.retry")}>
                        <Button
                          size="icon-sm"
                          data-test="boards-retry"
                          aria-label={t("actions.retry")}
                          onClick={() => {
                            void observability.refetch()
                          }}
                        >
                          <RotateCw aria-hidden="true" />
                        </Button>
                      </Tooltip>
                    }
                  />
                ) : null}

                {observability.data ? (
                  <>
                    <BoardsPanel boards={boards} />
                    <ConnectGuide
                      grafana={observability.data.grafana}
                      boardsRepo={observability.data.boardsRepo}
                      noBoards={noBoards}
                    />
                  </>
                ) : null}
              </Section>
            ) : null}
          </>
        ) : null}
      </div>

      <ConfirmDialog
        open={retiring !== null}
        danger
        title={t("versions.retireTitle")}
        body={
          retiring
            ? t("versions.retireBody", {
                label: versionLabel(retiring),
                idle: retiring.idle ?? 0,
                rest: (retiring.workers ?? 0) - (retiring.idle ?? 0),
              })
            : ""
        }
        confirmLabel={t("versions.retireConfirm")}
        cancelLabel={t("versions.retireCancel")}
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

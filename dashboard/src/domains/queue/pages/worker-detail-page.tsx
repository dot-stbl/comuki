import { useCallback, useMemo, useRef, useState } from "react"
import type { ReactNode } from "react"
import { Link } from "@tanstack/react-router"
import { Loader2, LogOut, PowerOff, RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { cn } from "@/shared/lib/utils"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, ConfirmDialog, Section, Tooltip } from "@/shared/ui"

import { formatDuration } from "@/domains/runs/model/format"
import {
  useDrainWorker,
  useForceStopWorker,
} from "@/domains/queue/api/mutations"
import { useQueueQuery } from "@/domains/queue/api/queries"
import {
  HEARTBEAT_STALE_SEC,
  leaseHeat,
  lostHeartbeatSentence,
} from "@/domains/queue/model/queue"
import type { QueueItem, Worker } from "@/domains/queue/model/types"
import { AgeMeter, LeaseMeter } from "@/domains/queue/ui/meters"
import { WorkerStateBadge } from "@/domains/queue/ui/queue-badges"

import styles from "./worker-detail-page.module.css"

const SKELETON_WIDTHS = ["44%", "78%", "61%", "35%"]

export interface WorkerDetailPageProps {
  /**
   * From the path. A container is a thing while it is up, so looking at one
   * has an address.
   *
   * Handed down as a prop rather than read from `getRouteApi` inside the page,
   * which is the split `LinkOidcPage` already uses: the route reads the param
   * and the page is then an ordinary component that a story or a test can
   * mount without standing the product's whole route tree up first.
   */
  workerId: string
}

/**
 * One worker, at `/queue/workers/<id>`.
 *
 * ## What this page is for, and what it deliberately refuses to draw
 *
 * A detail page links to the real screens with a filter applied; it does not
 * redraw their tables. Everything here is something that is only knowable
 * about *this* container — the clocks it is running against, the image it came
 * up on, the one item it holds a lease on — and everything else is handed off
 * with a link the product already mints: `/queue?w=<digest>` for every
 * container on an image, `/queue?q=<itemId>` for a work item, `/runs/<id>` for
 * a run. There is no second set of URL parameters here and there must not be:
 * `app/search/shapes.ts` is the catalogue of hand-offs, and a page that
 * invented its own would be a destination the resolver could never point at.
 *
 * ## Why this is the detail page with time in it
 *
 * A run's detail page is a record; this one is a *reading*. A lease expires, a
 * heartbeat ages, and the container itself is ephemeral by design — the
 * product tears one down deliberately after the work it was raised for. So the
 * live facts are set as live facts: what is left of the lease, how long the
 * silence has been, how long the container has been up. The thresholds that
 * decide when any of those stop being routine live in `model/queue.ts` beside
 * the words that go with them; nothing here restates a number.
 *
 * The two readings are drawn by `LeaseMeter` and `AgeMeter`, the same two the
 * pool's own columns use. A third meter spelled out on this page would be a
 * second way of drawing a duration inside one screen's walk.
 */
export function WorkerDetailPage({ workerId }: WorkerDetailPageProps) {
  const { data, isLoading, isError, error, refetch } = useQueueQuery()
  const session = useSession()
  const [confirming, setConfirming] = useState(false)

  const drain = useDrainWorker()
  const forceStop = useForceStopWorker()

  const worker = useMemo(
    () => data?.workers.find((entry) => entry.id === workerId) ?? null,
    [data, workerId]
  )

  /* The last snapshot of this container that actually arrived in a payload.
   *
   * A **ref**, and both halves of that matter. It must not cause a render of
   * its own — it is written during the render that already has the value, and
   * nothing downstream is waiting to be told about it. And it is a *record of
   * the past* rather than a value to be reconciled: state exists to be kept in
   * step with something, and there is nothing left to keep this in step with.
   * The container is gone.
   *
   * State here would also be visibly wrong, not merely heavier. A `setState`
   * in an effect lands one frame *after* the render that discovered the worker
   * had vanished — and that frame has no memory yet, so it would paint the
   * not-found state for a moment before correcting itself to the torn-down
   * one. Two different sentences, one of them a lie, in the same blink. */
  const seen = useRef<Worker | null>(null)
  /* eslint-disable-next-line react-hooks/refs -- The rule guards against a
     stale read: a ref consulted during a render that will not re-run when the
     ref moves. That cannot happen here. The write and the read are the same
     statement, in program order, and every render that reads it has just
     written it if there is anything to write. What the ref carries is only
     ever *consumed* on a render the query already caused. */
  const lastSeen = rememberWorker(seen, workerId, worker)

  const item = useMemo(() => {
    const held = worker?.itemId
    if (!held) {
      return null
    }
    return data?.items.find((entry) => entry.id === held) ?? null
  }, [data, worker])

  /* What the container was holding when it went. `pool.store.ts` requeues an
     orphaned item rather than failing it, so this is still in the payload —
     with a fresh age and no claimant — and that is exactly the thing the
     torn-down state has to be able to point at. */
  const orphaned = useMemo(() => {
    const held = lastSeen?.itemId
    if (!held) {
      return null
    }
    return data?.items.find((entry) => entry.id === held) ?? null
  }, [data, lastSeen])

  const project = worker ? projectOf(session, worker.projectId) : null
  const known = worker ?? lastSeen

  // Resolved per page for the same reason the pool resolves it per row: the
  // same person administers one project's pool and can only watch the next
  // one's, and this page is opened from a list that mixes them.
  const allowed = worker ? can(session, "runs.stop", worker.projectId) : false
  const denial =
    worker && !allowed
      ? needsLabel("runs.stop", projectOf(session, worker.projectId)?.key)
      : null

  const draining = drain.isPending
  const stopping = forceStop.isPending
  const busy = draining || stopping

  const onDrain = useCallback(() => {
    if (worker) {
      drain.mutate(worker.id)
    }
  }, [drain, worker])

  const onConfirmStop = useCallback(() => {
    if (worker) {
      forceStop.mutate(worker.id)
    }
    setConfirming(false)
  }, [forceStop, worker])

  /* The query answered. Everything below turns on this rather than on
     `worker !== null`, because "not in the payload" and "no payload yet" are
     two different sentences and only one of them is about the worker. */
  const resolved = Boolean(data) && !isError
  const gone = resolved && !worker
  const failure = drain.error ?? forceStop.error

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: "observe", to: "/runs" },
            { label: "queue", to: "/queue" },
            { label: workerId },
          ]}
          // The id is a value, so it keeps the data voice even at title size:
          // it is the one thing on this screen that was pasted in from a log
          // and will be pasted back out into a ticket.
          title={<span className={styles.titleId}>{workerId}</span>}
          summary={
            known ? (
              <>
                <span className={styles.summaryValue}>
                  {projectOf(session, known.projectId)?.key ?? known.projectId}
                </span>{" "}
                · <span className={styles.summaryValue}>{known.profile}</span> ·{" "}
                <span className={styles.summaryValue}>{known.provider}</span>
              </>
            ) : undefined
          }
          actions={
            worker ? (
              <div className={styles.acts}>
                <WorkerStateBadge state={worker.state} />
                {/* Two acts, not two intensities of one act. Drain is polite
                    and lossless — the container stops claiming and the item in
                    hand finishes — and force stop is neither. So they are two
                    icon buttons rather than a menu or a severity slider, and
                    only the one that loses something asks first. */}
                <Tooltip content={denial ?? "Drain"}>
                  <Button
                    variant="outline"
                    size="icon-sm"
                    data-test="worker-drain"
                    /* `disabled` is for busy and invalid — a container already
                       leaving has nothing left to drain. A refusal is a
                       different thing and takes `denied`, which keeps the
                       control reachable so its sentence is. */
                    disabled={busy || worker.state === "draining"}
                    denied={denial}
                    aria-busy={draining || undefined}
                    aria-label={`Drain ${worker.id}`}
                    onClick={onDrain}
                  >
                    {draining ? (
                      <Loader2 className={styles.spin} aria-hidden="true" />
                    ) : (
                      <LogOut aria-hidden="true" />
                    )}
                  </Button>
                </Tooltip>
                <Tooltip content={denial ?? "Force stop"}>
                  <Button
                    variant="destructive"
                    size="icon-sm"
                    data-test="worker-force-stop"
                    disabled={busy}
                    denied={denial}
                    aria-busy={stopping || undefined}
                    aria-label={`Force stop ${worker.id}`}
                    onClick={() => setConfirming(true)}
                  >
                    <PowerOff aria-hidden="true" />
                  </Button>
                </Tooltip>
              </div>
            ) : null
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="worker-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {/* A load failure is a third thing, and the only one of the three that
            is an error. The container being gone is not a failure of this
            screen; this is. */}
        {isError ? (
          <div className={styles.state} role="alert" data-test="worker-error">
            <p className={styles.stateTitle}>Couldn&apos;t load the pool</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="worker-retry"
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
          <p
            className={styles.failure}
            role="alert"
            data-test="worker-act-failed"
          >
            {failure instanceof Error
              ? failure.message
              : "The pool did not take that."}{" "}
            Nothing changed — the worker is as it was.
          </p>
        ) : null}

        {worker ? (
          <>
            {/* --- live ------------------------------------------------- */}
            {/* The clocks. Everything in this region is true for as long as it
                takes to read it, which is the whole reason the page exists in
                the shape it does. The state itself is up in the header beside
                the acts that change it — said once, where the verbs are. */}
            <Section title="live" id="worker-live" data-test="worker-live">
              <div className={styles.readings}>
                <Reading
                  label="lease left"
                  note={
                    worker.leaseSec === null
                      ? "nothing claimed, so there is no lease to defend"
                      : undefined
                  }
                >
                  <LeaseMeter worker={worker} />
                </Reading>

                <Reading
                  label="since heartbeat"
                  note={`stale past ${formatDuration(HEARTBEAT_STALE_SEC)}`}
                >
                  {formatDuration(worker.heartbeatAgeSec)}
                </Reading>

                <Reading label="up" note="since the container came up">
                  {formatDuration(worker.upSec)}
                </Reading>
              </div>

              {/* Said out loud rather than left in a tooltip: on a page about
                  one container there is room for the consequence, and somebody
                  who came here came to find out what it means. The wording is
                  the model's, so the pool's tooltip and this cannot drift. */}
              {leaseHeat(worker) === "lost" ? (
                <p className={styles.lost} data-test="worker-lost-heartbeat">
                  {lostHeartbeatSentence(worker)}
                </p>
              ) : null}
            </Section>

            {/* --- container -------------------------------------------- */}
            <Section
              title="container"
              id="worker-container"
              data-test="worker-container"
            >
              <div className={styles.readings}>
                <Reading label="compute">{worker.provider}</Reading>

                <Reading label="handle" wrap>
                  {worker.handle}
                </Reading>

                {/* The one hand-off in this region, and the question a digest
                    is nearly always being asked in aid of: a claim only
                    matches a container whose image is current, so "why is that
                    one draining" is answered by everything else on the same
                    image. `/queue?w=` is the href `shapes.ts` already mints
                    for a digest — the same address a pasted `sha256:…`
                    resolves to. */}
                <Reading label="image" note="every container on this image">
                  <Link
                    to="/queue"
                    search={{ w: worker.digest }}
                    className={styles.link}
                    data-test="worker-image-link"
                  >
                    {worker.digest}
                  </Link>
                </Reading>

                <Reading label="profile" note="the axis a claim matches on">
                  {worker.profile}
                </Reading>

                <Reading label="project">
                  {project?.key ?? worker.projectId}
                </Reading>
              </div>
            </Section>

            {/* --- current work ----------------------------------------- */}
            {/* One item, named — never a table. The queue is a screen of its
                own and this page links to it with a filter applied. */}
            <Section
              title="current work"
              id="worker-work"
              data-test="worker-work"
            >
              {item ? (
                <div className={styles.work}>
                  <p className={styles.workLabel}>{item.label}</p>
                  <div className={styles.workFacts}>
                    <Link
                      to="/runs/$runId"
                      params={{ runId: item.runId }}
                      className={styles.link}
                      data-test="worker-work-run"
                    >
                      {item.runId}
                    </Link>
                    <Link
                      to="/queue"
                      search={{ q: item.id }}
                      className={styles.link}
                      data-test="worker-work-item"
                    >
                      {item.id}
                    </Link>
                    <AgeMeter item={item} />
                  </div>
                </div>
              ) : worker.itemId ? (
                /* A lease on an item this payload does not carry. Rare, and
                   the honest answer is the id and the way to look it up —
                   not a blank region that reads as a rendering fault. */
                <div className={styles.work}>
                  <p className={styles.stateBody}>
                    Holding an item the queue has not sent with this payload.
                  </p>
                  <Link
                    to="/queue"
                    search={{ q: worker.itemId }}
                    className={styles.link}
                    data-test="worker-work-item"
                  >
                    {worker.itemId}
                  </Link>
                </div>
              ) : (
                /* Idle is the pool doing its job, not a gap — and it is said
                   in the same word the pool's own column says it in. */
                <div className={styles.work}>
                  <span className={styles.idle}>idle</span>
                  <p className={styles.readingNote}>
                    Holding nothing, and free to claim the next item that
                    matches its profile.
                  </p>
                </div>
              )}
            </Section>
          </>
        ) : null}

        {/* --- the container is gone ---------------------------------- */}
        {/* Two readings, and they are genuinely different. Which one is
            rendered turns on whether *this session* ever saw the worker:
            holding the last payload it was in is what makes the difference
            knowable at all, and neither reading is an error. */}
        {gone && lastSeen ? (
          <TornDown worker={lastSeen} item={orphaned} />
        ) : null}

        {gone && !lastSeen ? <NotFound workerId={workerId} /> : null}
      </div>

      <ConfirmDialog
        open={confirming}
        danger
        title="Force stop this worker?"
        body={stopBody(worker, item)}
        confirmLabel="Force stop"
        cancelLabel="Leave it running"
        onConfirm={onConfirmStop}
        onCancel={() => setConfirming(false)}
      />
    </AppShell>
  )
}

/* ------------------------------------------------------------------ *
 * Page furniture
 * ------------------------------------------------------------------ */

/**
 * Remember the container, and answer with it once it is gone.
 *
 * One statement doing both halves, and deliberately so: the whole safety
 * argument for reading a ref during a render is that the write and the read
 * are the same gesture. Splitting them across the component would put two
 * places between which the memory could go stale.
 *
 * Keyed by id on the way *out* rather than cleared on the way in, so walking
 * from one worker's page to another's can never show the first container's
 * facts under the second one's title — the memory simply stops answering.
 */
function rememberWorker(
  memory: { current: Worker | null },
  workerId: string,
  worker: Worker | null
): Worker | null {
  if (worker) {
    memory.current = worker
  }
  return memory.current?.id === workerId ? memory.current : null
}

interface ReadingProps {
  /** Names a value, so it is set in the tight gesture. */
  label: string
  /** What the figure is measuring, when the label alone does not say. */
  note?: string
  /** Long, unbreakable values — a provider handle — give rather than overflow. */
  wrap?: boolean
  children: ReactNode
}

/**
 * One labelled value.
 *
 * Private to this page rather than a domain component: it is the shape a
 * *readout* takes here and nothing else consumes it yet. If a second screen
 * wants the same three lines, that is when it earns a file of its own.
 */
function Reading({ label, note, wrap, children }: ReadingProps) {
  return (
    <div className={styles.reading}>
      <span className={styles.readingLabel}>{label}</span>
      <span className={cn(styles.readingValue, wrap && styles.wrap)}>
        {children}
      </span>
      {note ? <span className={styles.readingNote}>{note}</span> : null}
    </div>
  )
}

/**
 * The container was here and is not any more.
 *
 * **This is the decision this screen turns on.** A worker vanishing while its
 * page is open is normal here rather than exceptional: force stop tears the
 * container down on purpose, a pool on `minIdle: 0` scales to zero the moment
 * it has nothing to do, and the product's whole compute model is a container
 * raised for one piece of work and removed after it. A blank screen and a 404
 * would both be lies about that — one says the screen broke, the other says
 * the id was never real.
 *
 * So the honest answer is the third one: *this container was torn down, here
 * is what it was holding, and here is where that work went*. The work is the
 * part that actually matters to whoever is reading, and it is knowable —
 * `pool.store.ts` requeues an orphaned item rather than failing it, so the
 * item is still in the same payload that no longer carries the worker, with a
 * fresh age and no claimant.
 *
 * Not `role="alert"`, and not styled as one. Nothing went wrong.
 */
function TornDown({
  worker,
  item,
}: {
  worker: Worker
  item: QueueItem | null
}) {
  return (
    <div className={styles.state} data-test="worker-torn-down">
      <p className={styles.stateTitle}>This container is gone</p>
      <p className={styles.stateBody}>
        <span className={styles.figure}>{worker.id}</span> was torn down while
        this page was open. A stopped container is not kept — there is no record
        of it to go back to, and there is not meant to be. Workers are raised
        for the work in front of them and removed when it is done or when
        somebody stops them; this is the pool behaving the way it is configured
        to, not a failure.
      </p>

      {item ? (
        <>
          <p className={styles.stateBody}>
            It was holding <span className={styles.figure}>{item.label}</span>.
            That work did not go with it: the lease was released and the item
            went back to the queue for another worker to claim.
          </p>
          <div className={styles.exits}>
            <Link
              to="/queue"
              search={{ q: item.id }}
              className={styles.link}
              data-test="worker-torn-down-item"
            >
              {item.id}
            </Link>
            <Link
              to="/runs/$runId"
              params={{ runId: item.runId }}
              className={styles.link}
              data-test="worker-torn-down-run"
            >
              {item.runId}
            </Link>
          </div>
        </>
      ) : (
        <p className={styles.stateBody}>
          It was idle when it went, so it was holding nothing and nothing
          returned to the queue.
        </p>
      )}

      <div className={styles.exits}>
        <Link
          to="/queue"
          className={styles.link}
          data-test="worker-torn-down-pool"
        >
          queue &amp; workers
        </Link>
      </div>
    </div>
  )
}

/**
 * The query answered and this session never saw the worker at all.
 *
 * A different reading from the one above, and it has to be: this is an old
 * link, a pasted id, a container from a shift that ended hours ago. Nothing
 * was torn down in front of anybody, so there is nothing to say about what it
 * was holding — and there is no honest way to invent it.
 *
 * It is the not-found state, and it names the missing thing rather than saying
 * "404" at somebody: the id is the only fact there is, and seeing it spelled
 * back is how a person catches the truncated paste that brought them here. The
 * pool itself is one link away, narrowed to the id they asked for, so they can
 * see for themselves that nothing matches rather than taking this page's word
 * for it. Not an error and not `role="alert"` — an id that no longer resolves
 * is the ordinary end of an ephemeral thing.
 */
function NotFound({ workerId }: { workerId: string }) {
  return (
    <div className={styles.state} data-test="worker-not-found">
      <p className={styles.stateTitle}>
        No worker called <span className={styles.figure}>{workerId}</span>
      </p>
      <p className={styles.stateBody}>
        Nothing in the pool answers to that id. Workers are ephemeral — one is
        raised for a piece of work and removed after it — so an id out of
        yesterday&apos;s log, an old bookmark or a link that lost its tail all
        land here, and none of them mean anything is wrong.
      </p>
      <div className={styles.exits}>
        <Link
          to="/queue"
          search={{ w: workerId }}
          className={styles.link}
          data-test="worker-not-found-pool"
        >
          look for {workerId} in the pool
        </Link>
        <Link
          to="/queue"
          className={styles.link}
          data-test="worker-not-found-queue"
        >
          queue &amp; workers
        </Link>
      </div>
    </div>
  )
}

/**
 * What the operator is actually about to do, in the two cases that differ.
 *
 * A busy container loses the item it is holding back to the queue; an idle one
 * loses nothing but itself. Saying which is the whole reason this act asks at
 * all — and the identifiers in it are values, which is why the dialog takes a
 * node rather than a string.
 */
function stopBody(worker: Worker | null, item: QueueItem | null): ReactNode {
  if (!worker) {
    return ""
  }
  if (!item) {
    return (
      <>
        <span className={styles.figure}>{worker.id}</span> is idle. The
        container is torn down now, and scale raises another when there is work
        for it.
      </>
    )
  }
  return (
    <>
      <span className={styles.figure}>{worker.id}</span> is holding{" "}
      <span className={styles.figure}>{item.label}</span> on run{" "}
      <span className={styles.figure}>{item.runId}</span>. The container is torn
      down now, the lease is released, and the item goes back to the queue for
      another worker to claim.
    </>
  )
}

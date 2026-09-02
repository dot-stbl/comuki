import { useCallback, useMemo, useState } from "react"
import { RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useRevokeKey,
  useSetProxyEnabled,
} from "@/domains/models/api/mutations"
import { useModelsQuery } from "@/domains/models/api/queries"
import { expiredKeys, keysNearCap } from "@/domains/models/model/keys"
import type { VirtualKey } from "@/domains/models/model/types"
import { EndpointsPanel } from "@/domains/models/ui/endpoints-panel"
import { ProxyPanel } from "@/domains/models/ui/proxy-panel"
import { RoleRoutingPanel } from "@/domains/models/ui/role-routing-panel"
import { VirtualKeysPanel } from "@/domains/models/ui/virtual-keys-panel"
import { can, useSession } from "@/shared/session"
import { Button, ConfirmDialog, Section, Tooltip } from "@/shared/ui"

import styles from "./models-page.module.css"

const SKELETON_WIDTHS = ["52%", "84%", "66%", "40%", "74%"]

/** What a confirm is currently asking about. One dialog, two questions. */
type Pending =
  { kind: "revoke"; entry: VirtualKey } | { kind: "proxy-off" } | null

/**
 * What the swarm is allowed to think with, and what that costs.
 *
 * The lower tier of the rail, and a different clock from the duty screens: read
 * rarely, deliberately, and usually because a bill or a refusal has already
 * happened. Dense, not urgent.
 *
 * Four sections, and the first one changes what the other three mean:
 *
 *   1. **proxy** — the thin optional proxy. Off, nothing below is enforced.
 *   2. **upstream endpoints** — openai-compatible and anthropic-compatible
 *      wires. A self-hosted url is an ordinary row.
 *   3. **Spend keys** — route, budget, models, scope and TTL all live inside
 *      the key, which is what makes a leaked one nearly useless. Revoking is
 *      destructive and asks first.
 *   4. **role → model** — the platform speaks in roles, and this table is where
 *      one becomes a physical model on a physical endpoint.
 *
 * Both acts gate on `models.manage`, a *platform* permission: it reads platform
 * roles alone, so no `projectId` is passed with it even for a key scoped to one
 * project. The route already gated `models.view`; nothing here re-gates viewing.
 */
export function ModelsPage() {
  const { data, isLoading, isError, error, refetch } = useModelsQuery()
  const session = useSession()

  const [pending, setPending] = useState<Pending>(null)

  const revoke = useRevokeKey()
  const setProxy = useSetProxyEnabled()

  const endpoints = useMemo(() => data?.endpoints ?? [], [data])
  const keys = useMemo(() => data?.keys ?? [], [data])
  const routes = useMemo(() => data?.routes ?? [], [data])
  const proxy = data?.proxy

  const enforced = proxy?.enabled ?? false
  const nearCap = useMemo(() => keysNearCap(keys), [keys])
  const expired = useMemo(() => expiredKeys(keys), [keys])

  // The control already refuses a denied click, but the handler answers the
  // same question again on the way in: the gate is the permission, not the
  // button that happens to be carrying it today.
  const onRevoke = useCallback(
    (entry: VirtualKey) => {
      if (!can(session, "models.manage")) {
        return
      }
      setPending({ kind: "revoke", entry })
    },
    [session]
  )

  const setProxyMutate = setProxy.mutate
  const onToggleProxy = useCallback(
    (next: boolean) => {
      if (!can(session, "models.manage")) {
        return
      }
      // Turning it on takes effect immediately; turning it off stops every
      // budget in the table below being enforced, which is a thing to be asked
      // about rather than a thing to discover afterwards.
      if (next) {
        setProxyMutate(true)
        return
      }
      setPending({ kind: "proxy-off" })
    },
    [setProxyMutate, session]
  )

  const revokingId = revoke.isPending ? (revoke.variables ?? null) : null
  const failure = revoke.error ?? setProxy.error
  const ready = !isLoading && !isError && proxy !== undefined

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "platform" }, { label: "models" }]}
          title="Models"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{endpoints.length}</span>{" "}
                endpoints · <span className={styles.strong}>{keys.length}</span>{" "}
                Spend keys ·{" "}
                {proxy.enabled ? (
                  <>
                    proxy <span className={styles.strong}>on</span>
                  </>
                ) : (
                  <>
                    proxy <span className={styles.warn}>off</span>, nothing
                    metered
                  </>
                )}
                {nearCap.length > 0 ? (
                  <>
                    {" · "}
                    <span className={styles.warn}>{nearCap.length}</span> near
                    the cap
                  </>
                ) : null}
                {expired.length > 0 ? (
                  <>
                    {" · "}
                    <span className={styles.strong}>{expired.length}</span>{" "}
                    expired
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
          <div className={styles.skeleton} data-test="models-loading">
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
            <p className={styles.stateTitle}>Couldn&apos;t load models</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="models-retry"
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
              data-test="models-proxy"
              title="Proxy"
              note={
                <>
                  A thin optional hop in front of the upstreams. It is what
                  issues a spend key, holds it to a budget, meters a run and
                  pulls a key when its lease ends — so its switch decides
                  whether the three sections below are configuration or
                  enforcement.
                </>
              }
            >
              <ProxyPanel
                proxy={proxy}
                busy={setProxy.isPending}
                onToggle={onToggleProxy}
              />
            </Section>

            <Section
              variant="screen"
              data-test="models-endpoints"
              title="Upstream endpoints"
              note={
                <>
                  Two wires, openai-compatible and anthropic-compatible, and
                  nothing else. Workers take a url and a key through their
                  provider config; the lead and chat take the same endpoints
                  through a chat client. A self-hosted url is an ordinary row.
                </>
              }
            >
              <EndpointsPanel endpoints={endpoints} />
            </Section>

            <Section
              variant="screen"
              data-test="models-keys"
              title="Spend keys"
              note={
                <>
                  A key carries its own route, cap, model list, scope and TTL —
                  so a leaked one buys a single endpoint, the models named on
                  it, what is left of a budget, until a day the holder does not
                  control. The secret is shown once when it is issued and never
                  again.
                  {enforced ? null : (
                    <span className={styles.inlineWarn}>
                      {" "}
                      With the proxy off none of it is being checked.
                    </span>
                  )}
                </>
              }
            >
              <VirtualKeysPanel
                keys={keys}
                endpoints={endpoints}
                enforced={enforced}
                revokingId={revokingId}
                onRevoke={onRevoke}
              />
            </Section>

            <Section
              variant="screen"
              data-test="models-routing"
              title="Role to model"
              note={
                <>
                  The platform speaks in roles, never in vendors: the lead
                  plans, writes contracts, reviews results and repairs a failed
                  run; the worker runs profile steps in a container. This is
                  where a role becomes a physical model, and the only place that
                  mapping exists.
                </>
              }
            >
              <RoleRoutingPanel routes={routes} endpoints={endpoints} />
            </Section>
          </>
        ) : null}
      </div>

      <ConfirmDialog
        open={pending !== null}
        danger
        title={
          pending?.kind === "revoke"
            ? "Revoke this key?"
            : "Turn the proxy off?"
        }
        body={
          pending?.kind === "revoke"
            ? `${pending.entry.prefix} · ${pending.entry.label} — the key stops working immediately and cannot be brought back. A worker holding it loses it with its lease, mid-run.`
            : pending?.kind === "proxy-off"
              ? "Workers get a url and a key injected directly. Spend keys stop being checked, every budget below stops being enforced, and no run is metered until it is turned back on."
              : ""
        }
        confirmLabel={pending?.kind === "revoke" ? "Revoke key" : "Turn it off"}
        cancelLabel={
          pending?.kind === "revoke" ? "Keep the key" : "Leave it on"
        }
        onConfirm={() => {
          if (pending && can(session, "models.manage")) {
            if (pending.kind === "revoke") {
              revoke.mutate(pending.entry.id)
            } else {
              setProxy.mutate(false)
            }
          }
          setPending(null)
        }}
        onCancel={() => setPending(null)}
      />
    </AppShell>
  )
}

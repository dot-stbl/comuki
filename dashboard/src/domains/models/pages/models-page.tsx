import { useCallback, useMemo, useState } from "react"
import { RotateCw } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useRevokeKey,
  useSetProxyEnabled,
} from "@/domains/models/api/mutations"
import { useModelsQuery, useProxyKeysQuery } from "@/domains/models/api/queries"
import { expiredKeys, keysNearCap } from "@/domains/models/model/keys"
import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
import { EndpointsPanel } from "@/domains/models/ui/endpoints-panel"
import { KeyDetailSheet } from "@/domains/models/ui/key-detail-sheet"
import { ProxyPanel } from "@/domains/models/ui/proxy-panel"
import { RoleRoutingPanel } from "@/domains/models/ui/role-routing-panel"
import { VirtualKeysPanel } from "@/domains/models/ui/virtual-keys-panel"
import { env } from "@/shared/config/env"
import { requestFailureMessage } from "@/shared/api/problem"
import { can, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  ScreenState,
  Section,
  Skeleton,
  Tooltip,
} from "@/shared/ui"

import styles from "./models-page.module.css"

const SKELETON_WIDTHS = ["52%", "84%", "66%", "40%", "74%"]

/**
 * The revocation caveat the host's store deserves: config-seeded keys come
 * back with a restart, and a confirm that promised permanence would be the
 * one lie on this screen that cannot be undone.
 */
function revokeRestartNote(t: (key: string) => string): string {
  return env.useMock ? "" : t("keys.revokeRestartNote")
}

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
  const { t } = useTranslation("models")
  const { t: tShell } = useTranslation("shell")
  const { data, isLoading, isError, error, refetch } = useModelsQuery()
  /* Real mode's spend keys come from the proxy's own catalogue rather than
     the seed registry — fingerprints, caps and expiries, no token material.
     Each key's upstream also becomes an endpoint row, so the route column
     names the destination the key actually buys. */
  const proxyKeys = useProxyKeysQuery()
  const session = useSession()

  const [pending, setPending] = useState<Pending>(null)
  /* The drawer's key, by id: the row hands the entry over and the sheet is
     fed from whichever list produced it, so a revoke lands here too without
     a second source of truth. */
  const [openKeyId, setOpenKeyId] = useState<string | null>(null)

  const revoke = useRevokeKey()
  const setProxy = useSetProxyEnabled()

  const endpoints = useMemo<ModelEndpoint[]>(() => {
    const base = data?.endpoints ?? []
    if (env.useMock || !proxyKeys.data) {
      return base
    }
    return [...base, ...proxyKeys.data.endpoints]
  }, [data, proxyKeys.data])
  const keys = useMemo(() => {
    if (env.useMock) {
      return data?.keys ?? []
    }
    return proxyKeys.data?.keys ?? []
  }, [data, proxyKeys.data])
  const openKey = useMemo(
    () => keys.find((key) => key.id === openKeyId) ?? null,
    [keys, openKeyId]
  )
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
  /* The key catalogue is the screen's own half of the wire, and its failure
     is the screen's failure: an empty table here would read as "no keys",
     which is a different (and quieter) lie than the one an error panel
     tells. So the sections stay away until the keys answer too. */
  const keysFailed = !env.useMock && proxyKeys.isError
  const ready =
    !isLoading &&
    !isError &&
    !keysFailed &&
    proxy !== undefined &&
    (env.useMock || !proxyKeys.isLoading)

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: tShell("crumb.platform") },
            { label: tShell("crumb.models") },
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
                  ns="models"
                  i18nKey={
                    proxy.enabled
                      ? "page.summaryProxyOn"
                      : "page.summaryProxyOff"
                  }
                  components={{
                    endpoints: (
                      <span className={styles.strong}>
                        {String(endpoints.length)}
                      </span>
                    ),
                    keys: (
                      <span className={styles.strong}>
                        {String(keys.length)}
                      </span>
                    ),
                    proxy: (
                      <span
                        className={proxy.enabled ? styles.strong : styles.warn}
                      >
                        {proxy.enabled ? t("proxy.on") : t("proxy.off")}
                      </span>
                    ),
                  }}
                />
                {nearCap.length > 0 ? (
                  <>
                    {" · "}
                    <Trans
                      ns="models"
                      i18nKey="page.summaryNearCap"
                      components={{
                        count: (
                          <span className={styles.warn}>
                            {String(nearCap.length)}
                          </span>
                        ),
                      }}
                    />
                  </>
                ) : null}
                {expired.length > 0 ? (
                  <>
                    {" · "}
                    <Trans
                      ns="models"
                      i18nKey="page.summaryExpired"
                      components={{
                        count: (
                          <span className={styles.strong}>
                            {String(expired.length)}
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
          <Skeleton lines={SKELETON_WIDTHS} data-test="models-loading" />
        ) : null}

        {isError || keysFailed ? (
          <ScreenState
            kind="error"
            title={
              isError ? t("page.modelsErrorTitle") : t("page.keysErrorTitle")
            }
            description={requestFailureMessage(
              error ?? proxyKeys.error,
              t("errors.unknown")
            )}
            action={
              <Tooltip content={t("actions.retry")}>
                <Button
                  size="icon-sm"
                  data-test="models-retry"
                  aria-label={t("actions.retry")}
                  onClick={() => {
                    void refetch()
                    void proxyKeys.refetch()
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
            {/* The host's own sentence, not "request failed 501" for an
                operator to translate — the proxy switch is exactly the act
                whose whole value on this side of the wire is the sentence. */}
            {requestFailureMessage(failure, t("errors.changeFailed"))}{" "}
            {t("page.failureTail")}
          </p>
        ) : null}

        {ready ? (
          <>
            <Section
              variant="screen"
              data-test="models-proxy"
              title={t("proxy.section")}
              note={t("proxy.note")}
            >
              <ProxyPanel
                proxy={proxy}
                busy={setProxy.isPending}
                /* The switch is mock-only: the host's key store is seeded
                   from configuration and a PATCH answers 501, so real mode
                   shows the proxy as a fact rather than an act. */
                onToggle={env.useMock ? onToggleProxy : undefined}
              />
            </Section>

            <Section
              variant="screen"
              data-test="models-endpoints"
              title={t("endpoints.section")}
              note={t("endpoints.note")}
            >
              <EndpointsPanel endpoints={endpoints} />
            </Section>

            <Section
              variant="screen"
              data-test="models-keys"
              title={t("keys.section")}
              note={
                <>
                  {t("keys.note")}
                  {enforced ? null : (
                    <span className={styles.inlineWarn}>
                      {t("keys.noteUnenforced")}
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
                onOpen={(entry) => setOpenKeyId(entry.id)}
              />
            </Section>

            <Section
              variant="screen"
              data-test="models-routing"
              title={t("routing.section")}
              note={t("routing.note")}
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
            ? t("keys.revokeConfirmTitle")
            : t("proxy.offConfirmTitle")
        }
        body={
          pending?.kind === "revoke"
            ? t("keys.revokeConfirmBody", {
                prefix: pending.entry.prefix,
                label: pending.entry.label,
              }) + revokeRestartNote(t)
            : pending?.kind === "proxy-off"
              ? t("proxy.offConfirmBody")
              : ""
        }
        confirmLabel={
          pending?.kind === "revoke"
            ? t("keys.revokeConfirm")
            : t("proxy.offConfirm")
        }
        cancelLabel={
          pending?.kind === "revoke"
            ? t("keys.revokeCancel")
            : t("proxy.offCancel")
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

      <KeyDetailSheet
        entry={openKey}
        endpoints={endpoints}
        enforced={enforced}
        revokingId={revokingId}
        onRevoke={onRevoke}
        session={session}
        open={openKey !== null}
        onOpenChange={(next) => {
          if (!next) {
            setOpenKeyId(null)
          }
        }}
      />
    </AppShell>
  )
}

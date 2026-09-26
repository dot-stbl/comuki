import { Ban, Cpu, Users, X } from "lucide-react"
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components"
import { useTranslation } from "react-i18next"

import { formatCost } from "@/domains/runs/model/format"
import {
  createdReading,
  endpointOf,
  expiryReading,
  grantReading,
  isLive,
  keySpendAverage,
  keySpendPeakDay,
  keySpendSilentDays,
  keySpendTotal,
  lastUsedReading,
  scopeReading,
  wireLabel,
} from "@/domains/models/model/keys"
import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
import { can, needsLabel, projectOf, type Session } from "@/shared/session"
import { BarSeries, Button, Fact, FactList, Tooltip } from "@/shared/ui"

import { KeyBudgetMeter } from "./key-budget-meter"
import { KeyStateBadge } from "./model-badges"
import styles from "./key-detail-sheet.module.css"

export interface KeyDetailSheetProps {
  entry: VirtualKey | null
  endpoints: ModelEndpoint[]
  /** Whether the cap is actually being applied — the meter's one caveat. */
  enforced: boolean
  revokingId: string | null
  onRevoke: (entry: VirtualKey) => void
  /** The shift, for the same reason the table's cells take it. */
  session: Session
  open: boolean
  onOpenChange: (open: boolean) => void
}

/**
 * One spend key, in full, without leaving the table.
 *
 * The row is the argument for the product's idea of a key — route, cap,
 * models, scope, TTL, all inside it — and the drawer is the evidence: the
 * spend that made the number, the grants that made it someone's, and the date
 * it stops. Nothing here exists only to fill space; every section is a
 * question the row planted and somebody standing at this screen would ask.
 *
 * Built as the knowledge sheet's sibling rather than added to `shared/ui`:
 * the kit still has no docked sheet, and a domain that needs one reports the
 * gap rather than widening the kit for a second customer that is not a sheet.
 * React Aria owns the behaviour — focus trap, escape, scrim dismissal, focus
 * return — and the module owns the look, docked inline-end at full height so
 * the table stays readable behind it and the operator keeps their row.
 *
 * Real mode's drawer is the catalogue row read deeply rather than a fetched
 * detail: the host has no `GET /proxy/keys/{id}`, so the spend window, the
 * grants and the issued date each say "not on this wire" instead of
 * pretending. That endpoint is the backend follow-up; when it lands it drops
 * in behind `useProxyKeysQuery` without touching this component.
 */
export function KeyDetailSheet({
  entry,
  endpoints,
  enforced,
  revokingId,
  onRevoke,
  session,
  open,
  onOpenChange,
}: KeyDetailSheetProps) {
  const { t } = useTranslation("models")
  const projectKey = (projectId: string) =>
    projectOf(session, projectId)?.key ?? projectId
  // Resolved once: the route line and its hover say the same endpoint, and a
  // partial registry answers it with its own reading rather than a gap.
  const endpoint =
    entry === null ? undefined : endpointOf(endpoints, entry.endpointId)

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      className={styles.scrim}
    >
      <Modal className={styles.sheet}>
        <Dialog className={styles.dialog} data-test="key-detail-sheet">
          {entry ? (
            <>
              <header className={styles.head}>
                <div className={styles.headText}>
                  <Heading slot="title" className={styles.title}>
                    {entry.prefix}
                  </Heading>
                  <p className={styles.summary}>{entry.label}</p>
                </div>
                <Tooltip content={t("sheet.closeTooltip")}>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    data-test="key-detail-close"
                    aria-label={t("sheet.closeAria")}
                    onClick={() => onOpenChange(false)}
                  >
                    <X aria-hidden="true" />
                  </Button>
                </Tooltip>
              </header>

              <div className={styles.marks}>
                <KeyStateBadge entry={entry} t={t} />
                {endpoint ? (
                  <span className={styles.route} title={endpoint.baseUrl}>
                    {endpoint.name} · {wireLabel(endpoint.wire, t)}
                  </span>
                ) : (
                  <span className={styles.route}>
                    {t("sheet.routeNotKnown")}
                  </span>
                )}
              </div>

              {/* The dates a key lives by, on the kit's pair at the sheet's
                  step. This block used to be the knowledge sheet's copied
                  byte for byte — its own comment said so out loud — which is
                  two files that would have taken a correction one at a time. */}
              <FactList layout="split" size="sm">
                <Fact name={t("sheet.issued")}>{createdReading(entry, t)}</Fact>
                <Fact name={t("sheet.lastUsed")}>
                  {lastUsedReading(entry, t)}
                </Fact>
                <Fact name={t("sheet.expires")}>{expiryReading(entry, t)}</Fact>
                <Fact name={t("sheet.scope")}>
                  {scopeReading(entry, projectKey, t)}
                </Fact>
              </FactList>

              <SpendSection entry={entry} enforced={enforced} />

              <section className={styles.section}>
                <h3 className={styles.sectionName}>{t("sheet.grants")}</h3>
                {entry.grants === null ? (
                  <p className={styles.absent}>{t("sheet.notRecorded")}</p>
                ) : entry.grants.length === 0 ? (
                  <p className={styles.absent}>{t("sheet.issuedToNobody")}</p>
                ) : (
                  <ul className={styles.list}>
                    {entry.grants.map((grant) => (
                      <li
                        key={`${grant.role}:${grant.projectId ?? "platform"}`}
                        className={styles.listRow}
                      >
                        <Users className={styles.rowIcon} aria-hidden="true" />
                        <span className={styles.rowValue}>
                          {grantReading(grant, projectKey, t)}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section className={styles.section}>
                <h3 className={styles.sectionName}>{t("sheet.mayReach")}</h3>
                {entry.models.length === 0 ? (
                  // The wire's empty allow-list means *every* model is
                  // permitted — the row says so, and so does the drawer.
                  <p
                    className={styles.absent}
                    title={t("sheet.everyModelTitle")}
                  >
                    {t("sheet.everyModel")}
                  </p>
                ) : (
                  <ul className={styles.list}>
                    {entry.models.map((model) => (
                      <li key={model} className={styles.listRow}>
                        <Cpu className={styles.rowIcon} aria-hidden="true" />
                        <span className={styles.rowValue}>{model}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <footer className={styles.foot}>
                {isLive(entry) ? (
                  <FooterRevoke
                    entry={entry}
                    revokingId={revokingId}
                    onRevoke={onRevoke}
                    session={session}
                  />
                ) : (
                  <p className={styles.stopped}>{t("sheet.alreadyStopped")}</p>
                )}
              </footer>
            </>
          ) : null}
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}

function FooterRevoke({
  entry,
  revokingId,
  onRevoke,
  session,
}: {
  entry: VirtualKey
  revokingId: string | null
  onRevoke: (entry: VirtualKey) => void
  session: Session
}) {
  const busy = revokingId === entry.id
  const denial = can(session, "models.manage")
    ? null
    : needsLabel("models.manage")

  // The row's act, in the drawer's footer: same permission, same confirm,
  // same caveat. A button with its word rather than a glyph — the footer has
  // the room the row never did, and an irreversible act should say itself.
  const { t } = useTranslation("models")
  return (
    <Tooltip content={denial ?? t("sheet.irreversible")}>
      <Button
        variant="destructive"
        data-test="key-detail-revoke"
        loading={busy}
        denied={denial}
        onClick={() => onRevoke(entry)}
      >
        <Ban aria-hidden="true" />
        {t("sheet.revoke")}
      </Button>
    </Tooltip>
  )
}

/**
 * The spend window: the meter the row shows, then the fortnight behind it.
 *
 * The figure states the reading — total, a day, the heaviest day — and the
 * bars confirm it in shape; the chart is `role="img"` with that sentence for
 * its whole accessible name, so a screen reader loses nothing the sighted
 * operator keeps. A window the wire does not carry says so instead of drawing
 * fourteen zeros, and a window that ends in silence says how long it has been
 * silent without claiming to know why.
 */
function SpendSection({
  entry,
  enforced,
}: {
  entry: VirtualKey
  enforced: boolean
}) {
  const { t } = useTranslation("models")
  if (entry.spendDaily === null) {
    return (
      <section className={styles.section}>
        <h3 className={styles.sectionName}>{t("sheet.spend")}</h3>
        <KeyBudgetMeter entry={entry} enforced={enforced} />
        <p className={styles.absent}>{t("sheet.notMetered")}</p>
      </section>
    )
  }

  const days = entry.spendDaily
  const total = keySpendTotal(days)
  const average = keySpendAverage(days)
  const peak = keySpendPeakDay(days)
  const silent = keySpendSilentDays(days)

  if (total === null || average === null || peak === null || total === 0) {
    return (
      <section className={styles.section}>
        <h3 className={styles.sectionName}>{t("sheet.spend")}</h3>
        <KeyBudgetMeter entry={entry} enforced={enforced} />
        <p className={styles.absent}>{t("sheet.noSpend")}</p>
      </section>
    )
  }

  const reading = t("sheet.reading", {
    total: formatCost(total),
    days: days.length,
    average: formatCost(average),
    day: peak.label,
    peak: formatCost(peak.usd),
  })
  const staleness = silent > 0 ? t("sheet.silentDays", { count: silent }) : ""

  return (
    <section className={styles.section}>
      <h3 className={styles.sectionName}>{t("sheet.spend")}</h3>
      <KeyBudgetMeter entry={entry} enforced={enforced} />
      <p className={styles.figure}>{reading + staleness}</p>
      <BarSeries
        className={styles.chart}
        points={days.map((day, index) => ({
          // A fortnight repeats every weekday label, so the stable key is the
          // column's place in the window; the label stays what is drawn.
          key: `d${index}`,
          label: day.label,
          segments: [{ value: day.usd }],
        }))}
        label={t("sheet.chartLabel", { reading: reading + staleness })}
      />
    </section>
  )
}

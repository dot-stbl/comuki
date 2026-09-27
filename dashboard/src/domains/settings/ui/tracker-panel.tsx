import { Plug, RotateCcw } from "lucide-react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import type { TrackerProvider } from "@/domains/settings/model/types"
import type { PermissionCheck } from "@/shared/session"
import {
  Button,
  Notice,
  ScreenState,
  Section,
  StatusBadge,
  Tooltip,
} from "@/shared/ui"

import styles from "./tracker-panel.module.css"

export interface TrackerPanelProps {
  trackers: TrackerProvider[]
  /**
   * May this session turn a live setting. Connecting a tracker and forcing a
   * sync both change what the intake does, so both answer to it — a sync is
   * not a read: it pulls new issues into the backlog.
   */
  edit: PermissionCheck
}

/**
 * Where tickets come from before anybody types one.
 *
 * The notice sits above the grid rather than under it because it is the rule
 * that stops a disconnected tracker from reading as a broken screen: manual
 * intake works either way, and a connected tracker only adds a second door.
 *
 * A tracker is a data surface, not a card: a hairline on its start edge, the
 * lane material it is made of, and the surface step of the corner scale. The
 * connected one marks its own edge rather than growing a ring — a ring is a
 * focus state, and this is a fact about a tracker rather than about the
 * pointer.
 */
export function TrackerPanel({ trackers, edit }: TrackerPanelProps) {
  const { t } = useTranslation("settings")

  return (
    <Section
      variant="screen"
      data-test="settings-tracker"
      title={t("tracker.section")}
    >
      {/* The sentence stays where it was — above the grid, and above the act.
          It is the rule that stops a disconnected tracker from reading as a
          broken screen, and a rule explained afterwards is an apology. */}
      <Notice data-test="tracker-intake">{t("tracker.notice")}</Notice>

      {/* No providers at all. The grid used to draw nothing here and the
          section read as a half-rendered screen — an intake list with no
          trackers in it is an ordinary answer, and it is worth one sentence
          saying so. */}
      {trackers.length === 0 ? (
        <ScreenState
          kind="empty"
          inset="none"
          title={t("tracker.emptyTitle")}
          description={t("tracker.emptyDescription")}
          data-test="tracker-empty"
        />
      ) : (
        <div className={styles.grid}>
          {trackers.map((provider) => (
            <article
              key={provider.id}
              className={styles.tracker}
              data-test="tracker"
              data-connected={provider.connected ? "" : undefined}
            >
              <header className={styles.head}>
                <h3 className={styles.name}>{provider.name}</h3>
                {provider.connected ? (
                  <StatusBadge status="success" size="sm">
                    {t("tracker.connected")}
                  </StatusBadge>
                ) : null}
              </header>

              <p className={styles.meta}>{provider.meta}</p>

              <div className={styles.foot}>
                {provider.connected ? (
                  <>
                    <span className={styles.synced}>
                      {t("tracker.synced", { when: provider.last })}
                    </span>
                    <Tooltip content={edit.denial ?? t("tracker.sync")}>
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="ghost"
                        data-test="tracker-sync"
                        denied={edit.denial}
                        aria-label={t("tracker.syncAria", {
                          name: provider.name,
                        })}
                        /* No mutation behind it yet — the tracker surface
                           has no wire. The word in the description is the
                           same one every other mock act on this screen
                           carries, so nobody reads a green answer as a round
                           trip that happened. */
                        onClick={() =>
                          toast.message(
                            t("tracker.syncedToast", { name: provider.name }),
                            {
                              description: t("tracker.syncedToastNote"),
                            }
                          )
                        }
                      >
                        <RotateCcw aria-hidden="true" />
                      </Button>
                    </Tooltip>
                  </>
                ) : (
                  /* A plug, not a plus. `Plus` is *new* everywhere else in this
                   product — a new ticket, a new key, a new project — and a
                   tracker that is already there is not being created by this
                   control, it is being wired up. Same mark, same act, as the
                   one that connects a source. */
                  <Tooltip content={edit.denial ?? t("tracker.connect")}>
                    <Button
                      type="button"
                      size="icon-sm"
                      variant="secondary"
                      data-test="tracker-connect"
                      denied={edit.denial}
                      aria-label={t("tracker.connectAria", {
                        name: provider.name,
                      })}
                      onClick={() =>
                        toast.message(
                          t("tracker.connectToast", { name: provider.name }),
                          {
                            description: t("tracker.connectToastNote"),
                          }
                        )
                      }
                    >
                      <Plug aria-hidden="true" />
                    </Button>
                  </Tooltip>
                )}
              </div>
            </article>
          ))}
        </div>
      )}
    </Section>
  )
}

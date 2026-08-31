import { Plug, RotateCcw } from "lucide-react"
import { toast } from "sonner"

import type { TrackerProvider } from "@/domains/settings/model/types"
import type { PermissionCheck } from "@/shared/session"
import { Button, Notice, Section, StatusBadge, Tooltip } from "@/shared/ui"

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
  return (
    <Section
      variant="screen"
      data-test="settings-tracker"
      title="Intake sources"
    >
      {/* The sentence stays where it was — above the grid, and above the act.
          It is the rule that stops a disconnected tracker from reading as a
          broken screen, and a rule explained afterwards is an apology. */}
      <Notice data-test="tracker-intake">
        A connected tracker syncs its tickets into the backlog · manual intake
        stays open either way.
      </Notice>

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
                  connected
                </StatusBadge>
              ) : null}
            </header>

            <p className={styles.meta}>{provider.meta}</p>

            <div className={styles.foot}>
              {provider.connected ? (
                <>
                  <span className={styles.synced}>synced {provider.last}</span>
                  <Tooltip content={edit.denial ?? "Sync"}>
                    <Button
                      type="button"
                      size="icon-sm"
                      variant="ghost"
                      data-test="tracker-sync"
                      denied={edit.denial}
                      aria-label={`Sync ${provider.name}`}
                      onClick={() =>
                        toast.message(`Synced ${provider.name}`, {
                          description: "imported new issues",
                        })
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
                <Tooltip content={edit.denial ?? "Connect"}>
                  <Button
                    type="button"
                    size="icon-sm"
                    variant="secondary"
                    data-test="tracker-connect"
                    denied={edit.denial}
                    aria-label={`Connect ${provider.name}`}
                    onClick={() =>
                      toast.message(`Connect ${provider.name}`, {
                        description: "OAuth flow…",
                      })
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
    </Section>
  )
}

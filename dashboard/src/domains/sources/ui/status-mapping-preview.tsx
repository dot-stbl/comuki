import { Fragment } from "react"

import { SOURCE_KIND_LABEL } from "@/domains/sources/model/providers"
import type { SourceKind, StatusMap } from "@/domains/sources/model/types"
import { StatusBadge } from "@/shared/ui"

import styles from "./status-mapping-preview.module.css"

export interface StatusMappingPreviewProps {
  kind: SourceKind
  mapping: StatusMap[]
}

/**
 * What the tracker will say once the swarm has been through the ticket.
 *
 * Read-only, and not because an editor is missing: the mapping belongs to the
 * connector, which speaks one provider's vocabulary — a Jira transition is not
 * a GitHub label — so there is nothing on this screen that could sensibly
 * change it. It is here as a *preview* because turning a watch on is the moment
 * somebody should find out that the swarm is about to start closing their
 * issues.
 *
 * The left column is the product's six real run statuses, rendered by the kit's
 * own badge so the words and the hues are the same ones every other screen
 * uses. The right column is the provider's own words.
 */
export function StatusMappingPreview({
  kind,
  mapping,
}: StatusMappingPreviewProps) {
  return (
    <section className={styles.preview} data-test="status-mapping">
      <h3 className={styles.head}>
        status written back to {SOURCE_KIND_LABEL[kind]}
      </h3>
      {mapping.length === 0 ? (
        <p className={styles.none}>
          native intake is the tracker. A run&apos;s status is the ticket&apos;s
          status, so there is nowhere to write it back to.
        </p>
      ) : (
        // `dt` and `dd` sit directly under the `dl` rather than in the
        // optional wrapping `div`: the two-column grid is declared on the list
        // itself, and a wrapper would collapse both cells into one track.
        <dl className={styles.list}>
          {mapping.map((entry) => (
            <Fragment key={entry.from}>
              <dt className={styles.from}>
                <StatusBadge status={entry.from} size="sm">
                  {entry.from}
                </StatusBadge>
              </dt>
              <dd className={styles.to}>{entry.to}</dd>
            </Fragment>
          ))}
        </dl>
      )}
    </section>
  )
}

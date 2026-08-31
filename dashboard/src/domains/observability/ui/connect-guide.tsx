import { AlertTriangle, ExternalLink, FileCode2 } from "lucide-react"

import type { BoardsRepo, Grafana } from "@/domains/observability/model/types"
import { Tooltip, buttonClass } from "@/shared/ui"

import styles from "./connect-guide.module.css"

export interface ConnectGuideProps {
  /** `null` when no Grafana is configured for this platform at all. */
  grafana: Grafana | null
  boardsRepo: BoardsRepo
  /** True when nothing on this platform has been imported yet. */
  noBoards: boolean
}

/**
 * What to do when there is nothing to click.
 *
 * The half of this section that most pages would leave out, and the half that
 * makes it a section rather than a stub: an operator who opens Observability on
 * a fresh installation finds three boards they cannot reach and no idea whose
 * job it is. Four steps, in order, naming the two coordinates they need — where
 * the metrics are served from, and where the board definitions live.
 *
 * It renders whether or not a Grafana is configured. When one is, this is the
 * reference for adding the board that is still missing; when one is not, it is
 * the whole page.
 */
export function ConnectGuide({ grafana, boardsRepo, noBoards }: ConnectGuideProps) {
  return (
    <div className={styles.guide} data-test="connect-guide">
      {grafana ? (
        <p className={styles.where} data-test="grafana-configured">
          {grafana.baseUrl} · org {grafana.org} · authored against grafana{" "}
          {grafana.version}
        </p>
      ) : (
        <p className={styles.none} data-test="no-grafana">
          <AlertTriangle className={styles.noneIcon} aria-hidden="true" />
          <span>
            No grafana is configured for this platform, so none of the boards
            above can be opened yet. Their definitions still exist — they live in
            our repository, not in a database — so importing them is the only
            step between here and a working board.
          </span>
        </p>
      )}

      {noBoards && grafana ? (
        <p className={styles.none} data-test="no-boards">
          <AlertTriangle className={styles.noneIcon} aria-hidden="true" />
          <span>
            Grafana is configured and none of the boards have been imported into
            it yet. Nothing is broken — the definitions are in our repository
            and the import below is the whole of the remaining work.
          </span>
        </p>
      ) : null}

      <ol className={styles.steps}>
        <li className={styles.step}>
          Point a grafana at the platform&apos;s metrics endpoint. The
          orchestrator serves prometheus metrics on its own port; the boards
          expect that datasource to be named{" "}
          <span className={styles.code}>comuki</span>.
        </li>
        <li className={styles.step}>
          Import the board definitions from{" "}
          <span className={styles.code}>{boardsRepo.path}</span> in{" "}
          <span className={styles.code}>{boardsRepo.repo}</span>. They are
          ordinary dashboard json — they are versioned with the platform on
          purpose, so a board and the metric it reads change in one commit.
        </li>
        <li className={styles.step}>
          Keep the uids the definitions declare. This page links by uid, and a
          board imported under a new one is a board this page cannot find.
        </li>
        <li className={styles.step}>
          Set the grafana base url in the platform&apos;s deployment config. It
          is what turns the entries above into links.
        </li>
      </ol>

      {/* Two glyphs side by side, so they are deliberately not the same one:
          the first goes to a repository of declarations, the second to the
          running Grafana. Two `ExternalLink`s here would be two controls the
          eye cannot tell apart until it has hovered both. */}
      <div className={styles.actions}>
        <Tooltip content="Board definitions">
          <a
            className={buttonClass({ variant: "outline", size: "icon-sm" })}
            href={boardsRepo.url}
            target="_blank"
            rel="noreferrer"
            aria-label="Board definitions"
            data-test="boards-repo-link"
          >
            <FileCode2 aria-hidden="true" />
          </a>
        </Tooltip>
        {grafana ? (
          <Tooltip content="Open grafana">
            <a
              className={buttonClass({ variant: "outline", size: "icon-sm" })}
              href={grafana.baseUrl}
              target="_blank"
              rel="noreferrer"
              aria-label="Open grafana"
              data-test="grafana-link"
            >
              <ExternalLink aria-hidden="true" />
            </a>
          </Tooltip>
        ) : null}
      </div>
    </div>
  )
}

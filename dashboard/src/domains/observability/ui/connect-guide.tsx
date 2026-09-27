import { AlertTriangle, ExternalLink, FileCode2 } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

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
export function ConnectGuide({
  grafana,
  boardsRepo,
  noBoards,
}: ConnectGuideProps) {
  const { t } = useTranslation("observability")

  return (
    <div className={styles.guide} data-test="connect-guide">
      {grafana ? (
        <p className={styles.where} data-test="grafana-configured">
          {t("connect.where", {
            base: grafana.baseUrl,
            org: grafana.org,
            version: grafana.version,
          })}
        </p>
      ) : (
        <p className={styles.none} data-test="no-grafana">
          <AlertTriangle className={styles.noneIcon} aria-hidden="true" />
          <span>{t("connect.noGrafana")}</span>
        </p>
      )}

      {noBoards && grafana ? (
        <p className={styles.none} data-test="no-boards">
          <AlertTriangle className={styles.noneIcon} aria-hidden="true" />
          <span>{t("connect.noBoards")}</span>
        </p>
      ) : null}

      <ol className={styles.steps}>
        <li className={styles.step}>
          <Trans
            ns="observability"
            i18nKey="connect.steps.one"
            components={{
              code: <span className={styles.code} />,
            }}
          />
        </li>
        <li className={styles.step}>
          <Trans
            ns="observability"
            i18nKey="connect.steps.two"
            components={{
              path: <span className={styles.code}>{boardsRepo.path}</span>,
              repo: <span className={styles.code}>{boardsRepo.repo}</span>,
            }}
          />
        </li>
        <li className={styles.step}>{t("connect.steps.three")}</li>
        <li className={styles.step}>{t("connect.steps.four")}</li>
      </ol>

      {/* Two glyphs side by side, so they are deliberately not the same one:
          the first goes to a repository of declarations, the second to the
          running Grafana. Two `ExternalLink`s here would be two controls the
          eye cannot tell apart until it has hovered both. */}
      <div className={styles.actions}>
        <Tooltip content={t("connect.boardsLabel")}>
          <a
            className={buttonClass({ variant: "outline", size: "icon-sm" })}
            href={boardsRepo.url}
            target="_blank"
            rel="noreferrer"
            aria-label={t("connect.boardsLabel")}
            data-test="boards-repo-link"
          >
            <FileCode2 aria-hidden="true" />
          </a>
        </Tooltip>
        {grafana ? (
          <Tooltip content={t("connect.openGrafana")}>
            <a
              className={buttonClass({ variant: "outline", size: "icon-sm" })}
              href={grafana.baseUrl}
              target="_blank"
              rel="noreferrer"
              aria-label={t("connect.openGrafana")}
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

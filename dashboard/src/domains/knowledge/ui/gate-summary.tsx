import { Trans } from "react-i18next"

import { useVerifyQuery } from "@/domains/verify/api/queries"
import { failingCount, neverRanCount } from "@/domains/verify/model/gate"

import styles from "./gate-tab.module.css"

/**
 * The header's one line about the gate, while the gate is the showing section.
 *
 * A component rather than a computed string because the numbers come from the
 * gate's own query: mounting this only when the tab is showing is what keeps a
 * session that cannot see the gate from ever asking for it. Quiet until the
 * query answers — the panels below say everything this line summarizes, so a
 * summary that flickered in late would be noise rather than a reading.
 */
export function GateSummary() {
  const { data } = useVerifyQuery()

  if (!data) {
    return null
  }

  const projects = data.projects
  const commands = data.commands
  const failing = failingCount(commands)
  const never = neverRanCount(commands)
  const gatesOn = projects.filter((project) => project.enabled).length

  return (
    <>
      {/* The figures are values in their own voice and the words are the
          product's; the slots stringify the counts because a Trans slot
          holding a bare falsy `0` renders empty, and zero is a reading. */}
      <Trans
        ns="knowledge"
        i18nKey="gate.summary"
        components={{
          on: <span className={styles.strong}>{String(gatesOn)}</span>,
          total: (
            <span className={styles.strong}>{String(projects.length)}</span>
          ),
          checks: (
            <span className={styles.strong}>{String(commands.length)}</span>
          ),
        }}
      />
      {failing > 0 ? (
        <>
          {" · "}
          <Trans
            ns="knowledge"
            i18nKey="gate.summaryFailing"
            components={{
              failing: <span className={styles.warn}>{String(failing)}</span>,
            }}
          />
        </>
      ) : null}
      {never > 0 ? (
        <>
          {" · "}
          <Trans
            ns="knowledge"
            i18nKey="gate.summaryNeverRan"
            components={{
              never: <span className={styles.strong}>{String(never)}</span>,
            }}
          />
        </>
      ) : null}
    </>
  )
}

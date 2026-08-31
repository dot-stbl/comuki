import { useVerifyQuery } from "@/domains/verify/api/queries"
import {
  failingCount,
  neverRanCount,
} from "@/domains/verify/model/gate"

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
      <span className={styles.strong}>{gatesOn}</span> of{" "}
      <span className={styles.strong}>{projects.length}</span> gates on ·{" "}
      <span className={styles.strong}>{commands.length}</span> checks declared
      {failing > 0 ? (
        <>
          {" · "}
          <span className={styles.warn}>{failing}</span> failing
        </>
      ) : null}
      {never > 0 ? <> · {never} never ran</> : null}
    </>
  )
}

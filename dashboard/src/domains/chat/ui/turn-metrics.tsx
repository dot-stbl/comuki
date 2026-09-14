import type { TurnMetric } from "@/domains/chat/model/dynamics"

import styles from "./chat-message.module.css"

export interface TurnMetricsProps {
  metrics: TurnMetric[]
}

/**
 * What the turn cost, as one quiet line under the answer.
 *
 * Every figure is a reading the turn itself reported — latency, the tool
 * calls it made, the tokens, the dollars — and the line is pipe-separated in
 * the data voice because these are values in a log, not a sentence. What the
 * turn did not report is absent rather than zeroed; when it reported nothing
 * the line does not render at all, because `8.2s | 0 tools | 0 tok` with no
 * measurement behind it is the thread lying with confidence.
 */
export function TurnMetrics({ metrics }: TurnMetricsProps) {
  return (
    <p className={styles.metrics} data-test="chat-metrics">
      {metrics
        .map((metric) => metric.value)
        .map((value, index) => (
          <span key={index} className={styles.metricSegment}>
            {index > 0 ? <span className={styles.metricBar}> | </span> : null}
            {value}
          </span>
        ))}
    </p>
  )
}

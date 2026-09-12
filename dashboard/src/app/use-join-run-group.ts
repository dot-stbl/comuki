import { useEffect } from "react"

import {
  getRunsHubConnection,
  joinRunGroup,
  leaveRunGroup,
  useRunsHubStatus,
} from "@/shared/realtime/runs-hub"

/**
 * Joins the `run:{id}` timeline group for as long as the calling screen is
 * mounted — the run detail page's live timeline. The join rides the
 * provider's connection; when the socket is not live yet (still connecting,
 * or mid-reconnect) the effect re-runs on the status change and joins then.
 *
 * Leaving rides the same connection on unmount; leaving is always allowed
 * server-side, so an unmount during a reconnect is a no-op, not an error.
 * A permission rejection on the join is swallowed inside `joinRunGroup` —
 * the run's REST query remains the truth for a session that cannot stream.
 */
export function useJoinRunGroup(runId: string): void {
  const status = useRunsHubStatus()

  useEffect(() => {
    if (runId.length === 0 || status !== "live") {
      return
    }
    const connection = getRunsHubConnection()
    if (!connection) {
      return
    }
    void joinRunGroup(connection, runId)
    return () => {
      void leaveRunGroup(connection, runId)
    }
  }, [runId, status])
}

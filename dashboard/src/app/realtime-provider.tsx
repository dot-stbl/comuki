import { useEffect, useRef, type ReactNode } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { HubConnectionState } from "@microsoft/signalr"

import { useAuthState } from "@/domains/auth"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import {
  bindRunsHubEvents,
  createRunsHubConnection,
  getRunsHubConnection,
  joinProjectAttentionGroup,
  leaveProjectAttentionGroup,
  setRunsHubConnection,
  updateRunsHubStatus,
  useRunsHubStatus,
} from "@/shared/realtime/runs-hub"
import { env } from "@/shared/config/env"

/**
 * The socket half of "live": starts the runs-hub connection once the
 * session is real, maps every server event onto query invalidation, and
 * stops the connection when the session ends.
 *
 * **When it connects.** `useAuthState` resolves `user` only after the
 * `/api/v1/auth/me` query succeeds in real mode, so the socket never opens
 * for a signed-out visitor. A mid-session 401 clears the query cache
 * (`wireUnauthorizedRedirect`), the `me` query goes with it, `user` becomes
 * `null`, and this provider stops the connection — the socket and the REST
 * session start and end together, because they authenticate with the same
 * cookie.
 *
 * **What arrives.** The hub broadcasts only to groups (`run:{id}` timeline
 * groups and `project:{id}:attention` groups), so hearing anything at all
 * means joining. The provider joins the attention group of every project
 * the session's project list carries, which is exactly the set the
 * operator can see; the run detail screen joins its one run through
 * `useJoinRunGroup` below. After an automatic reconnect the connection id
 * is new and every group membership is gone — the reconnect handler
 * replays the joins the provider still holds.
 *
 * **When it stays quiet.** Mock mode never builds a connection (`demo`
 * badge, no network). A real-mode connection that fails to start or drops
 * sets `polling` and stays silent — the polling fallback refreshes the same
 * queries, so a dead socket is slower data, not an error screen.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { user } = useAuthState()
  const status = useRunsHubStatus()
  const projects = useProjectsQuery()

  const authenticated = user !== null && !env.useMock

  /** Attention groups this connection has joined — replayed after a reconnect. */
  const joinedProjects = useRef<Set<string>>(new Set())

  useEffect(() => {
    if (!authenticated) {
      return
    }

    // A fresh connection per sign-in: a stopped HubConnection cannot be
    // restarted, so sign-out drops the singleton and the next sign-in builds
    // a new one. Handlers are bound exactly once, at creation.
    let connection = getRunsHubConnection()
    if (
      !connection ||
      connection.state === HubConnectionState.Disconnected
    ) {
      const created = createRunsHubConnection()
      if (!created) {
        // Real mode without a pointed backend (`VITE_API_BASE_URL` empty) —
        // the kubb transport refuses the same setup, so REST polling would
        // not work either; nothing to connect to.
        return
      }
      connection = created
      setRunsHubConnection(created)

      bindRunsHubEvents(created, (queryKey) => {
        void queryClient.invalidateQueries({ queryKey })
      })

      created.onreconnecting(() => {
        updateRunsHubStatus("polling")
      })
      created.onreconnected(() => {
        updateRunsHubStatus("live")
        // New connection id — every group membership was lost with the old
        // one. Replay the joins this provider still holds.
        for (const projectId of joinedProjects.current) {
          void joinProjectAttentionGroup(created, projectId)
        }
      })
      created.onclose(() => {
        updateRunsHubStatus("polling")
      })

      updateRunsHubStatus("polling")
      connection
        .start()
        .then(() => {
          // The session may have ended while the handshake was in flight —
          // the cleanup already dropped this connection; a late `live`
          // would be a badge lying about a socket nobody owns.
          if (getRunsHubConnection() === created) {
            updateRunsHubStatus("live")
          }
        })
        .catch(() => {
          // Silent by design: the polling fallback owns refresh until the
          // automatic reconnect brings the socket back.
        })
    }

    return () => {
      joinedProjects.current = new Set()
      const current = getRunsHubConnection()
      if (current) {
        void current.stop()
        setRunsHubConnection(null)
      }
      updateRunsHubStatus("polling")
    }
  }, [authenticated, queryClient])

  // Join/leave attention groups as the visible project set changes. Runs
  // when the connection is actually connected (status `live`), so a join is
  // never invoked into a half-open socket.
  useEffect(() => {
    if (!authenticated || status !== "live") {
      return
    }
    const connection = getRunsHubConnection()
    if (!connection) {
      return
    }

    const visible = new Set((projects.data ?? []).map((row) => row.id))

    for (const projectId of visible) {
      if (!joinedProjects.current.has(projectId)) {
        joinedProjects.current.add(projectId)
        void joinProjectAttentionGroup(connection, projectId)
      }
    }
    for (const projectId of joinedProjects.current) {
      if (!visible.has(projectId)) {
        joinedProjects.current.delete(projectId)
        void leaveProjectAttentionGroup(connection, projectId)
      }
    }
  }, [authenticated, status, projects.data])

  return <>{children}</>
}

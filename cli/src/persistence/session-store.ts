import { isJsonObject } from "../lib/json"
import type {
  PersistedSession,
  PersistedSessions,
  SessionStatus,
} from "../lib/session-state"

export type { PersistedSession, PersistedSessions }

/** Persistence seam for the sessions-on-disk document. */
export interface SessionStore {
  read(): Promise<PersistedSessions>
  write(sessions: PersistedSessions): Promise<void>
}

const SESSION_STATUSES: readonly SessionStatus[] = [
  "idle",
  "thinking",
  "running",
  "done",
]

function isSessionStatus(value: unknown): value is SessionStatus {
  return (
    typeof value === "string" &&
    SESSION_STATUSES.some((status) => status === value)
  )
}

/** Decodes unknown JSON to the stable sessions-on-disk contract. */
export function decodePersistedSessions(value: unknown): PersistedSessions {
  if (!isJsonObject(value) || !Array.isArray(value.sessions)) {
    return { sessions: [] }
  }

  const sessions = value.sessions.flatMap((entry): readonly PersistedSession[] => {
    if (!isJsonObject(entry) || typeof entry.id !== "string") {
      return []
    }
    const history = Array.isArray(entry.history)
      ? entry.history.filter((item): item is string => typeof item === "string")
      : []
    return [
      {
        id: entry.id,
        name: typeof entry.name === "string" ? entry.name : "session",
        status: isSessionStatus(entry.status) ? entry.status : "idle",
        createdAt:
          typeof entry.createdAt === "number" && Number.isFinite(entry.createdAt)
            ? entry.createdAt
            : 0,
        ...(entry.renamed === true ? { renamed: true } : {}),
        ...(history.length > 0 ? { history } : {}),
      },
    ]
  })

  return {
    sessions,
    ...(typeof value.activeSessionId === "string"
      ? { activeSessionId: value.activeSessionId }
      : {}),
  }
}

/** JSON file adapter for the SessionStore seam. */
export class JsonSessionStore implements SessionStore {
  constructor(private readonly path: string) {}

  async read(): Promise<PersistedSessions> {
    const file = Bun.file(this.path)
    if (!(await file.exists())) {
      return { sessions: [] }
    }
    try {
      const parsed: unknown = JSON.parse(await file.text())
      return decodePersistedSessions(parsed)
    } catch {
      return { sessions: [] }
    }
  }

  async write(sessions: PersistedSessions): Promise<void> {
    await Bun.write(this.path, JSON.stringify(sessions, null, 2) + "\n", {
      createPath: true,
      mode: 0o600,
    })
  }
}

/**
 * Compatibility facade for session state and persistence. New pure consumers
 * import `session-state`; existing commands keep their stable imports here.
 */
import { sessionsFilePath } from "./config"
import { toPersisted, type SessionsState } from "./session-state"
import {
  decodePersistedSessions,
  JsonSessionStore,
  type PersistedSessions,
  type SessionStore,
} from "../persistence/session-store"

export * from "./session-state"
export {
  decodePersistedSessions,
  JsonSessionStore,
  type SessionStore,
}

export async function readSessionsFile(
  path: string = sessionsFilePath()
): Promise<PersistedSessions> {
  return new JsonSessionStore(path).read()
}

export async function writeSessionsFile(
  state: SessionsState,
  path: string = sessionsFilePath()
): Promise<void> {
  await new JsonSessionStore(path).write(toPersisted(state))
}

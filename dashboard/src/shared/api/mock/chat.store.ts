import {
  CHAT_SCRIPT,
  CHAT_SESSIONS_SEED,
  CUSTOM_COMMANDS_SEED,
  type SeedChatMessage,
  type SeedChatSession,
  type SeedProposalDecision,
  type SeedSlashCommand,
} from "./chat.seed"
import { approveSeedRun, cancelSeedRun, listSeedRuns } from "./runs.store"

/**
 * Mutable mock store for the chat console.
 *
 * The same reason `runs.store.ts` exists, and the same trap it was written to
 * avoid: a query whose `queryFn` maps a module constant can never show the
 * result of a decision, because the refetch that follows the mutation restores
 * the constant — about 200ms after the optimistic write, which is long enough
 * to look like it worked. Everything the console changes lives here.
 *
 * **A confirmed proposal writes where the screens write.** Confirming a stop in
 * chat calls the same `cancelSeedRun` the duty list calls, so the run is gone
 * from `/runs` as well: chat is another way in, never a second system of
 * record. The proposal itself is also marked decided here, so the thread it
 * sits in reads as history rather than as a question that is still open.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 */

function cloneMessage(message: SeedChatMessage): SeedChatMessage {
  return {
    ...message,
    tool: message.tool ? { ...message.tool } : undefined,
    proposal: message.proposal
      ? {
          ...message.proposal,
          steps: message.proposal.steps?.map((step) => ({ ...step })),
        }
      : undefined,
  }
}

function cloneSession(session: SeedChatSession): SeedChatSession {
  return { ...session, messages: session.messages.map(cloneMessage) }
}

let sessions: SeedChatSession[] = CHAT_SESSIONS_SEED.map(cloneSession)
let counter = 0

function nextId(prefix: string): string {
  counter += 1
  return `${prefix}_${counter.toString(36)}`
}

/** `HH:MM`, the way the seed spells one. Local, because a shift is local. */
function clock(): string {
  const now = new Date()
  const hh = `${now.getHours()}`.padStart(2, "0")
  const mm = `${now.getMinutes()}`.padStart(2, "0")
  return `${hh}:${mm}`
}

export function listChatSessions(): SeedChatSession[] {
  return sessions
}

export function findChatSession(id: string): SeedChatSession | undefined {
  return sessions.find((session) => session.id === id)
}

/** Every custom command the client's git declared. Data, never hardcoded. */
export function listCustomCommands(): SeedSlashCommand[] {
  return CUSTOM_COMMANDS_SEED
}

/** Which project a run belongs to — how an implied scope is resolved. */
export function projectOfRun(runId: string): string | undefined {
  return listSeedRuns().find((run) => run.id === runId)?.projectId
}

/** A fresh conversation, at the top of the list. */
export function startChatSession(): SeedChatSession {
  const session: SeedChatSession = {
    id: nextId("cs"),
    title: "New conversation",
    age: "just now",
    messages: [],
  }
  sessions = [session, ...sessions]
  return session
}

/** The first identifier-shaped word in a message — what `/stop` was aimed at. */
const SUBJECT = /\b([0-9a-f]{8})\b/i

export interface SendResult {
  session: SeedChatSession
  /** Ids of everything appended, so a caller can stream the last one. */
  appended: string[]
}

/**
 * What the assistant says back, scripted.
 *
 * `scopeProjectId` is the composer's scope chip. A proposal that names a run
 * takes that run's project instead — the argument already said where the act
 * lands, and asking a second time only creates something to disagree with.
 */
export function sendChatMessage(
  sessionId: string,
  text: string,
  scopeProjectId?: string
): SendResult | undefined {
  const session = findChatSession(sessionId)
  if (!session) {
    return undefined
  }

  const typed = text.trim()
  if (!typed) {
    return { session, appended: [] }
  }

  const at = clock()
  const person: SeedChatMessage = {
    id: nextId("m"),
    kind: "person",
    text: typed,
    at,
  }

  const lowered = typed.toLowerCase()
  const script =
    CHAT_SCRIPT.find((entry) => entry.when && lowered.startsWith(entry.when)) ??
    CHAT_SCRIPT[CHAT_SCRIPT.length - 1]

  const subject = SUBJECT.exec(typed)?.[1]
  const impliedProject = subject ? projectOfRun(subject) : undefined
  const scopeKey = scopeProjectId ?? impliedProject ?? ""

  const replies = script.reply.map<SeedChatMessage>((template) => {
    const message = cloneMessage({ ...template, id: nextId("m"), at })
    if (message.proposal) {
      message.proposal = {
        ...message.proposal,
        id: nextId("cp"),
        projectId: impliedProject ?? scopeProjectId ?? "",
        subject: subject ?? message.proposal.subject,
        summary: message.proposal.summary.replace(
          "{scope}",
          projectKey(scopeKey)
        ),
      }
    }
    return message
  })

  const appended = [person, ...replies]
  sessions = sessions.map((entry) =>
    entry.id === sessionId
      ? {
          ...entry,
          age: "just now",
          // A conversation is named by the first thing said in it, which is
          // what a person searches the list for later.
          title: entry.messages.length === 0 ? titleFrom(typed) : entry.title,
          messages: [
            // Anything that was mid-flight has landed by the time the next
            // thing is said. Settling on a real event rather than on a timer
            // is what makes the streaming state reproducible: the seed can
            // hold a reply frozen mid-sentence for as long as it is being
            // looked at, and it resolves exactly when something happens.
            ...entry.messages.map((message) =>
              message.streaming ? { ...message, streaming: false } : message
            ),
            ...appended,
          ],
        }
      : entry
  )

  return {
    session: findChatSession(sessionId) as SeedChatSession,
    appended: appended.map((message) => message.id),
  }
}

/** The project's handle, for prose. Empty when the scope was never set. */
function projectKey(projectId: string): string {
  if (!projectId) {
    return "the project you name"
  }
  return projectId.replace(/^p_/, "")
}

const TITLE_MAX = 48

function titleFrom(text: string): string {
  const first = text.split("\n")[0] ?? text
  return first.length > TITLE_MAX ? `${first.slice(0, TITLE_MAX)}…` : first
}

/**
 * A human decided.
 *
 * The confirming half writes twice on purpose: once here, so the thread stops
 * asking, and once against the run store, so the act shows up on the duty
 * screens exactly as it would have if it had been pressed there.
 */
export function decideChatProposal(
  sessionId: string,
  proposalId: string,
  decision: SeedProposalDecision
): SeedChatSession | undefined {
  const session = findChatSession(sessionId)
  if (!session) {
    return undefined
  }

  const held = session.messages.find(
    (message) => message.proposal?.id === proposalId
  )?.proposal
  if (!held || held.decision) {
    return session
  }

  if (decision === "confirmed" && held.subject) {
    if (held.act === "run.stop") {
      cancelSeedRun(held.subject)
    }
    if (held.act === "plan.approve") {
      approveSeedRun(held.subject)
    }
  }

  sessions = sessions.map((entry) =>
    entry.id === sessionId
      ? {
          ...entry,
          messages: entry.messages.map((message) =>
            message.proposal?.id === proposalId
              ? {
                  ...message,
                  proposal: { ...message.proposal, decision },
                }
              : message
          ),
        }
      : entry
  )

  return findChatSession(sessionId)
}

/** Back to the seeded shift — used by tests and stories. */
export function resetChatSessions(): void {
  sessions = CHAT_SESSIONS_SEED.map(cloneSession)
  counter = 0
}

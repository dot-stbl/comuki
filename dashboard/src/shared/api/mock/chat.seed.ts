/**
 * The chat console's seed. Fictional, like every other file in this folder.
 *
 * ## There is no language model here, and there must not be one
 *
 * The assistant's replies below are **scripted**: a small table keyed on what
 * was typed, matched by the store next door, returning messages written by
 * hand. Nothing is generated, nothing is inferred, and no request leaves the
 * browser. The point of the mock is to make every *state* of the console
 * reachable by clicking — a reply mid-flight, a tool that failed, a proposal
 * this shift may not confirm — and a real model would make exactly those
 * states the ones you cannot reproduce on demand.
 *
 * When the Orchestration API lands, `CHAT_SCRIPT` is the piece that dies and
 * the shapes around it stay: a session is a session, a tool call is a tool
 * call, and a proposal is still a thing a human presses.
 *
 * ## What a proposal is
 *
 * The rule the product is built on: **the assistant proposes and a human
 * confirms.** Anything that changes state — starting a run, stopping one,
 * approving a plan, turning a live setting — arrives as a `SeedChatProposal`
 * and sits there until somebody decides. Every proposal names the project it
 * would land in, because the permission that gates it is resolved per project
 * and there is no "current project" in this product to fall back on.
 *
 * The seeded shift is `operator` on the platform, `approver` on `p_comuki`,
 * `viewer` on `p_plexor` and `project-admin` on `p_atlas` — so the `/stop`
 * proposal seeded against a `p_plexor` run is refused, on purpose, and is the
 * one state a chat console usually fakes.
 *
 * ## Language
 *
 * A person's own messages and the assistant's prose are *content*, so they are
 * in the language the shift speaks. Everything the interface says about them —
 * a tool name, a status word, a command — is UI copy and stays English.
 */

/** Where a slash command was declared. */
export type SeedCommandOrigin = "built-in" | "client"

/**
 * A slash command the client declared in its own git.
 *
 * These arrive as *data* — the platform does not know them, the dashboard
 * cannot hardcode them, and they appear in the composer's menu beside the
 * built-in set with whatever description their repository gave them.
 */
export interface SeedSlashCommand {
  /** With the slash, the way it is typed and the way it is displayed. */
  name: string
  description: string
  /** Which project's git declared it — a custom command is not global. */
  projectId: string
}

export const CUSTOM_COMMANDS_SEED: SeedSlashCommand[] = [
  {
    name: "/release",
    description: "cut a release branch and open the changelog draft",
    projectId: "p_comuki",
  },
  {
    name: "/flake",
    description: "re-run the last failed test ten times and report the rate",
    projectId: "p_comuki",
  },
  {
    name: "/rotate",
    description: "start the signing-key rotation runbook",
    projectId: "p_plexor",
  },
  {
    name: "/refund",
    description: "trace a payment through ledger-core and explain its state",
    projectId: "p_atlas",
  },
]

/** The act a proposal performs, and therefore the permission it answers to. */
export type SeedProposalAct =
  | "run.start"
  | "run.stop"
  | "plan.approve"
  | "settings.debug"

export type SeedProposalDecision = "confirmed" | "rejected"

/** One node of a proposed plan — the profile that runs it and what it does. */
export interface SeedProposalStep {
  profile: string
  label: string
}

export interface SeedChatProposal {
  id: string
  act: SeedProposalAct
  /** One line: what pressing the confirming half would do. */
  summary: string
  /**
   * Where it lands. Never optional: a permission in this product is answered
   * per project, and a proposal that could not name one could not be checked.
   */
  projectId: string
  /** The identifier the act names, when it names one — a run, a plan. */
  subject?: string
  /** The plan, when the proposal is a plan. */
  steps?: SeedProposalStep[]
  /** Decided already, and how. Absent while it is still a question. */
  decision?: SeedProposalDecision
}

export type SeedToolStatus = "running" | "success" | "failed"

/** One call the assistant made against the Orchestration API. */
export interface SeedToolCall {
  /** The endpoint, in the product's own spelling. A value, not a sentence. */
  name: string
  /** The arguments, already rendered. */
  args: string
  status: SeedToolStatus
  /** What came back, or what went wrong. */
  result?: string
}

export type SeedChatMessageKind =
  | "person"
  | "reply"
  | "tool"
  | "proposal"
  | "error"

export interface SeedChatMessage {
  id: string
  kind: SeedChatMessageKind
  /** A person's words, or the assistant's prose. Content, so not English-only. */
  text?: string
  /** True while tokens are still arriving. Only ever on a `reply`. */
  streaming?: boolean
  tool?: SeedToolCall
  proposal?: SeedChatProposal
  /**
   * The question the assistant hands off rather than answering with a table.
   *
   * A *query*, not a destination. The console does not draw its own runs list:
   * asked to find something, it offers a filter on the real screen, and the
   * destinations come from the same resolver the command palette uses. Storing
   * an href here would be a second copy of that mapping, and the day `/runs`
   * renames its search parameter one of the two copies would be wrong.
   */
  handoff?: string
  /** Run-relative clock, `HH:MM`, the way every other seed spells one. */
  at: string
}

export interface SeedChatSession {
  id: string
  /** What the conversation turned out to be about, in the shift's own words. */
  title: string
  /** How long ago it was last spoken to. */
  age: string
  messages: SeedChatMessage[]
}

/* --------------------------------------------------------------------------
 * The sessions.
 *
 * Five, chosen so that every state worth designing for is reachable without
 * typing anything: a reply mid-flight, a tool that worked and one that did
 * not, a plan awaiting a decision, a proposal this shift may not confirm, an
 * error, and a session with nothing in it at all.
 * ----------------------------------------------------------------------- */

export const CHAT_SESSIONS_SEED: SeedChatSession[] = [
  {
    id: "cs_webhooks",
    title: "Идемпотентность вебхуков Stripe",
    age: "4 min",
    messages: [
      {
        id: "m_wh_1",
        kind: "person",
        text: "что там с прогоном по вебхукам? почему он до сих пор висит",
        at: "09:12",
      },
      {
        id: "m_wh_2",
        kind: "tool",
        at: "09:12",
        tool: {
          name: "runs.get",
          args: "run=8f3c2a91",
          status: "success",
          result:
            "status=running · current=w4 · profile=implementer · 4m12s · $0.42",
        },
      },
      {
        id: "m_wh_3",
        kind: "tool",
        at: "09:12",
        tool: {
          name: "queue.workers",
          args: "image=sha256:9a41c0",
          status: "failed",
          result: "504 from the compute provider after 30s — pool unreachable",
        },
      },
      {
        id: "m_wh_4",
        kind: "reply",
        at: "09:13",
        text: "Прогон 8f3c2a91 живой: воркер стоит на шаге w4, переписывает обработчик под ключ идемпотентности. Пул воркеров я опросить не смог — провайдер не ответил за 30 секунд, так что сказать, сколько контейнеров сейчас на этом образе, нечем. Соседний прогон 5b1d7e40 ждёт человека на аппруве раскатки.",
        handoff: "webhook",
      },
      {
        id: "m_wh_5",
        kind: "person",
        text: "давай тогда разберёмся с 5b1d7e40",
        at: "09:14",
      },
      {
        id: "m_wh_6",
        kind: "proposal",
        at: "09:14",
        proposal: {
          id: "cp_plan_5b1d",
          act: "plan.approve",
          summary:
            "approve the deploy gate on 5b1d7e40 and release the run back to the swarm",
          projectId: "p_comuki",
          subject: "5b1d7e40",
          steps: [
            { profile: "verifier", label: "дождаться аппрува на раскатку" },
            { profile: "docs", label: "записать решение в базу знаний" },
          ],
        },
      },
    ],
  },

  {
    id: "cs_rotation",
    title: "Ротация ключей в auth-svc",
    age: "21 min",
    messages: [
      {
        id: "m_rot_1",
        kind: "person",
        text: "2a6f1c33 доехал до гейта отката. апрувни его, дальше пусть докатывает сам",
        at: "08:51",
      },
      {
        id: "m_rot_2",
        kind: "tool",
        at: "08:51",
        tool: {
          name: "runs.get",
          args: "run=2a6f1c33",
          status: "success",
          result:
            "status=escalated · current=w6 · profile=implementer · project=plexor",
        },
      },
      {
        id: "m_rot_3",
        kind: "proposal",
        at: "08:51",
        // The refusal, seeded on purpose, and seeded against the one act the
        // shift's platform role does *not* carry. `operator` grants
        // `runs.stop` everywhere, so a stop would have been allowed here and
        // proved nothing; approving a plan is a project judgement the seeded
        // shift holds on `p_comuki` and `p_atlas` and not on `p_plexor`. So
        // the confirming half renders present, refused and explained, and
        // pressing it does nothing. A chat tool is not an RBAC bypass.
        proposal: {
          id: "cp_plan_2a6f",
          act: "plan.approve",
          summary:
            "approve the rollback gate on 2a6f1c33 and release the run back to the swarm",
          projectId: "p_plexor",
          subject: "2a6f1c33",
          steps: [
            { profile: "verifier", label: "подтвердить, что откат готов" },
            { profile: "docs", label: "описать новый формат ключей" },
          ],
        },
      },
    ],
  },

  {
    id: "cs_theme",
    title: "Разбор падения на theme API",
    age: "1 h",
    messages: [
      {
        id: "m_th_1",
        kind: "person",
        text: "9d72b5f0 упал на третьем шаге. что произошло",
        at: "08:04",
      },
      {
        id: "m_th_2",
        kind: "tool",
        at: "08:04",
        tool: {
          name: "runs.trace",
          args: "run=9d72b5f0 item=w3",
          status: "success",
          result: "exit=1 · 214 lines · last: cannot find module 'theme/v1'",
        },
      },
      {
        id: "m_th_3",
        kind: "reply",
        at: "08:05",
        // Mid-flight. The composition renders it outside the log region and
        // announces only that a reply is arriving — see `chat-thread.tsx`.
        streaming: true,
        text: "Шаг w3 умер сразу после установки зависимостей: воркер импортирует theme/v1, а в новом пакете этого пути больше нет. Судя по трассе, план писался ещё",
      },
    ],
  },

  {
    id: "cs_cost",
    title: "Расход за ночную смену",
    age: "3 h",
    messages: [
      {
        id: "m_co_1",
        kind: "person",
        text: "сколько мы сожгли за ночь по atlas",
        at: "06:40",
      },
      {
        id: "m_co_2",
        kind: "tool",
        at: "06:40",
        tool: {
          name: "cost.window",
          args: "project=p_atlas window=12h",
          status: "failed",
          result: "the cost roll-up has not been built for this window yet",
        },
      },
      {
        id: "m_co_3",
        kind: "error",
        at: "06:40",
        text: "The lead model gave up on this turn: the cost roll-up it needs has not been built for the window you asked about, and it has no second way to answer.",
      },
    ],
  },

  {
    // The empty first session. Every console has one and almost none of them
    // are designed — so it is seeded rather than left to chance.
    id: "cs_new",
    title: "New conversation",
    age: "just now",
    messages: [],
  },
]

/* --------------------------------------------------------------------------
 * The script.
 *
 * A table, matched top to bottom against what was typed, lowercased. First
 * match wins; the last entry matches everything, which is what makes the
 * console answer at all rather than sometimes.
 *
 * A reply may carry a `{scope}` placeholder, which the store replaces with the
 * project the composer was scoped to. Nothing else is templated: the moment a
 * scripted reply starts interpolating the operator's words it is pretending to
 * understand them.
 * ----------------------------------------------------------------------- */

/** A scripted turn: what it answers to, and the messages it answers with. */
export interface SeedChatScript {
  /** Matched as a prefix of the typed text, lowercased. `""` matches all. */
  when: string
  /** The messages the assistant appends, in order. Ids are stamped by the store. */
  reply: Omit<SeedChatMessage, "id" | "at">[]
}

export const CHAT_SCRIPT: SeedChatScript[] = [
  {
    when: "/help",
    reply: [
      {
        kind: "reply",
        text: "The console drives the same control plane the screens do. Type a slash to see every command this shift can reach — the built-in set plus whatever this client declared in its own git. Anything that changes state comes back as a proposal you press; nothing here acts on its own.",
      },
    ],
  },
  {
    when: "/status",
    reply: [
      {
        kind: "tool",
        tool: {
          name: "runs.list",
          args: "status=running,waiting",
          status: "success",
          result: "6 runs · 2 running · 1 waiting on a human · 1 escalated",
        },
      },
      {
        kind: "reply",
        text: "Шесть прогонов в работе. Один ждёт человека — 5b1d7e40, аппрув на раскатку. Один эскалирован — 2a6f1c33.",
        handoff: "waiting",
      },
    ],
  },
  {
    when: "/run",
    reply: [
      {
        kind: "proposal",
        proposal: {
          id: "cp_start",
          act: "run.start",
          summary: "start a run on {scope} from the ticket you named",
          projectId: "",
          steps: [
            { profile: "explorer", label: "прочитать тикет и затронутый код" },
            { profile: "planner", label: "разложить работу на шаги" },
            { profile: "implementer", label: "внести правку" },
            { profile: "reviewer", label: "вычитать диф" },
          ],
        },
      },
    ],
  },
  {
    when: "/stop",
    reply: [
      {
        kind: "proposal",
        proposal: {
          id: "cp_stop",
          act: "run.stop",
          summary: "stop the run you named and tear down its container",
          projectId: "",
        },
      },
    ],
  },
  {
    when: "/plan",
    reply: [
      {
        kind: "reply",
        text: "План этого прогона — граф work items, а не текст. Открой карточку прогона: там он нарисован, и там же стоит аппрув.",
        handoff: "plan",
      },
    ],
  },
  {
    when: "/debug",
    reply: [
      {
        kind: "proposal",
        proposal: {
          id: "cp_debug",
          act: "settings.debug",
          summary: "turn verbose worker logging on for {scope} for one hour",
          projectId: "",
        },
      },
    ],
  },
  {
    when: "/project",
    reply: [
      {
        kind: "reply",
        text: "A project is a filter in this product, not a mode the console is in — so there is nothing to switch. Set the scope beside the composer when a command needs one; the list only offers the projects you may act in.",
      },
    ],
  },
  {
    when: "",
    reply: [
      {
        kind: "reply",
        text: "Отвечу тем, что могу проверить: список я не рисую здесь, а отправляю на экран, который его умеет.",
      },
    ],
  },
]

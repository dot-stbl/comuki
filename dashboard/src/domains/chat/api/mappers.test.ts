import { describe, expect, it } from "vitest"

import {
  chatMessagesPageToDomainMessages,
  chatMessageViewToDomainMessage,
  chatSlashCommandsToDomainCommands,
  chatSlashCommandToDomainCommand,
  toChatMessage,
} from "@/domains/chat/api/mappers"
import {
  availableCommands,
  commandMenuQuery,
  commandOf,
  matchCommands,
} from "@/domains/chat/model/commands"
import type { ChatMessageView } from "@/shared/api/_generated/types/ChatMessageView"
import type { ChatSlashCommand } from "@/shared/api/_generated/types/ChatSlashCommand"
import type { MessagePart as WireMessagePart } from "@/shared/api/_generated/types/MessagePart"
import type { Session } from "@/shared/session"

/**
 * Wire → domain mappers for the chat console's real mode.
 *
 * The console renders one shape (`ChatMessage`) and the host sends another
 * (`ChatMessageView`), and for a while it sent it into nothing at all: the
 * session mapper returned `messages: []` and no query ever asked the
 * transcript endpoint for the rest. These assertions pin the seam that
 * replaced that.
 *
 * What is pinned, and why each one is a defect if it moves:
 *
 *  - the four wire roles land on the domain's kinds, `system` included —
 *    a system row is the memory digest journaled for audit, and it is prose,
 *    so it reads as a reply rather than announcing itself as an alert;
 *  - an unrecognised role still renders its text, because a host newer than
 *    this bundle must degrade the row and not punch a hole in the thread;
 *  - a tool row becomes a tool *record* (name from `toolName`, result from
 *    `content`) rather than a paragraph, which is what `ui/tool-call.tsx`
 *    needs to show the call instead of summarising it;
 *  - `at` stays the domain's pre-formatted local `HH:MM`, spelled exactly the
 *    way `shared/api/mock/chat.store.ts` spells it, so mock and real threads
 *    read identically until that contract is widened deliberately;
 *  - a page carries its `items` through oldest-first, which is the order the
 *    log renders in.
 */

function messageViewFixture(
  overrides: Partial<ChatMessageView> = {}
): ChatMessageView {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    role: "assistant",
    content: "the swarm is idle",
    createdAt: "2026-09-04T10:07:00.000Z",
    ...overrides,
  }
}

/** The wire stamp above, in the local zone the domain's `at` is written in. */
function localClock(createdAt: string): string {
  const at = new Date(createdAt)
  const hh = `${at.getHours()}`.padStart(2, "0")
  const mm = `${at.getMinutes()}`.padStart(2, "0")
  return `${hh}:${mm}`
}

describe("chatMessageViewToDomainMessage", () => {
  it("maps every role of the host's closed set onto a domain kind", () => {
    expect(
      chatMessageViewToDomainMessage(messageViewFixture({ role: "user" })).kind
    ).toBe("person")
    expect(
      chatMessageViewToDomainMessage(messageViewFixture({ role: "assistant" }))
        .kind
    ).toBe("reply")
    expect(
      chatMessageViewToDomainMessage(
        messageViewFixture({ role: "tool", toolName: "runs.get" })
      ).kind
    ).toBe("tool")
    // A digest journaled for audit is prose, not a failure and not a call.
    expect(
      chatMessageViewToDomainMessage(messageViewFixture({ role: "system" }))
        .kind
    ).toBe("reply")
  })

  it("renders an unrecognised role rather than dropping the row", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({ role: "oracle", content: "something new" })
    )

    expect(message.kind).toBe("reply")
    expect(message.text).toBe("something new")
  })

  it("turns a tool row into a record, with the result off the content", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "tool",
        toolName: "runs.get",
        content: "status=running · current=w4",
      })
    )

    expect(message.tool).toEqual({
      name: "runs.get",
      args: "",
      status: "success",
      result: "status=running · current=w4",
    })
    // The card reads the result off the record; a second copy on `text`
    // would render the same line twice.
    expect(message.text).toBeUndefined()
  })

  it("carries the prose on `text` and leaves the tool record off", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({ role: "user", content: "stop 2a6f1c33" })
    )

    expect(message.text).toBe("stop 2a6f1c33")
    expect(message.tool).toBeUndefined()
    // The wire has no proposal, no hand-off and no mid-flight reply; none of
    // the three is invented here.
    expect(message.proposal).toBeUndefined()
    expect(message.handoff).toBeUndefined()
    expect(message.streaming).toBeUndefined()
  })

  it("stamps `at` as the local HH:MM the domain already holds", () => {
    const createdAt = "2026-09-04T10:07:00.000Z"
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({ createdAt })
    )

    expect(message.at).toBe(localClock(createdAt))
    expect(message.at).toMatch(/^\d{2}:\d{2}$/)
  })

  it("reads an unparseable timestamp as no stamp at all", () => {
    expect(
      chatMessageViewToDomainMessage(messageViewFixture({ createdAt: "soon" }))
        .at
    ).toBe("")
  })
})

/**
 * The slash catalog's `source`, as the host actually spells it.
 *
 * `ChatSlashSources` on the host names `builtin` and `control-plane`. The
 * mapper was keyed `built_in` / `client`, which nothing has ever sent, so
 * every real-mode command arrived with `origin: undefined` — and because the
 * same two literals also decided the scope and the project, every command
 * carried its own source label as a `projectId` and `availableCommands`
 * filtered the whole menu away. These cases pin the vocabulary, the
 * fallback, and the menu that depends on both.
 */

function slashCommandFixture(
  overrides: Partial<ChatSlashCommand> = {}
): ChatSlashCommand {
  return {
    key: "help",
    name: "/help",
    description: "what the console can do",
    body: "List the available slash commands.",
    source: "builtin",
    ...overrides,
  }
}

function sessionFixture(): Session {
  return {
    user: {
      id: "u_test",
      name: "Test",
      email: "test@comuki.local",
      platformRoles: ["operator"],
      projectRoles: {},
    },
    projects: [{ id: "p_one", key: "one", name: "One" }],
  }
}

describe("chatSlashCommandToDomainCommand", () => {
  it("reads the host's `builtin` as a platform command", () => {
    const command = chatSlashCommandToDomainCommand(
      slashCommandFixture({ source: "builtin" })
    )

    expect(command.origin).toBe("built-in")
    expect(command.scope).toBe("none")
  })

  it("reads the host's `control-plane` as a declared command", () => {
    const command = chatSlashCommandToDomainCommand(
      slashCommandFixture({
        key: "restart",
        name: "Restart",
        source: "control-plane",
      })
    )

    expect(command.origin).toBe("client")
    expect(command.scope).toBe("implied")
  })

  it("builds the typed name out of the wire's key, not its label", () => {
    // The wire's `name` is the human label ("Restart"); the domain's is what
    // the operator types. Copying one to the other produced menu rows no
    // slash query could ever match.
    const command = chatSlashCommandToDomainCommand(
      slashCommandFixture({ key: "restart", name: "Restart" })
    )

    expect(command.name).toBe("/restart")
  })

  it("normalises a key a pack author hand-wrote", () => {
    const named = (key: string) =>
      chatSlashCommandToDomainCommand(slashCommandFixture({ key })).name

    // A leading slash the author already wrote is stripped, not doubled:
    // `//restart` matches no query and `commandOf` never resolves it.
    expect(named("/restart")).toBe("/restart")
    expect(named("//restart")).toBe("/restart")
    // The menu matches a lower-cased query, so a capitalised key would be a
    // command nobody could type.
    expect(named("  Restart  ")).toBe("/restart")
  })

  it("keeps the author's description, and falls back to the label without one", () => {
    expect(
      chatSlashCommandToDomainCommand(
        slashCommandFixture({ name: "Restart", description: "restart the run" })
      ).description
    ).toBe("restart the run")

    // Never both — a row reading "Restart — Restart" says one thing twice.
    expect(
      chatSlashCommandToDomainCommand(
        slashCommandFixture({ name: "Restart", description: "   " })
      ).description
    ).toBe("Restart")
  })

  it("never hands the menu an undefined origin", () => {
    // The exact regression: `built_in` is the spelling the mapper used to
    // key on, and the host has never sent it. An unrecognised source has to
    // land on a defined value, and on the one this bundle can vouch for.
    for (const source of ["built_in", "client", "", "something-new"]) {
      const command = chatSlashCommandToDomainCommand(
        slashCommandFixture({ source })
      )
      expect(command.origin).toBeDefined()
      expect(["built-in", "client"]).toContain(command.origin)
    }

    expect(
      chatSlashCommandToDomainCommand(
        slashCommandFixture({ source: "built_in" })
      ).origin
    ).toBe("client")
  })

  it("names no project, because neither wire source is one", () => {
    // `projectId` is what `availableCommands` filters on. Holding the source
    // label there took every wire command out of the menu.
    expect(
      chatSlashCommandToDomainCommand(
        slashCommandFixture({ source: "builtin" })
      ).projectId
    ).toBeUndefined()
    expect(
      chatSlashCommandToDomainCommand(
        slashCommandFixture({ source: "control-plane" })
      ).projectId
    ).toBeUndefined()
  })

  it("keeps every wire command in the composer's menu", () => {
    const wire = chatSlashCommandsToDomainCommands([
      slashCommandFixture({ key: "audit", name: "Audit", source: "builtin" }),
      slashCommandFixture({
        key: "restart",
        name: "Restart",
        source: "control-plane",
      }),
    ])

    const offered = availableCommands(sessionFixture(), wire)

    // Before the fix both carried their own source label as a `projectId`,
    // no session has ever seen a project called `builtin`, and the whole
    // wire half of the menu was filtered away.
    expect(offered.some((entry) => entry.name === "/audit")).toBe(true)
    expect(offered.some((entry) => entry.name === "/restart")).toBe(true)
  })

  it("lands a wire command in the menu the way it is typed", () => {
    // The whole path, end to end: a control-plane pack row becomes a menu
    // entry that the composer's query matches and `commandOf` resolves.
    const wire = chatSlashCommandsToDomainCommands([
      slashCommandFixture({
        key: "restart",
        name: "Restart",
        description: "restart the run",
        source: "control-plane",
      }),
    ])
    const offered = availableCommands(sessionFixture(), wire)

    const query = commandMenuQuery("/rest")
    expect(query).toBe("/rest")
    expect(
      matchCommands(query as string, offered).map((entry) => entry.name)
    ).toEqual(["/restart"])

    expect(commandOf("/restart now", offered)).toMatchObject({
      name: "/restart",
      description: "restart the run",
      origin: "client",
    })
  })
})

describe("chatMessagesPageToDomainMessages", () => {
  it("keeps the page's oldest-first order", () => {
    const messages = chatMessagesPageToDomainMessages({
      items: [
        messageViewFixture({ id: "m1", role: "user", content: "first" }),
        messageViewFixture({ id: "m2", role: "assistant", content: "second" }),
      ],
      page: 1,
      pageSize: 50,
      total: 2,
    })

    expect(messages.map((message) => message.id)).toEqual(["m1", "m2"])
    expect(messages.map((message) => message.kind)).toEqual(["person", "reply"])
  })

  it("reads an empty page as an empty thread", () => {
    expect(
      chatMessagesPageToDomainMessages({
        items: [],
        page: 1,
        pageSize: 50,
        total: 0,
      })
    ).toEqual([])
  })
})

/* ------------------------------------------------------------------ *
 * Message parts.
 *
 * Two seams onto the same domain union: the seed's, and the wire's now that
 * `ChatMessageView.parts` ships. Both are pinned here because the console
 * renders one shape and a drift on either side is invisible until a thread
 * comes back wrong.
 *
 * Three claims the wire side makes and that these cases are here to keep
 * true, because each one was a decision rather than a transcription:
 *
 *  - a row with no parts stays *absent*, never `[]` — an empty list means
 *    "this turn had no body", which is the wrong thing to say about a row
 *    whose prose is sitting in `content`;
 *  - a `kind` this bundle has never heard of degrades the part and not the
 *    thread, the way `normalizeTicketStatus` degrades a ticket row;
 *  - the two sides spell a plan node and an int64 differently, so both go
 *    through a conversion rather than a copy.
 * ------------------------------------------------------------------ */

describe("toChatMessage carries the part list", () => {
  it("maps every kind in the frozen list without losing a field", () => {
    const message = toChatMessage({
      id: "m1",
      kind: "reply",
      at: "09:21",
      parts: [
        { kind: "text", markdown: "**bold**" },
        {
          kind: "code",
          language: "ts",
          source: "const a = 1",
          path: "src/a.ts",
          startLine: 12,
        },
        { kind: "diagram", dialect: "mermaid", source: "flowchart LR" },
        { kind: "thinking", text: "weighing", tokens: 42 },
        {
          kind: "tool",
          name: "runs.get",
          inputJson: "{}",
          status: "success",
          outputJson: "ok",
          durationMs: 412,
        },
        { kind: "handoff", query: "waiting" },
        {
          kind: "plan",
          nodes: [{ id: "w1", label: "read", profile: "explorer" }],
          edges: [{ from: "w1", to: "w2" }],
        },
      ],
    })

    expect(message.parts?.map((part) => part.kind)).toEqual([
      "text",
      "code",
      "diagram",
      "thinking",
      "tool",
      "handoff",
      "plan",
    ])
    expect(message.parts?.[1]).toEqual({
      kind: "code",
      language: "ts",
      source: "const a = 1",
      path: "src/a.ts",
      startLine: 12,
    })
    expect(message.parts?.[4]).toEqual({
      kind: "tool",
      name: "runs.get",
      inputJson: "{}",
      status: "success",
      outputJson: "ok",
      durationMs: 412,
    })
  })

  it("copies a plan rather than sharing the seed's own arrays", () => {
    const nodes = [{ id: "w1", label: "read" }]
    const message = toChatMessage({
      id: "m1",
      kind: "reply",
      at: "09:21",
      parts: [{ kind: "plan", nodes, edges: [] }],
    })

    const mapped = message.parts?.[0]
    expect(mapped?.kind).toBe("plan")
    if (mapped?.kind === "plan") {
      expect(mapped.nodes).not.toBe(nodes)
      expect(mapped.nodes[0]).not.toBe(nodes[0])
    }
  })

  it("leaves a message with no parts flat, for the derivation to read", () => {
    const message = toChatMessage({
      id: "m1",
      kind: "reply",
      at: "09:21",
      text: "plain",
    })
    expect(message.parts).toBeUndefined()
    expect(message.text).toBe("plain")
  })
})

describe("the wire's part list reaches the domain", () => {
  it("maps every kind in the frozen list without losing a field", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        content: "flattened prose",
        parts: [
          { kind: "text", markdown: "**bold**" },
          {
            kind: "code",
            language: "ts",
            source: "const a = 1",
            path: "src/a.ts",
            startLine: 12,
          },
          { kind: "diagram", dialect: "mermaid", source: "flowchart LR" },
          { kind: "thinking", text: "weighing", tokens: 42 },
          {
            kind: "tool",
            name: "runs.get",
            inputJson: "{}",
            status: "success",
            outputJson: "ok",
            durationMs: 412,
          },
          { kind: "handoff", query: "waiting" },
          {
            kind: "plan",
            nodes: [
              {
                id: "w1",
                title: "read the repository",
                profileKey: "explorer",
                brief: "map the modules",
              },
            ],
            edges: [{ from: "w1", to: "w2" }],
          },
        ],
      })
    )

    expect(message.parts?.map((part) => part.kind)).toEqual([
      "text",
      "code",
      "diagram",
      "thinking",
      "tool",
      "handoff",
      "plan",
    ])
    expect(message.parts?.[1]).toEqual({
      kind: "code",
      language: "ts",
      source: "const a = 1",
      path: "src/a.ts",
      startLine: 12,
    })
    expect(message.parts?.[4]).toEqual({
      kind: "tool",
      name: "runs.get",
      inputJson: "{}",
      status: "success",
      outputJson: "ok",
      durationMs: 412,
    })
    // The two sides spell a plan node differently — `title`/`profileKey` on
    // the wire's canonical `Plan` shape, `label`/`profile` in the domain —
    // and `brief` has no home here. Copying the wire's spelling through
    // would render a step with no label at all.
    expect(message.parts?.[6]).toEqual({
      kind: "plan",
      nodes: [{ id: "w1", label: "read the repository", profile: "explorer" }],
      edges: [{ from: "w1", to: "w2" }],
    })
  })

  it("reads an int64 the wire quoted as a number", () => {
    // kubb types every int32/int64 as `number | string` because JSON may
    // carry a 64-bit value quoted rather than lose precision. The renderer
    // does arithmetic on these, so a string that reaches it is a defect.
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        parts: [
          {
            kind: "tool",
            name: "runs.get",
            inputJson: "{}",
            status: "running",
            durationMs: "9007199254",
          },
          { kind: "thinking", text: "weighing", tokens: "1200" },
        ],
      })
    )

    expect(message.parts?.[0]).toMatchObject({ durationMs: 9007199254 })
    expect(message.parts?.[1]).toMatchObject({ tokens: 1200 })
  })

  it("reads a tool status it does not know as a finished call", () => {
    // `status` is a bare `string` on the wire. A journaled part is terminal,
    // so the failure modes are a spinner that never stops and an alarm about
    // a call that worked; `success` is the same reading the flat tool path
    // already takes.
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        parts: [
          {
            kind: "tool",
            name: "runs.get",
            inputJson: "{}",
            status: "cancelled",
          },
        ],
      })
    )

    expect(message.parts?.[0]).toMatchObject({ status: "success" })
  })
})

describe("a row with no parts stays flat", () => {
  it("leaves `parts` absent when the wire sends none", () => {
    // The contract this pins: absent, never an empty array — an empty list
    // means "this turn had no body", and a row that carries text plainly
    // did. Rows written before parts existed arrive exactly like this, and
    // `model/parts.ts` derives their body from the flat fields.
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({ role: "assistant", content: "**markdown**" })
    )

    expect(message.parts).toBeUndefined()
    expect(message.text).toBe("**markdown**")
  })

  it("reads an explicit null the same way", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        content: "**markdown**",
        parts: null,
      })
    )

    expect(message.parts).toBeUndefined()
    expect(message.text).toBe("**markdown**")
  })

  it("collapses an empty wire array to absent rather than carrying it", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({ role: "assistant", content: "prose", parts: [] })
    )

    expect(message.parts).toBeUndefined()
    expect(message.text).toBe("prose")
  })
})

/**
 * A part kind this bundle has never heard of — the P2 `question` part, as a
 * host one release ahead would send it.
 *
 * The cast is the point: the generated union carries seven kinds and this is
 * not one of them, so there is no honest way to write this fixture in the
 * wire's own types. That is exactly the situation the mapper has to survive.
 */
const unknownWirePart = {
  kind: "question",
  prompt: "which branch?",
} as unknown as WireMessagePart

describe("a part kind the client does not know degrades the part", () => {
  it("drops it and keeps the parts around it", () => {
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        content: "prose and a question",
        parts: [
          { kind: "text", markdown: "prose" },
          unknownWirePart,
          { kind: "handoff", query: "waiting" },
        ],
      })
    )

    expect(message.parts?.map((part) => part.kind)).toEqual(["text", "handoff"])
  })

  it("falls back to the flat projection when nothing survives", () => {
    // The host guarantees `content` is the flattened projection of the same
    // row, so the words are still there with the structure removed. A hole
    // in the thread would be the worse answer, and a throw would take the
    // whole page with it.
    const message = chatMessageViewToDomainMessage(
      messageViewFixture({
        role: "assistant",
        content: "which branch?",
        parts: [unknownWirePart],
      })
    )

    expect(message.parts).toBeUndefined()
    expect(message.text).toBe("which branch?")
  })

  it("does not throw on a whole page of them", () => {
    expect(() =>
      chatMessagesPageToDomainMessages({
        items: [
          messageViewFixture({ id: "m1", parts: [unknownWirePart] }),
          messageViewFixture({ id: "m2", parts: [unknownWirePart] }),
        ],
        page: 1,
        pageSize: 50,
        total: 2,
      })
    ).not.toThrow()
  })
})

import { describe, expect, it } from "vitest"

import {
  chatMessagesPageToDomainMessages,
  chatMessageViewToDomainMessage,
} from "@/domains/chat/api/mappers"
import type { ChatMessageView } from "@/shared/api/_generated/types/ChatMessageView"

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

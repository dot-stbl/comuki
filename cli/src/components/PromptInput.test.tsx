/**
 * History-recall tests for the `›` prompt editor: ↑/↓ cycling, draft
 * preservation, position kept while editing a recalled entry, and the
 * reset-to-newest on submit. Keystrokes go through ink-testing-library's
 * mock stdin, so the whole editor runs as in the real TUI.
 *
 * The second suite covers the everyday pack: slash autocomplete (open,
 * filter, navigate, complete, dismiss) and multiline input (LF newline,
 * backslash continuation, submit on plain enter, row-count reporting).
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { PromptInput } from "./PromptInput"

/** Lets React flush the state update a keystroke schedules. */
function settle(ms = 100): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

const UP = "\x1b[A"
const DOWN = "\x1b[B"
const ENTER = "\r"
const LF = "\n"
const ESC = "\x1b"
const TAB = "\t"

interface PromptHandle {
  readonly stdin: { write: (data: string) => void }
  readonly lastFrame: () => string | undefined
  readonly unmount: () => void
  readonly submitted: string[]
}

async function renderPrompt(
  history: readonly string[],
  historyRecallEnabled: boolean = true
): Promise<PromptHandle> {
  const submitted: string[] = []
  const { stdin, lastFrame, unmount } = render(
    <PromptInput
      history={history}
      historyRecallEnabled={historyRecallEnabled}
      onSubmit={(value) => submitted.push(value)}
    />
  )
  // The first keystroke is lost without this: useInput subscribes in an
  // effect that must flush before the mock stdin can deliver anything.
  await settle()
  return { stdin, lastFrame, unmount, submitted }
}

describe("PromptInput history recall", () => {
  test("↑ recalls the newest entry, then walks older", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha", "beta"])
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("beta")
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("alpha")
    unmount()
  })

  test("↓ past the newest entry returns to the empty draft line", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha", "beta"])
    stdin.write(UP)
    await settle()
    // ↑ from the draft lands on the newest entry directly.
    expect(lastFrame()).toContain("beta")
    stdin.write(DOWN)
    await settle()
    // One ↓ past the newest entry drops back to the live line…
    expect(lastFrame()).not.toContain("beta")
    // …and ↓ on the draft stays on the draft.
    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).not.toContain("beta")
    unmount()
  })

  test("a half-typed draft is saved on ↑ and restored after ↓", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha"])
    stdin.write("draf")
    await settle()
    stdin.write("t")
    await settle()
    expect(lastFrame()).toContain("draft")

    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("alpha")

    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).toContain("draft")
    unmount()
  })

  test("editing a recalled entry keeps its position for further ↑/↓", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["one", "two"])
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("two")

    // Walk to the oldest entry and edit it — ↑ must not move away.
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("one")
    stdin.write("!")
    await settle()
    expect(lastFrame()).toContain("one!")

    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("one!")

    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).toContain("two")
    unmount()
  })

  test("enter submits the recalled entry and resets to the live line", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderPrompt(["solo"])
    stdin.write(UP)
    await settle()
    stdin.write(ENTER)
    await settle()

    expect(submitted).toEqual(["solo"])
    expect(lastFrame()).not.toContain("solo")

    // Position is back at the draft: ↑ recalls from newest again.
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("solo")
    unmount()
  })

  test("typing then enter submits the typed value", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderPrompt([])
    stdin.write("hello")
    await settle()
    expect(lastFrame()).toContain("hello")
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["hello"])
    expect(lastFrame()).not.toContain("hello")
    unmount()
  })

  test("↑ on an empty history stays on the live line", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt([])
    stdin.write("x")
    await settle()
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("x")
    unmount()
  })

  test("historyRecallEnabled=false: arrows neither recall nor disturb typing", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(
      ["alpha", "beta"],
      false
    )
    stdin.write("draf")
    await settle()
    stdin.write(UP)
    await settle()
    stdin.write(UP)
    await settle()
    // The draft is untouched — the viewport scrolled instead.
    expect(lastFrame()).toContain("draf")
    expect(lastFrame()).not.toContain("beta")
    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).not.toContain("alpha")
    unmount()
  })

  test("historyRecallEnabled=false: j/k/g/G are not inserted (viewport owns them)", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha"], false)
    stdin.write("j")
    await settle()
    stdin.write("k")
    await settle()
    stdin.write("g")
    await settle()
    stdin.write("G")
    await settle()
    const frame = lastFrame() ?? ""
    expect(frame).not.toContain("jk")
    expect(frame).not.toContain("gG")
    unmount()
  })

  test("historyRecallEnabled=false: enter still submits the typed line", async () => {
    const { stdin, unmount, submitted } = await renderPrompt(["old"], false)
    stdin.write("hi")
    await settle()
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["hi"])
    unmount()
  })
})

interface MenuHandle {
  readonly stdin: { write: (data: string) => void }
  readonly lastFrame: () => string | undefined
  readonly unmount: () => void
  readonly submitted: string[]
  readonly menuOpen: boolean[]
  readonly rows: number[]
}

async function renderMenuPrompt(): Promise<MenuHandle> {
  const submitted: string[] = []
  const menuOpen: boolean[] = []
  const rows: number[] = []
  const { stdin, lastFrame, unmount } = render(
    <PromptInput
      history={["alpha"]}
      onSubmit={(value) => submitted.push(value)}
      onMenuOpenChange={(open) => menuOpen.push(open)}
      onRowsChange={(count) => rows.push(count)}
    />
  )
  await settle()
  return { stdin, lastFrame, unmount, submitted, menuOpen, rows }
}

describe("PromptInput slash autocomplete", () => {
  test("a lone slash opens the menu above the prompt", async () => {
    const { stdin, lastFrame, unmount, menuOpen } = await renderMenuPrompt()
    stdin.write("/")
    await settle()
    const frame = lastFrame() ?? ""
    expect(menuOpen.at(-1)).toBe(true)
    expect(frame).toContain("/exit")
    expect(frame).toContain("leave the cli")
    expect(frame).toContain("/stop")
    unmount()
  })

  test("typing after the slash filters as you go", async () => {
    const { stdin, lastFrame, unmount } = await renderMenuPrompt()
    stdin.write("/re")
    await settle()
    const frame = lastFrame() ?? ""
    expect(frame).toContain("/retry")
    expect(frame).toContain("/rename")
    expect(frame).toContain("/reject")
    expect(frame).not.toContain("leave the cli")
    unmount()
  })

  test("↑/↓ move the selection with wrap-around, not history recall", async () => {
    const { stdin, lastFrame, unmount } = await renderMenuPrompt()
    stdin.write("/re")
    await settle()
    // Selection starts at retry; ↓ picks rename — completing proves it.
    // (ink trims trailing spaces per row, so the completed value shows
    // without its argument-ready trailing space here.)
    stdin.write(DOWN)
    await settle()
    stdin.write(TAB)
    await settle()
    expect(lastFrame() ?? "").toContain("/rename")
    expect(lastFrame() ?? "").not.toContain("/rename <title>")
    // While the menu is open the arrows never touched history.
    expect(lastFrame() ?? "").not.toContain("alpha")
    unmount()
  })

  test("↑ wraps from the first row back to the last", async () => {
    const { stdin, lastFrame, unmount } = await renderMenuPrompt()
    stdin.write("/re")
    await settle()
    stdin.write(UP)
    await settle()
    stdin.write(TAB)
    await settle()
    expect(lastFrame() ?? "").toContain("/reject")
    unmount()
  })

  test("enter completes the selection instead of submitting", async () => {
    const { stdin, lastFrame, unmount, submitted, menuOpen } =
      await renderMenuPrompt()
    stdin.write("/re")
    await settle()
    stdin.write(ENTER)
    await settle()
    expect(lastFrame() ?? "").toContain("/retry")
    expect(submitted).toEqual([])
    expect(menuOpen.at(-1)).toBe(false)
    unmount()
  })

  test("esc dismisses the menu; further typing reopens it", async () => {
    const { stdin, lastFrame, unmount, menuOpen } = await renderMenuPrompt()
    stdin.write("/re")
    await settle()
    stdin.write(ESC)
    await settle()
    expect(menuOpen.at(-1)).toBe(false)
    expect(lastFrame() ?? "").not.toContain("resend the last message")
    // Still on the partial command — one more character reopens the
    // menu for the new query ("/ren" → rename).
    stdin.write("n")
    await settle()
    expect(menuOpen.at(-1)).toBe(true)
    expect(lastFrame() ?? "").toContain("/rename")
    unmount()
  })

  test("completing with arguments closes the menu (whitespace query)", async () => {
    const { stdin, lastFrame, unmount, menuOpen } = await renderMenuPrompt()
    stdin.write("/rena")
    await settle()
    stdin.write(TAB)
    await settle()
    expect(lastFrame() ?? "").toContain("/rename")
    expect(menuOpen.at(-1)).toBe(false)
    unmount()
  })
})

describe("PromptInput multiline", () => {
  test("a raw LF inserts a newline instead of submitting", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderMenuPrompt()
    stdin.write("one")
    await settle()
    stdin.write(LF)
    await settle()
    stdin.write("two")
    await settle()
    stdin.write(ENTER)
    await settle()

    expect(submitted).toEqual(["one\ntwo"])
    // Both lines rendered before submit: the frame held the wrapped prompt.
    unmount()
  })

  test("a trailing backslash + enter continues the line", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderMenuPrompt()
    stdin.write("select \\")
    await settle()
    stdin.write(ENTER)
    await settle()
    // Not submitted — the backslash turned into a newline.
    expect(submitted).toEqual([])
    const continued = lastFrame() ?? ""
    expect(continued).not.toContain("\\")
    stdin.write("tail")
    await settle()
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["select \ntail"])
    unmount()
  })

  test("the multiline prompt spans rows and reports the count", async () => {
    const { stdin, unmount, rows } = await renderMenuPrompt()
    stdin.write("a")
    await settle()
    stdin.write(LF)
    await settle()
    stdin.write("b")
    await settle()
    stdin.write(LF)
    await settle()
    stdin.write("c")
    await settle()
    expect(rows.at(-1)).toBe(3)
    unmount()
  })

  test("the menu rows add to the reported count", async () => {
    const { stdin, unmount, rows } = await renderMenuPrompt()
    stdin.write("/")
    await settle()
    expect(rows.at(-1)).toBeGreaterThan(1)
    unmount()
  })
})

describe("PromptInput mention seams", () => {
  test("onDraftChange fires for every draft and for the submit reset", async () => {
    const drafts: string[] = []
    const { stdin, unmount } = render(
      <PromptInput
        onSubmit={() => {}}
        onDraftChange={(value) => drafts.push(value)}
      />
    )
    await settle()
    for (const character of ["@", "i", "d", "x"]) {
      stdin.write(character)
      await settle()
    }
    stdin.write(ENTER)
    await settle()
    // "" is the mount sync — the menu learns the (empty) initial draft.
    expect(drafts).toEqual(["", "@", "@i", "@id", "@idx", ""])
    unmount()
  })

  test("interceptKey consumes a keystroke and may rewrite the draft", async () => {
    const { stdin, lastFrame, unmount } = render(
      <PromptInput
        onSubmit={() => {}}
        interceptKey={(input, _key, editor) => {
          if (input !== "z") {
            return false
          }
          const { value } = editor.get()
          const next = `${value}!`
          editor.set({ value: next, cursor: next.length })
          return true
        }}
      />
    )
    await settle()
    stdin.write("a")
    await settle()
    stdin.write("z")
    await settle()
    const frame = lastFrame() ?? ""
    expect(frame).toContain("a!")
    expect(frame).not.toContain("az")
    unmount()
  })

  test("mask hides typed characters but submits the real value", async () => {
    const submitted: string[] = []
    const { stdin, lastFrame, unmount } = render(
      <PromptInput
        mask="*"
        slashMenuEnabled={false}
        onSubmit={(value) => submitted.push(value)}
      />
    )
    await settle()
    for (const character of ["s", "e", "c", "r", "e", "t"]) {
      stdin.write(character)
      await settle()
    }
    const frame = lastFrame() ?? ""
    expect(frame).toContain("******")
    expect(frame).not.toContain("secret")
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["secret"])
    unmount()
  })
})

describe("PromptInput seed prefill", () => {
  test("a seed with a new seq replaces the editor value without submitting", async () => {
    const submitted: string[] = []
    const { lastFrame, rerender, unmount } = render(
      <PromptInput
        onSubmit={(value) => submitted.push(value)}
        seed={{ value: "edit me", seq: 1 }}
      />
    )
    await settle()
    expect(lastFrame()).toContain("edit me")
    expect(submitted).toEqual([])
    rerender(
      <PromptInput
        onSubmit={(value) => submitted.push(value)}
        seed={{ value: "edit me", seq: 1 }}
      />
    )
    await settle()
    expect(lastFrame()).toContain("edit me")
    expect(submitted).toEqual([])
    unmount()
  })

  test("a later seq overwrites the current draft", async () => {
    const submitted: string[] = []
    const { stdin, lastFrame, rerender, unmount } = render(
      <PromptInput
        onSubmit={(value) => submitted.push(value)}
        seed={{ value: "first", seq: 1 }}
      />
    )
    await settle()
    stdin.write("!")
    await settle()
    expect(lastFrame()).toContain("first!")
    rerender(
      <PromptInput
        onSubmit={(value) => submitted.push(value)}
        seed={{ value: "second", seq: 2 }}
      />
    )
    await settle()
    expect(lastFrame()).toContain("second")
    expect(lastFrame()).not.toContain("first")
    expect(submitted).toEqual([])
    unmount()
  })

  test("same seq after typing does not clobber the draft", async () => {
    const { stdin, lastFrame, rerender, unmount } = render(
      <PromptInput
        onSubmit={() => {}}
        seed={{ value: "seeded", seq: 1 }}
      />
    )
    await settle()
    stdin.write("x")
    await settle()
    expect(lastFrame()).toContain("seededx")
    rerender(
      <PromptInput
        onSubmit={() => {}}
        seed={{ value: "seeded", seq: 1 }}
      />
    )
    await settle()
    expect(lastFrame()).toContain("seededx")
    unmount()
  })
})

/**
 * Byte-exact tests for the terminal control sequences: OSC 2 title,
 * BEL, OSC 9 notification, OSC 8 hyperlinks, the title composition
 * and the combined turn-completion bytes.
 */
import { describe, expect, it } from "bun:test"
import {
  bellSequence,
  linkSequence,
  notifySequence,
  terminalTitle,
  titleSequence,
  turnDoneSequences,
} from "./term"

describe("titleSequence", () => {
  it("wraps text in OSC 2 with a BEL terminator", () => {
    expect(titleSequence("comuki — fix auth ⏳")).toBe(
      "\x1b]2;comuki — fix auth ⏳\x07"
    )
  })

  it("strips control bytes so a session name cannot inject sequences", () => {
    // The ESC/BEL/CR/LF are gone — no sequence can execute. The literal
    // leftovers of a hostile name just become inert title text.
    const sequence = titleSequence("a\x1b]0;pwned\x07b\nc\rd")
    expect(sequence).toBe("\x1b]2;a]0;pwnedbcd\x07")
    expect(sequence.slice(3, -1)).not.toMatch(/[\x00-\x1f\x7f]/)
  })
})

describe("bellSequence / notifySequence", () => {
  it("bell is the single attention byte", () => {
    expect(bellSequence()).toBe("\x07")
  })

  it("notify wraps text in OSC 9 with a BEL terminator", () => {
    expect(notifySequence("comuki: turn done")).toBe(
      "\x1b]9;comuki: turn done\x07"
    )
  })

  it("notify sanitizes its payload too", () => {
    expect(notifySequence("a\x1bb\x07c")).toBe("\x1b]9;abc\x07")
  })
})

describe("linkSequence", () => {
  it("wraps the label in OSC 8 with an empty closing target", () => {
    expect(linkSequence("http://h:17173/runs/abc", "abc")).toBe(
      "\x1b]8;;http://h:17173/runs/abc\x1b\\abc\x1b]8;;\x1b\\"
    )
  })

  it("keeps SGR paint inside the label untouched", () => {
    const label = "\x1b[38;2;184;184;189mrun\x1b[0m"
    expect(linkSequence("http://h/runs/x", label)).toBe(
      `\x1b]8;;http://h/runs/x\x1b\\${label}\x1b]8;;\x1b\\`
    )
  })

  it("strips ESC and BEL from the url so nothing can break out of the sequence", () => {
    const sequence = linkSequence("http://h/\x1b]8;;evil\x07", "label")
    // Byte-exact: the hostile ESC/BEL are gone from the target slot —
    // the leftovers are inert url text.
    expect(sequence).toBe("\x1b]8;;http://h/]8;;evil\x1b\\label\x1b]8;;\x1b\\")
  })

  it("degrades to the bare label when OSC 8 is unsupported — the label is the only visible text", () => {
    const plain = "abc123"
    // Every other byte of the sequence is an escape — stripping the
    // two OSC 8 wrappers leaves exactly the label.
    const stripped = linkSequence("http://h/runs/abc123", plain).replace(

      /\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g,
      ""
    )
    expect(stripped).toBe(plain)
  })
})

describe("turnDoneSequences", () => {
  it("emits BEL + OSC 9 when the bell is on", () => {
    expect(turnDoneSequences(true)).toBe(
      "\x07\x1b]9;comuki: turn done\x07"
    )
  })

  it("emits the OSC 9 toast alone when the bell is off", () => {
    expect(turnDoneSequences(false)).toBe("\x1b]9;comuki: turn done\x07")
  })
})

describe("terminalTitle", () => {
  it("is the bare app name when no session is open", () => {
    expect(terminalTitle(undefined, false)).toBe("comuki")
    expect(terminalTitle("", true)).toBe("comuki")
  })

  it("carries the hourglass while thinking and the check when idle", () => {
    expect(terminalTitle("fix auth", true)).toBe("comuki - fix auth [working]")
    expect(terminalTitle("fix auth", false)).toBe("comuki - fix auth [ready]")
  })
})

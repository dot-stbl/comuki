/* What a pane group is allowed to remember.
 *
 * `react-resizable-panels` reports a layout only when one actually changes, and
 * jsdom lays nothing out, so a real `Group` in a test never emits anything and
 * the persistence path would go unexercised. The library is therefore stubbed
 * down to the one seam this component owns — the `onLayoutChanged` callback it
 * hands over — and the tests drive that callback directly. Everything the real
 * library does with sizes is deliberately out of scope here; the question is
 * only which of its reports get written down.
 */
import { render } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { SplitPane, SplitPanel, SplitSeparator, type SplitLayout } from "./split-pane"

/** The callback the last-rendered group handed the stub. */
let report: ((layout: SplitLayout) => void) | undefined

vi.mock("react-resizable-panels", () => ({
  Group: ({
    children,
    onLayoutChanged,
  }: {
    children: React.ReactNode
    onLayoutChanged?: (layout: SplitLayout) => void
  }) => {
    report = onLayoutChanged
    return <div>{children}</div>
  },
  Panel: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Separator: ({ children }: { children: React.ReactNode }) => (
    <div>{children}</div>
  ),
}))

const KEY = "comuki.test.split"

/** Mounts a group and hands back the callback it reports layouts through. */
function mount(shouldPersist?: () => boolean): (layout: SplitLayout) => void {
  report = undefined
  render(
    <SplitPane
      orientation="horizontal"
      storageKey={KEY}
      shouldPersist={shouldPersist}
    >
      <SplitPanel id="a">a</SplitPanel>
      <SplitSeparator orientation="horizontal" />
      <SplitPanel id="b">b</SplitPanel>
    </SplitPane>
  )
  const emit = report
  if (!emit) {
    // Loud rather than optional: a test that silently emitted nothing would
    // assert "storage was not written" for entirely the wrong reason.
    throw new Error("the pane group handed over no layout callback")
  }
  return emit
}

let write: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
  write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {})
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe("what a pane group remembers", () => {
  it("writes a layout when nothing objects", () => {
    mount()?.({ a: 0.4, b: 0.6 })

    expect(write).toHaveBeenCalledWith(KEY, JSON.stringify({ a: 0.4, b: 0.6 }))
  })

  it("writes nothing while the gate is shut", () => {
    // The rail's case: below the narrow breakpoint the shell collapses the rail
    // on the viewport's say-so, and the layout is a single key — so without the
    // gate that collapse is written straight over the width the operator chose
    // at their desk, and the next wide session opens collapsed for no reason it
    // can explain.
    mount(() => false)?.({ a: 0.05, b: 0.95 })

    expect(write).not.toHaveBeenCalled()
  })

  it("asks the gate again on every report, never once at mount", () => {
    // The window is resized under a mounted shell far more often than a shell
    // is mounted, so a gate captured at mount would be answering about a window
    // that is no longer there.
    let open = false
    const emit = mount(() => open)

    emit?.({ a: 0.1, b: 0.9 })
    expect(write).not.toHaveBeenCalled()

    open = true
    emit?.({ a: 0.3, b: 0.7 })
    expect(write).toHaveBeenCalledTimes(1)
  })

  it("keeps rendering when storage refuses to be written to", () => {
    // A private window and blocked site data both throw on access. A duty
    // console must not fail to remember quietly rather than fail to render.
    write.mockImplementation(() => {
      throw new Error("denied")
    })

    expect(() => mount()?.({ a: 0.5, b: 0.5 })).not.toThrow()
  })
})

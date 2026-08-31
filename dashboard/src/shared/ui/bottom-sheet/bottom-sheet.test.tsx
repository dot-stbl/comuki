/* What a modal bottom sheet owes, and what it must not write down.
 *
 * jsdom lays nothing out, so a real pane group never emits a layout and the
 * drag itself cannot be simulated. The pane library is therefore stubbed down
 * to the one seam this component owns — the `onLayoutChanged` callback the
 * group hands over — and the tests drive that callback directly, exactly as
 * `split-pane.test.tsx` does for the persistence gate it shares with this
 * sheet. React Aria itself is NOT stubbed: the modal behaviour under test
 * (the focus trap, escape, the hidden rest of the document) is the real one.
 *
 * The separator stub spells `disabled` as `aria-disabled` because that is
 * what the real library renders — the assertions are about the contract, not
 * about the stub's invention.
 */
import type { ComponentProps, ReactNode } from "react"
import { fireEvent, render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import type { SplitLayout } from "../split-pane"
import { BottomSheet } from "./bottom-sheet"

/** The callback the last-rendered pane group handed the stub. */
let report: ((layout: SplitLayout) => void) | undefined

vi.mock("react-resizable-panels", () => ({
  Group: ({
    children,
    onLayoutChanged,
  }: {
    children?: ReactNode
    onLayoutChanged?: (layout: SplitLayout) => void
  }) => {
    report = onLayoutChanged
    return <div>{children}</div>
  },
  Panel: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  Separator: ({
    children,
    disabled,
    ...props
  }: ComponentProps<"div"> & { disabled?: boolean }) => (
    <div {...props} role="separator" aria-disabled={disabled || undefined}>
      {children}
    </div>
  ),
}))

const KEY = "comuki.test.bottom-sheet"

/** The sheet at whatever depth the caller says, with the seams a test needs. */
function Sheet({
  expanded = false,
  dismissable = false,
  onOpenChange = () => undefined,
  onExpandedChange = () => undefined,
}: {
  expanded?: boolean
  dismissable?: boolean
  onOpenChange?: (open: boolean) => void
  onExpandedChange?: (next: boolean) => void
}) {
  return (
    <BottomSheet
      open
      onOpenChange={onOpenChange}
      title="Run log"
      storageKey={KEY}
      expanded={expanded}
      dismissable={dismissable}
      onExpandedChange={onExpandedChange}
    >
      lines
    </BottomSheet>
  )
}

const byTest = (name: string) => {
  const found = document.querySelector<HTMLElement>(`[data-test="${name}"]`)
  if (!found) {
    // Loud rather than optional: "the edge does not exist" and "the edge was
    // not found" are different failures, and only one of them is a defect.
    throw new Error(`no element carries data-test="${name}"`)
  }
  return found
}

/** Hands back the layout callback the mounted sheet's pane group reported. */
function emit(): (layout: SplitLayout) => void {
  if (!report) {
    throw new Error("the pane group handed over no layout callback")
  }
  return report
}

let write: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  // The established second-mount fix (see `chat-page.test.tsx`): a stored
  // layout restored into a fresh mount is what makes the real pane group
  // throw, and keeping the read null keeps every case here at the same
  // starting depth regardless of what the case before it dragged.
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
  write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {})
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe("the sheet is a modal", () => {
  it("hides the page behind it, and closes on escape", () => {
    // Modality is asserted as the hiding of everything outside, not as an
    // `aria-modal` attribute: React Aria filters that attribute out of every
    // Dialog on purpose (a Safari iframe focus bug) and its own comment says
    // the hiding "will behave as a modal even without aria-modal". An
    // assertion on the attribute would be testing a filter, not the sheet.
    //
    // Grabbed before opening: the hiding applies to the document as it was
    // when the overlay opened, so an element created after the fact would be
    // querying a tree that is already under the sheet's control.
    const outside = document.createElement("button")
    document.body.appendChild(outside)

    const onOpenChange = vi.fn()
    render(<Sheet onOpenChange={onOpenChange} />)

    const dialog = screen.getByRole("dialog")
    expect(dialog.getAttribute("data-test")).toBe("bottom-sheet")
    expect(outside.getAttribute("aria-hidden")).toBe("true")

    fireEvent.keyDown(dialog, { key: "Escape" })
    expect(onOpenChange).toHaveBeenCalledWith(false)

    document.body.removeChild(outside)
  })

  it("does not close on a click at the empty scrim above the sheet", () => {
    // The default, and the product's form-dialog rule: a form half-filled is
    // lost to a stray click, so the dark is not a way out unless the caller
    // says so. Real pointer events through the real React Aria handlers —
    // jsdom has PointerEvent, so the library's interact-outside listener runs
    // exactly as it does in a browser.
    const onOpenChange = vi.fn()
    render(<Sheet onOpenChange={onOpenChange} />)

    // The portal's topmost element is the scrim; with the pane group stubbed,
    // the whole window above the dialog is that element's own background.
    const scrim = document.body.lastElementChild as Element
    for (const fire of [
      () => fireEvent.pointerDown(scrim, { button: 0 }),
      () => fireEvent.mouseDown(scrim, { button: 0 }),
      () => fireEvent.mouseUp(scrim, { button: 0 }),
      () => fireEvent.click(scrim, { button: 0 }),
    ]) {
      fire()
    }

    expect(onOpenChange).not.toHaveBeenCalled()
  })
})

describe("the dismissable dark", () => {
  it("closes on a click in the empty window above the sheet", () => {
    const onOpenChange = vi.fn()
    render(<Sheet dismissable onOpenChange={onOpenChange} />)

    const hit = byTest("bottom-sheet-scrim-hit")
    fireEvent.click(hit)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it("keeps the sheet itself immune to a stray click", () => {
    // The dark closes; the sheet does not. A click that lands inside the
    // dialog — on the bar, the body, a control — is a click *in* the console,
    // and consoles are where people click while thinking.
    const onOpenChange = vi.fn()
    render(<Sheet dismissable onOpenChange={onOpenChange} />)

    fireEvent.click(screen.getByRole("dialog"))
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it("is not drawn when the caller kept the default", () => {
    render(<Sheet />)
    expect(document.querySelector('[data-test="bottom-sheet-scrim-hit"]')).toBeNull()
  })
})

describe("the drag edge", () => {
  it("is live at panel depth and retired when the sheet fills the window", () => {
    const view = render(<Sheet expanded={false} />)

    expect(byTest("split-separator").getAttribute("aria-disabled")).toBeNull()

    view.rerender(<Sheet expanded={true} />)
    expect(byTest("split-separator").getAttribute("aria-disabled")).toBe("true")
  })
})

describe("what the sheet remembers", () => {
  it("writes a dragged depth while it sits at panel depth", () => {
    render(<Sheet />)

    emit()({ above: 0.4, sheet: 0.6 })

    expect(write).toHaveBeenCalledWith(
      KEY,
      JSON.stringify({ above: 0.4, sheet: 0.6 })
    )
  })

  it("writes nothing while the sheet fills the window", () => {
    // The sheet's own reason to exist: "the whole window" is not a depth
    // anybody dragged, and writing it down would replace the depth they did.
    // The gate is asked at write time, so this must hold even though the
    // same callback was happy to write a moment earlier.
    const view = render(<Sheet expanded={false} />)
    emit()({ above: 0.4, sheet: 0.6 })
    expect(write).toHaveBeenCalledTimes(1)

    view.rerender(<Sheet expanded={true} />)
    emit()({ above: 0, sheet: 1 })
    expect(write).toHaveBeenCalledTimes(1)
  })
})

describe("the expand control", () => {
  it("answers both ways and says which state it is in", () => {
    const onExpandedChange = vi.fn()
    const view = render(
      <Sheet expanded={false} onExpandedChange={onExpandedChange} />
    )

    const expand = byTest("bottom-sheet-expand")
    expect(expand.getAttribute("aria-pressed")).toBe("false")
    expect(expand.getAttribute("aria-label")).toBe(
      "Fill the window with Run log"
    )

    fireEvent.click(expand)
    expect(onExpandedChange).toHaveBeenCalledWith(true)

    view.rerender(<Sheet expanded={true} onExpandedChange={onExpandedChange} />)
    expect(expand.getAttribute("aria-pressed")).toBe("true")
    expect(expand.getAttribute("aria-label")).toBe(
      "Shrink Run log back to a panel"
    )

    fireEvent.click(expand)
    expect(onExpandedChange).toHaveBeenCalledWith(false)
  })
})

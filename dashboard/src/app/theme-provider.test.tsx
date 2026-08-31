import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
/* The two axes, and what happens when the browser refuses to remember them.
 *
 * Everything here is about independence: a person who picks "dockside" has not
 * picked "light", and a person who picks "light" has not thrown away
 * "dockside". The two live in two storage keys, restore separately, and are
 * applied to the root by two different mechanisms so neither can clobber the
 * other.
 */
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { act } from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider, useTheme } from "@/app/theme-provider"

const MODE_KEY = "test-mode"
const THEME_KEY = "test-mode-name"

function Probe() {
  const { theme, mode, resolvedMode, themeId, setMode, setThemeId } = useTheme()

  return (
    <div>
      <span data-testid="legacy-theme">{theme}</span>
      <span data-testid="mode">{mode}</span>
      <span data-testid="resolved">{resolvedMode}</span>
      <span data-testid="theme-id">{themeId}</span>
      <button type="button" onClick={() => setMode("light")}>
        to light
      </button>
      <button type="button" onClick={() => setThemeId("dockside")}>
        to dockside
      </button>
      <button type="button" onClick={() => setThemeId("not-a-theme")}>
        to nothing
      </button>
    </div>
  )
}

function mount() {
  return render(
    <ThemeProvider defaultTheme="dark" storageKey={MODE_KEY}>
      <Probe />
    </ThemeProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.className = ""
  document.documentElement.removeAttribute("data-theme")
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe("the mode and the palette are two independent choices", () => {
  it("derives the palette's storage key from the mode's", () => {
    // A caller that isolates one axis — every shell test passes its own
    // `storageKey` — gets both isolated, without having to know this axis
    // exists.
    localStorage.setItem(MODE_KEY, "light")
    localStorage.setItem(THEME_KEY, "blueprint")
    mount()

    expect(screen.getByTestId("mode").textContent).toBe("light")
    expect(screen.getByTestId("theme-id").textContent).toBe("blueprint")
  })

  it("restores one when the other was never written", () => {
    localStorage.setItem(THEME_KEY, "bureau")
    mount()

    expect(screen.getByTestId("mode").textContent).toBe("dark")
    expect(screen.getByTestId("theme-id").textContent).toBe("bureau")
  })

  it("changes the palette without disturbing the mode", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: "to dockside" }))

    expect(screen.getByTestId("theme-id").textContent).toBe("dockside")
    expect(screen.getByTestId("mode").textContent).toBe("dark")
    expect(localStorage.getItem(THEME_KEY)).toBe("dockside")
    expect(localStorage.getItem(MODE_KEY)).toBe(null)
  })

  it("changes the mode without disturbing the palette", async () => {
    const user = userEvent.setup()
    localStorage.setItem(THEME_KEY, "aperture")
    mount()

    await user.click(screen.getByRole("button", { name: "to light" }))

    expect(screen.getByTestId("mode").textContent).toBe("light")
    expect(screen.getByTestId("theme-id").textContent).toBe("aperture")
    expect(localStorage.getItem(MODE_KEY)).toBe("light")
    expect(localStorage.getItem(THEME_KEY)).toBe("aperture")
  })

  it("keeps `theme` meaning the mode, for the control that already reads it", () => {
    // `app/layout/theme-control.tsx` is built on `theme` / `setTheme`. Adding
    // an axis is not a licence to rename the one that was already there.
    localStorage.setItem(MODE_KEY, "light")
    mount()

    expect(screen.getByTestId("legacy-theme").textContent).toBe("light")
  })
})

describe("the choice reaches the document by two different mechanisms", () => {
  it("writes the mode as a class and the palette as an attribute", async () => {
    const user = userEvent.setup()
    mount()

    expect(document.documentElement.classList.contains("dark")).toBe(true)
    expect(document.documentElement.getAttribute("data-theme")).toBe(
      "dichromat-deck"
    )

    await user.click(screen.getByRole("button", { name: "to dockside" }))

    // The class survives the palette change, which is the whole point of two
    // axes: the room did not change, only the paint.
    expect(document.documentElement.classList.contains("dark")).toBe(true)
    expect(document.documentElement.getAttribute("data-theme")).toBe("dockside")

    await user.click(screen.getByRole("button", { name: "to light" }))

    expect(document.documentElement.classList.contains("light")).toBe(true)
    expect(document.documentElement.classList.contains("dark")).toBe(false)
    expect(document.documentElement.getAttribute("data-theme")).toBe("dockside")
  })

  it("tells the browser which room its own furniture is in", () => {
    // Without `color-scheme` the scrollbar gutter and form controls stay light
    // on a dark board — chrome the stylesheet cannot reach.
    mount()

    expect(document.documentElement.style.colorScheme).toBe("dark")
  })
})

describe("nothing it is handed is trusted", () => {
  it("falls back when storage holds a palette that no longer exists", () => {
    localStorage.setItem(THEME_KEY, "some-removed-theme")
    mount()

    expect(screen.getByTestId("theme-id").textContent).toBe("dichromat-deck")
  })

  it("refuses to set a palette that is not in the registry", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: "to nothing" }))

    expect(screen.getByTestId("theme-id").textContent).toBe("dichromat-deck")
    expect(localStorage.getItem(THEME_KEY)).toBe(null)
  })

  it("follows a change made in another tab, on the axis it was made on", () => {
    mount()

    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: THEME_KEY,
          newValue: "graphite",
          storageArea: localStorage,
        })
      )
    })

    expect(screen.getByTestId("theme-id").textContent).toBe("graphite")
    expect(screen.getByTestId("mode").textContent).toBe("dark")
  })

  it("returns to the default when another tab clears the key", () => {
    localStorage.setItem(THEME_KEY, "bureau")
    mount()

    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: THEME_KEY,
          newValue: null,
          storageArea: localStorage,
        })
      )
    })

    expect(screen.getByTestId("theme-id").textContent).toBe("dichromat-deck")
  })
})

describe("a browser that denies storage forgets rather than breaks", () => {
  it("mounts on the defaults when reading throws", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("denied", "SecurityError")
    })

    expect(() => mount()).not.toThrow()
    expect(screen.getByTestId("mode").textContent).toBe("dark")
    expect(screen.getByTestId("theme-id").textContent).toBe("dichromat-deck")
  })

  it("still applies a choice when writing throws", async () => {
    const user = userEvent.setup()
    mount()
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("denied", "SecurityError")
    })

    await user.click(screen.getByRole("button", { name: "to dockside" }))

    // The session keeps the choice; the reload will not. That is the bargain,
    // and it is better than a click handler that throws.
    expect(screen.getByTestId("theme-id").textContent).toBe("dockside")
    expect(document.documentElement.getAttribute("data-theme")).toBe("dockside")
  })
})

describe("system is the absence of an override, not a third palette", () => {
  it("resolves against the machine and follows it", () => {
    const listeners: ((event: MediaQueryListEvent) => void)[] = []
    vi.spyOn(window, "matchMedia").mockImplementation(
      (query: string) =>
        ({
          matches: true,
          media: query,
          onchange: null,
          addEventListener: (_type: string, listener: unknown) => {
            listeners.push(listener as (event: MediaQueryListEvent) => void)
          },
          removeEventListener: () => {},
          addListener: () => {},
          removeListener: () => {},
          dispatchEvent: () => false,
        }) as unknown as MediaQueryList
    )

    render(
      <ThemeProvider defaultTheme="system" storageKey={MODE_KEY}>
        <Probe />
      </ThemeProvider>
    )

    expect(screen.getByTestId("mode").textContent).toBe("system")
    expect(screen.getByTestId("resolved").textContent).toBe("dark")

    act(() => {
      for (const listener of listeners) {
        listener({ matches: false } as MediaQueryListEvent)
      }
    })

    expect(screen.getByTestId("resolved").textContent).toBe("light")
    expect(screen.getByTestId("mode").textContent).toBe("system")
  })
})

describe("the no-flash script in index.html", () => {
  /* The script is duplicated logic by design — importing a module would cost a
     round trip and the flash would happen before it resolved. Duplication is
     only safe while something notices the two drifting apart, so this reads the
     document off disk and pins the three facts that must agree. */
  // Split in two steps: `new URL("…", import.meta.url)` is a pattern Vite
  // rewrites, and the rewritten value is no longer a file URL by the time the
  // test runs. `data-table.test.tsx` reads its stylesheet the same way.
  const html = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), "..", "..", "index.html"),
    "utf8"
  )

  it("reads the same two storage keys the provider writes", () => {
    expect(html).toContain('"comuki-ui-theme"')
    expect(html).toContain('"comuki-ui-theme-name"')
  })

  it("resolves system to a real mode before the first paint", () => {
    // Applying `system` as a class would leave the board unstyled until React
    // mounted, which is the flash the script exists to prevent.
    expect(html).toContain("prefers-color-scheme: dark")
  })

  it("refuses a palette id that is not one", () => {
    // The value comes out of storage, and storage is writable by anything that
    // ran on this origin. It reaches `setAttribute`, so it is validated.
    expect(html).toMatch(/\/\^\[a-z0-9-\]\{1,32\}\$\//)
  })
})

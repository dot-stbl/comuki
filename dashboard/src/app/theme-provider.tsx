/* eslint-disable react-refresh/only-export-components */
import * as React from "react"

import { DEFAULT_THEME_ID, isThemeId, THEMES } from "@/app/theme/themes"
import type { Theme } from "@/app/theme/themes"

/* Appearance has two axes and they do not collapse into one.
 *
 *   mode   dark | light | system   — which room the person is in
 *   theme  a palette id            — which palette that room is painted in
 *
 * Every theme has *both* a dark and a light rendering, so "light" is not a
 * theme and "graphite" is not a mode. Folding them together would double the
 * list the picker shows and would make choosing a palette silently choose a
 * room as well.
 *
 * The two are stored separately, restored separately, and applied to the root
 * element by two different mechanisms — mode by the `.dark` class (which
 * Tailwind's `dark:` variant already keys off) and theme by `data-theme`. Both
 * fall back rather than throw when storage is denied: a private window is a
 * browser that forgets, not a browser that breaks.
 *
 * `theme` / `setTheme` still mean the *mode*, because `app/layout/
 * theme-control.tsx` is built on those names. `mode` / `setMode` are the same
 * two values under the names the rest of this file uses.
 */

type Mode = "dark" | "light" | "system"
type ResolvedMode = "dark" | "light"

type ThemeProviderProps = {
  children: React.ReactNode
  /** Mode to use when storage holds nothing usable. */
  defaultTheme?: Mode
  /** Palette to use when storage holds nothing usable. */
  defaultThemeId?: string
  /** Storage key for the mode. */
  storageKey?: string
  /**
   * Storage key for the palette. Defaults to the mode's key with `-name`
   * appended, so a caller that isolates one axis isolates both — every test
   * that passes `storageKey="comuki-test-theme"` gets its own palette slot too,
   * without having to know this axis exists.
   */
  themeStorageKey?: string
  disableTransitionOnChange?: boolean
}

type ThemeProviderState = {
  /** The mode. Legacy name — `theme-control.tsx` reads these two. */
  theme: Mode
  setTheme: (theme: Mode) => void
  /** The mode, under the name the two-axis vocabulary uses. */
  mode: Mode
  setMode: (mode: Mode) => void
  /** `system` resolved against the machine — never `system` itself. */
  resolvedMode: ResolvedMode
  /** The palette. */
  themeId: string
  setThemeId: (id: string) => void
  /** Everything a picker needs to draw itself. */
  themes: readonly Theme[]
}

const COLOR_SCHEME_QUERY = "(prefers-color-scheme: dark)"
const MODE_VALUES: Mode[] = ["dark", "light", "system"]

const ThemeProviderContext = React.createContext<
  ThemeProviderState | undefined
>(undefined)

function isMode(value: string | null): value is Mode {
  if (value === null) {
    return false
  }

  return MODE_VALUES.includes(value as Mode)
}

function readStored(key: string): string | null {
  try {
    return window.localStorage.getItem(key)
  } catch {
    // Storage is denied. The session still works; it just starts from the
    // defaults every time.
    return null
  }
}

function writeStored(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value)
  } catch {
    // Same bargain in the other direction: the choice holds for this session
    // and is forgotten on reload, rather than throwing out of a click handler.
  }
}

function getSystemMode(): ResolvedMode {
  if (window.matchMedia(COLOR_SCHEME_QUERY).matches) {
    return "dark"
  }

  return "light"
}

function disableTransitionsTemporarily() {
  const style = document.createElement("style")
  style.appendChild(
    document.createTextNode(
      "*,*::before,*::after{-webkit-transition:none!important;transition:none!important}"
    )
  )
  document.head.appendChild(style)

  return () => {
    window.getComputedStyle(document.body)
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        style.remove()
      })
    })
  }
}

export function ThemeProvider({
  children,
  defaultTheme = "system",
  defaultThemeId = DEFAULT_THEME_ID,
  storageKey = "theme",
  themeStorageKey,
  disableTransitionOnChange = true,
  ...props
}: ThemeProviderProps) {
  const paletteKey = themeStorageKey ?? `${storageKey}-name`
  const fallbackThemeId = isThemeId(defaultThemeId)
    ? defaultThemeId
    : DEFAULT_THEME_ID

  const [mode, setModeState] = React.useState<Mode>(() => {
    const stored = readStored(storageKey)
    if (isMode(stored)) {
      return stored
    }

    return defaultTheme
  })

  const [themeId, setThemeIdState] = React.useState<string>(() => {
    const stored = readStored(paletteKey)
    if (isThemeId(stored)) {
      return stored
    }

    return fallbackThemeId
  })

  /* The machine's own preference, tracked rather than read once: a person who
     flips their OS to dark while the board is open is telling it something. */
  const [systemMode, setSystemMode] = React.useState<ResolvedMode>(() =>
    getSystemMode()
  )

  const setMode = React.useCallback(
    (next: Mode) => {
      writeStored(storageKey, next)
      setModeState(next)
    },
    [storageKey]
  )

  const setThemeId = React.useCallback(
    (next: string) => {
      if (!isThemeId(next)) {
        return
      }
      writeStored(paletteKey, next)
      setThemeIdState(next)
    },
    [paletteKey]
  )

  React.useEffect(() => {
    const mediaQuery = window.matchMedia(COLOR_SCHEME_QUERY)
    const handleChange = (event: MediaQueryListEvent) => {
      setSystemMode(event.matches ? "dark" : "light")
    }

    mediaQuery.addEventListener("change", handleChange)

    return () => {
      mediaQuery.removeEventListener("change", handleChange)
    }
  }, [])

  const resolvedMode: ResolvedMode = mode === "system" ? systemMode : mode

  React.useEffect(() => {
    const root = document.documentElement
    const restoreTransitions = disableTransitionOnChange
      ? disableTransitionsTemporarily()
      : null

    root.classList.remove("light", "dark")
    root.classList.add(resolvedMode)
    root.setAttribute("data-theme", themeId)
    /* `color-scheme` is what makes the browser's own furniture — form
       controls, the scrollbar gutter, the canvas behind an overscroll — agree
       with the palette. Without it a dark board keeps a white scroll gutter. */
    root.style.colorScheme = resolvedMode

    if (restoreTransitions) {
      restoreTransitions()
    }
  }, [resolvedMode, themeId, disableTransitionOnChange])

  React.useEffect(() => {
    const handleStorageChange = (event: StorageEvent) => {
      if (event.storageArea !== window.localStorage) {
        return
      }

      if (event.key === storageKey) {
        setModeState(isMode(event.newValue) ? event.newValue : defaultTheme)
        return
      }

      if (event.key === paletteKey) {
        setThemeIdState(
          isThemeId(event.newValue) ? event.newValue : fallbackThemeId
        )
      }
    }

    window.addEventListener("storage", handleStorageChange)

    return () => {
      window.removeEventListener("storage", handleStorageChange)
    }
  }, [defaultTheme, fallbackThemeId, paletteKey, storageKey])

  const value = React.useMemo(
    () => ({
      theme: mode,
      setTheme: setMode,
      mode,
      setMode,
      resolvedMode,
      themeId,
      setThemeId,
      themes: THEMES,
    }),
    [mode, setMode, resolvedMode, themeId, setThemeId]
  )

  return (
    <ThemeProviderContext.Provider {...props} value={value}>
      {children}
    </ThemeProviderContext.Provider>
  )
}

export const useTheme = () => {
  const context = React.useContext(ThemeProviderContext)

  if (context === undefined) {
    throw new Error("useTheme must be used within a ThemeProvider")
  }

  return context
}

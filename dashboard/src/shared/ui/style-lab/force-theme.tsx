import { useEffect, type ReactNode } from "react"

/**
 * Pins the document's colour mode for as long as the story is mounted.
 *
 * The lab specimens that compare themes cannot rely on the Storybook theme
 * toolbar, because the toolbar is a global the reader sets once and forgets —
 * a specimen that depends on it shows one theme and *describes* the other.
 * This component applies the mode to the document root the same way the
 * app's `ThemeProvider` does (a `dark` class; its absence is the light
 * mode), and puts back whatever it found on unmount so the next story opens
 * in the mode the toolbar still says it is in.
 */
export interface ForceThemeProps {
  readonly mode: "dark" | "light"
  readonly children: ReactNode
}

export function ForceTheme({ mode, children }: ForceThemeProps) {
  useEffect(() => {
    const root = document.documentElement
    const hadDark = root.classList.contains("dark")
    const hadLight = root.classList.contains("light")
    root.classList.remove("light", "dark")
    root.classList.add(mode)
    return () => {
      root.classList.remove("light", "dark")
      if (hadDark) {
        root.classList.add("dark")
      }
      if (hadLight) {
        root.classList.add("light")
      }
    }
  }, [mode])

  return <>{children}</>
}

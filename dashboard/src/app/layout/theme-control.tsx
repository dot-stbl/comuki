import { Check, Monitor, Moon, Sun } from "lucide-react"
import {
  Button as AriaButton,
  Menu,
  MenuItem,
  MenuTrigger,
  Popover,
} from "react-aria-components"

import { useTheme } from "@/app/theme-provider"

import styles from "./theme-control.module.css"

const THEMES = [
  { id: "light", label: "Light", icon: Sun },
  { id: "dark", label: "Dark", icon: Moon },
  { id: "system", label: "System", icon: Monitor },
] as const

type ThemeId = (typeof THEMES)[number]["id"]

function isThemeId(value: unknown): value is ThemeId {
  return THEMES.some((entry) => entry.id === value)
}

/**
 * Appearance, in the chrome where it belongs.
 *
 * Three states rather than two, and the third is the honest default: "system"
 * is not a theme, it is the absence of an override, and a two-way toggle has no
 * way to say that — it silently pins whatever the machine happened to be when
 * the switch was first touched.
 *
 * The trigger wears the *current* mode's glyph, so the bar answers "what am I
 * in" without being opened.
 */
export function ThemeControl() {
  const { theme, setTheme } = useTheme()
  const current = THEMES.find((entry) => entry.id === theme) ?? THEMES[1]
  const Glyph = current.icon

  return (
    <MenuTrigger>
      <AriaButton
        className={styles.trigger}
        data-test="theme-control"
        aria-label={`Appearance — ${current.label}`}
      >
        <Glyph aria-hidden="true" className={styles.glyph} />
      </AriaButton>

      <Popover className={styles.popover} placement="bottom end">
        <Menu
          className={styles.menu}
          aria-label="Appearance"
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[theme]}
          onSelectionChange={(keys) => {
            if (keys === "all") {
              return
            }
            const next = [...keys][0]
            if (isThemeId(next)) {
              setTheme(next)
            }
          }}
        >
          {THEMES.map((entry) => (
            <MenuItem
              key={entry.id}
              id={entry.id}
              textValue={entry.label}
              className={styles.item}
            >
              <Check aria-hidden="true" className={styles.check} />
              <span className={styles.itemLabel}>{entry.label}</span>
            </MenuItem>
          ))}
        </Menu>
      </Popover>
    </MenuTrigger>
  )
}

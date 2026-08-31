import { Check, Palette } from "lucide-react"
import type { CSSProperties } from "react"
import {
  Button as AriaButton,
  Menu,
  MenuItem,
  MenuTrigger,
  Popover,
} from "react-aria-components"

import { useTheme } from "@/app/theme-provider"
import { Tooltip } from "@/shared/ui"

import { previewToken } from "./theme-css"
import { findTheme } from "./themes"
import styles from "./theme-picker.module.css"

/* Every swatch is drawn from the theme it stands for, not from the theme that
   happens to be showing — otherwise six rows would be six identical chips. The
   values arrive as `--preview-*` custom properties published by `themes.css`,
   so this component still holds no colour of its own. They follow the *mode*,
   so the row previews the palette in the room the person is standing in. */
function swatchStyle(id: string): CSSProperties {
  return {
    "--sw-floor": `var(${previewToken(id, "floor")})`,
    "--sw-rail": `var(${previewToken(id, "rail")})`,
    "--sw-rule": `var(${previewToken(id, "rule")})`,
    "--sw-text": `var(${previewToken(id, "text")})`,
    "--sw-accent": `var(${previewToken(id, "accent")})`,
    "--sw-running": `var(${previewToken(id, "running")})`,
    "--sw-failed": `var(${previewToken(id, "failed")})`,
  } as CSSProperties
}

/**
 * The palette axis, in the chrome beside the mode control.
 *
 * A theme and a mode are different questions — "which palette" and "which
 * room" — so they get two controls rather than one list of twelve. This one
 * changes nothing about light and dark; whatever mode is showing stays showing,
 * repainted.
 *
 * Each row carries the palette's own floor, hairline, accent and two statuses,
 * because the name of a theme tells a person nothing and a strip of its actual
 * colours tells them everything. The current row is marked with a shape rather
 * than a hue, the same way `ThemeControl` marks its own.
 */
export function ThemePicker() {
  const { themeId, setThemeId, themes } = useTheme()
  const current = findTheme(themeId) ?? themes[0]

  if (!current) {
    return null
  }

  return (
    <MenuTrigger>
      <AriaButton
        className={styles.trigger}
        data-test="theme-picker"
        aria-label={`Palette — ${current.name}`}
      >
        <Palette aria-hidden="true" className={styles.glyph} />
      </AriaButton>

      <Popover className={styles.popover} placement="bottom end">
        <Menu
          className={styles.menu}
          aria-label="Palette"
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[current.id]}
          onSelectionChange={(keys) => {
            if (keys === "all") {
              return
            }
            const next = [...keys][0]
            if (typeof next === "string") {
              setThemeId(next)
            }
          }}
        >
          {themes.map((theme) => (
            <Tooltip key={theme.id} content={theme.name} placement="bottom">
              <MenuItem
                id={theme.id}
                textValue={theme.name}
                aria-label={theme.name}
                className={styles.card}
                style={swatchStyle(theme.id)}
              >
                {/* The card is the whole reading. A palette's name tells a
                    person nothing they can act on; five bands of the palette
                    itself tell them everything, and a grid of them can be
                    compared at a glance in a way a column of labelled rows
                    never could. The name survives as the accessible name and
                    in the tooltip, so nothing is lost — it is only stopped
                    from crowding out the thing being chosen. */}
                <span className={styles.bandFloor} />
                <span className={styles.bandRail} />
                <span className={styles.bandAccent} />
                <span className={styles.bandRunning} />
                <span className={styles.bandFailed} />
                <Check aria-hidden="true" className={styles.check} />
              </MenuItem>
            </Tooltip>
          ))}
        </Menu>
      </Popover>
    </MenuTrigger>
  )
}

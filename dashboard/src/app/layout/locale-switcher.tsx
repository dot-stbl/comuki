import { useState } from "react"
import { Check, Languages } from "lucide-react"
import { useTranslation } from "react-i18next"
import {
  Button as AriaButton,
  Menu,
  MenuItem,
  MenuTrigger,
  Popover,
} from "react-aria-components"

import { activateLocale, LOCALES } from "@/shared/i18n"

import styles from "./theme-control.module.css"

/**
 * The board's language, beside the appearance it is read in.
 *
 * A dropdown rather than a segmented control because the shipped set is
 * eight and growing (D12): the trigger wears one glyph — the endonym is
 * the option's own name, not the trigger's — and every option is named by
 * its native endonym straight from the `LOCALES` registry, because a
 * person switches languages while not yet reading the one on screen.
 *
 * Selecting a non-default locale awaits that locale's lazy chunk before
 * switching; the trigger marks itself busy for that one beat, and the
 * switch re-renders in place — react-i18next re-renders on language
 * change by design, no reload.
 */
export function LocaleSwitcher() {
  const { t, i18n } = useTranslation("shell")
  const [pending, setPending] = useState(false)

  const current =
    LOCALES.find((entry) => entry.code === i18n.language) ?? LOCALES[0]

  function select(code: string): void {
    if (code === i18n.language || pending) {
      return
    }
    setPending(true)
    void activateLocale(code).finally(() => {
      setPending(false)
    })
  }

  return (
    <MenuTrigger>
      <AriaButton
        className={styles.trigger}
        data-test="locale-control"
        data-pending={pending ? "" : undefined}
        aria-busy={pending}
        aria-label={`${t("locale.label")} — ${current.endonym}`}
      >
        <Languages aria-hidden="true" className={styles.glyph} />
      </AriaButton>

      <Popover className={styles.popover} placement="bottom end">
        <Menu
          className={styles.menu}
          aria-label={t("locale.label")}
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[i18n.language]}
          onSelectionChange={(keys) => {
            if (keys === "all") {
              return
            }
            const next = [...keys][0]
            if (typeof next === "string") {
              select(next)
            }
          }}
        >
          {LOCALES.map((entry) => (
            <MenuItem
              key={entry.code}
              id={entry.code}
              textValue={entry.endonym}
              className={styles.item}
              data-test={`locale-option-${entry.code}`}
            >
              <Check aria-hidden="true" className={styles.check} />
              <span className={styles.itemLabel}>{entry.endonym}</span>
            </MenuItem>
          ))}
        </Menu>
      </Popover>
    </MenuTrigger>
  )
}

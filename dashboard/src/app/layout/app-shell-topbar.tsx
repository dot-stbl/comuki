import { Plus } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { ThemeControl } from "@/app/layout/theme-control"
import { ThemePicker } from "@/app/theme"
import { GlobalSearch } from "@/app/search"
import { env } from "@/shared/config/env"
import { can, useSession } from "@/shared/session"
import { BrandIcon, buttonClass, ComukiMark, Tooltip } from "@/shared/ui"

import styles from "./app-shell-topbar.module.css"

export function AppShellTopbar() {
  const session = useSession()

  return (
    <header className={styles.bar}>
      {/* The mark is the entire brand lockup — there is no wordmark, on the
          board or in the bar. A container is a shape an operator learns in one
          shift, and a word beside it was saying a second time what the shape
          already said. It is the one link that always goes home. */}
      <Link to="/" aria-label="Comuki — home" className={styles.brand}>
        <ComukiMark className={styles.mark} />
      </Link>

      <span className={styles.spacer} />

      {/* Everything a person aims at rather than reads, gathered at the one
          edge and spaced as a group. With the wordmark gone the bar is a mark
          at one end and a set of controls at the other, and the controls have
          to read as one set rather than as four things that drifted apart. */}
      <div className={styles.controls}>
        <GlobalSearch />

        {/* Hidden rather than explained, and the two halves of the access
            rule are what decide it: this looks like an action but it is a
            link, and what it links to is a screen the same role cannot open.
            Left visible, the loudest control in the chrome would walk every
            viewer into a wall. Actions explain themselves where they act —
            this one acts on the Inbox screen, and that screen's Create button
            carries the sentence. */}
        {can(session, "inbox.take") ? (
          <Link to="/tasks" data-test="new-run" className={buttonClass()}>
            <Plus aria-hidden="true" />
            New run
          </Link>
        ) : null}

        {/* Appearance is a property of the machine, not of the person: the
            same account is dark on the big screen at the desk and light on the
            laptop in a bright room. It belongs beside the other chrome
            controls, not inside an account menu that also holds identity and
            sign-out. */}
        {/* Palette before mode: the palette is chosen once and left alone,
            the mode gets flipped as the room changes. The slower-changing
            control reads first. The console has no place here any more: its
            door is the floating dock trigger, and one container gets one
            door. */}
        <ThemePicker />
        <ThemeControl />

        {/* The source, at the far edge. It renders only when there is
            somewhere for it to go — `repoUrl` is `null` for a deployment that
            has taken the address out of its environment, and a mark linking
            nowhere is worse than no mark. The mark takes its size from the
            control it sits in, and the accessible name is a sentence rather
            than a glyph: "github" alone does not say what pressing it does,
            and it does not say that it leaves the product either. */}
        {env.repoUrl ? (
          <Tooltip content="github">
            <a
              href={env.repoUrl}
              target="_blank"
              rel="noreferrer noopener"
              data-test="repo-link"
              aria-label="Comuki on GitHub — opens in a new tab"
              className={buttonClass({ variant: "ghost", size: "icon" })}
            >
              <BrandIcon brand="github" label={null} />
            </a>
          </Tooltip>
        ) : null}
      </div>
    </header>
  )
}

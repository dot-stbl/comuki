import { KeyRound, LogOut, Settings, UserRound } from "lucide-react"
import { useNavigate } from "@tanstack/react-router"
import {
  Button as AriaButton,
  Menu,
  MenuItem,
  MenuTrigger,
  Popover,
} from "react-aria-components"

import { useLogoutMutation } from "@/domains/identity/api/mutations"
import { can, useSession } from "@/shared/session"

import styles from "./rail-account.module.css"

export interface RailAccountProps {
  /** Icon-only rail: the name and address have nowhere to go. */
  collapsed?: boolean
}

/**
 * Who is signed in, at the floor of the rail.
 *
 * Not in the topbar, and not sharing a menu with appearance. Two different
 * questions were being answered by one control: *who am I and how do I leave*
 * is about the account, *how should this look* is about the machine in front
 * of it. They have nothing to do with each other, they are changed on wildly
 * different clocks, and putting them together made the rare one hard to find
 * behind the rarer one.
 *
 * The menu opens upward — there is nothing below it — and the trigger keeps its
 * footprint when the rail collapses so the floor of the rail does not shift.
 *
 * Settings and API keys live here rather than as rail destinations: they are
 * about *this person* (and the keys they hold), visited on a different clock
 * from the duty screens above.
 */
export function RailAccount({ collapsed = false }: RailAccountProps) {
  const navigate = useNavigate()
  const session = useSession()
  const { user } = session
  const initial = user.name.trim().charAt(0).toUpperCase() || "?"
  const logout = useLogoutMutation()

  return (
    <MenuTrigger>
      <AriaButton
        className={styles.trigger}
        data-test="rail-account"
        aria-label={`Account — ${user.name}`}
      >
        <span className={styles.avatar} aria-hidden="true" data-initial={initial}>
          <span className={styles.avatarMark}>{initial}</span>
        </span>
        <span className={styles.identity}>
          <span className={styles.name}>{user.name}</span>
          <span className={styles.email}>{user.email}</span>
        </span>
      </AriaButton>

      <Popover
        className={styles.popover}
        placement={collapsed ? "right bottom" : "top start"}
      >
        {/* Static, beside the collection rather than inside it: a menu item
            that cannot be chosen is a trap for a keyboard. The trigger's label
            carries the same name for assistive technology. */}
        <div className={styles.header}>
          <span className={styles.headerAvatar} aria-hidden="true">
            <span className={styles.avatarMark}>{initial}</span>
          </span>
          <span className={styles.headerText}>
            <span className={styles.headerName}>{user.name}</span>
            <span className={styles.headerEmail}>{user.email}</span>
          </span>
        </div>

        <Menu className={styles.menu} aria-label="Account">
          <MenuItem
            className={styles.item}
            onAction={() => void navigate({ to: "/settings" })}
            isDisabled={!can(session, "settings.live")}
            textValue="Settings"
          >
            <Settings aria-hidden="true" className={styles.itemIcon} />
            <span className={styles.itemLabel}>Settings</span>
          </MenuItem>

          <MenuItem
            className={styles.item}
            onAction={() => void navigate({ to: "/identity" })}
            isDisabled={!can(session, "identity.manage")}
            textValue="API keys"
          >
            <KeyRound aria-hidden="true" className={styles.itemIcon} />
            <span className={styles.itemLabel}>API keys</span>
          </MenuItem>

          <MenuItem
            className={styles.item}
            onAction={() => void navigate({ to: "/identity" })}
            isDisabled={!can(session, "identity.manage")}
            textValue="Profile"
          >
            <UserRound aria-hidden="true" className={styles.itemIcon} />
            <span className={styles.itemLabel}>Profile</span>
          </MenuItem>

          {/* Clears the session first, *then* lands. The order is the whole
              point: navigating alone left the shell holding a signed-in shift
              behind a sign-in screen, so leaving and arriving looked identical
              and the guard had nothing to catch. `reason=signed-out` is what
              turns the landing into a quiet confirmation rather than an alarm
              about a session that expired — this one did not expire, they
              closed it.

              `useLogoutMutation` reads `env.useMock` and routes to the seed
              store (mock mode) or `POST /api/v1/auth/logout` (real mode) —
              one hook, two backends. We `await mutateAsync` so the navigate
              only fires after the seed is cleared (or the cookie is dropped
              in real mode); the same call would have left the shell holding
              a signed-in shift behind a sign-in screen, and the guard would
              have had nothing to catch. The mutation invalidates `me` and
              `projects` on success — the URL change is what tells the guard
              to render the login screen rather than the half-cleared shell. */}
          <MenuItem
            className={styles.item}
            onAction={() => {
              void logout
                .mutateAsync()
                .then(() => {
                  void navigate({
                    to: "/login",
                    search: { reason: "signed-out" },
                    replace: true,
                  })
                })
                .catch(() => {
                  /* A refused logout leaves the operator on the rail — the
                     shell still holds a signed-in session, so navigating to
                     /login would 401 them back to the same screen. */
                })
            }}
            textValue="Sign out"
          >
            <LogOut aria-hidden="true" className={styles.itemIcon} />
            <span className={styles.itemLabel}>Sign out</span>
          </MenuItem>
        </Menu>
      </Popover>
    </MenuTrigger>
  )
}
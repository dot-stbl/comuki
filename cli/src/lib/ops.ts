/**
 * Pure ops-pack B formatters for the chat REPL (`/open`): dashboard
 * URL + OSC-8 hyperlink. No React, no I/O — opening the OS browser is
 * the caller's job (`openDashboardUrl` below is the one side-effect,
 * injectable).
 *
 * Dashboard routes (see `dashboard/src/routeTree.gen.ts`): `/chat` is
 * the console (no `/chat/{id}` — sessions live in client state, not
 * the URL); `/runs` is the run ledger. `/open` therefore lands on
 * `/chat` when a session is live, else `/runs`.
 */
import { spawn } from "node:child_process"
import { PENDING_PREFIX } from "./sessions"
import { linkSequence } from "./term"
import { colors, paint, symbols } from "../theme"

/** Dashboard path `/open` lands on when a live (server) session is active. */
export const DASHBOARD_CHAT_PATH = "/chat"

/** Dashboard path `/open` lands on with no live session. */
export const DASHBOARD_RUNS_PATH = "/runs"

/**
 * The dashboard URL `/open` would open. Live sessions (server UUID)
 * go to `/chat` — there is no `/chat/{id}` route; pending `local-…`
 * tabs have no server counterpart, so they fall through to `/runs`
 * like a session-less REPL.
 */
export function dashboardOpenUrl(
  hostUrl: string,
  sessionId: string | undefined
): string {
  const base = hostUrl.replace(/\/+$/, "")
  return sessionId !== undefined &&
    sessionId.length > 0 &&
    !sessionId.startsWith(PENDING_PREFIX)
    ? `${base}${DASHBOARD_CHAT_PATH}`
    : `${base}${DASHBOARD_RUNS_PATH}`
}

/** One OSC-8 hyperlink wrapping `url` — terminals without OSC-8 show the bare URL. */
export function dashboardLinkLine(url: string): string {
  return `  ${paint(symbols.event, colors.dim)} ${linkSequence(url, url)}`
}

/**
 * The `/open` transcript block: the OSC-8 link, plus a dim note that
 * the console has no per-session URL when we fell through to `/runs`
 * (or landed on `/chat` because `/chat/{id}` is not a route).
 */
export function openPanelLines(
  url: string,
  opened: boolean,
  reason: "chat" | "runs"
): string[] {
  return [
    `  ${paint(symbols.event, colors.dim)} ${paint("open", colors.muted)}`,
    dashboardLinkLine(url),
    reason === "chat"
      ? paint(
          `  ${symbols.bullet} dashboard has no /chat/{id} — opening the console`,
          colors.faint
        )
      : paint(
          `  ${symbols.bullet} no live session — opening the runs ledger`,
          colors.faint
        ),
    opened
      ? paint(`  ${symbols.checkmark} opened in the system browser`, colors.ok)
      : paint(
          `  ${symbols.bullet} no system opener — copy the url above`,
          colors.faint
        ),
  ]
}

/** The OS-specific opener binary: `start` / `xdg-open` / `open`. */
export function openerCommand(
  platform: NodeJS.Platform = process.platform
): string | null {
  if (platform === "win32") {
    return "cmd"
  }
  if (platform === "darwin") {
    return "open"
  }
  if (platform === "linux") {
    return "xdg-open"
  }
  return null
}

/**
 * Tries to open `url` with the platform opener. Resolves `true` when
 * the child spawned (the OS still may fail later — we don't wait);
 * `false` when there is no opener or spawn itself threw. `spawnImpl`
 * is the test seam.
 */
export function openDashboardUrl(
  url: string,
  options: {
    platform?: NodeJS.Platform
    spawnImpl?: typeof spawn
  } = {}
): boolean {
  const platform = options.platform ?? process.platform
  const command = openerCommand(platform)
  if (command === null) {
    return false
  }
  const spawnImpl = options.spawnImpl ?? spawn
  try {
    if (platform === "win32") {
      // `start` is a cmd builtin — `spawn("start", …)` looks for
      // start.exe and misses. Empty title arg so a quoted URL is not
      // taken as the window title.
      spawnImpl(command, ["/c", "start", "", url], {
        detached: true,
        stdio: "ignore",
        windowsHide: true,
      }).unref()
      return true
    }
    spawnImpl(command, [url], { detached: true, stdio: "ignore" }).unref()
    return true
  } catch {
    return false
  }
}

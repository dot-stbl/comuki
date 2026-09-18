/**
 * Entry + command routing: `comuki` (default = the multi-session chat
 * REPL), `status`, `runs list`, `login`, `whoami`, `config [show]`,
 * `setup` (first-run wizard), `completion <shell>`, `doctor`.
 * Each command renders its own Ink app (`config show`, `completion`
 * and `doctor` are the plain-console exceptions); the process exits when the app
 * unmounts (Ink's `exitOnCtrlC` covers ctrl+c). The removed `chat`
 * subcommand is unknown on purpose — bare `comuki` is the REPL.
 */
import { render } from "ink"
import React from "react"
import yargs from "yargs"
import { hideBin } from "yargs/helpers"
import { ChatApp } from "./commands/chat"
import { LoginApp } from "./commands/login"
import { RunsApp } from "./commands/runs"
import { StatusApp } from "./commands/status"
import { printConfigShow } from "./commands/config"
import { printCompletion } from "./commands/completion"
import { printDoctor } from "./commands/doctor"
import { SetupApp } from "./commands/setup"
import { ComukiClient } from "./lib/client"
import {
  readConfigFile,
  resolveConfig,
  type ResolvedConfig,
} from "./lib/config"
import { resolveCommand } from "./lib/commands"
import { CLI_VERSION } from "./components/StatusLine"
import { formatWhoamiLines, whoFromError, whoFromMe } from "./lib/auth"
import {
  DEFAULT_THEME_CHOICE,
  THEME_CHOICE_IDS,
  colors,
  isThemeChoice,
  resolveTheme,
} from "./theme"

interface GlobalOptions {
  url?: string
  apiKey?: string
  project?: string
  theme?: string
}

async function loadConfig(overrides: GlobalOptions): Promise<ResolvedConfig> {
  return resolveConfig(process.env, await readConfigFile(), overrides)
}

async function main(): Promise<void> {
  const argv = await yargs(hideBin(process.argv))
    .scriptName("comuki")
    .version(CLI_VERSION)
    .option("url", { type: "string", describe: "Comuki host URL" })
    .option("api-key", { type: "string", describe: "API key (ck_…)" })
    .option("project", { type: "string", describe: "project id, slug or name" })
    .option("theme", {
      type: "string",
      describe: "terminal theme: <theme>-<dark|light>",
    })
    .command("status", "platform snapshot")
    .command("runs [list]", "run ledger", (y) =>
      y
        .positional("list", { type: "string", default: "list" })
        .option("page", {
          type: "number",
          default: 1,
          describe: "1-based page",
        })
        .option("pageSize", {
          type: "number",
          default: 20,
          describe: "rows per page (max 100)",
        })
        .option("filter", {
          type: "string",
          describe: "filter DSL, e.g. status==queued",
        })
    )
    .command("login", "email+password → session cookie")
    .command("whoami", "current subject, roles and permissions")
    .command(
      "config [show]",
      "resolved configuration (secrets masked)",
      (y) =>
        y.positional("show", {
          type: "string",
          default: "show",
          describe: "print the resolved configuration",
        })
    )
    .command("setup", "interactive first-run wizard")
    .command(
      "completion [shell]",
      "print a completion script to stdout",
      (y) =>
        y.positional("shell", {
          type: "string",
          describe: "target shell: pwsh or bash",
        })
    )
    .command("doctor", "check host, auth, config and theme")
    .demandCommand(0, 0) // no command → the REPL
    .strict()
    .parse()

  const overrides: GlobalOptions = {
    url: argv.url,
    apiKey: argv["api-key"],
    project: argv.project,
    theme: argv.theme,
  }
  const command = resolveCommand(argv._)

  if (command === "login") {
    render(<LoginApp url={overrides.url} />, { exitOnCtrlC: true })
    return
  }

  // Before loadConfig: config show reports a missing url as
  // `(source: none)` instead of letting resolveConfig throw.
  if (command === "config") {
    await printConfigShow(overrides)
    return
  }

  // Also before loadConfig — both must work on a machine with no
  // config.json yet (setup is what creates it; completion is static).
  if (command === "setup") {
    render(<SetupApp />, { exitOnCtrlC: true })
    return
  }
  if (command === "completion") {
    printCompletion(String(argv.shell ?? ""))
    return
  }
  // Before loadConfig: a missing url is a failed check, not a throw.
  if (command === "doctor") {
    process.exitCode = await printDoctor(overrides)
    return
  }

  const config = await loadConfig(overrides)

  // Theme: a bad --theme flag is a hard error (the user just typed it);
  // a stale config.json value falls back to the default with a note.
  if (overrides.theme !== undefined && !isThemeChoice(overrides.theme)) {
    console.error(
      `${colors.error}unknown theme: ${overrides.theme}${colors.reset}`
    )
    console.error(
      `${colors.faint}available: ${THEME_CHOICE_IDS.join(", ")}${colors.reset}`
    )
    process.exitCode = 1
    return
  }
  if (config.theme !== undefined && !isThemeChoice(config.theme)) {
    console.error(
      `${colors.faint}unknown theme in config.json: ${config.theme} — using ${DEFAULT_THEME_CHOICE}${colors.reset}`
    )
  }
  resolveTheme(config.theme)

  if (command === "repl") {
    render(<ChatApp config={config} project={overrides.project} />, {
      exitOnCtrlC: true,
    })
    return
  }
  if (command === "status") {
    render(<StatusApp config={config} />, { exitOnCtrlC: true })
    return
  }
  if (command === "runs") {
    render(
      <RunsApp
        config={config}
        page={argv.page as number}
        pageSize={argv.pageSize as number}
        filter={argv.filter as string | undefined}
      />,
      { exitOnCtrlC: true }
    )
    return
  }
  if (command === "whoami") {
    const client = new ComukiClient(config)
    try {
      const me = await client.me()
      for (const line of formatWhoamiLines(whoFromMe(me), me)) {
        console.log(line)
      }
    } catch (error) {
      for (const line of formatWhoamiLines(whoFromError(error))) {
        console.log(line)
      }
    }
    return
  }

  console.error(
    `${colors.error}unknown command: ${String(argv._[0])}${colors.reset}`
  )
  process.exitCode = 1
}

void main()

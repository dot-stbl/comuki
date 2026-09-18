/**
 * Entry + command routing: `comuki` (default = the multi-session chat
 * REPL), `status`, `runs list`, `login`, `whoami`, `config [show]`,
 * `setup` (first-run wizard), `completion <shell>`, `archive`.
 * `-m` / piped stdin skip the REPL (`commands/oneshot.ts`).
 * Each command renders its own Ink app (`config show`, `completion`,
 * `archive`, oneshot, and `--json` printers are the plain-console
 * exceptions); the process exits when the app unmounts (Ink's
 * `exitOnCtrlC` covers ctrl+c). The removed `chat` subcommand is
 * unknown on purpose — bare `comuki` is the REPL.
 */
import { render } from "ink"
import React from "react"
import yargs from "yargs"
import { hideBin } from "yargs/helpers"
import { ChatApp, describeError } from "./commands/chat"
import { LoginApp } from "./commands/login"
import { printRunsJson, RunsApp } from "./commands/runs"
import { printStatusJson, StatusApp } from "./commands/status"
import { printConfigShow } from "./commands/config"
import { printCompletion } from "./commands/completion"
import { SetupApp } from "./commands/setup"
import { printArchiveList } from "./commands/archive"
import {
  ONESHOT_TIMEOUT_MS,
  readStdinText,
  resolveOneshotMode,
  runOneshot,
} from "./commands/oneshot"
import { ComukiClient } from "./lib/client"
import {
  readConfigFile,
  resolveConfig,
  type ResolvedConfig,
} from "./lib/config"
import { resolveCommand } from "./lib/commands"
import { CLI_VERSION } from "./components/StatusLine"
import { whoAmI } from "./lib/auth"
import { mapWhoamiJson, printJson } from "./lib/jsonout"
import { stripMarkdownToPlain } from "./lib/markdown"
import {
  DEFAULT_THEME_CHOICE,
  THEME_CHOICE_IDS,
  colors,
  isThemeChoice,
  resolveTheme,
  symbols,
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
    .option("json", {
      type: "boolean",
      default: false,
      describe: "machine-readable JSON (status, runs, whoami)",
    })
    .option("raw", {
      type: "boolean",
      default: false,
      describe: "print the one-shot reply as markdown, not plain text",
    })
    .option("message", {
      alias: "m",
      type: "string",
      describe: "one-shot prompt (skips the REPL)",
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
    .command("archive", "list archived session transcripts")
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
  const json = argv.json === true
  const raw = argv.raw === true

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
  if (command === "archive") {
    await printArchiveList()
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
    const oneshot = resolveOneshotMode(
      argv.message as string | undefined,
      process.stdin.isTTY
    )
    if (oneshot !== "repl") {
      const message =
        oneshot === "flag"
          ? String(argv.message ?? "")
          : await readStdinText()
      if (message.trim().length === 0) {
        console.error(
          `${colors.error}empty message — pass -m <text> or pipe stdin${colors.reset}`
        )
        process.exitCode = 1
        return
      }
      const controller = new AbortController()
      const timer = setTimeout(() => controller.abort(), ONESHOT_TIMEOUT_MS)
      try {
        const result = await runOneshot({
          client: new ComukiClient(config, { signal: controller.signal }),
          message,
          projectId: config.defaultProject,
          signal: controller.signal,
        })
        const body = raw ? result.reply : stripMarkdownToPlain(result.reply)
        process.stdout.write(body.endsWith("\n") ? body : `${body}\n`)
      } catch (error) {
        console.error(
          `${colors.error}${symbols.cross} ${describeError(error)}${colors.reset}`
        )
        process.exitCode = 1
      } finally {
        clearTimeout(timer)
      }
      return
    }
    render(<ChatApp config={config} project={overrides.project} />, {
      exitOnCtrlC: true,
    })
    return
  }
  if (command === "status") {
    if (json) {
      await printStatusJson(config)
      return
    }
    render(<StatusApp config={config} />, { exitOnCtrlC: true })
    return
  }
  if (command === "runs") {
    if (json) {
      await printRunsJson(
        config,
        argv.page as number,
        argv.pageSize as number,
        argv.filter as string | undefined
      )
      return
    }
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
    const who = await whoAmI(client)
    let me = null
    try {
      me = await client.me()
    } catch {
      // whoAmI already reported the failure shape.
    }
    if (json) {
      printJson(mapWhoamiJson(who, me))
      return
    }
    console.log(
      `${colors.accent}  ${who.kind}${colors.reset} ${symbols.bullet} ${who.label}`
    )
    if (me && me.roles.length > 0) {
      console.log(`${colors.dim}  roles: ${me.roles.join(", ")}${colors.reset}`)
    }
    if (me && me.permissions.length > 0) {
      console.log(
        `${colors.dim}  permissions: ${me.permissions.join(", ")}${colors.reset}`
      )
    }
    return
  }

  console.error(
    `${colors.error}unknown command: ${String(argv._[0])}${colors.reset}`
  )
  process.exitCode = 1
}

void main()

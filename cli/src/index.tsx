/**
 * Entry + command routing: `comuki` (default = the multi-session chat
 * REPL), `status`, `runs list`, `login`, `whoami`, `config [show]`.
 * Each command renders its own Ink app (`config show` is the
 * plain-console exception); the process exits when the app unmounts
 * (Ink's `exitOnCtrlC` covers ctrl+c). The removed `chat` subcommand is
 * unknown on purpose — bare `comuki` is the REPL.
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
import { ComukiClient } from "./lib/client"
import {
  readConfigFile,
  resolveConfig,
  type ResolvedConfig,
} from "./lib/config"
import { resolveCommand } from "./lib/commands"
import { CLI_VERSION } from "./components/StatusLine"
import { whoAmI } from "./lib/auth"
import { colors, symbols } from "./theme"

interface GlobalOptions {
  url?: string
  apiKey?: string
  project?: string
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
    .demandCommand(0, 0) // no command → the REPL
    .strict()
    .parse()

  const overrides: GlobalOptions = {
    url: argv.url,
    apiKey: argv["api-key"],
    project: argv.project,
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

  const config = await loadConfig(overrides)

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
    const who = await whoAmI(client)
    console.log(
      `${colors.accent}  ${who.kind}${colors.reset} ${symbols.bullet} ${who.label}`
    )
    try {
      const me = await client.me()
      if (me.roles.length > 0) {
        console.log(
          `${colors.dim}  roles: ${me.roles.join(", ")}${colors.reset}`
        )
      }
      if (me.permissions.length > 0) {
        console.log(
          `${colors.dim}  permissions: ${me.permissions.join(", ")}${colors.reset}`
        )
      }
    } catch {
      // whoAmI already reported the failure shape.
    }
    return
  }

  console.error(
    `${colors.error}unknown command: ${String(argv._[0])}${colors.reset}`
  )
  process.exitCode = 1
}

void main()

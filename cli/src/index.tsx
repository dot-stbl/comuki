/**
 * Entry + command routing: `comuki` (default = the multi-session chat
 * REPL), `status`, `runs list`, `login`, `whoami`, `config [show]`,
 * `setup`, `completion`, `doctor`, `archive`. `-m` / piped stdin skip
 * the REPL. Plain-console: config show, completion, doctor, archive, oneshot, --json.
 */
import { render } from "ink"
import React from "react"
import yargs from "yargs"
import { hideBin } from "yargs/helpers"
import { ChatApp, describeError } from "./commands/chat"
import { runOpentuiRepl } from "./commands/opentui"
import { LoginApp } from "./commands/login"
import { printRunsJson, RunsApp } from "./commands/runs"
import { printStatusJson, StatusApp } from "./commands/status"
import { printConfigShow } from "./commands/config"
import { printCompletion } from "./commands/completion"
import { printDoctor } from "./commands/doctor"
import { SetupApp } from "./commands/setup"
import { printArchiveList } from "./commands/archive"
import {
  ONESHOT_TIMEOUT_MS,
  readStdinText,
  resolveOneshotMode,
  runOneshot,
} from "./commands/oneshot"
import { ComukiClient } from "./lib/client"
import { JsonOutput } from "./machine/output"
import { machineErrorFrom } from "./machine/mapping"
import { runOneshotMachine } from "./machine/oneshot-machine"
import {
  createI18nFor,
  tr,
  DEFAULT_LOCALE,
} from "./locales"
import {
  configStore,
  resolveConfig,
  type ResolvedConfig,
} from "./lib/config"
import { resolveCommand } from "./lib/commands"
import { CLI_VERSION } from "./components/StatusLine"
import { formatWhoamiLines, whoAmI, whoFromError, whoFromMe } from "./lib/auth"
import { mapWhoamiJson } from "./lib/jsonout"
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
  return resolveConfig(process.env, await configStore.read(), overrides)
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
    .option("tui", {
      type: "string",
      describe: "REPL host: opentui (OpenTUI Core focus mode) or ink (default)",
    })
    .option("message", {
      alias: "m",
      type: "string",
      describe: "one-shot prompt (skips the REPL)",
    })
    .option("format", {
      type: "string",
      choices: ["text", "json", "ndjson"],
      default: "text",
      describe: "oneshot output format (with -m or piped stdin)",
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
  // Before loadConfig: a missing url is a failed check, not a throw.
  if (command === "doctor") {
    process.exitCode = await printDoctor(overrides)
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
      const format =
        argv.format !== "text"
          ? (argv.format as "json" | "ndjson")
          : json
            ? "json"
            : "text"
      if (format === "text") {
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
      const controller = new AbortController()
      const timer = setTimeout(() => controller.abort(), ONESHOT_TIMEOUT_MS)
      try {
        process.exitCode = await runOneshotMachine({
          client: new ComukiClient(config, { signal: controller.signal }),
          message,
          projectId: config.defaultProject,
          format,
          signal: controller.signal,
        })
      } finally {
        clearTimeout(timer)
      }
      return
    }
    // Opt-in OpenTUI Core host (issue #73). Anything but "opentui"/"ink"
    // is a hard error — the user just typed it.
    const tuiHost = argv.tui as string | undefined
    if (tuiHost !== undefined && tuiHost !== "ink") {
      if (tuiHost !== "opentui") {
        const i18n = await createI18nFor(DEFAULT_LOCALE)
        console.error(
          `${colors.error}${tr(i18n, "cli.unknownTuiHost")} ${tuiHost}${colors.reset}`
        )
        console.error(
          `${colors.faint}${tr(i18n, "cli.tuiHostsAvailable")}${colors.reset}`
        )
        process.exitCode = 1
        return
      }
      await runOpentuiRepl(config, overrides.project)
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
    if (json) {
      const out = new JsonOutput()
      out.start("whoami")
      try {
        const client = new ComukiClient(config)
        const who = await whoAmI(client)
        let me = null
        try {
          me = await client.me()
        } catch {
          // whoAmI already reported the failure shape.
        }
        out.complete({ ...mapWhoamiJson(who, me) })
      } catch (error) {
        out.fail(machineErrorFrom(error))
        process.exitCode = out.exitCode
      }
      return
    }
    const client = new ComukiClient(config)
    const who = await whoAmI(client)
    let me = null
    try {
      me = await client.me()
    } catch {
      // whoAmI already reported the failure shape.
    }
    if (me) {
      for (const line of formatWhoamiLines(whoFromMe(me), me)) {
        console.log(line)
      }
    } else {
      for (const line of formatWhoamiLines(whoFromError(new Error(who.label)))) {
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

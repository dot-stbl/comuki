import { describe, expect, it } from "bun:test"
import {
  COMPLETION_COMMANDS,
  COMPLETION_FLAGS,
  bashCompletion,
  completionScript,
  powershellCompletion,
} from "./completion"
import { THEME_CHOICE_IDS } from "../themes"

describe("powershellCompletion", () => {
  const script = powershellCompletion(
    COMPLETION_COMMANDS,
    COMPLETION_FLAGS,
    THEME_CHOICE_IDS
  )

  it("registers a native argument completer for comuki", () => {
    expect(script).toContain(
      "Register-ArgumentCompleter -CommandName 'comuki' -Native -ScriptBlock {"
    )
    expect(script).toContain("param($wordToComplete, $commandAst, $cursorPosition)")
    expect(script).toContain("System.Management.Automation.CompletionResult")
  })

  it("offers every subcommand by name", () => {
    for (const command of COMPLETION_COMMANDS) {
      expect(script).toContain(`'${command}'`)
    }
  })

  it("offers every flag and routes --theme to the theme list", () => {
    for (const flag of COMPLETION_FLAGS) {
      expect(script).toContain(`'${flag}'`)
    }
    expect(script).toContain("if ($line -match '--theme\\s+(\\S*)$') {")
    for (const theme of THEME_CHOICE_IDS) {
      expect(script).toContain(`'${theme}'`)
    }
  })

  it("is valid powershell boilerplate — comments, single-quoted lists, trailing newline", () => {
    expect(script.startsWith("# comuki completion (pwsh)")).toBe(true)
    expect(script).toMatch(/\$commands = @\('status'/)
    expect(script.endsWith("}\n")).toBe(true)
    // No stray double-quote imbalance in the generated block.
    expect(script.split('"').length - 1).toBe(0)
  })
})

describe("bashCompletion", () => {
  const script = bashCompletion(
    COMPLETION_COMMANDS,
    COMPLETION_FLAGS,
    THEME_CHOICE_IDS
  )

  it("registers the completion function via complete -F", () => {
    expect(script).toContain("_comuki_complete() {")
    expect(script).toContain("complete -F _comuki_complete comuki")
  })

  it("offers every subcommand and flag", () => {
    for (const command of COMPLETION_COMMANDS) {
      expect(script).toContain(command)
    }
    for (const flag of COMPLETION_FLAGS) {
      expect(script).toContain(flag)
    }
  })

  it("expands the theme list after --theme via compgen -W", () => {
    expect(script).toContain('if [ "$prev" = "--theme" ]; then')
    expect(script).toContain("compgen -W")
    for (const theme of THEME_CHOICE_IDS) {
      expect(script).toContain(theme)
    }
  })

  it("is valid bash boilerplate — reads COMP_WORDS, trailing newline", () => {
    expect(script).toContain("${COMP_WORDS[COMP_CWORD]}")
    expect(script).toContain("${COMP_WORDS[COMP_CWORD-1]}")
    expect(script.endsWith("complete -F _comuki_complete comuki\n")).toBe(true)
  })
})

describe("completionScript", () => {
  it("resolves pwsh and bash", () => {
    expect(completionScript("pwsh")).toContain("Register-ArgumentCompleter")
    expect(completionScript("bash")).toContain("complete -F")
  })

  it("rejects unknown and empty shells", () => {
    expect(completionScript("zsh")).toBeUndefined()
    expect(completionScript("")).toBeUndefined()
  })

  it("uses the injected theme list verbatim", () => {
    const script = completionScript("bash", ["x-dark", "x-light"])
    expect(script).toContain('themes="x-dark x-light"')
    expect(script).not.toContain("dichromat-dark")
  })
})

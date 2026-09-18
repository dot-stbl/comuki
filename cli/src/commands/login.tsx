/**
 * `comuki login` — email + password → session cookie stored in
 * `~/.comuki/config.json`. Two inline prompts; the password field masks
 * via ink-text-input's `mask`.
 */
import { Text, useApp } from "ink"
import TextInput from "ink-text-input"
import React, { useEffect, useState } from "react"
import { loginAndStore } from "../lib/auth"
import { describeError } from "./chat"
import { colors, palette, symbols } from "../theme"

export interface LoginCommandProps {
  readonly url: string | undefined
}

type Step = "email" | "password" | "working" | "done"

export function LoginApp({ url }: LoginCommandProps) {
  const { exit } = useApp()
  const [step, setStep] = useState<Step>("email")
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [result, setResult] = useState("")

  useEffect(() => {
    if (step === "done") {
      exit()
    }
  }, [step, exit])

  if (step === "done" || step === "working") {
    return (
      <Text>
        {"  "}
        {step === "working"
          ? `${colors.dim}... signing in${colors.reset}`
          : result}
      </Text>
    )
  }

  return step === "email" ? (
    <Text>
      <Text color={palette.brand}>{"  email  > "}</Text>
      <TextInput
        value={email}
        onChange={setEmail}
        onSubmit={(submitted) => {
          if (submitted.trim().length > 0) {
            setStep("password")
          }
        }}
      />
    </Text>
  ) : (
    <Text>
      <Text color={palette.brand}>{"  pass   > "}</Text>
      <TextInput
        value={password}
        onChange={setPassword}
        mask="*"
        onSubmit={(submitted) => {
          setStep("working")
          void (async () => {
            try {
              const success = await loginAndStore(url, email.trim(), submitted)
              setResult(
                `${colors.ok}${symbols.checkmark}${colors.reset} signed in as ${success.displayName} (${success.email}) — cookie stored in ~/.config/comuki/config.json`
              )
            } catch (error) {
              setResult(
                `${colors.error}${symbols.cross}${colors.reset} ${describeError(error)}`
              )
            }
            setStep("done")
          })()
        }}
      />
    </Text>
  )
}

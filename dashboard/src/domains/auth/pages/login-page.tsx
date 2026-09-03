import { useId, useState, type FormEvent } from "react"
import { Check, CircleAlert, TimerOff } from "lucide-react"

import { useAuthState } from "@/domains/auth/api/auth"
import {
  landingFor,
  signInTarget,
  type LoginReason,
} from "@/domains/auth/model/landing"
import { env } from "@/shared/config/env"
import { signInMock, signInWithOidcMock } from "@/shared/api/mock/auth.store"
import { cn } from "@/shared/lib/utils"
import { Button, ComukiMark } from "@/shared/ui"

import styles from "./login-page.module.css"

export interface LoginPageProps {
  /** Which of the three arrivals this is. Absent is the cold one. */
  reason?: LoginReason
  /** The path the operator was refused, to be resumed after signing in. */
  redirect?: string
  /**
   * Where to go once the session is set.
   *
   * Passed in rather than reached for with `useNavigate` so the screen renders
   * — and can be photographed and asserted on — without a router underneath it.
   * The route wires it to the real navigation.
   */
  onSignedIn?: (target: string) => void
}

/**
 * The gate.
 *
 * The one route with no rail and no topbar. That is not a layout preference: a
 * rail is an offer of navigation, and this screen is showing itself to someone
 * the product has not identified yet. There is nothing here to navigate to.
 *
 * One screen, three arrivals (§1.3, §16). The form never changes between them
 * — same fields, same mark, same provider button — so what changes is one
 * sentence, driven by a search param rather than by three components that would
 * immediately start drifting apart.
 */
export function LoginPage({ reason, redirect, onSignedIn }: LoginPageProps) {
  const { oidc } = useAuthState()
  const landing = landingFor(reason)

  const identityId = useId()
  const passwordId = useId()
  const failureId = useId()

  const [identity, setIdentity] = useState("")
  const [password, setPassword] = useState("")
  const [failure, setFailure] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const incomplete = identity.trim() === "" || password === ""

  const land = () => {
    onSignedIn?.(signInTarget(redirect))
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (pending || incomplete) {
      return
    }

    setPending(true)
    setFailure(null)
    // Mock-first product: the password form goes through the mock store
    // until a real POST /api/v1/auth/sign-in is wired in. The mock accepts
    // any credentials (the rejection path was removed when the screen was
    // made into a page rather than a modal) and writes a duty-engineer
    // session that the rest of the app recognises. In real prod, replace
    // this with a fetch against the auth API.
    await signInMock({ identity, password })
    setPending(false)
    land()
  }

  const onContinueWithProvider = async () => {
    if (pending) {
      return
    }
    setPending(true)
    setFailure(null)
    await signInWithOidcMock()
    setPending(false)
    land()
  }

  return (
    <main className={styles.screen} data-test="login-screen">
      <div className={styles.panel}>
        <div className={styles.head}>
          <ComukiMark className={styles.mark} />
          <h1 className={styles.title}>Sign in</h1>
          {landing.notice ? null : (
            <p className={styles.lead}>{landing.lead}</p>
          )}
        </div>

        {landing.notice ? (
          <Landing
            kind={landing.kind}
            notice={landing.notice}
            lead={landing.lead}
            redirect={redirect}
          />
        ) : null}

        {failure ? (
          <p
            className={styles.failure}
            id={failureId}
            role="alert"
            data-test="login-failure"
          >
            <CircleAlert aria-hidden="true" className={styles.failureIcon} />
            <span>{failure}</span>
          </p>
        ) : null}

        {/* A real form element, so Enter submits from either field without a
            key handler pretending to be one. */}
        <form
          className={styles.form}
          onSubmit={(event) => void submit(event)}
          noValidate
        >
          <div className={styles.field}>
            <label className={styles.label} htmlFor={identityId}>
              Email or username
            </label>
            <input
              className={styles.input}
              id={identityId}
              name="identity"
              type="text"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              placeholder="you@comuki.local or your handle"
              value={identity}
              aria-invalid={failure ? true : undefined}
              aria-describedby={failure ? failureId : undefined}
              onChange={(event) => setIdentity(event.target.value)}
            />
          </div>

          <div className={styles.field}>
            <label className={styles.label} htmlFor={passwordId}>
              Password
            </label>
            <input
              className={styles.input}
              id={passwordId}
              name="password"
              type="password"
              autoComplete="current-password"
              placeholder="your account password"
              value={password}
              aria-invalid={failure ? true : undefined}
              aria-describedby={failure ? failureId : undefined}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>

          {/* `disabled` is for busy and invalid, which is exactly these two —
              nobody is being refused a permission here. */}
          <Button
            type="submit"
            size="lg"
            className={styles.submit}
            disabled={pending || incomplete}
            data-test="login-submit"
          >
            {pending ? "Signing in…" : "Sign in"}
          </Button>
        </form>

        {/* §16: the provider button exists only when a provider is configured.
            An always-present button that leads nowhere teaches the operator to
            distrust the screen. */}
        {oidc ? (
          <div className={styles.alternative}>
            <div className={styles.divider} aria-hidden="true">
              or
            </div>
            <Button
              variant="outline"
              size="lg"
              className={styles.submit}
              disabled={pending}
              onClick={() => void onContinueWithProvider()}
              data-test="login-oidc"
            >
              Continue with {oidc.label}
            </Button>
          </div>
        ) : null}

        {/* The footer, anchored at the floor of the screen. What build this
            is, and where its source lives — the two facts an operator checks
            before they put a real password in. Pulled off the panel so it
            does not steal room from the form, and rendered in the data voice
            because both fields are values (a SHA is a value, a repo URL is a
            value). The env label reads aloud on every build except
            production, where the green pill is the env hint. */}
        <footer className={styles.footer} data-test="login-footer">
          <p className={styles.footerLine}>
            © 2026 dot-stbl · source at{" "}
            {env.repoUrl ? (
              <a
                href={env.repoUrl}
                className={styles.footerLink}
                target="_blank"
                rel="noreferrer noopener"
              >
                {new URL(env.repoUrl).host + new URL(env.repoUrl).pathname}
              </a>
            ) : (
              <span className={styles.footerLink}>github.com/dot-stbl/comuki</span>
            )}
          </p>
          <p className={styles.footerLine}>
            build {env.commitSha || "—"} · {env.deployEnv}
          </p>
        </footer>
      </div>
    </main>
  )
}

interface LandingProps {
  kind: string
  notice: string
  lead: string
  redirect?: string
}

/**
 * The sentence that distinguishes the arrivals.
 *
 * Expired gets a marked block — something happened to them and they need to
 * see it. Signed out gets the same words with the marking taken off, because a
 * departure that worked is not an incident and should not be dressed as one.
 */
function Landing({ kind, notice, lead, redirect }: LandingProps) {
  const expired = kind === "expired"
  const Icon = expired ? TimerOff : Check

  return (
    <div
      className={cn(styles.notice, !expired && styles.quiet)}
      data-test="login-landing"
      data-landing={kind}
    >
      <Icon aria-hidden="true" className={styles.noticeIcon} />
      <span className={styles.noticeBody}>
        <span className={styles.noticeTitle}>{notice}</span>
        <span className={styles.noticeLead}>{lead}</span>
        {redirect ? (
          <span className={styles.noticeLead}>
            You'll return to <span className={styles.path}>{redirect}</span>
          </span>
        ) : null}
      </span>
    </div>
  )
}

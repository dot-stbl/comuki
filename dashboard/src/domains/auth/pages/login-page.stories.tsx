import { useEffect } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import {
  resetMockAuth,
  setMockOidcProvider,
  type MockOidcProvider,
} from "@/shared/api/mock/auth.store"

import { LoginPage, type LoginPageProps } from "./login-page"

/**
 * Whether an identity provider is configured is store state, not a prop — §16
 * says the button appears "if configured", and a screen that can only ever be
 * photographed with it present has not been built for the tenant without one.
 * So a story sets the store and the screen reads it, exactly as the app does.
 */
function WithProvider({
  provider,
  ...props
}: LoginPageProps & { provider: MockOidcProvider | null }) {
  useEffect(() => {
    setMockOidcProvider(provider)
    return () => {
      resetMockAuth()
    }
  }, [provider])

  return <LoginPage {...props} />
}

const OIDC: MockOidcProvider = { id: "comuki-oidc", label: "OIDC" }

const meta: Meta<typeof LoginPage> = {
  title: "Auth/Sign in",
  component: LoginPage,
  // The screen owns the whole viewport — it is the one route with no rail and
  // no topbar, so a padded canvas would misrepresent its own centring.
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof LoginPage>

/** First landing: no session, nothing to explain. */
export const Cold: Story = {
  render: () => <WithProvider provider={OIDC} />,
}

/** Second landing: thrown out mid-shift, and put back afterwards. */
export const SessionExpired: Story = {
  render: () => (
    <WithProvider provider={OIDC} reason="expired" redirect="/runs?status=waiting" />
  ),
}

/** Third landing: they pressed Sign out. A confirmation, not an incident. */
export const SignedOut: Story = {
  render: () => <WithProvider provider={OIDC} reason="signed-out" />,
}

/** The tenant with no identity provider: local sign-in and nothing else. */
export const NoIdentityProvider: Story = {
  render: () => <WithProvider provider={null} />,
}

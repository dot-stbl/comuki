import type { ReactNode } from "react"

import type { Role } from "./permissions"
import { SessionProvider } from "./session"

/**
 * A session with exactly the roles a test cares about.
 *
 * Test-only, and deliberately kept out of the barrel: nothing in the app should
 * be able to conjure a session out of a prop. `roles` land on the platform side
 * so a test can say "a viewer" without inventing a project graph too; pass
 * `projectRoles` when what is under test is a row's project — which is where
 * most of the interesting cases now live.
 */
export interface TestSessionProps {
  roles?: Role[]
  projectRoles?: Record<string, Role[]>
  children: ReactNode
}

export function TestSession({
  roles = ["member"],
  projectRoles = {},
  children,
}: TestSessionProps) {
  return (
    <SessionProvider
      user={{
        id: "u_test",
        name: "Test User",
        email: "test@comuki.local",
        platformRoles: roles,
        projectRoles,
      }}
      projects={[
        { id: "p_test", key: "test", name: "Test project" },
        { id: "p_other", key: "other", name: "Other project" },
      ]}
    >
      {children}
    </SessionProvider>
  )
}

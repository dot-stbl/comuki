import type { Meta, StoryObj } from "@storybook/react"

import { buildIdentitySnapshot } from "@/domains/identity/model/identity"
import { createGrantColumns, getGrantId } from "@/domains/identity/ui/grants-columns"
import {
  createApiKeyColumns,
  getApiKeyId,
} from "@/domains/identity/ui/keys-columns"
import { createUserColumns, getUserId } from "@/domains/identity/ui/users-columns"
import {
  API_KEYS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  USERS_SEED,
} from "@/shared/api/mock/identity.seed"
import { PLATFORM_PROJECTS_SEED } from "@/shared/api/mock/projects.seed"
import type { Session } from "@/shared/session"
import { DataTable } from "@/shared/ui"

const snapshot = buildIdentitySnapshot(
  USERS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  API_KEYS_SEED,
  PLATFORM_PROJECTS_SEED,
  new Date("2026-08-30T09:00:00Z")
)

/**
 * The session goes into the column factory rather than a hook into a cell: a
 * `cell` is called as a plain function while the table builds a row, so a hook
 * inside one typechecks and then throws.
 */
function shift(roles: Session["user"]["platformRoles"]): Session {
  return {
    user: {
      id: "u_story",
      name: "Story",
      email: "story@comuki.local",
      platformRoles: roles,
      projectRoles: {},
    },
    projects: snapshot.projects.map((project) => ({
      id: project.id,
      key: project.slug,
      name: project.name,
    })),
  }
}

const scopes = [...new Set(snapshot.grants.map((grant) => grant.scopeLabel))]

const meta: Meta<typeof DataTable> = {
  title: "Identity/Lists",
  component: DataTable,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof DataTable>

/**
 * Who exists.
 *
 * The seeded awkward rows are the point: somebody holding two projects and no
 * platform standing, an account that was invited and has never arrived, and a
 * disabled account that still carries a live platform grant.
 */
export const Users: Story = {
  render: () => (
    <DataTable
      columns={createUserColumns({
        session: shift(["platform-admin"]),
        busyId: null,
        onLink: () => {},
        onToggleDisabled: () => {},
      })}
      data={snapshot.users}
      getRowId={getUserId}
      density="compact"
    />
  ),
}

/** Subject, role, scope. There is no fourth column and no role editor. */
export const Grants: Story = {
  render: () => (
    <DataTable
      columns={createGrantColumns({
        session: shift(["platform-admin"]),
        scopes,
        revokingId: null,
        onRevoke: () => {},
      })}
      data={snapshot.grants}
      getRowId={getGrantId}
      density="compact"
    />
  ),
}

/**
 * Keys, and never a secret.
 *
 * A key nobody has ever used, a key three days from expiry and a key already
 * revoked — the three states worth catching an eye, each said in words before
 * it is said in colour.
 */
export const ApiKeys: Story = {
  render: () => (
    <DataTable
      columns={createApiKeyColumns({
        session: shift(["platform-admin"]),
        revokingId: null,
        onRevoke: () => {},
      })}
      data={snapshot.keys}
      getRowId={getApiKeyId}
      density="compact"
    />
  ),
}

/**
 * The same lists for a shift that may not administer identity. Every act keeps
 * its place and its size and explains what it needs.
 */
export const Denied: Story = {
  render: () => (
    <DataTable
      columns={createApiKeyColumns({
        session: shift(["operator"]),
        revokingId: null,
        onRevoke: () => {},
      })}
      data={snapshot.keys}
      getRowId={getApiKeyId}
      density="compact"
    />
  ),
}

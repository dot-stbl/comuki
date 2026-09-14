import type { Meta, StoryObj } from "@storybook/react"

import type { VirtualKey } from "@/domains/models/model/types"
import type { Session } from "@/shared/session"
import {
  MODEL_ENDPOINTS_SEED,
  VIRTUAL_KEYS_SEED,
} from "@/shared/api/mock/models.seed"

import { KeyDetailSheet } from "./key-detail-sheet"

/* The sheet reads the shift for its grant and denial readings; a plain object
   rather than the test harness, which is test-only by its own declaration. */
const SESSION: Session = {
  user: {
    id: "u_story",
    name: "Story Operator",
    email: "story@comuki.local",
    platformRoles: ["platform-admin"],
    projectRoles: {},
  },
  projects: [
    { id: "p_comuki", key: "comuki", name: "Comuki" },
    { id: "p_plexor", key: "plexor", name: "Plexor" },
    { id: "p_atlas", key: "atlas", name: "Atlas" },
  ],
}

/** The admin catalogue's shape: three readings the wire does not carry. */
const CATALOGUE_KEY: VirtualKey = {
  id: "sha256:story",
  prefix: "ck_live_9f2a",
  label: "default lead-xl-2",
  endpointId: "provider-A:https://api.provider-a.example/v1",
  models: ["lead-xl-2"],
  scope: { kind: "project", projectId: "p_comuki" },
  budgetUsd: 400,
  spentUsd: null,
  expiresInSec: 12 * 86_400,
  lastUsedAgoSec: null,
  revoked: false,
  createdAgoSec: null,
  grants: null,
  spendDaily: null,
}

const meta: Meta<typeof KeyDetailSheet> = {
  title: "Models/Key detail sheet",
  component: KeyDetailSheet,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  args: {
    endpoints: [...MODEL_ENDPOINTS_SEED],
    enforced: false,
    revokingId: null,
    onRevoke: () => {},
    session: SESSION,
    onOpenChange: () => {},
  },
}

export default meta
type Story = StoryObj<typeof KeyDetailSheet>

/**
 * The key at ninety percent of its cap: the drawer's whole argument in one
 * story — the meter the row shows, the fortnight behind it ending in six
 * silent days, and the act waiting at the bottom.
 */
export const NearTheCap: Story = {
  args: { entry: VIRTUAL_KEYS_SEED[0] ?? null, open: true },
}

/**
 * Three days past its TTL: the same spend history, a stopped key's badge, and
 * no act in the footer because there is nothing left to revoke.
 */
export const ExpiredTrial: Story = {
  args: { entry: VIRTUAL_KEYS_SEED[2] ?? null, open: true },
}

/** Revoked and never used: fourteen zeros read as "no spend recorded". */
export const RevokedNeverUsed: Story = {
  args: { entry: VIRTUAL_KEYS_SEED[4] ?? null, open: true },
}

/**
 * Real mode today: the catalogue row. Spend, grants and the issued date each
 * say "not on this wire" rather than inventing a history the host never kept —
 * the gaps a `GET /proxy/keys/{id}` endpoint is the follow-up for.
 */
export const CatalogueRow: Story = {
  args: { entry: CATALOGUE_KEY, open: true },
}

/** Closed: the overlay renders nothing at all rather than a hidden box. */
export const Closed: Story = {
  args: { entry: VIRTUAL_KEYS_SEED[0] ?? null, open: false },
}

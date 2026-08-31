import type { Meta, StoryObj } from "@storybook/react"

import type {
  ComputePool,
  ComputeProvider,
} from "@/domains/compute/model/types"

import { CapacityCard } from "./capacity-card"

/**
 * Every reading the card has to be able to give, on one page — because the
 * whole point of the component is that the four look different at a glance.
 */

function provider(
  id: string,
  kind: ComputeProvider["kind"],
  allocatable: { used: number; limit: number } | null
): ComputeProvider {
  return {
    id,
    kind,
    endpoint: `https://${id}.example:6443`,
    state: allocatable ? "active" : "unreachable",
    takingWork: Boolean(allocatable),
    allocatable: allocatable
      ? { ...allocatable, source: "capacity api" }
      : null,
    note: "",
  }
}

function pool(
  providerId: string,
  quota: { used: number; limit: number },
  knobs: { minIdle: number; maxIdle: number; idle: number }
): ComputePool {
  return {
    projectId: "p_comuki",
    providerId,
    minIdle: knobs.minIdle,
    maxIdle: knobs.maxIdle,
    workers: quota.used,
    idle: knobs.idle,
    quota: { ...quota, source: "project quota" },
    profiles: ["implementer", "reviewer"],
  }
}

const k8s = provider("cp_k8s_prod", "kubernetes", { used: 31, limit: 96 })
const docker = provider("cp_docker_dev", "docker", { used: 5, limit: 6 })
const silent = provider("cp_k8s_staging", "kubernetes", null)

const meta: Meta<typeof CapacityCard> = {
  title: "Compute/Capacity card",
  component: CapacityCard,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CapacityCard>

/** The reading the screen exists for: the project is full, the cluster is not. */
export const QuotaBinds: Story = {
  args: {
    pool: pool(
      "cp_k8s_prod",
      { used: 24, limit: 24 },
      {
        minIdle: 2,
        maxIdle: 6,
        idle: 5,
      }
    ),
    provider: k8s,
    projectKey: "comuki",
  },
}

/** The mirror: a generous quota on a single dev host the host cannot honour. */
export const ClusterBinds: Story = {
  args: {
    pool: pool(
      "cp_docker_dev",
      { used: 5, limit: 10 },
      {
        minIdle: 0,
        maxIdle: 2,
        idle: 2,
      }
    ),
    provider: docker,
    projectKey: "comuki",
  },
}

/** Equal headroom. Raising either alone buys nothing, so the card says both. */
export const BothBind: Story = {
  args: {
    pool: pool(
      "cp_k8s_prod",
      { used: 22, limit: 24 },
      {
        minIdle: 1,
        maxIdle: 4,
        idle: 1,
      }
    ),
    provider: provider("cp_k8s_prod", "kubernetes", { used: 30, limit: 32 }),
    projectKey: "plexor",
  },
}

/**
 * Create-per-task. Nothing is wrong: the pool is configured to sit at zero
 * until a backlog appears, and the knob line is what stops it reading as an
 * outage.
 */
export const CreatePerTask: Story = {
  args: {
    pool: pool(
      "cp_docker_dev",
      { used: 0, limit: 8 },
      {
        minIdle: 0,
        maxIdle: 0,
        idle: 0,
      }
    ),
    provider: docker,
    projectKey: "atlas",
  },
}

/** The capacity api did not answer. `null` is not zero, and it must not look it. */
export const NoCapacityReading: Story = {
  args: {
    pool: pool(
      "cp_k8s_staging",
      { used: 3, limit: 12 },
      {
        minIdle: 1,
        maxIdle: 3,
        idle: 3,
      }
    ),
    provider: silent,
    projectKey: "plexor",
  },
}

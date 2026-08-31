import { beforeEach, describe, expect, it } from "vitest"

import { COMPUTE_SEED } from "@/shared/api/mock/compute.seed"
import {
  readSeedCompute,
  resetSeedCompute,
  retireSeedComputeIdle,
  takeSeedComputeWork,
} from "@/shared/api/mock/compute.store"

import { readCapacity, strandedIdle } from "./capacity"

/**
 * The seed's arithmetic, asserted rather than assumed.
 *
 * A registry mock is three lists that describe the same containers from three
 * angles, and the moment they stop summing the screen is teaching an operator a
 * state the product cannot produce — a pool with more workers than the cluster
 * says it placed, a rollout that adds up to a different fleet. These are cheap
 * to check and expensive to notice by eye.
 */

beforeEach(() => {
  resetSeedCompute()
})

describe("the compute seed adds up", () => {
  it("gives a provider exactly the containers its pools are holding", () => {
    for (const provider of COMPUTE_SEED.providers) {
      if (!provider.allocatable) {
        // A provider that stopped answering still has containers on it — that
        // is exactly what makes its silence a problem — but there is no reading
        // to reconcile them against, so there is nothing to check here.
        continue
      }
      const held = COMPUTE_SEED.pools
        .filter((pool) => pool.providerId === provider.id)
        .reduce((total, pool) => total + pool.workers, 0)
      expect(provider.allocatable.used).toBe(held)
    }
  })

  it("describes one fleet from both the pool and the label side", () => {
    const byPool = COMPUTE_SEED.pools.reduce(
      (total, pool) => total + pool.workers,
      0
    )
    const byLabel = COMPUTE_SEED.versions.reduce(
      (total, version) => total + version.workers,
      0
    )
    expect(byLabel).toBe(byPool)

    const idleByPool = COMPUTE_SEED.pools.reduce(
      (total, pool) => total + pool.idle,
      0
    )
    const idleByLabel = COMPUTE_SEED.versions.reduce(
      (total, version) => total + version.idle,
      0
    )
    expect(idleByLabel).toBe(idleByPool)
  })

  it("has exactly one provider taking work and one target label", () => {
    expect(
      COMPUTE_SEED.providers.filter((provider) => provider.takingWork)
    ).toHaveLength(1)
    expect(
      COMPUTE_SEED.versions.filter((version) => version.target)
    ).toHaveLength(1)
  })

  it("seeds every reading the screen has to be able to show", () => {
    const readings = COMPUTE_SEED.pools.map((pool) =>
      readCapacity(
        pool,
        COMPUTE_SEED.providers.find(
          (provider) => provider.id === pool.providerId
        )
      )
    )

    // A quota ceiling with the cluster still open, and a cluster ceiling with
    // the quota still open. Both, or the screen only ever gets tested one way.
    expect(readings.some((reading) => reading.binding === "quota")).toBe(true)
    expect(readings.some((reading) => reading.binding === "capacity")).toBe(
      true
    )
    expect(readings.some((reading) => reading.room === 0)).toBe(true)
    // And a pool whose ceiling cannot be read at all — seeded rather than left
    // to a branch of the card that nothing ever reaches.
    expect(readings.some((reading) => reading.binding === "unknown")).toBe(true)

    expect(COMPUTE_SEED.pools.some((pool) => pool.minIdle === 0)).toBe(true)
    expect(strandedIdle(COMPUTE_SEED.versions)).toBeGreaterThan(0)
    expect(
      COMPUTE_SEED.providers.some((provider) => provider.allocatable === null)
    ).toBe(true)
  })
})

describe("the store keeps those sums while it is written to", () => {
  it("moves new starts to one provider and drains the other", () => {
    takeSeedComputeWork("cp_docker_dev")
    const after = readSeedCompute()

    expect(after.providers.filter((entry) => entry.takingWork)).toHaveLength(1)
    expect(
      after.providers.find((entry) => entry.id === "cp_docker_dev")?.state
    ).toBe("active")
    // Draining rather than standby: it still holds the leases it handed out.
    expect(
      after.providers.find((entry) => entry.id === "cp_k8s_prod")?.state
    ).toBe("draining")
  })

  it("refuses to hand work to a provider that is not answering", () => {
    takeSeedComputeWork("cp_k8s_staging")

    expect(
      readSeedCompute().providers.find((entry) => entry.id === "cp_k8s_prod")
        ?.takingWork
    ).toBe(true)
  })

  it("retires only the idle containers on a stale label, and keeps the sums", () => {
    const before = readSeedCompute()
    const stale = before.versions.find(
      (entry) => !entry.target && entry.idle > 0
    )
    expect(stale).toBeDefined()
    const busy = stale!.workers - stale!.idle

    retireSeedComputeIdle(stale!.digest, stale!.profilesRef)
    const after = readSeedCompute()

    const now = after.versions.find(
      (entry) =>
        entry.digest === stale!.digest &&
        entry.profilesRef === stale!.profilesRef
    )
    // The ones holding a lease keep running: killing them would fail an item
    // that was about to land.
    expect(now?.idle).toBe(0)
    expect(now?.workers).toBe(busy)

    for (const provider of after.providers) {
      if (!provider.allocatable) {
        continue
      }
      const held = after.pools
        .filter((pool) => pool.providerId === provider.id)
        .reduce((total, pool) => total + pool.workers, 0)
      expect(provider.allocatable.used).toBe(held)
    }
  })

  it("leaves the target label alone", () => {
    const target = readSeedCompute().versions.find((entry) => entry.target)!
    retireSeedComputeIdle(target.digest, target.profilesRef)

    expect(readSeedCompute().versions.find((entry) => entry.target)?.idle).toBe(
      target.idle
    )
  })
})

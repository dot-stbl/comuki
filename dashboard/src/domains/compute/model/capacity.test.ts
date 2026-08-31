import { describe, expect, it } from "vitest"

import {
  bindingFirst,
  bindingSentence,
  headroom,
  idleReading,
  isCreatePerTask,
  readCapacity,
  share,
  staleReason,
  strandedIdle,
  targetVersion,
  versionLabel,
} from "./capacity"
import type { ComputePool, ComputeProvider, WorkerVersion } from "./types"

function provider(
  id: string,
  allocatable: { used: number; limit: number } | null
): ComputeProvider {
  return {
    id,
    kind: "kubernetes",
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
  knobs: { minIdle: number; maxIdle: number } = { minIdle: 2, maxIdle: 6 }
): ComputePool {
  return {
    projectId: "p_comuki",
    providerId,
    minIdle: knobs.minIdle,
    maxIdle: knobs.maxIdle,
    workers: quota.used,
    idle: 0,
    quota: { ...quota, source: "project quota" },
    profiles: ["implementer"],
  }
}

function version(
  digest: string,
  profilesRef: string,
  extra: Partial<WorkerVersion> = {}
): WorkerVersion {
  return {
    digest,
    profilesRef,
    target: false,
    workers: 1,
    idle: 0,
    oldestUpSec: 60,
    providerIds: ["cp_a"],
    ...extra,
  }
}

describe("the two ceilings, and which one is binding", () => {
  it("names the quota when the cluster still has room", () => {
    // The case the screen exists for: a project at its own ceiling on a cluster
    // that could have placed sixty-five more containers. Nothing about the
    // hardware is wrong, and buying nodes would buy nothing.
    const reading = readCapacity(
      pool("cp_a", { used: 24, limit: 24 }),
      provider("cp_a", { used: 31, limit: 96 })
    )

    expect(reading.binding).toBe("quota")
    expect(reading.room).toBe(0)
    expect(reading.capacityRoom).toBe(65)
    expect(bindingSentence(reading)).toBe(
      "quota is the ceiling — nothing can start, and the cluster still has 65 slots free"
    )
  })

  it("names the cluster when the quota is the one with room to spare", () => {
    // The mirror case, and it has to come out the other way round: a generous
    // quota on a single dev host is stopped by the host.
    const reading = readCapacity(
      pool("cp_b", { used: 5, limit: 10 }),
      provider("cp_b", { used: 5, limit: 6 })
    )

    expect(reading.binding).toBe("capacity")
    expect(reading.room).toBe(1)
    expect(reading.quotaRoom).toBe(5)
    expect(bindingSentence(reading)).toContain("the cluster is the ceiling")
    expect(bindingSentence(reading)).toContain("5 slots left under the quota")
  })

  it("says both when the two run out together", () => {
    // Not a rounding artefact: raising only one of two equal ceilings buys
    // nothing, and telling an operator to raise the quota would waste a day.
    const reading = readCapacity(
      pool("cp_a", { used: 8, limit: 10 }),
      provider("cp_a", { used: 30, limit: 32 })
    )

    expect(reading.binding).toBe("both")
    expect(reading.room).toBe(2)
    expect(bindingSentence(reading)).toBe(
      "quota and cluster agree — room for 2 slots"
    )
  })

  it("refuses to guess when the capacity api did not answer", () => {
    // `null` is not zero. Zero reads as a full cluster, which an operator acts
    // on differently from a provider that is not talking.
    const reading = readCapacity(
      pool("cp_c", { used: 3, limit: 12 }),
      provider("cp_c", null)
    )

    expect(reading.binding).toBe("unknown")
    expect(reading.room).toBeNull()
    expect(reading.capacityRoom).toBeNull()
    expect(bindingSentence(reading)).toContain("capacity api did not answer")
  })

  it("treats a missing provider as an unreadable ceiling, not a full one", () => {
    const reading = readCapacity(
      pool("cp_gone", { used: 1, limit: 4 }),
      undefined
    )

    expect(reading.binding).toBe("unknown")
    expect(reading.room).toBeNull()
  })

  it("never reports negative headroom, however far over a limit sits", () => {
    expect(headroom({ used: 30, limit: 24, source: "" })).toBe(0)
    expect(share({ used: 30, limit: 24, source: "" })).toBe(1)
    // A limit of zero is full, not free — the opposite of what a plain division
    // would produce.
    expect(share({ used: 0, limit: 0, source: "" })).toBe(1)
  })

  it("orders pools by how close each one is to refusing work", () => {
    const providers = [
      provider("cp_a", { used: 31, limit: 96 }),
      provider("cp_b", { used: 5, limit: 6 }),
    ]
    const pools = [
      pool("cp_a", { used: 7, limit: 12 }), // room 5
      pool("cp_a", { used: 24, limit: 24 }), // room 0
      pool("cp_b", { used: 5, limit: 10 }), // room 1
    ]

    expect(
      bindingFirst(pools, providers).map((entry) => entry.quota.used)
    ).toEqual([24, 5, 7])
  })
})

describe("pool knobs", () => {
  it("reads min idle 0 as a configuration, not as an empty field", () => {
    // The whole point: an unset knob and a create-per-task pool both show zero
    // containers, and only one of them is a fault.
    const created = pool(
      "cp_a",
      { used: 0, limit: 8 },
      { minIdle: 0, maxIdle: 0 }
    )

    expect(isCreatePerTask(created)).toBe(true)
    expect(idleReading(created)).toBe("min idle 0 — create-per-task")
  })

  it("states both knobs when the pool actually keeps workers warm", () => {
    const warm = pool("cp_a", { used: 4, limit: 8 }, { minIdle: 2, maxIdle: 6 })

    expect(isCreatePerTask(warm)).toBe(false)
    expect(idleReading(warm)).toBe("min idle 2 · max idle 6")
  })
})

describe("worker labels", () => {
  const target = version("sha256:9c41ab", "profiles@a1b9e0", { target: true })

  it("keeps both halves of the label together", () => {
    expect(versionLabel(target)).toBe("sha256:9c41ab · profiles@a1b9e0")
  })

  it("counts idle workers that can never be matched to an item", () => {
    // The reading no other screen can produce: a stranded worker looks exactly
    // like a healthy idle one everywhere else in the product.
    const versions = [
      { ...target, idle: 3 },
      version("sha256:41b7de", "profiles@a1b9e0", { idle: 5, workers: 6 }),
      version("sha256:9c41ab", "profiles@7b3d10", { idle: 2, workers: 2 }),
    ]

    expect(strandedIdle(versions)).toBe(7)
    expect(targetVersion(versions)).toBe(versions[0])
  })

  it("says which half of the label moved", () => {
    // The second one is the trap: the image is the target image, and the
    // container is still never handed an item, because the label is both.
    expect(
      staleReason(version("sha256:41b7de", "profiles@a1b9e0"), target)
    ).toBe("a release behind on the image")
    expect(
      staleReason(version("sha256:9c41ab", "profiles@7b3d10"), target)
    ).toBe("same image, the profiles ref moved")
    expect(
      staleReason(version("sha256:41b7de", "profiles@7b3d10"), target)
    ).toBe("image and profiles ref both moved")
  })

  it("has nothing to say about the target label itself", () => {
    expect(staleReason(target, target)).toBeNull()
  })
})

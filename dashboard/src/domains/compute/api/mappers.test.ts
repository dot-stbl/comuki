import { describe, expect, it } from "vitest"

import { computeSnapshotWireToSnapshot } from "@/domains/compute/api/mappers"

/** A wire snapshot with one busy pool and one waiting, docker selected. */
const WIRE = {
  provider: "docker",
  defaults: {
    workerImage: "ghcr.io/comuki/worker@sha256:9c41ab",
    profilesGitRef: "profiles@v0.4.1",
    minIdle: 1,
    maxConcurrent: 6,
    idleTtlSeconds: 300,
    pollIntervalSeconds: 15,
    profileKeys: ["implementer", "reviewer"],
  },
  pools: [
    {
      projectId: "b3d8a402-1111-2222-3333-444444444444",
      profileKey: "implementer",
      queued: 3,
      running: 2,
      minIdle: 1,
      maxConcurrent: 4,
    },
  ],
} as const

describe("the compute snapshot wire onto the registry", () => {
  it("answers one provider — configuration's own word, taking new starts", () => {
    const snapshot = computeSnapshotWireToSnapshot(WIRE)

    expect(snapshot.providers).toHaveLength(1)
    const provider = snapshot.providers[0]
    expect(provider?.kind).toBe("docker")
    expect(provider?.takingWork).toBe(true)
    // No capacity API is composed; the track's existing "no answer" reading
    // is the honest draw, not a zero-slot cluster.
    expect(provider?.allocatable).toBeNull()
  })

  it("maps a pool's queued/running onto the concurrency cap as its quota", () => {
    const snapshot = computeSnapshotWireToSnapshot(WIRE)

    const pool = snapshot.pools[0]
    expect(pool?.workers).toBe(2)
    expect(pool?.quota).toEqual({
      used: 2,
      limit: 4,
      source: "project concurrency cap",
    })
    expect(pool?.profiles).toEqual(["implementer"])
    // The idle ceiling is a knob the snapshot does not carry.
    expect(pool?.maxIdle).toBeNull()
    expect(pool?.idle).toBeNull()
  })

  it("answers exactly one worker version — the target label, counts unknown", () => {
    const snapshot = computeSnapshotWireToSnapshot(WIRE)

    expect(snapshot.versions).toHaveLength(1)
    const version = snapshot.versions[0]
    expect(version?.digest).toBe("ghcr.io/comuki/worker@sha256:9c41ab")
    expect(version?.profilesRef).toBe("profiles@v0.4.1")
    expect(version?.target).toBe(true)
    // The snapshot cannot count the fleet per label; a zero would read as
    // "nothing runs this image".
    expect(version?.workers).toBeNull()
    expect(version?.idle).toBeNull()
  })

  it("spells kubernetes as itself and everything unknown as docker", () => {
    expect(
      computeSnapshotWireToSnapshot({ ...WIRE, provider: "kubernetes" })
        .providers[0]?.kind
    ).toBe("kubernetes")
    expect(
      computeSnapshotWireToSnapshot({ ...WIRE, provider: "containerd" })
        .providers[0]?.kind
    ).toBe("docker")
  })
})

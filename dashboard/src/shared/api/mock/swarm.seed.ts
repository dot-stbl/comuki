export interface SeedSwarm {
  running: number
  waiting: number
  failed: number
  queued: number
  escalated: number
}

export const SWARM_SEED: SeedSwarm = {
  running: 7,
  waiting: 3,
  failed: 1,
  queued: 5,
  escalated: 1,
}

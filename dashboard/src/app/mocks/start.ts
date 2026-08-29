/**
 * Mock bootstrap stub (W0).
 * Real MSW / handlers land in W1 when VITE_USE_MOCK=true.
 */
export async function startMocks(): Promise<void> {
  console.info("[comuki] VITE_USE_MOCK=true — mock mode (handlers land in W1)")
}

/**
 * Mock bootstrap (W1).
 * Domain hooks read `shared/api/mock` seeds when `env.useMock` is true.
 * MSW service worker is optional and not required for Observe screens.
 */
export async function startMocks(): Promise<void> {
  console.info(
    "[comuki] VITE_USE_MOCK=true — Observe screens serve shared mock seeds"
  )
}

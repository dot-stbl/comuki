import { z } from "zod"

const envSchema = z.object({
  /** When true, domain hooks serve shared mock seeds instead of live API. */
  VITE_USE_MOCK: z
    .enum(["true", "false", "1", "0", ""])
    .optional()
    .transform((value) => value === "true" || value === "1"),
})

const parsed = envSchema.parse({
  VITE_USE_MOCK: import.meta.env.VITE_USE_MOCK ?? "",
})

export const env = {
  useMock: parsed.VITE_USE_MOCK,
}

export type Env = typeof env

import { z } from "zod"

const envSchema = z.object({
  /** When true, mock handlers land in W1; until then start() is a no-op stub. */
  VITE_USE_MOCK: z
    .enum(["true", "false", "1", "0", ""])
    .optional()
    .transform((value) => value === "true" || value === "1"),
})

export const env = envSchema.parse({
  VITE_USE_MOCK: import.meta.env.VITE_USE_MOCK ?? "",
})

export type Env = typeof env

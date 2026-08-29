import { z } from "zod"

export const budgetFormSchema = z.object({
  perTaskUsd: z.coerce.number().min(0.01).max(1000),
  perAppUsd: z.coerce.number().min(1).max(10_000),
  globalUsd: z.coerce.number().min(1).max(100_000),
  killSwitch: z.boolean(),
  pauseSwarm: z.boolean(),
})

export type BudgetFormValues = z.infer<typeof budgetFormSchema>

import { z } from "zod"

/**
 * The caps, and only the caps.
 *
 * The two stops left this form when they became immediate acts beside the
 * meter: an emergency brake that has to be typed into a form and submitted is
 * a brake that arrives late, and a form value that is really a switch is a
 * switch wearing a disguise. The order is the reading order on the screen —
 * the global cap first, because it is the one the meter measures against.
 */
export const budgetFormSchema = z.object({
  globalUsd: z.coerce.number().min(1).max(100_000),
  perTaskUsd: z.coerce.number().min(0.01).max(1000),
  perAppUsd: z.coerce.number().min(1).max(10_000),
})

/** `z.coerce.number()` accepts the form's raw strings, so input ≠ output here. */
export type BudgetFormInput = z.input<typeof budgetFormSchema>
export type BudgetFormValues = z.output<typeof budgetFormSchema>

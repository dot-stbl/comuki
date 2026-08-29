import { z } from "zod"

export const routingFormSchema = z.object({
  leadModel: z.string().trim().min(1).max(120),
  workerModel: z.string().trim().min(1).max(120),
  judgeModel: z.string().trim().min(1).max(120),
})

export type RoutingFormValues = z.infer<typeof routingFormSchema>

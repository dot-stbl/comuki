import { z } from 'zod';

/**
 * Report — the worker's final answer to the orchestrator (TS agent side
 * serializes it; the C# side parses it). Mirrors the C# DTO until codegen
 * lands (post Slice-0).
 */
export const reportStatusSchema = z.enum(['succeeded', 'failed', 'cancelled']);
export type ReportStatus = z.infer<typeof reportStatusSchema>;

export const reportSchema = z.object({
  status: reportStatusSchema,
  summary: z.string(),
  artifacts: z.array(z.string().min(1)).optional(),
});
export type Report = z.infer<typeof reportSchema>;

/** Parses and validates a report from an untrusted JSON payload. Throws `ZodError` on mismatch. */
export function parseReport(input: unknown): Report {
  return reportSchema.parse(input);
}

import { z } from 'zod';

/**
 * Brief — the work order the orchestrator hands to a worker (C# `Orchestrator`
 * side serializes it; the TS agent side parses it). Mirrors the C# DTO until
 * codegen lands (post Slice-0).
 */
export const briefSchema = z.object({
  taskId: z.string().min(1),
  profileKey: z.string().min(1),
  prompt: z.string().min(1),
  contextFiles: z.array(z.string().min(1)).optional(),
  rulesDigest: z.string().optional(),
});
export type Brief = z.infer<typeof briefSchema>;

/** Parses and validates a brief from an untrusted JSON payload. Throws `ZodError` on mismatch. */
export function parseBrief(input: unknown): Brief {
  return briefSchema.parse(input);
}

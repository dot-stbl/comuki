/**
 * Pure wire → kernel codecs for the CLI chat vertical slice (issue #84).
 *
 * The codecs sit at the contract boundary: GENERATED HTTP and realtime
 * shapes flow in, the kernel's existing shapes flow out. They never
 * touch I/O, throw across a wire, or import from `lib/` or
 * `kernel/adapters/` — the import direction is strictly
 * `contracts → _generated`, plus `import type` from `harness/state`,
 * `harness/effect-runner`, `kernel/feed` for the kernel target shapes.
 *
 * Three concerns live here:
 *
 * - `harnessMessageFromWire` / `turnOutcomeFromWire` normalise an HTTP
 *   turn payload into the kernel's `HarnessMessage` / `TurnOutcome`
 *   shapes, including the structural lift from generated
 *   `ChatMessageView` to the kernel `ChatMessageView` (the older
 *   lib/client shape with required-null fields).
 *
 * - `decodeChatChunk` / `decodeChatTurnComplete` guard realtime frames
 *   so unknown or malformed input becomes a cursor-advancing "unknown"
 *   feed message. The reason field is a fixed string — never the
 *   raw frame — so a malformed payload with sensitive text cannot
 *   leak into feed rendering.
 *
 * - `classifyProblem` maps RFC 9457 `ProblemDetails` to the small set
 *   of machine-stable `ProblemKind` values the rest of the CLI
 *   branches on (status mapping with code-suffix refinement when
 *   present).
 */
import type { FeedMessage } from "../kernel/feed";
import type { TurnOutcome } from "../harness/effect-runner";
import type { HarnessMessage } from "../harness/state";

import type { ChatMessageMeta as GeneratedChatMessageMeta } from "./_generated/http/types/ChatMessageMeta";
import type { ChatMessageView as GeneratedChatMessageView } from "./_generated/http/types/ChatMessageView";
import type { ChatMessagesPageView as GeneratedChatMessagesPageView } from "./_generated/http/types/ChatMessagesPageView";
import type { ChatTurnResultView as GeneratedChatTurnResultView } from "./_generated/http/types/ChatTurnResultView";
import type { MessagePart as GeneratedMessagePart } from "./_generated/http/types/MessagePart";
import type { PlanEdge as GeneratedPlanEdge } from "./_generated/http/types/PlanEdge";
import type { PlanNode as GeneratedPlanNode } from "./_generated/http/types/PlanNode";

// ---------------------------------------------------------------------------
// HarnessMessage / TurnOutcome
// ---------------------------------------------------------------------------

/**
 * Decode one transcript row from the wire into the kernel shape.
 *
 * - role: one of the four kernel-recognised roles passes through; any
 *   other string collapses to `"system"` so the chrome renderer
 *   still has a safe mark.
 * - createdAtUnixMs: `Date.parse` of the wire ISO timestamp. The
 *   kernel never re-parses, so all downstream time math reads the
 *   precomputed unix ms.
 * - view: the wire view is structurally normalised to the kernel
 *   `ChatMessageView` shape (required-null fields, kernel-side
 *   MessagePart union with literal discriminators, `PlanItemView`
 *   derived from generated `PlanNode` + `PlanEdge`).
 */
export function harnessMessageFromWire(view: GeneratedChatMessageView): HarnessMessage {
  return {
    id: view.id,
    role: normalizeRole(view.role),
    content: view.content,
    createdAtUnixMs: Date.parse(view.createdAt),
    view: normalizeMessageView(view),
  };
}

/**
 * Decode a turn result. The `pendingPlan` field carries the parsed
 * `JsonDocument` from the server verbatim — it is rendered as a
 * kernel-typed `unknown` so adapters cannot accidentally treat it as
 * a known shape.
 */
export function turnOutcomeFromWire(result: GeneratedChatTurnResultView): TurnOutcome {
  return {
    messages: result.messages.map(harnessMessageFromWire),
    awaitingApproval: result.awaitingApproval,
    pendingPlan: result.pendingPlan ?? null,
  };
}

/** Decode a paged transcript response; pages wrap the same message codec. */
export function chatMessagesPageFromWire(page: GeneratedChatMessagesPageView): readonly HarnessMessage[] {
  return page.items.map(harnessMessageFromWire);
}

function normalizeRole(role: string): HarnessMessage["role"] {
  if (role === "user" || role === "assistant" || role === "system" || role === "tool") {
    return role;
  }
  return "system";
}

/**
 * Lift a generated `ChatMessageView` into the kernel shape
 * (`ChatMessageView` from `lib/client`, referenced transitively via
 * `HarnessMessage.view`). Generated makes optional fields nullable;
 * kernel makes them required-null. We construct every required key,
 * coercing `undefined` to `null`, so the value is assignable to the
 * kernel shape without any `as` cast.
 */
function normalizeMessageView(input: GeneratedChatMessageView): HarnessMessage["view"] {
  const parts = input.parts === null || input.parts === undefined
    ? null
    : input.parts.map((part) => normalizePart(part));
  const meta = normalizeMeta(input.meta);
  return {
    id: input.id,
    role: input.role,
    content: input.content,
    toolName: input.toolName ?? null,
    parts,
    meta,
    createdAt: input.createdAt,
  };
}

function normalizeMeta(
  input: GeneratedChatMessageMeta | null | undefined
): NonNullable<HarnessMessage["view"]>["meta"] {
  if (input === null || input === undefined) {
    return null;
  }
  return {
    model: input.model ?? null,
    tokensIn: numericOrNull(input.tokensIn),
    tokensOut: numericOrNull(input.tokensOut),
    costMicros: numericOrNull(input.costMicros),
    latencyMs: numericOrNull(input.latencyMs),
    stopReason: input.stopReason ?? null,
  };
}

function numericOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "number") {
    return value;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * Lift a generated `MessagePart` into the kernel variant.
 *
 * The kernel variant for a plan part carries `PlanItemView` nodes with
 * a `dependsOn` array (vs the generated `PlanNode` with `id`/`title`);
 * the codec derives `dependsOn` from the edges — every edge whose `to`
 * is the node's id contributes its `from` to the dependency list.
 *
 * Discriminator-missing parts are coerced to an empty text part so
 * the surface cannot silently drop the row.
 */
function normalizePart(
  input: GeneratedMessagePart
): NonNullable<NonNullable<HarnessMessage["view"]>["parts"]>[number] {
  const kind = input.kind;
  if (kind === "text") {
    return { kind: "text" as const, markdown: input.markdown };
  }
  if (kind === "code") {
    return {
      kind: "code" as const,
      language: input.language,
      source: input.source,
      path: input.path ?? null,
      startLine: numericOrNull(input.startLine),
    };
  }
  if (kind === "diagram") {
    return {
      kind: "diagram" as const,
      dialect: input.dialect,
      source: input.source,
    };
  }
  if (kind === "thinking") {
    return {
      kind: "thinking" as const,
      text: input.text,
      tokens: numericOrNull(input.tokens),
    };
  }
  if (kind === "tool") {
    return {
      kind: "tool" as const,
      name: input.name,
      inputJson: input.inputJson,
      status: input.status,
      outputJson: input.outputJson ?? null,
      durationMs: numericOrNull(input.durationMs),
    };
  }
  if (kind === "handoff") {
    return { kind: "handoff" as const, query: input.query };
  }
  if (kind === "plan") {
    const nodeViews = input.nodes.map((node) => normalizePlanNode(node, input.edges));
    const edgeViews = input.edges.map((edge) => normalizePlanEdge(edge));
    return { kind: "plan" as const, nodes: nodeViews, edges: edgeViews };
  }
  // Unknown / missing discriminator — collapse to an empty text part so
  // the row still surfaces in the transcript.
  return { kind: "text" as const, markdown: "" };
}

function normalizePlanNode(
  node: GeneratedPlanNode,
  edges: readonly GeneratedPlanEdge[]
): { readonly key: string; readonly profileKey: string; readonly brief: string; readonly dependsOn: readonly string[] } {
  const dependsOn: string[] = [];
  for (const edge of edges) {
    if (edge.to === node.id) {
      dependsOn.push(edge.from);
    }
  }
  return {
    key: node.id,
    profileKey: node.profileKey,
    brief: node.brief,
    dependsOn,
  };
}

function normalizePlanEdge(edge: GeneratedPlanEdge): { readonly from: string; readonly to: string } {
  return { from: edge.from, to: edge.to };
}

// ---------------------------------------------------------------------------
// Realtime frame decoders
// ---------------------------------------------------------------------------

/**
 * Decode one realtime chunk frame. A valid frame yields a `"chunk"`
 * feed message with the full wire text; anything else yields an
 * `"unknown"` feed message so the durable session cursor still
 * advances (issue #83 — the kernel must survive frames it does not
 * understand).
 *
 * The `reason` field is a fixed string — never the raw payload, never
 * a JSON dump — so sensitive frame contents cannot leak into the
 * feed renderer or downstream logs.
 */
export function decodeChatChunk(frame: unknown, receivedAtUnixMs: number): FeedMessage {
  if (frame === null || typeof frame !== "object") {
    return { kind: "unknown", receivedAtUnixMs, reason: "malformed ChatChunk frame" };
  }
  if (isChatChunk(frame)) {
    return {
      kind: "chunk",
      sessionId: frame.sessionId,
      seq: frame.seq,
      text: frame.text,
      receivedAtUnixMs,
    };
  }
  return {
    kind: "unknown",
    sessionId: recoverString(frame, "sessionId"),
    receivedAtUnixMs,
    reason: "malformed ChatChunk frame",
  };
}

/**
 * Decode one realtime turn-complete frame. The same guards apply;
 * an `outcome` that is not a string falls back to `"unknown"` so the
 * caller still receives a `"turn-complete"` envelope rather than a
 * more disruptive `"unknown"` kind.
 */
export function decodeChatTurnComplete(frame: unknown, receivedAtUnixMs: number): FeedMessage {
  if (frame === null || typeof frame !== "object") {
    return { kind: "unknown", receivedAtUnixMs, reason: "malformed ChatTurnComplete frame" };
  }
  if (isChatTurnComplete(frame)) {
    return {
      kind: "turn-complete",
      sessionId: frame.sessionId,
      outcome: frame.outcome,
      receivedAtUnixMs,
    };
  }
  if ("sessionId" in frame && typeof frame.sessionId === "string" && "outcome" in frame) {
    return {
      kind: "turn-complete",
      sessionId: frame.sessionId,
      outcome: typeof frame.outcome === "string" ? frame.outcome : "unknown",
      receivedAtUnixMs,
    };
  }
  return {
    kind: "unknown",
    sessionId: recoverString(frame, "sessionId"),
    receivedAtUnixMs,
    reason: "malformed ChatTurnComplete frame",
  };
}

function isChatChunk(value: object): value is { sessionId: string; seq: number; text: string } {
  return (
    "sessionId" in value && typeof value.sessionId === "string" &&
    "seq" in value && typeof value.seq === "number" &&
    "text" in value && typeof value.text === "string"
  );
}

function isChatTurnComplete(value: object): value is { sessionId: string; outcome: string } {
  return (
    "sessionId" in value && typeof value.sessionId === "string" &&
    "outcome" in value && typeof value.outcome === "string"
  );
}

function recoverString(value: object, key: string): string | undefined {
  if (key in value) {
    const candidate = value[key as keyof typeof value];
    if (typeof candidate === "string") {
      return candidate;
    }
  }
  return undefined;
}

// ---------------------------------------------------------------------------
// ProblemDetails classification
// ---------------------------------------------------------------------------

/**
 * The small set of machine-stable problem classes the CLI branches on.
 * The wire protocol carries RFC 9457 `ProblemDetails` with a numeric
 * `status` and a dot.case `code`; the kernel reduces both to one of
 * these strings.
 */
export type ProblemKind =
  | "validation"
  | "not-found"
  | "conflict"
  | "unauthorized"
  | "forbidden"
  | "rate-limited"
  | "unavailable"
  | "server-error"
  | "unknown";

const PROBLEM_KINDS: readonly ProblemKind[] = [
  "validation",
  "not-found",
  "conflict",
  "unauthorized",
  "forbidden",
  "rate-limited",
  "unavailable",
  "server-error",
  "unknown",
];

/** Suffix (hyphen-normalised) → kind lookup, so refinement needs no cast. */
const PROBLEM_KIND_BY_SUFFIX: Readonly<Record<string, ProblemKind>> = Object.fromEntries(
  PROBLEM_KINDS.map((kind) => [kind, kind]),
);

/**
 * Map an inbound problem into the kernel's machine-stable enum.
 *
 * The code suffix (last dot.case segment, with underscores normalised
 * to hyphens) is consulted first when present — codes like
 * `knowledge.forbidden` carry the kind in their suffix and beat the
 * generic status mapping. When the suffix is not a kind, the HTTP
 * status decides: 400 / 401 / 403 / 404 / 409 / 429 / 503 fall
 * through to their natural classes; everything in the 5xx range
 * collapses to `"server-error"`; the rest is `"unknown"`.
 */
export function classifyProblem(input: { readonly status: number; readonly code?: string }): ProblemKind {
  if (input.code !== undefined) {
    const suffix = lastSegment(input.code);
    const normalized = suffix.replace(/_/g, "-");
    const refined = PROBLEM_KIND_BY_SUFFIX[normalized];
    if (refined !== undefined && refined !== "unknown") {
      return refined;
    }
  }
  if (input.status === 400) return "validation";
  if (input.status === 401) return "unauthorized";
  if (input.status === 403) return "forbidden";
  if (input.status === 404) return "not-found";
  if (input.status === 409) return "conflict";
  if (input.status === 429) return "rate-limited";
  if (input.status === 503) return "unavailable";
  if (input.status >= 500 && input.status < 600) return "server-error";
  return "unknown";
}

function lastSegment(code: string): string {
  const index = code.lastIndexOf(".");
  return index === -1 ? code : code.slice(index + 1);
}
/**
 * Codec tests (issue #84). Three concerns:
 *
 * - Fixture replay — read each C#-producer-shaped JSON fixture off
 *   disk, decode through the codecs, and assert the kernel shape is
 *   right (id, role, content, unix-ms conversion, parts semantics).
 *   The fixtures are the C# producer's wire contract; the codec test
 *   is the consumer's acceptance gate.
 *
 * - Realtime frame guards — malformed/unknown/missing fields must
 *   collapse to `kind: "unknown"` with a fixed `reason` that never
 *   contains the raw payload. A live "SECRET-LEAK-CHECK" token in a
 *   malformed frame asserts no leak.
 *
 * - ProblemDetails classification — status mapping with code-suffix
 *   refinement, every `ProblemKind` branch exercised.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "bun:test";

import type { ChatMessageView as GeneratedChatMessageView } from "./_generated/http/types/ChatMessageView";
import type { ChatMessagesPageView as GeneratedChatMessagesPageView } from "./_generated/http/types/ChatMessagesPageView";
import type { ChatTurnResultView as GeneratedChatTurnResultView } from "./_generated/http/types/ChatTurnResultView";
import type { MessagePart as GeneratedMessagePart } from "./_generated/http/types/MessagePart";

import {
  chatMessagesPageFromWire,
  classifyProblem,
  decodeChatChunk,
  decodeChatTurnComplete,
  harnessMessageFromWire,
  turnOutcomeFromWire,
  type ProblemKind,
} from "./codecs";

const FIXTURE_DIR = join(import.meta.dir, "__fixtures__");

function loadFixture<T>(name: string): T {
  const raw = readFileSync(join(FIXTURE_DIR, name), "utf8");
  return JSON.parse(raw) as T;
}

/**
 * Compile-time exhaustiveness guard for `MessagePart["kind"]`.
 * If the server adds a new variant, the record literal below stops
 * compiling and forces a fixture + test update.
 */
const ALL_MESSAGE_PART_KINDS: Record<GeneratedMessagePart["kind"], true> = {
  text: true,
  code: true,
  diagram: true,
  thinking: true,
  tool: true,
  handoff: true,
  plan: true,
};

const ALL_PROBLEM_KINDS: readonly ProblemKind[] = [
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

// ---------------------------------------------------------------------------
// Fixtures — file/key shape sanity
// ---------------------------------------------------------------------------

describe("fixtures", () => {
  it("chat-session-view.json uses camelCase keys matching ChatSessionView", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-session-view.json");
    expect(Object.keys(raw).sort()).toEqual([
      "_comment",
      "createdAt",
      "id",
      "projectId",
      "status",
      "title",
      "updatedAt",
    ]);
  });

  it("chat-message-view.json uses camelCase keys matching ChatMessageView", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-message-view.json");
    expect(Object.keys(raw).sort()).toEqual([
      "_comment",
      "content",
      "createdAt",
      "id",
      "meta",
      "parts",
      "role",
      "toolName",
    ]);
  });

  it("chat-turn-result-view.json uses camelCase keys matching ChatTurnResultView", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-turn-result-view.json");
    expect(Object.keys(raw).sort()).toEqual([
      "_comment",
      "awaitingApproval",
      "messages",
      "pendingPlan",
    ]);
  });

  it("chat-chunk.json uses camelCase keys matching ChatChunkView", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-chunk.json");
    expect(Object.keys(raw).sort()).toEqual(["_comment", "seq", "sessionId", "text"]);
  });

  it("chat-turn-complete.json uses camelCase keys matching ChatTurnCompleteView", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-turn-complete.json");
    expect(Object.keys(raw).sort()).toEqual(["_comment", "outcome", "sessionId"]);
  });
});

// ---------------------------------------------------------------------------
// harnessMessageFromWire — fixture replay
// ---------------------------------------------------------------------------

describe("harnessMessageFromWire", () => {
  it("decodes chat-message-view.json to the full kernel shape", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view.json");
    const message = harnessMessageFromWire(raw);

    expect(message.id).toBe("7e3c4d2f-9a2b-4f8a-bd61-4e9f3a8c1b22");
    expect(message.role).toBe("assistant");
    expect(message.content).toBe(raw.content);
    expect(message.createdAtUnixMs).toBe(Date.parse("2026-09-20T10:16:12.000+00:00"));
    expect(message.createdAtUnixMs).toBe(1789899372000);
    expect(message.view?.id).toBe(raw.id);
  });

  it("maps every recognised role verbatim", () => {
    for (const role of ["user", "assistant", "system", "tool"] as const) {
      const message = harnessMessageFromWire({
        ...minimalMessage(),
        role,
      });
      expect(message.role).toBe(role);
    }
  });

  it("collapses any unrecognised role to 'system'", () => {
    const message = harnessMessageFromWire({
      ...minimalMessage(),
      role: "brain",
    });
    expect(message.role).toBe("system");
  });

  it("survives the nulls fixture — toolName/parts/meta all null", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view-nulls.json");
    const message = harnessMessageFromWire(raw);

    expect(message.role).toBe("tool");
    expect(message.view?.toolName).toBeNull();
    expect(message.view?.parts).toBeNull();
    expect(message.view?.meta).toBeNull();
    expect(message.createdAtUnixMs).toBe(1789718575250);
  });

  it("chat-message-view.json parts cover every MessagePart kind", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view.json");
    const message = harnessMessageFromWire(raw);
    const kinds = new Set<GeneratedMessagePart["kind"]>();

    if (message.view?.parts) {
      for (const part of message.view.parts) {
        kinds.add(part.kind);
      }
    }
    expect(kinds.size).toBe(Object.keys(ALL_MESSAGE_PART_KINDS).length);
    for (const expectedKind of Object.keys(ALL_MESSAGE_PART_KINDS) as Array<keyof typeof ALL_MESSAGE_PART_KINDS>) {
      expect(kinds.has(expectedKind)).toBe(true);
    }
  });

  it("chat-message-view.json plan part passes through the canonical nodes/edges shape", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view.json");
    const message = harnessMessageFromWire(raw);
    const planPart = message.view?.parts?.find((p) => p.kind === "plan");
    expect(planPart).toBeDefined();
    if (!planPart || planPart.kind !== "plan") {
      return;
    }
    const n1 = planPart.nodes.find((n) => n.id === "n1");
    expect(n1?.profileKey).toBe("implement");
    expect(n1?.brief).toContain("harnessMessageFromWire");
    expect(n1?.title).toBeDefined();
    expect(planPart.edges.map((e) => ({ from: e.from, to: e.to }))).toEqual([
      { from: "n1", to: "n2" },
      { from: "n2", to: "n3" },
    ]);
    // Dependencies live in the edges, not on the node — a consumer
    // derives n2's upstream as [n1] from the edge list.
    expect(planPart.edges.filter((e) => e.to === "n2").map((e) => e.from)).toEqual(["n1"]);
  });

  it("tool part carries outputJson and durationMs from the fixture", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view.json");
    const message = harnessMessageFromWire(raw);
    const tool = message.view?.parts?.find((p) => p.kind === "tool");
    expect(tool).toBeDefined();
    if (!tool || tool.kind !== "tool") {
      return;
    }
    expect(tool.name).toBe("create_ticket");
    expect(tool.status).toBe("succeeded");
    expect(tool.outputJson).toBe("{\"id\":\"tk-9af1\",\"status\":\"open\"}");
    expect(tool.durationMs).toBe(312);
  });

  it("meta fields carry the fixture scalars verbatim", () => {
    const raw = loadFixture<GeneratedChatMessageView>("chat-message-view.json");
    const message = harnessMessageFromWire(raw);
    expect(message.view?.meta).toEqual({
      model: "glm-4.7",
      tokensIn: 412,
      tokensOut: 188,
      costMicros: 18400,
      latencyMs: 1820,
      stopReason: "stop",
    });
  });
});

// ---------------------------------------------------------------------------
// turnOutcomeFromWire — fixture replay
// ---------------------------------------------------------------------------

describe("turnOutcomeFromWire", () => {
  it("decodes chat-turn-result-view.json with awaitingApproval + plan", () => {
    const raw = loadFixture<GeneratedChatTurnResultView>("chat-turn-result-view.json");
    const outcome = turnOutcomeFromWire(raw);

    expect(outcome.awaitingApproval).toBe(true);
    expect(outcome.messages).toHaveLength(1);
    expect(outcome.messages[0]?.role).toBe("assistant");
    expect(outcome.pendingPlan).not.toBeNull();
    const plan = outcome.pendingPlan as { nodes?: unknown; edges?: unknown } | null;
    expect(plan?.nodes).toBeDefined();
    expect(plan?.edges).toBeDefined();
  });

  it("decodes chat-turn-result-view-plan-null.json with pendingPlan null", () => {
    const raw = loadFixture<GeneratedChatTurnResultView>("chat-turn-result-view-plan-null.json");
    const outcome = turnOutcomeFromWire(raw);

    expect(outcome.awaitingApproval).toBe(false);
    expect(outcome.messages).toHaveLength(1);
    expect(outcome.pendingPlan).toBeNull();
  });

  it("every message converts to the right unix ms", () => {
    const raw = loadFixture<GeneratedChatTurnResultView>("chat-turn-result-view.json");
    const outcome = turnOutcomeFromWire(raw);
    expect(outcome.messages[0]?.createdAtUnixMs).toBe(1789899480000);
  });
});

// ---------------------------------------------------------------------------
// chatMessagesPageFromWire — fixture replay
// ---------------------------------------------------------------------------

describe("chatMessagesPageFromWire", () => {
  it("decodes chat-messages-page-view.json preserving order and unix-ms", () => {
    const raw = loadFixture<GeneratedChatMessagesPageView>("chat-messages-page-view.json");
    const messages = chatMessagesPageFromWire(raw);

    expect(messages).toHaveLength(2);
    expect(messages[0]?.role).toBe("assistant");
    expect(messages[1]?.role).toBe("user");
    expect(messages[0]?.createdAtUnixMs).toBe(1789899372000);
    expect(messages[1]?.createdAtUnixMs).toBe(1789899398500);
  });
});

// ---------------------------------------------------------------------------
// Realtime decoders
// ---------------------------------------------------------------------------

describe("decodeChatChunk", () => {
  it("decodes chat-chunk.json to a 'chunk' FeedMessage", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-chunk.json");
    const message = decodeChatChunk(raw, 1789899380000);

    expect(message.kind).toBe("chunk");
    if (message.kind !== "chunk") {
      return;
    }
    expect(message.sessionId).toBe("9f3e5d3a-5b1f-4d6f-9d77-2f7b9f1a6c3d");
    expect(message.seq).toBe(17);
    expect(message.text).toBe("Here is the plan, the relevant source listing,");
    expect(message.receivedAtUnixMs).toBe(1789899380000);
  });

  it("collapses null to 'unknown' with receivedAtUnixMs passed through", () => {
    const message = decodeChatChunk(null, 1789899380000);
    expect(message.kind).toBe("unknown");
    if (message.kind !== "unknown") {
      return;
    }
    expect(message.receivedAtUnixMs).toBe(1789899380000);
    expect(message.reason).toBe("malformed ChatChunk frame");
  });

  it("collapses undefined to 'unknown'", () => {
    const message = decodeChatChunk(undefined, 1789899380000);
    expect(message.kind).toBe("unknown");
  });

  it("collapses missing seq to 'unknown'", () => {
    const message = decodeChatChunk({ sessionId: "s1", text: "hi" }, 1789899380000);
    expect(message.kind).toBe("unknown");
  });

  it("collapses wrong-type seq to 'unknown'", () => {
    const message = decodeChatChunk(
      { sessionId: "s1", seq: "17", text: "hi" },
      1789899380000,
    );
    expect(message.kind).toBe("unknown");
  });

  it("collapses missing text to 'unknown'", () => {
    const message = decodeChatChunk({ sessionId: "s1", seq: 1 }, 1789899380000);
    expect(message.kind).toBe("unknown");
  });

  it("collapses non-string text to 'unknown'", () => {
    const message = decodeChatChunk(
      { sessionId: "s1", seq: 1, text: 42 },
      1789899380000,
    );
    expect(message.kind).toBe("unknown");
  });

  it("never leaks the raw payload into the reason field", () => {
    const leak = "SECRET-LEAK-CHECK-DO-NOT-DUMP-42";
    const message = decodeChatChunk(
      { sessionId: "s1", seq: "not-a-number", text: leak },
      1789899380000,
    );
    expect(message.kind).toBe("unknown");
    if (message.kind !== "unknown") {
      return;
    }
    expect(message.reason).toBeDefined();
    expect(message.reason?.includes(leak)).toBe(false);
    expect(message.reason?.includes("SECRET")).toBe(false);
  });

  it("recovers sessionId when other fields are malformed", () => {
    const message = decodeChatChunk(
      { sessionId: "recovered-session", seq: "not-a-number", text: "ok" },
      1789899380000,
    );
    expect(message.kind).toBe("unknown");
    if (message.kind !== "unknown") {
      return;
    }
    expect(message.sessionId).toBe("recovered-session");
  });

  it("tolerates extra unknown fields (forward-compat)", () => {
    const message = decodeChatChunk(
      {
        sessionId: "s1",
        seq: 1,
        text: "ok",
        futureField: "ignored",
        nestedThing: { whatever: true },
      },
      1789899380000,
    );
    expect(message.kind).toBe("chunk");
    if (message.kind !== "chunk") {
      return;
    }
    expect(message.text).toBe("ok");
  });
});

describe("decodeChatTurnComplete", () => {
  it("decodes chat-turn-complete.json to a 'turn-complete' FeedMessage", () => {
    const raw = loadFixture<Record<string, unknown>>("chat-turn-complete.json");
    const message = decodeChatTurnComplete(raw, 1789899385000);

    expect(message.kind).toBe("turn-complete");
    if (message.kind !== "turn-complete") {
      return;
    }
    expect(message.sessionId).toBe("9f3e5d3a-5b1f-4d6f-9d77-2f7b9f1a6c3d");
    expect(message.outcome).toBe("approved");
    expect(message.receivedAtUnixMs).toBe(1789899385000);
  });

  it("falls back to outcome='unknown' when outcome is not a string", () => {
    const message = decodeChatTurnComplete(
      { sessionId: "s1", outcome: 42 },
      1789899385000,
    );
    expect(message.kind).toBe("turn-complete");
    if (message.kind !== "turn-complete") {
      return;
    }
    expect(message.outcome).toBe("unknown");
  });

  it("collapses null/undefined to 'unknown'", () => {
    for (const frame of [null, undefined] as const) {
      const message = decodeChatTurnComplete(frame, 1789899385000);
      expect(message.kind).toBe("unknown");
      if (message.kind !== "unknown") {
        continue;
      }
      expect(message.reason).toBe("malformed ChatTurnComplete frame");
    }
  });

  it("never leaks the raw payload into the reason field on the unknown path", () => {
    const leak = "SECRET-LEAK-CHECK-DO-NOT-DUMP-99";
    const message = decodeChatTurnComplete(
      { outcome: leak },
      1789899385000,
    );
    expect(message.kind).toBe("unknown");
    if (message.kind !== "unknown") {
      return;
    }
    expect(message.reason).toBeDefined();
    expect(message.reason?.includes(leak)).toBe(false);
    expect(message.reason?.includes("SECRET")).toBe(false);
  });

  it("collapses missing sessionId to 'unknown'", () => {
    const message = decodeChatTurnComplete({ outcome: "approved" }, 1789899385000);
    expect(message.kind).toBe("unknown");
  });
});

// ---------------------------------------------------------------------------
// classifyProblem
// ---------------------------------------------------------------------------

describe("classifyProblem", () => {
  it("maps every status branch deterministically", () => {
    const cases: Array<[number, ProblemKind]> = [
      [400, "validation"],
      [401, "unauthorized"],
      [403, "forbidden"],
      [404, "not-found"],
      [409, "conflict"],
      [429, "rate-limited"],
      [500, "server-error"],
      [502, "server-error"],
      [503, "unavailable"],
      [504, "server-error"],
    ];
    for (const [status, expected] of cases) {
      expect(classifyProblem({ status })).toBe(expected);
    }
  });

  it("collapses any unrecognised status to 'unknown'", () => {
    for (const status of [0, 100, 200, 301, 418, 451, 600, 999]) {
      expect(classifyProblem({ status })).toBe("unknown");
    }
  });

  it("uses code-suffix refinement when the suffix matches a kind", () => {
    expect(classifyProblem({ status: 403, code: "knowledge.forbidden" })).toBe("forbidden");
    expect(classifyProblem({ status: 404, code: "chat.session_not_found" })).toBe("not-found");
    expect(classifyProblem({ status: 503, code: "chat.unavailable" })).toBe("unavailable");
  });

  it("lets code-suffix refinement beat the status mapping", () => {
    expect(classifyProblem({ status: 400, code: "chat.conflict" })).toBe("conflict");
    expect(classifyProblem({ status: 500, code: "chat.validation" })).toBe("validation");
    expect(classifyProblem({ status: 401, code: "chat.forbidden" })).toBe("forbidden");
  });

  it("normalises underscore suffixes to hyphenated kind names", () => {
    expect(classifyProblem({ status: 429, code: "chat.rate_limited" })).toBe("rate-limited");
    expect(classifyProblem({ status: 404, code: "session.not_found" })).toBe("not-found");
    expect(classifyProblem({ status: 500, code: "upstream.server_error" })).toBe("server-error");
  });

  it("ignores codes whose suffix is not a known kind", () => {
    expect(classifyProblem({ status: 404, code: "chat.session_not_found" })).toBe("not-found");
    expect(classifyProblem({ status: 400, code: "weird.shape" })).toBe("validation");
  });

  it("classifies every fixture ProblemDetails file", () => {
    const cases: Array<[string, string, ProblemKind]> = [
      ["problem-details-404.json", "chat.session_not_found", "not-found"],
      ["problem-details-409.json", "chat.conflict", "conflict"],
      ["problem-details-429.json", "chat.rate_limited", "rate-limited"],
    ];
    for (const [name, expectedCode, expectedKind] of cases) {
      const raw = loadFixture<Record<string, unknown>>(name);
      const status = typeof raw.status === "number" ? raw.status : 0;
      const code = typeof raw.code === "string" ? raw.code : undefined;
      expect(code).toBe(expectedCode);
      expect(classifyProblem({ status, code })).toBe(expectedKind);
    }
  });

  it("covers every ProblemKind branch from the type's full enum", () => {
    const observed = new Set<ProblemKind>();
    const cases: Array<{ readonly status: number; readonly code?: string }> = [
      { status: 400 },
      { status: 401 },
      { status: 403 },
      { status: 404 },
      { status: 409 },
      { status: 429 },
      { status: 503 },
      { status: 500 },
      { status: 200 },
      { status: 400, code: "chat.conflict" },
      { status: 400, code: "chat.validation" },
    ];
    for (const input of cases) {
      observed.add(classifyProblem(input));
    }
    for (const kind of ALL_PROBLEM_KINDS) {
      expect(observed.has(kind)).toBe(true);
    }
  });
});

// ---------------------------------------------------------------------------
// Helpers — inline minimal payload for role-fallback tests
// ---------------------------------------------------------------------------

function minimalMessage(): GeneratedChatMessageView {
  return {
    id: "test-id",
    role: "user",
    content: "test",
    toolName: null,
    parts: null,
    meta: null,
    createdAt: "2026-09-20T10:16:12.000+00:00",
  };
}
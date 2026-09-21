/**
 * Public surface of the kernel package. Renderers import from here —
 * everything else (`fakes.ts`, `adapters/`) is wiring for hosts and
 * tests.
 */
export type { ClientEvent, ClientKernel, ClientSnapshot, DispatchResult, Unsubscribe } from "./kernel"
export { createClientKernel, type ClientKernelOptions } from "./kernel"
export type {
  AttentionItem,
  AttentionListener,
  AttentionPriority,
  AttentionReason,
  AttentionSignal,
  AttentionSource,
  AttentionStateKind,
} from "./attention"
export {
  DEFAULT_EVIDENCE_WINDOW_MS,
  DEFAULT_FAILED_WINDOW_MS,
  DEFAULT_STALL_THRESHOLD_MS,
  deriveAttention,
} from "./attention"
export type { EventFeedPort, FeedMessage } from "./feed"
export type {
  ApprovalPort,
  ConversationPort,
  HarnessEffectPorts,
  RealtimePort,
  TurnOutcome,
  WorkspaceStore,
} from "../harness/effect-runner"
export type { UserIntent } from "../harness/intents"
export { translateIntent } from "../harness/intents"
export type {
  DecisionReceipt,
  DecisionReceiptStore,
  DecisionVerdict,
} from "./receipts"
export {
  createDecisionReceiptStore,
  defaultStateDirectory,
  fingerprintFor,
  receiptsFilePath,
} from "./receipts"
export type {
  CatchUpRequest,
  CatchUpResult,
  CursorEntry,
  CursorStore,
  CursorStoreOptions,
} from "./cursors"
export {
  createCursorStore,
  cursorsFilePath,
  decodeCursor,
  encodeCursor,
} from "./cursors"
export type { DraftStore, DraftStoreOptions } from "./drafts"
export {
  createDraftStore,
  draftFilePath,
  DRAFTS_SUBDIR,
} from "./drafts"
export type {
  ReconnectListener,
  ReconnectOrchestrator,
  ReconnectState,
} from "./reconnect"
export { createReconnectOrchestrator } from "./reconnect"
export type {
  SessionFilter,
  SessionMeta,
  SessionMetaUpdate,
  SessionStore,
  SessionStoreOptions,
} from "./sessions"
export {
  createSessionStore,
  decodeMeta,
  encodeMeta,
  SESSIONS_FILE,
  sessionsFilePath,
} from "./sessions"

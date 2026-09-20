/**
 * Public surface of the kernel package. Renderers import from here —
 * everything else (`fakes.ts`, `adapters/`) is wiring for hosts and
 * tests.
 */
export type { ClientEvent, ClientKernel, ClientSnapshot, Unsubscribe } from "./kernel"
export { createClientKernel, type ClientKernelOptions } from "./kernel"
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

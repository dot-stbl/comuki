export type HarnessLayer = "base" | "overlay" | "notification"

export interface LayerStackInput {
  readonly overlay: boolean
  readonly notifications: boolean
}

export interface LayerState {
  readonly overlayId: string | null
}

export type LayerAction =
  | { readonly type: "open"; readonly id: string }
  | { readonly type: "close" }

export function resolveLayerStack(input: LayerStackInput): readonly HarnessLayer[] {
  return [
    "base",
    ...(input.overlay ? (["overlay"] as const) : []),
    ...(input.notifications ? (["notification"] as const) : []),
  ]
}

export function reduceLayerState(
  state: LayerState,
  action: LayerAction
): LayerState {
  return action.type === "open"
    ? { overlayId: action.id }
    : { overlayId: null }
}

import { describe, expect, test } from "bun:test"
import { reduceLayerState, resolveLayerStack } from "./layers"

describe("resolveLayerStack", () => {
  test("keeps paint order base, overlay, notification", () => {
    expect(resolveLayerStack({ overlay: true, notifications: true })).toEqual([
      "base",
      "overlay",
      "notification",
    ])
  })

  test("omits inactive layers", () => {
    expect(resolveLayerStack({ overlay: false, notifications: false })).toEqual([
      "base",
    ])
  })
})

describe("reduceLayerState", () => {
  test("opens and closes logical overlays without business state", () => {
    const opened = reduceLayerState(
      { overlayId: null },
      { type: "open", id: "palette" }
    )

    expect(opened).toEqual({ overlayId: "palette" })
    expect(reduceLayerState(opened, { type: "close" })).toEqual({
      overlayId: null,
    })
  })
})

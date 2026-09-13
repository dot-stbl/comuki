import type { Meta, StoryObj } from "@storybook/react"

import { ARTIFACT_MIME } from "@/domains/artifacts/model/types"

import { EvidenceThumbnail } from "./evidence-thumbnail"

/**
 * The PNG bytes are the same 32x32 catalog fixture the pane story uses, so the
 * thumbnail's `<img>` branch renders from a data URL — no network, no 404.
 */
const PNG_DATA_URL =
  "data:image/png;base64," +
  "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAzSURBVFhH7c4hAQAwCABBcpKTEGs1PAHAnHjz6qLy/ctiju0AAAAAAAAAAAAAAAAAAAAaDEhUprIt2GkAAAAASUVORK5CYII="

const meta = {
  title: "UI Kit/Overlays/EvidenceThumbnail",
  component: EvidenceThumbnail,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: {
    src: PNG_DATA_URL,
    contentType: ARTIFACT_MIME.png,
    filename: "screenshot.png",
    onActivate: () => undefined,
  },
  argTypes: {
    contentType: {
      control: "select",
      options: [
        ARTIFACT_MIME.png,
        ARTIFACT_MIME.html,
        ARTIFACT_MIME.svg,
        "application/octet-stream",
      ],
    },
    size: {
      control: "radio",
      options: ["sm", "md"],
    },
  },
} satisfies Meta<typeof EvidenceThumbnail>

export default meta
type Story = StoryObj<typeof meta>

/** `image/png` — the `<img>` branch, the run-strip's natural case. */
export const Png: Story = {}

/** `text/html` — the labelled tile, never the document inside the box. */
export const Html: Story = {
  args: {
    src: "about:blank",
    contentType: ARTIFACT_MIME.html,
    filename: "pr-report.html",
  },
}

/** `image/svg+xml` — same tile branch, different label. */
export const Svg: Story = {
  args: {
    src: "about:blank",
    contentType: ARTIFACT_MIME.svg,
    filename: "diag-bundle.svg",
  },
}

/** Unknown mime — the labelled tile with the generic `file` word. */
export const UnknownMime: Story = {
  args: {
    src: "about:blank",
    contentType: "application/octet-stream",
    filename: "report.bin",
  },
}

/** `sm` is the ticket-inbox row's natural size. */
export const Small: Story = {
  args: { size: "sm" },
}

/** A long filename still fits in the title attribute; the button keeps its
 *  fixed box regardless of the name. */
export const LongFilename: Story = {
  args: {
    filename: "2026-09-13T15-22-04Z_run_2f9c1a_patch-412--38_pr-report-v3.html",
  },
}

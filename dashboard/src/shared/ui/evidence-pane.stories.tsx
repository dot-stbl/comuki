import { useEffect, useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { ARTIFACT_MIME } from "@/domains/artifacts/model/types"

import { Button } from "./button"
import { EvidencePane, type EvidencePaneProps } from "./evidence-pane"

/**
 * A tiny 32x32 PNG produced once for the catalog (the dashboard serves the
 * real bytes from `VisualArtifactsResponseHelpers` in slice 1). Embedding it
 * keeps the story offline — the image renders from the data URL, the modal
 * exercises the `<img>` branch, and there is no network dependency.
 */
const PNG_DATA_URL =
  "data:image/png;base64," +
  "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAzSURBVFhH7c4hAQAwCABBcpKTEGs1PAHAnHjz6qLy/ctiju0AAAAAAAAAAAAAAAAAAAAaDEhUprIt2GkAAAAASUVORK5CYII="

/**
 * The HTML body the iframe sandbox renders for the catalog. A blob URL gives
 * the iframe a same-page origin so `sandbox="allow-scripts"` (no
 * `allow-same-origin`) is the only thing standing between the worker markup
 * and the dashboard — the same posture the host pins in production.
 */
const HTML_BODY =
  "<!doctype html><meta charset='utf-8'><title>pr-report</title>" +
  "<style>body{font:14px/1.5 system-ui;padding:24px;background:#FBFBFA;color:#15171B;margin:0}" +
  "h1{font-size:16px;margin:0 0 12px}.row{display:flex;gap:8px;margin:6px 0}" +
  ".k{color:#8a8d92;font-family:ui-monospace,monospace;font-size:12px}</style>" +
  "<h1>pr-report.html</h1><div class='row'><span class='k'>run</span>run_2f9c1a</div>" +
  "<div class='row'><span class='k'>outcome</span>success</div>" +
  "<div class='row'><span class='k'>tests</span>142 / 142</div>" +
  "<div class='row'><span class='k'>patch</span>+412 / -38</div>"

const SVG_BODY =
  "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 320 180'>" +
  "<rect width='320' height='180' fill='#FBFBFA'/>" +
  "<rect x='12' y='12' width='296' height='36' fill='#15171B' rx='4'/>" +
  "<text x='24' y='36' fill='#FBFBFA' font-family='ui-monospace,monospace' font-size='14'>diag-bundle.svg</text>" +
  "<polyline points='20,150 60,120 100,130 140,90 180,100 220,60 260,80 300,40'" +
  " fill='none' stroke='#B45AC8' stroke-width='2'/>" +
  "<g fill='#8a8d92' font-family='ui-monospace,monospace' font-size='10'>" +
  "<text x='20' y='170'>t=0</text><text x='300' y='170'>t=8h</text></g></svg>"

/**
 * A blob-URL hook: returns a `Blob` URL for `body` and revokes it on unmount
 * or when the body changes. The dashboard's production GET goes through the
 * host (cookie auth, `nosniff` cache), but the shape is the same — `src` is
 * a URL the iframe can render.
 */
function useBlobUrl(body: string, mime: string): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    const blob = new Blob([body], { type: mime })
    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)
    return () => {
      URL.revokeObjectURL(objectUrl)
    }
  }, [body, mime])
  return url
}

function PngDemo(args: EvidencePaneProps) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open evidence</Button>
      <EvidencePane
        {...args}
        src={PNG_DATA_URL}
        open={open}
        onClose={() => setOpen(false)}
      />
    </>
  )
}

function HtmlDemo(args: EvidencePaneProps) {
  const [open, setOpen] = useState(false)
  const src = useBlobUrl(HTML_BODY, ARTIFACT_MIME.html)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open html</Button>
      <EvidencePane
        {...args}
        src={src ?? "about:blank"}
        contentType={ARTIFACT_MIME.html}
        open={open}
        onClose={() => setOpen(false)}
      />
    </>
  )
}

function SvgDemo(args: EvidencePaneProps) {
  const [open, setOpen] = useState(false)
  const src = useBlobUrl(SVG_BODY, ARTIFACT_MIME.svg)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open svg</Button>
      <EvidencePane
        {...args}
        src={src ?? "about:blank"}
        contentType={ARTIFACT_MIME.svg}
        open={open}
        onClose={() => setOpen(false)}
      />
    </>
  )
}

function EmptyDemo(args: EvidencePaneProps) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open unknown</Button>
      <EvidencePane
        {...args}
        src="about:blank"
        contentType="application/octet-stream"
        filename="report.bin"
        sizeBytes={4096}
        open={open}
        onClose={() => setOpen(false)}
      />
    </>
  )
}

const meta = {
  title: "UI Kit/Overlays/EvidencePane",
  component: EvidencePane,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: {
    open: false,
    filename: "screenshot.png",
    contentType: ARTIFACT_MIME.png,
    src: PNG_DATA_URL,
    sizeBytes: 24576,
    onClose: () => undefined,
  },
} satisfies Meta<typeof EvidencePane>

export default meta
type Story = StoryObj<typeof meta>

/** `image/png` opens in a sized `<img>`. The host's cookie auth rides the GET. */
export const Png: Story = {
  render: (args) => <PngDemo {...args} />,
}

/** `text/html` lands in a sandboxed iframe — no `allow-same-origin`, the
 *  CSP closes the server-side half of the same guarantee. */
export const Html: Story = {
  render: (args) => <HtmlDemo {...args} />,
}

/** `image/svg+xml` takes the iframe path too — markup is script-capable. */
export const Svg: Story = {
  render: (args) => <SvgDemo {...args} />,
}

/** Unknown mime: the pane stays open but draws the empty-state copy. */
export const UnknownMime: Story = {
  render: (args) => <EmptyDemo {...args} />,
}

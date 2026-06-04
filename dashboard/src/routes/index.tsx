import { createFileRoute } from "@tanstack/react-router"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

export const Route = createFileRoute("/")({
  component: IndexComponent,
})

function IndexComponent() {
  return (
    <main className="container mx-auto max-w-3xl space-y-8 p-8">
      <header className="space-y-2">
        <h1 className="font-mono text-3xl font-semibold tracking-tight">Comuki</h1>
        <p className="text-muted-foreground">
          Operational interface for the agent platform. Status: Phase 2 bootstrap.
        </p>
      </header>

      <Card>
        <CardHeader>
          <CardTitle>Dashboard scaffold</CardTitle>
          <CardDescription>
            {`Vite + React 19 + TypeScript + Tailwind v4. ${"56"} shadcn/ui components in
            place. Routing via TanStack Router (file-based). Theme defaults to dark per
            comuki-dashboard-designspec.md § 2.`}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <p className="text-sm text-muted-foreground">
            Design tokens still match shadcn defaults. Apply
            {" "}<code className="bg-muted rounded px-1 py-0.5 font-mono text-xs">comuki-dashboard-designspec.md</code>
            {" "}§ 3 tokens (bg <code className="bg-muted rounded px-1 py-0.5 font-mono text-xs">#1A1815</code>,
            accent <code className="bg-muted rounded px-1 py-0.5 font-mono text-xs">#C75D43</code>, status semantics)
            when ready.
          </p>
          <div className="flex gap-2">
            <Button>Get started</Button>
            <Button variant="outline">View docs</Button>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 sm:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Connected to backend</CardTitle>
            <CardDescription>Phase 7 — SignalR + Comuki.Platform.Api.Public</CardDescription>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Pages planned</CardTitle>
            <CardDescription>intake, runs, approvals, trace, settings, knowledge</CardDescription>
          </CardHeader>
        </Card>
      </div>
    </main>
  )
}

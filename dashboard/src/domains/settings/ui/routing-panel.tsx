import { useEffect } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import { ChevronsUp, Cpu } from "lucide-react"
import { useForm } from "react-hook-form"

import type { ModelRoute } from "@/domains/settings/model/types"
import {
  routingFormSchema,
  type RoutingFormValues,
} from "@/domains/settings/model/routing-form"
import { Alert, AlertDescription, AlertTitle } from "@/shared/ui/alert"
import { Button } from "@/shared/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/shared/ui/card"
import { Input } from "@/shared/ui/input"
import { Label } from "@/shared/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

export interface RoutingPanelProps {
  routes: ModelRoute[]
  busy?: boolean
  onSave: (values: RoutingFormValues) => void
}

export function RoutingPanel({ routes, busy = false, onSave }: RoutingPanelProps) {
  const lead = routes.find((route) => route.role === "lead")
  const worker = routes.find((route) => route.role === "worker")
  const judge = routes.find((route) => route.role === "judge")

  const form = useForm<RoutingFormValues>({
    resolver: zodResolver(routingFormSchema),
    defaultValues: {
      leadModel: lead?.model ?? "",
      workerModel: worker?.model ?? "",
      judgeModel: judge?.model ?? "",
    },
  })

  useEffect(() => {
    form.reset({
      leadModel: lead?.model ?? "",
      workerModel: worker?.model ?? "",
      judgeModel: judge?.model ?? "",
    })
  }, [form, lead?.model, worker?.model, judge?.model])

  return (
    <div className="flex flex-col gap-3">
      <Card>
        <CardHeader className="border-b">
          <CardTitle>ModelRouting</CardTitle>
          <CardDescription>role → physical model</CardDescription>
        </CardHeader>
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>role</TableHead>
                <TableHead>model</TableHead>
                <TableHead>usage</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {routes.map((route) => (
                <TableRow key={route.role}>
                  <TableCell>
                    <span className="inline-flex items-center gap-1 font-mono text-xs">
                      <Cpu className="size-3" />
                      {route.role}
                    </span>
                  </TableCell>
                  <TableCell>{route.model}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {route.use}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="border-b">
          <CardTitle>Edit role → model map</CardTitle>
          <CardDescription>leading / worker / judge</CardDescription>
        </CardHeader>
        <form
          onSubmit={form.handleSubmit((values) => {
            onSave(values)
          })}
        >
          <CardContent className="grid gap-3 sm:grid-cols-3">
            <div className="grid gap-1.5">
              <Label htmlFor="leadModel">lead</Label>
              <Input id="leadModel" {...form.register("leadModel")} />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="workerModel">worker</Label>
              <Input id="workerModel" {...form.register("workerModel")} />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="judgeModel">judge</Label>
              <Input id="judgeModel" {...form.register("judgeModel")} />
            </div>
          </CardContent>
          <CardFooter className="justify-end border-t">
            <Button type="submit" size="sm" disabled={busy}>
              Save routing
            </Button>
          </CardFooter>
        </form>
      </Card>

      <Alert>
        <ChevronsUp />
        <AlertTitle>Escalation policy</AlertTitle>
        <AlertDescription>
          2 failed retries on worker → escalate to lead. Red type gate → debug
          agent with a pinned revision.
        </AlertDescription>
      </Alert>
    </div>
  )
}

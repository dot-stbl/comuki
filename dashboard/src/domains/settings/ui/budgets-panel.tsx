import { useEffect } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm } from "react-hook-form"

import type { Budgets } from "@/domains/settings/model/types"
import {
  budgetFormSchema,
  type BudgetFormValues,
} from "@/domains/settings/model/budget-form"
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
import { Progress } from "@/shared/ui/progress"
import { Switch } from "@/shared/ui/switch"

export interface BudgetsPanelProps {
  budgets: Budgets
  busy?: boolean
  onSave: (values: BudgetFormValues) => void
}

export function BudgetsPanel({
  budgets,
  busy = false,
  onSave,
}: BudgetsPanelProps) {
  const usedPct = Math.min(
    100,
    Math.round((budgets.usedUsd / budgets.globalUsd) * 100)
  )

  const form = useForm<BudgetFormValues>({
    resolver: zodResolver(budgetFormSchema),
    defaultValues: {
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
      globalUsd: budgets.globalUsd,
      killSwitch: budgets.killSwitch,
      pauseSwarm: budgets.pauseSwarm,
    },
  })

  useEffect(() => {
    form.reset({
      perTaskUsd: budgets.perTaskUsd,
      perAppUsd: budgets.perAppUsd,
      globalUsd: budgets.globalUsd,
      killSwitch: budgets.killSwitch,
      pauseSwarm: budgets.pauseSwarm,
    })
  }, [budgets, form])

  return (
    <div className="flex flex-col gap-3">
      <Card size="sm">
        <CardHeader className="border-b">
          <CardDescription>Proxy budget</CardDescription>
          <CardTitle className="font-mono text-2xl">
            {usedPct}
            <span className="text-sm text-muted-foreground">%</span>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-2">
          <Progress value={usedPct} />
          <p className="font-mono text-xs text-muted-foreground">
            ${budgets.usedUsd.toFixed(0)} / ${budgets.globalUsd.toFixed(0)} ·
            kill-switch at cap
          </p>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="border-b">
          <CardTitle>Budget caps</CardTitle>
          <CardDescription>
            per-task / per-app / global · kill-switch &amp; pause swarm
          </CardDescription>
        </CardHeader>
        <form
          onSubmit={form.handleSubmit((values) => {
            onSave(values)
          })}
        >
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <div className="grid gap-1.5">
              <Label htmlFor="perTaskUsd">per task (USD)</Label>
              <Input
                id="perTaskUsd"
                type="number"
                step="0.01"
                {...form.register("perTaskUsd")}
              />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="perAppUsd">per app (USD)</Label>
              <Input
                id="perAppUsd"
                type="number"
                step="1"
                {...form.register("perAppUsd")}
              />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="globalUsd">global (USD)</Label>
              <Input
                id="globalUsd"
                type="number"
                step="1"
                {...form.register("globalUsd")}
              />
            </div>
            <div className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 sm:col-span-3">
              <div className="flex flex-col gap-0.5">
                <Label htmlFor="killSwitch">Kill-switch</Label>
                <p className="text-xs text-muted-foreground">
                  Hard-stop all new claims when global cap is hit or toggled on.
                </p>
              </div>
              <Controller
                control={form.control}
                name="killSwitch"
                render={({ field }) => (
                  <Switch
                    id="killSwitch"
                    checked={field.value}
                    onCheckedChange={field.onChange}
                  />
                )}
              />
            </div>
            <div className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 sm:col-span-3">
              <div className="flex flex-col gap-0.5">
                <Label htmlFor="pauseSwarm">Pause swarm</Label>
                <p className="text-xs text-muted-foreground">
                  Soft pause — running workers finish; no new containers start.
                </p>
              </div>
              <Controller
                control={form.control}
                name="pauseSwarm"
                render={({ field }) => (
                  <Switch
                    id="pauseSwarm"
                    checked={field.value}
                    onCheckedChange={field.onChange}
                  />
                )}
              />
            </div>
          </CardContent>
          <CardFooter className="justify-end border-t">
            <Button type="submit" size="sm" disabled={busy}>
              Save budgets
            </Button>
          </CardFooter>
        </form>
      </Card>
    </div>
  )
}

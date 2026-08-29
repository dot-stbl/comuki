import { Info } from "lucide-react"

import type { SwarmRule } from "@/domains/settings/model/types"
import { Alert, AlertDescription, AlertTitle } from "@/shared/ui/alert"
import { Badge } from "@/shared/ui/badge"
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/shared/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

export interface RulesPanelProps {
  rules: SwarmRule[]
}

export function RulesPanel({ rules }: RulesPanelProps) {
  return (
    <div className="flex flex-col gap-3">
      <Alert>
        <Info />
        <AlertTitle>No conflicts found</AlertTitle>
        <AlertDescription>
          {rules.length} active rules · scopes don&apos;t overlap.
        </AlertDescription>
      </Alert>
      <Card>
        <CardHeader className="border-b">
          <CardTitle>RulesEditor</CardTitle>
        </CardHeader>
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>rule</TableHead>
                <TableHead>scope</TableHead>
                <TableHead>kind</TableHead>
                <TableHead>version</TableHead>
                <TableHead>description</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rules.map((rule) => (
                <TableRow key={rule.id}>
                  <TableCell className="font-mono">{rule.id}</TableCell>
                  <TableCell className="font-mono text-muted-foreground">
                    {rule.scope}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={rule.kind === "hard" ? "secondary" : "outline"}
                    >
                      {rule.kind}
                    </Badge>
                  </TableCell>
                  <TableCell className="font-mono text-muted-foreground">
                    @{rule.ver}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {rule.desc}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  )
}

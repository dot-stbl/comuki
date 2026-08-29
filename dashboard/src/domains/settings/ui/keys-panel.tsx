import { Check } from "lucide-react"

import type { ProviderKey } from "@/domains/settings/model/types"
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

export interface KeysPanelProps {
  keys: ProviderKey[]
}

export function KeysPanel({ keys }: KeysPanelProps) {
  return (
    <Card>
      <CardHeader className="border-b">
        <CardTitle>KeysPanel</CardTitle>
      </CardHeader>
      <CardContent className="px-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>provider</TableHead>
              <TableHead>scope</TableHead>
              <TableHead>rotation</TableHead>
              <TableHead>status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {keys.map((key) => (
              <TableRow key={key.provider}>
                <TableCell className="font-mono">{key.provider}</TableCell>
                <TableCell className="text-muted-foreground">
                  {key.scope}
                </TableCell>
                <TableCell className="font-mono text-muted-foreground">
                  {key.rotation}
                </TableCell>
                <TableCell>
                  {key.status === "ok" ? (
                    <Badge variant="outline">
                      <Check />
                      ok
                    </Badge>
                  ) : (
                    <Badge variant="secondary">{key.statusLabel}</Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}

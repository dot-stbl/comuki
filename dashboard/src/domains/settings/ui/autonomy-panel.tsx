import { Users, Zap } from "lucide-react"

import type { AutonomyRow } from "@/domains/settings/model/types"
import { Badge } from "@/shared/ui/badge"
import {
  Card,
  CardContent,
  CardDescription,
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

export interface AutonomyPanelProps {
  rows: AutonomyRow[]
}

export function AutonomyPanel({ rows }: AutonomyPanelProps) {
  return (
    <Card>
      <CardHeader className="border-b">
        <CardTitle>AutonomyMatrix</CardTitle>
        <CardDescription>what&apos;s auto · what needs a human</CardDescription>
      </CardHeader>
      <CardContent className="px-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>change class</TableHead>
              <TableHead className="text-right">mode</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.cls}>
                <TableCell>{row.cls}</TableCell>
                <TableCell className="text-right">
                  {row.mode === "auto" ? (
                    <Badge>
                      <Zap />
                      auto
                    </Badge>
                  ) : (
                    <Badge variant="outline">
                      <Users />
                      human
                    </Badge>
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

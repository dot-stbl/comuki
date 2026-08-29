import type { AppRegistryItem } from "@/domains/settings/model/types"
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

export interface AppsPanelProps {
  apps: AppRegistryItem[]
}

export function AppsPanel({ apps }: AppsPanelProps) {
  return (
    <Card>
      <CardHeader className="border-b">
        <CardTitle>AppsRegistry</CardTitle>
      </CardHeader>
      <CardContent className="px-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>app</TableHead>
              <TableHead>repo</TableHead>
              <TableHead>stack</TableHead>
              <TableHead>envs</TableHead>
              <TableHead>deploy</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {apps.map((app) => (
              <TableRow key={app.name}>
                <TableCell>
                  <span className="inline-flex items-center gap-1.5 font-mono text-xs">
                    <span className="size-1.5 rounded-full bg-primary" />
                    {app.name}
                  </span>
                </TableCell>
                <TableCell className="font-mono text-muted-foreground">
                  {app.repo}
                </TableCell>
                <TableCell>{app.stack}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    {app.envs.map((envName) => (
                      <Badge key={envName} variant="outline">
                        {envName}
                      </Badge>
                    ))}
                  </div>
                </TableCell>
                <TableCell className="font-mono">{app.deploy}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}

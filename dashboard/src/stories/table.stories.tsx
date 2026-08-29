import type { Meta, StoryObj } from "@storybook/react"

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

const meta: Meta<typeof Table> = {
  title: "UI/Table",
  component: Table,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Table>

export const Default: Story = {
  render: () => (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Updated</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell>Run Alpha</TableCell>
          <TableCell>running</TableCell>
          <TableCell>2m ago</TableCell>
        </TableRow>
        <TableRow>
          <TableCell>Run Beta</TableCell>
          <TableCell>completed</TableCell>
          <TableCell>10m ago</TableCell>
        </TableRow>
      </TableBody>
    </Table>
  ),
}

export const Loading: Story = {
  render: () => (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead>Status</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {[1, 2].map((i) => (
          <TableRow key={i}>
            <TableCell><div className="h-4 w-24 animate-pulse rounded bg-muted" /></TableCell>
            <TableCell><div className="h-4 w-16 animate-pulse rounded bg-muted" /></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  ),
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell className="text-muted-foreground">No data available</TableCell>
        </TableRow>
      </TableBody>
    </Table>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Long Header Label Name</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell>
            This Is A Very Long Table Cell Text That Should Demonstrate Proper Text Handling In The Table Component
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  ),
}
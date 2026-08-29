import type { Meta, StoryObj } from "@storybook/react"

import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/shared/ui/card"
import { Button } from "@/shared/ui/button"

const meta: Meta<typeof Card> = {
  title: "UI/Card",
  component: Card,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Card>

export const Default: Story = {
  render: () => (
    <Card className="w-80">
      <CardHeader>
        <CardTitle>Card Title</CardTitle>
        <CardDescription>Card description text goes here.</CardDescription>
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground">
          Card content body text.
        </p>
      </CardContent>
      <CardFooter>
        <Button size="sm">Action</Button>
      </CardFooter>
    </Card>
  ),
}

export const Loading: Story = {
  render: () => (
    <Card className="w-80">
      <CardHeader>
        <div className="h-4 w-32 animate-pulse rounded bg-muted" />
        <div className="h-3 w-48 animate-pulse rounded bg-muted" />
      </CardHeader>
      <CardContent>
        <div className="h-3 w-full animate-pulse rounded bg-muted" />
      </CardContent>
      <CardFooter>
        <div className="h-7 w-20 animate-pulse rounded bg-muted" />
      </CardFooter>
    </Card>
  ),
}

export const Disabled: Story = {
  render: () => (
    <Card className="w-80 opacity-50">
      <CardHeader>
        <CardTitle>Disabled Card</CardTitle>
        <CardDescription>This card is disabled.</CardDescription>
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground">Content is muted.</p>
      </CardContent>
      <CardFooter>
        <Button size="sm" disabled>Action</Button>
      </CardFooter>
    </Card>
  ),
}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Card className="w-80">
      <CardContent>
        <p className="text-xs text-muted-foreground">No content</p>
      </CardContent>
    </Card>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Card className="w-80">
      <CardHeader>
        <CardTitle>
          This Is A Very Long Card Title That Should Demonstrate Proper Text
          Handling In The Card Component
        </CardTitle>
        <CardDescription>
          This description contains a lot of text that demonstrates how the
          card handles extended content without breaking the layout. It wraps
          properly and maintains readability across all viewport sizes.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground">
          This is a very long content text that demonstrates the card component
          handling extensive content without any layout issues. The text wraps
          correctly and maintains proper spacing throughout.
        </p>
      </CardContent>
      <CardFooter>
        <Button size="sm">This Is A Very Long Button Label</Button>
      </CardFooter>
    </Card>
  ),
}
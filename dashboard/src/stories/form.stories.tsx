import type { Meta, StoryObj } from "@storybook/react"
import { useForm } from "react-hook-form"

import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/shared/ui/_legacy/form"
import { Input } from "@/shared/ui/input"

const meta: Meta<typeof Form> = {
  title: "UI/Form",
  component: Form,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Form>

export const Default: Story = {
  render: () => {
    const form = useForm()
    return (
      <Form {...form}>
        <form className="space-y-4 w-72">
          <FormField
            control={form.control}
            name="email"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Email</FormLabel>
                <FormControl>
                  <Input placeholder="you@example.com" {...field} />
                </FormControl>
                <FormDescription>Enter your email address.</FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        </form>
      </Form>
    )
  },
}

export const Loading: Story = {}
export const Disabled: Story = {}
export const Error: Story = {}
export const Empty: Story = {}
export const WithLongText: Story = {}
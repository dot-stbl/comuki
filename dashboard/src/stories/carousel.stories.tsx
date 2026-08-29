import type { Meta, StoryObj } from "@storybook/react"

import { Carousel, CarouselContent, CarouselItem, CarouselNext, CarouselPrevious } from "@/shared/ui/carousel"

const meta: Meta<typeof Carousel> = {
  title: "UI/Carousel",
  component: Carousel,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Carousel>

export const Default: Story = {
  render: () => (
    <Carousel className="w-64">
      <CarouselContent>
        {["Slide 1", "Slide 2", "Slide 3", "Slide 4"].map((slide, i) => (
          <CarouselItem key={i}>
            <div className="flex h-40 items-center justify-center rounded-md border border-dashed bg-muted text-xs text-muted-foreground">
              {slide}
            </div>
          </CarouselItem>
        ))}
      </CarouselContent>
      <CarouselPrevious />
      <CarouselNext />
    </Carousel>
  ),
}

export const Loading: Story = {
  render: () => (
    <Carousel className="w-64">
      <CarouselContent>
        {[1, 2, 3].map((i) => (
          <CarouselItem key={i}>
            <div className="flex h-40 animate-pulse items-center justify-center rounded-md bg-muted text-xs text-muted-foreground">
              Loading...
            </div>
          </CarouselItem>
        ))}
      </CarouselContent>
    </Carousel>
  ),
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Carousel className="w-64">
      <CarouselContent>
        <CarouselItem>
          <div className="flex h-40 items-center justify-center rounded-md border border-dashed text-xs text-muted-foreground">
            No slides
          </div>
        </CarouselItem>
      </CarouselContent>
    </Carousel>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Carousel className="w-64">
      <CarouselContent>
        {["First Long Slide Title", "Second Very Long Slide Title That Should Wrap", "Third Slide"].map((slide, i) => (
          <CarouselItem key={i}>
            <div className="flex h-40 items-center justify-center rounded-md border border-dashed bg-muted p-4 text-center text-xs text-muted-foreground">
              {slide}
            </div>
          </CarouselItem>
        ))}
      </CarouselContent>
    </Carousel>
  ),
}
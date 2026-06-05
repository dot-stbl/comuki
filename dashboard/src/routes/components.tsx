import { createFileRoute } from "@tanstack/react-router"
import { type ReactNode, useState } from "react"
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { toast } from "sonner"
import {
  Home,
  LayoutGrid,
  PlusIcon,
  Trash2Icon,
  CopyIcon,
  SettingsIcon,
  BellIcon,
  SearchIcon,
  GitBranchIcon,
  MoonIcon,
  ChevronRightIcon,
  MenuIcon,
  XIcon,
  UserIcon,
  FileTextIcon,
  CheckIcon,
} from "lucide-react"

import { AppShell } from "@/components/layout/app-shell"
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from "@/components/ui/tooltip"
import { Toaster } from "@/components/ui/sonner"
import { Button } from "@/components/ui/button"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Checkbox } from "@/components/ui/checkbox"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Switch } from "@/components/ui/switch"
import { Slider } from "@/components/ui/slider"
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui/input-otp"
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { HoverCard, HoverCardContent, HoverCardTrigger } from "@/components/ui/hover-card"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/components/ui/drawer"
import {
  Combobox,
  ComboboxInput,
  ComboboxContent,
  ComboboxList,
  ComboboxItem,
  ComboboxLabel,
  ComboboxEmpty,
} from "@/components/ui/combobox"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"
import {
  Avatar,
  AvatarFallback,
  AvatarGroup,
  AvatarGroupCount,
  AvatarImage,
} from "@/components/ui/avatar"
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Separator } from "@/components/ui/separator"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Skeleton } from "@/components/ui/skeleton"
import { AspectRatio } from "@/components/ui/aspect-ratio"
import {
  Item,
  ItemContent,
  ItemDescription,
  ItemMedia,
  ItemTitle,
  ItemActions,
  ItemGroup,
  ItemSeparator,
} from "@/components/ui/item"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { Kbd, KbdGroup } from "@/components/ui/kbd"

export const Route = createFileRoute("/components")({
  component: ComponentsShowcase,
})

const navItems = [
  { label: "Home", href: "/", icon: Home },
  { label: "Components", href: "/components", icon: LayoutGrid },
]

function Section({
  title,
  subtitle,
  children,
}: {
  title: string
  subtitle?: string
  children: ReactNode
}) {
  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h2 className="font-mono text-lg font-semibold text-foreground">{title}</h2>
        {subtitle ? (
          <p className="font-mono text-sm text-muted-foreground">{subtitle}</p>
        ) : null}
      </div>
      <div className="rounded-md border border-border bg-card p-6">{children}</div>
    </section>
  )
}

function VariantLabel({ children }: { children: ReactNode }) {
  return (
    <span className="font-mono text-[0.625rem] uppercase tracking-wider text-muted-foreground">
      {children}
    </span>
  )
}

function ComponentsShowcase() {
  return (
    <AppShell navItems={navItems}>
      <TooltipProvider delayDuration={200}>
        <Toaster richColors position="top-right" />

        <div className="mx-auto max-w-6xl space-y-12">
          <ButtonsShowcase />
          <FormsShowcase />
          <OverlaysShowcase />
          <DataDisplayShowcase />
        </div>
      </TooltipProvider>
    </AppShell>
  )
}

// ─── SECTION 1: BUTTONS ─────────────────────────────────────────────────────

function ButtonsShowcase() {
  return (
    <Section title="Buttons" subtitle="Primary actions, toggles, and group patterns.">
      <div className="space-y-6">
        {/* Variants */}
        <div className="space-y-3">
          <VariantLabel>Variants</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="default">Default</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="destructive">Destructive</Button>
            <Button variant="link">Link</Button>
          </div>
        </div>

        {/* Sizes */}
        <div className="space-y-3">
          <VariantLabel>Sizes</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Button size="xs">xs</Button>
            <Button size="sm">sm</Button>
            <Button size="default">Default</Button>
            <Button size="lg">lg</Button>
          </div>
        </div>

        {/* Icon buttons */}
        <div className="space-y-3">
          <VariantLabel>Icon Sizes</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Button size="icon-xs" aria-label="Close">
              <XIcon />
            </Button>
            <Button size="icon-sm" aria-label="Settings">
              <SettingsIcon />
            </Button>
            <Button size="icon" aria-label="Add">
              <PlusIcon />
            </Button>
            <Button size="icon-lg" aria-label="Delete">
              <Trash2Icon />
            </Button>
          </div>
        </div>

        {/* Icon + text */}
        <div className="space-y-3">
          <VariantLabel>With Icons</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Button>
              <PlusIcon />
              New task
            </Button>
            <Button variant="outline">
              <CopyIcon />
              Copy ID
            </Button>
            <Button variant="secondary">
              <BellIcon />
              Notifications
            </Button>
            <Button variant="ghost">
              <SearchIcon />
              Search
            </Button>
          </div>
        </div>

        {/* States */}
        <div className="space-y-3">
          <VariantLabel>States</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Button disabled>Disabled</Button>
            <Button aria-invalid>Invalid</Button>
          </div>
        </div>

        {/* ButtonGroup */}
        <div className="space-y-3">
          <VariantLabel>Toggle Group</VariantLabel>
          <ToggleGroup type="single" defaultValue="left">
            <ToggleGroupItem value="left" aria-label="Left align">
              <MenuIcon />
            </ToggleGroupItem>
            <ToggleGroupItem value="center" aria-label="Center align">
              <LayoutGrid />
            </ToggleGroupItem>
            <ToggleGroupItem value="right" aria-label="Right align">
              <SettingsIcon />
            </ToggleGroupItem>
          </ToggleGroup>
        </div>
      </div>
    </Section>
  )
}

// ─── SECTION 2: FORMS ────────────────────────────────────────────────────────

const formSchema = z.object({
  username: z.string().min(2, "Must be at least 2 characters"),
  email: z.string().email("Invalid email address"),
  notes: z.string().optional(),
  enabled: z.boolean(),
})

type FormValues = z.infer<typeof formSchema>

function FormsShowcase() {
  const [sliderValue, setSliderValue] = useState([60])
  const [otpValue, setOtpValue] = useState("")
  const [selectValue, setSelectValue] = useState("")
  const [comboboxOpen, setComboboxOpen] = useState(false)
  const [comboboxValue, setComboboxValue] = useState("")

  const form = useForm<FormValues>({
    resolver: zodResolver(formSchema),
    defaultValues: {
      username: "",
      email: "",
      notes: "",
      enabled: false,
    },
  })

  function onSubmit(values: FormValues) {
    toast.success("Form submitted", {
      description: `${values.username} — enabled: ${values.enabled}`,
    })
    console.log(values)
  }

  const roles = [
    { value: "admin", label: "Admin" },
    { value: "editor", label: "Editor" },
    { value: "viewer", label: "Viewer" },
    { value: "operator", label: "Operator" },
    { value: "approver", label: "Approver" },
  ]

  return (
    <Section
      title="Forms"
      subtitle="Inputs, selects, checkboxes, sliders, and Form pattern with RHF + Zod."
    >
      <div className="space-y-6">
        {/* Input */}
        <div className="space-y-3">
          <VariantLabel>Input</VariantLabel>
          <div className="flex flex-wrap items-center gap-4">
            <div className="space-y-1">
              <Label htmlFor="input-default">Default</Label>
              <Input id="input-default" placeholder="Enter text..." />
            </div>
            <div className="space-y-1">
              <Label htmlFor="input-disabled">Disabled</Label>
              <Input id="input-disabled" placeholder="Disabled" disabled />
            </div>
            <div className="space-y-1">
              <Label htmlFor="input-error">Invalid</Label>
              <Input id="input-error" aria-invalid placeholder="Invalid" defaultValue="bad" />
            </div>
          </div>
        </div>

        {/* Textarea */}
        <div className="space-y-3">
          <VariantLabel>Textarea</VariantLabel>
          <Textarea
            placeholder="Describe the issue..."
            rows={3}
            className="max-w-sm"
          />
        </div>

        {/* Select */}
        <div className="space-y-3">
          <VariantLabel>Select</VariantLabel>
          <div className="flex items-center gap-4">
            <Select value={selectValue} onValueChange={setSelectValue}>
              <SelectTrigger className="w-48">
                <SelectValue placeholder="Select role" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectLabel>User roles</SelectLabel>
                  {roles.map((r) => (
                    <SelectItem key={r.value} value={r.value}>
                      {r.label}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
            {selectValue && (
              <span className="text-xs text-muted-foreground">Selected: {selectValue}</span>
            )}
          </div>
        </div>

        {/* Combobox (Base UI) */}
        <div className="space-y-3">
          <VariantLabel>Combobox (Base UI)</VariantLabel>
          <Combobox
            open={comboboxOpen}
            onOpenChange={setComboboxOpen}
            value={comboboxValue}
            onValueChange={(val) => {
              setComboboxValue(typeof val === "string" ? val : "")
            }}
          >
            <ComboboxInput
              placeholder="Search exchange..."
              className="w-56"
            />
            <ComboboxContent>
              <ComboboxList>
                <ComboboxLabel>Exchanges</ComboboxLabel>
                <ComboboxItem value="Binance">Binance</ComboboxItem>
                <ComboboxItem value="OKX">OKX</ComboboxItem>
                <ComboboxItem value="Bybit">Bybit</ComboboxItem>
                <ComboboxItem value="Bitget">Bitget</ComboboxItem>
                <ComboboxItem value="Gate.io">Gate.io</ComboboxItem>
              </ComboboxList>
              <ComboboxEmpty>No exchange found.</ComboboxEmpty>
            </ComboboxContent>
          </Combobox>
          {comboboxValue && (
            <span className="text-xs text-muted-foreground">Selected: {comboboxValue}</span>
          )}
        </div>

        {/* Checkbox & Switch */}
        <div className="space-y-3">
          <VariantLabel>Checkbox & Switch</VariantLabel>
          <div className="flex flex-wrap items-center gap-6">
            <div className="flex items-center gap-2">
              <Checkbox id="checkbox-optin" />
              <Label htmlFor="checkbox-optin" className="font-normal">Email notifications</Label>
            </div>
            <div className="flex items-center gap-2">
              <Switch id="switch-enabled" />
              <Label htmlFor="switch-enabled" className="font-normal">Enabled</Label>
            </div>
            <div className="flex items-center gap-2">
              <Switch id="switch-sm" size="sm" />
              <Label htmlFor="switch-sm" className="font-normal">Small</Label>
            </div>
          </div>
        </div>

        {/* RadioGroup */}
        <div className="space-y-3">
          <VariantLabel>Radio Group</VariantLabel>
          <RadioGroup defaultValue="medium">
            <div className="flex items-center gap-6">
              <div className="flex items-center gap-2">
                <RadioGroupItem value="low" id="radio-low" />
                <Label htmlFor="radio-low" className="font-normal">Low</Label>
              </div>
              <div className="flex items-center gap-2">
                <RadioGroupItem value="medium" id="radio-med" />
                <Label htmlFor="radio-med" className="font-normal">Medium</Label>
              </div>
              <div className="flex items-center gap-2">
                <RadioGroupItem value="high" id="radio-high" />
                <Label htmlFor="radio-high" className="font-normal">High</Label>
              </div>
            </div>
          </RadioGroup>
        </div>

        {/* Slider */}
        <div className="space-y-3">
          <VariantLabel>Slider</VariantLabel>
          <div className="w-64 space-y-2">
            <Slider
              value={sliderValue}
              onValueChange={setSliderValue}
              max={100}
              step={1}
            />
            <span className="text-xs text-muted-foreground">Value: {sliderValue[0]}</span>
          </div>
        </div>

        {/* InputOTP */}
        <div className="space-y-3">
          <VariantLabel>InputOTP</VariantLabel>
          <div className="space-y-2">
            <InputOTP
              maxLength={6}
              value={otpValue}
              onChange={(value) => setOtpValue(value)}
            >
              <InputOTPGroup>
                <InputOTPSlot index={0} />
                <InputOTPSlot index={1} />
                <InputOTPSlot index={2} />
                <InputOTPSlot index={3} />
                <InputOTPSlot index={4} />
                <InputOTPSlot index={5} />
              </InputOTPGroup>
            </InputOTP>
            <span className="text-xs text-muted-foreground">
              Entered: {otpValue || "(empty)"}
            </span>
          </div>
        </div>

        {/* Full Form with RHF + Zod */}
        <div className="space-y-3">
          <VariantLabel>Form (RHF + Zod)</VariantLabel>
          <div className="max-w-sm rounded-lg border border-border p-4">
            <Form {...form}>
              <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
                <FormField
                  control={form.control}
                  name="username"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Username</FormLabel>
                      <FormControl>
                        <Input placeholder="agent-001" {...field} />
                      </FormControl>
                      <FormDescription>Used for agent identification.</FormDescription>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="email"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Email</FormLabel>
                      <FormControl>
                        <Input placeholder="op@comuki.io" type="email" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <FormField
                  control={form.control}
                  name="enabled"
                  render={({ field }) => (
                    <FormItem className="flex flex-row items-center gap-2">
                      <FormControl>
                        <Switch
                          checked={field.value}
                          onCheckedChange={field.onChange}
                        />
                      </FormControl>
                      <FormLabel className="font-normal mt-0">Enable agent</FormLabel>
                      <FormMessage />
                    </FormItem>
                  )}
                />

                <div className="flex gap-2">
                  <Button type="submit" size="sm">
                    Submit
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => form.reset()}
                  >
                    Reset
                  </Button>
                </div>
              </form>
            </Form>
          </div>
        </div>
      </div>
    </Section>
  )
}

// ─── SECTION 3: OVERLAYS ────────────────────────────────────────────────────

function OverlaysShowcase() {
  const [sliderValue, setSliderValue] = useState([60])

  return (
    <Section
      title="Overlays"
      subtitle="Dialogs, sheets, popovers, dropdowns, tooltips, context menus, hover cards, and drawers."
    >
      <div className="space-y-6">
        {/* Dialog */}
        <div className="space-y-3">
          <VariantLabel>Dialog</VariantLabel>
          <Dialog>
            <DialogTrigger asChild>
              <Button variant="outline">Open Dialog</Button>
            </DialogTrigger>
            <DialogContent showCloseButton>
              <DialogHeader>
                <DialogTitle>Confirm action</DialogTitle>
                <DialogDescription>
                  This will permanently update the task status. Continue?
                </DialogDescription>
              </DialogHeader>
              <DialogFooter showCloseButton>
                <Button variant="outline">Cancel</Button>
                <Button variant="default">Confirm</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>

        {/* Sheet */}
        <div className="space-y-3">
          <VariantLabel>Sheet (Slide-in)</VariantLabel>
          <div className="flex gap-2">
            <Sheet>
              <SheetTrigger asChild>
                <Button variant="outline" size="sm">Right sheet</Button>
              </SheetTrigger>
              <SheetContent side="right" showCloseButton>
                <SheetHeader>
                  <SheetTitle>Task details</SheetTitle>
                  <SheetDescription>
                    Review and update the current task configuration.
                  </SheetDescription>
                </SheetHeader>
              </SheetContent>
            </Sheet>
            <Sheet>
              <SheetTrigger asChild>
                <Button variant="outline" size="sm">Bottom sheet</Button>
              </SheetTrigger>
              <SheetContent side="bottom" showCloseButton>
                <SheetHeader>
                  <SheetTitle>Filters</SheetTitle>
                  <SheetDescription>Narrow results by status, role, or date.</SheetDescription>
                </SheetHeader>
              </SheetContent>
            </Sheet>
          </div>
        </div>

        {/* Popover */}
        <div className="space-y-3">
          <VariantLabel>Popover</VariantLabel>
          <Popover>
            <PopoverTrigger asChild>
              <Button variant="outline" size="sm">Filter options</Button>
            </PopoverTrigger>
            <PopoverContent align="start" className="w-56">
              <div className="space-y-2">
                <p className="text-xs font-medium">Status</p>
                {["Running", "Queued", "Failed", "Waiting"].map((s) => (
                  <div key={s} className="flex items-center gap-2">
                    <Checkbox id={`pop-${s}`} />
                    <Label htmlFor={`pop-${s}`} className="font-normal text-xs">{s}</Label>
                  </div>
                ))}
              </div>
            </PopoverContent>
          </Popover>
        </div>

        {/* Tooltip */}
        <div className="space-y-3">
          <VariantLabel>Tooltip</VariantLabel>
          <div className="flex items-center gap-2">
            <Tooltip>
              <TooltipTrigger asChild>
                <Button size="icon-sm" variant="ghost">
                  <BellIcon />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Notifications</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button size="icon-sm" variant="ghost">
                  <SettingsIcon />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Settings</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button size="icon-sm" variant="ghost">
                  <GitBranchIcon />
                </Button>
              </TooltipTrigger>
              <TooltipContent>
                <span>Deploy</span>
                <Kbd>K</Kbd>
              </TooltipContent>
            </Tooltip>
          </div>
        </div>

        {/* DropdownMenu */}
        <div className="space-y-3">
          <VariantLabel>DropdownMenu</VariantLabel>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm">Open menu</Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuLabel>Agent actions</DropdownMenuLabel>
              <DropdownMenuItem>
                <UserIcon />
                View profile
              </DropdownMenuItem>
              <DropdownMenuItem>
                <FileTextIcon />
                View logs
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive">
                <Trash2Icon />
                Remove agent
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>

        {/* ContextMenu */}
        <div className="space-y-3">
          <VariantLabel>ContextMenu (right-click)</VariantLabel>
          <ContextMenu>
            <ContextMenuTrigger className="flex h-20 w-56 items-center justify-center rounded-md border border-border bg-muted/30 text-xs text-muted-foreground">
              Right-click here
            </ContextMenuTrigger>
            <ContextMenuContent>
              <ContextMenuItem>Copy</ContextMenuItem>
              <ContextMenuItem>Paste</ContextMenuItem>
              <ContextMenuSeparator />
              <ContextMenuItem>Delete</ContextMenuItem>
            </ContextMenuContent>
          </ContextMenu>
        </div>

        {/* HoverCard */}
        <div className="space-y-3">
          <VariantLabel>HoverCard</VariantLabel>
          <HoverCard>
            <HoverCardTrigger asChild>
              <Button variant="outline" size="sm">Hover to preview</Button>
            </HoverCardTrigger>
            <HoverCardContent align="start" sideOffset={8}>
              <div className="space-y-1">
                <p className="text-xs font-medium">agent-alpha-01</p>
                <p className="text-xs text-muted-foreground">Running since 09:42 UTC. 847 tasks completed.</p>
                <Badge variant="outline" className="mt-1">Active</Badge>
              </div>
            </HoverCardContent>
          </HoverCard>
        </div>

        {/* AlertDialog */}
        <div className="space-y-3">
          <VariantLabel>AlertDialog</VariantLabel>
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="destructive" size="sm">Dangerous action</Button>
            </AlertDialogTrigger>
            <AlertDialogContent size="sm">
              <AlertDialogHeader>
                <AlertDialogTitle>Delete agent?</AlertDialogTitle>
                <AlertDialogDescription>
                  This action cannot be undone. All queued tasks will be lost.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction variant="destructive">Delete</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>

        {/* Drawer */}
        <div className="space-y-3">
          <VariantLabel>Drawer</VariantLabel>
          <Drawer>
            <DrawerTrigger asChild>
              <Button variant="outline" size="sm">Open drawer</Button>
            </DrawerTrigger>
            <DrawerContent>
              <DrawerHeader>
                <DrawerTitle>Quick settings</DrawerTitle>
                <DrawerDescription>Adjust agent parameters before launch.</DrawerDescription>
              </DrawerHeader>
              <div className="space-y-4 px-4">
                <div className="space-y-2">
                  <Label className="text-xs">Timeout (ms)</Label>
                  <Slider
                    value={sliderValue}
                    onValueChange={setSliderValue}
                    max={300}
                    step={10}
                  />
                  <span className="text-xs text-muted-foreground">{sliderValue[0]}ms</span>
                </div>
              </div>
              <DrawerFooter>
                <Button variant="outline">Cancel</Button>
                <Button>Apply</Button>
              </DrawerFooter>
            </DrawerContent>
          </Drawer>
        </div>
      </div>
    </Section>
  )
}

// ─── SECTION 4: DATA DISPLAY ────────────────────────────────────────────────

function DataDisplayShowcase() {
  return (
    <Section
      title="Data Display"
      subtitle="Cards, tables, badges, avatars, accordions, tabs, and more."
    >
      <div className="space-y-6">
        {/* Card */}
        <div className="space-y-3">
          <VariantLabel>Card</VariantLabel>
          <div className="grid gap-4 sm:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>Task queue</CardTitle>
                <CardDescription>Active agents processing work items.</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="text-xs text-muted-foreground">
                  12 tasks in queue, 4 running, 2 waiting for approval.
                </p>
              </CardContent>
              <CardFooter>
                <Button size="sm">View all</Button>
              </CardFooter>
            </Card>
            <Card size="sm">
              <CardHeader>
                <CardTitle>System status</CardTitle>
                <CardDescription>Platform health</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="flex gap-2">
                  <Badge variant="default">Healthy</Badge>
                  <Badge variant="outline">3 warnings</Badge>
                </div>
              </CardContent>
            </Card>
          </div>
        </div>

        {/* Table */}
        <div className="space-y-3">
          <VariantLabel>Table</VariantLabel>
          <Table>
            <TableCaption>Recent agent activity.</TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead>Agent</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Tasks</TableHead>
                <TableHead className="text-right">Uptime</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableCell className="font-medium">agent-alpha</TableCell>
                <TableCell><Badge variant="default">Running</Badge></TableCell>
                <TableCell>847</TableCell>
                <TableCell className="text-right">12h 34m</TableCell>
              </TableRow>
              <TableRow>
                <TableCell className="font-medium">agent-beta</TableCell>
                <TableCell><Badge variant="outline">Idle</Badge></TableCell>
                <TableCell>412</TableCell>
                <TableCell className="text-right">8h 12m</TableCell>
              </TableRow>
              <TableRow>
                <TableCell className="font-medium">agent-gamma</TableCell>
                <TableCell><Badge variant="destructive">Failed</Badge></TableCell>
                <TableCell>19</TableCell>
                <TableCell className="text-right">2h 05m</TableCell>
              </TableRow>
            </TableBody>
            <TableFooter>
              <TableRow>
                <TableCell>Total</TableCell>
                <TableCell colSpan={2}>3 agents</TableCell>
                <TableCell className="text-right">1,278 tasks</TableCell>
              </TableRow>
            </TableFooter>
          </Table>
        </div>

        {/* Badge variants */}
        <div className="space-y-3">
          <VariantLabel>Badge</VariantLabel>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="default">Default</Badge>
            <Badge variant="secondary">Secondary</Badge>
            <Badge variant="destructive">Destructive</Badge>
            <Badge variant="outline">Outline</Badge>
            <Badge variant="ghost">Ghost</Badge>
            <Badge variant="link">Link</Badge>
          </div>
        </div>

        {/* Avatar */}
        <div className="space-y-3">
          <VariantLabel>Avatar</VariantLabel>
          <div className="flex items-center gap-3">
            <AvatarGroup>
              <Avatar>
                <AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=AA" />
                <AvatarFallback>AA</AvatarFallback>
              </Avatar>
              <Avatar>
                <AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=BB" />
                <AvatarFallback>BB</AvatarFallback>
              </Avatar>
              <Avatar>
                <AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=CC" />
                <AvatarFallback>CC</AvatarFallback>
              </Avatar>
              <AvatarGroupCount>+5</AvatarGroupCount>
            </AvatarGroup>
            <Avatar size="lg">
              <AvatarFallback>DS</AvatarFallback>
            </Avatar>
            <Avatar size="sm">
              <AvatarFallback>SM</AvatarFallback>
            </Avatar>
          </div>
        </div>

        {/* Accordion */}
        <div className="space-y-3">
          <VariantLabel>Accordion</VariantLabel>
          <Accordion type="single" collapsible className="max-w-sm">
            <AccordionItem value="item-1">
              <AccordionTrigger>How does claim/lease work?</AccordionTrigger>
              <AccordionContent>
                Workers claim tasks atomically using SELECT FOR UPDATE SKIP LOCKED within
                a PostgreSQL transaction, inserting a lease row to prevent double-claim.
              </AccordionContent>
            </AccordionItem>
            <AccordionItem value="item-2">
              <AccordionTrigger>What is the pull model?</AccordionTrigger>
              <AccordionContent>
                Workers poll a queue table for available work rather than receiving
                push events. This keeps infrastructure simple and allows any HTTP client.
              </AccordionContent>
            </AccordionItem>
            <AccordionItem value="item-3">
              <AccordionTrigger>How are agents scheduled?</AccordionTrigger>
              <AccordionContent>
                The orchestrator decomposes tickets into tasks and places them in a
                priority queue. Agents pull tasks in order, executing one at a time.
              </AccordionContent>
            </AccordionItem>
          </Accordion>
        </div>

        {/* Tabs */}
        <div className="space-y-3">
          <VariantLabel>Tabs</VariantLabel>
          <Tabs defaultValue="overview" className="max-w-sm">
            <TabsList>
              <TabsTrigger value="overview">Overview</TabsTrigger>
              <TabsTrigger value="agents">Agents</TabsTrigger>
              <TabsTrigger value="logs">Logs</TabsTrigger>
            </TabsList>
            <TabsContent value="overview">
              <p className="text-xs text-muted-foreground py-2">
                Platform overview — 3 active agents, 1,278 tasks completed.
              </p>
            </TabsContent>
            <TabsContent value="agents">
              <p className="text-xs text-muted-foreground py-2">
                Agent list with status, uptime, and task counts.
              </p>
            </TabsContent>
            <TabsContent value="logs">
              <p className="text-xs text-muted-foreground py-2">
                Recent log entries from all running agents.
              </p>
            </TabsContent>
          </Tabs>
        </div>

        {/* Separator */}
        <div className="space-y-3">
          <VariantLabel>Separator</VariantLabel>
          <div className="space-y-2 max-w-sm">
            <p className="text-xs">Section A</p>
            <Separator />
            <p className="text-xs">Section B</p>
            <Separator />
            <p className="text-xs">Section C</p>
          </div>
        </div>

        {/* ScrollArea */}
        <div className="space-y-3">
          <VariantLabel>ScrollArea</VariantLabel>
          <ScrollArea className="h-24 w-full max-w-xs rounded-md border border-border p-3">
            <div className="space-y-1">
              {Array.from({ length: 20 }, (_, i) => (
                <p key={i} className="text-xs text-muted-foreground">
                  Log entry {i + 1}: Agent processed task {i + 1} in 142ms.
                </p>
              ))}
            </div>
          </ScrollArea>
        </div>

        {/* Skeleton */}
        <div className="space-y-3">
          <VariantLabel>Skeleton</VariantLabel>
          <div className="flex items-center gap-3 max-w-sm">
            <Skeleton className="size-8 rounded-full" />
            <div className="space-y-1.5 flex-1">
              <Skeleton className="h-3 w-3/4" />
              <Skeleton className="h-2 w-1/2" />
            </div>
          </div>
        </div>

        {/* AspectRatio */}
        <div className="space-y-3">
          <VariantLabel>AspectRatio</VariantLabel>
          <div className="max-w-32 rounded-md border border-border overflow-hidden">
            <AspectRatio ratio={16 / 9}>
              <div className="flex size-full items-center justify-center bg-muted text-xs text-muted-foreground">
                16:9
              </div>
            </AspectRatio>
          </div>
        </div>

        {/* Item */}
        <div className="space-y-3">
          <VariantLabel>Item</VariantLabel>
          <ItemGroup className="max-w-sm">
            <Item>
              <ItemMedia variant="icon">
                <CheckIcon />
              </ItemMedia>
              <ItemContent>
                <ItemTitle>Task completed</ItemTitle>
                <ItemDescription>agent-alpha finished task #847 in 142ms.</ItemDescription>
              </ItemContent>
              <ItemActions>
                <Button size="icon-xs" variant="ghost">
                  <ChevronRightIcon />
                </Button>
              </ItemActions>
            </Item>
            <ItemSeparator />
            <Item variant="outline">
              <ItemMedia variant="icon">
                <MoonIcon />
              </ItemMedia>
              <ItemContent>
                <ItemTitle>Agent idle</ItemTitle>
                <ItemDescription>agent-beta waiting for next task.</ItemDescription>
              </ItemContent>
              <ItemActions>
                <Badge variant="outline">Idle</Badge>
              </ItemActions>
            </Item>
          </ItemGroup>
        </div>

        {/* Empty */}
        <div className="space-y-3">
          <VariantLabel>Empty</VariantLabel>
          <div className="w-full max-w-xs">
            <Empty>
              <EmptyMedia variant="icon">
                <SearchIcon />
              </EmptyMedia>
              <EmptyHeader>
                <EmptyTitle>No results found</EmptyTitle>
                <EmptyDescription>
                  Try adjusting your search or filter criteria.
                </EmptyDescription>
              </EmptyHeader>
              <EmptyContent>
                <Button size="sm" variant="outline">Clear filters</Button>
              </EmptyContent>
            </Empty>
          </div>
        </div>

        {/* Kbd */}
        <div className="space-y-3">
          <VariantLabel>Kbd</VariantLabel>
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex items-center gap-1">
              <Kbd>⌘</Kbd>
              <Kbd>K</Kbd>
            </div>
            <KbdGroup>
              <Kbd>⌘</Kbd>
              <Kbd>⇧</Kbd>
              <Kbd>P</Kbd>
            </KbdGroup>
            <Kbd>Esc</Kbd>
            <Kbd>Enter</Kbd>
          </div>
        </div>
      </div>
    </Section>
  )
}
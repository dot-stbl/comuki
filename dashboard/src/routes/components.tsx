import { createFileRoute } from "@tanstack/react-router"
import { type ReactNode, useState } from "react"
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { toast } from "sonner"
import {
  BellIcon,
  CheckIcon,
  ChevronRightIcon,
  CopyIcon,
  ExternalLinkIcon,
  FileTextIcon,
  GitBranchIcon,
  HelpCircleIcon,
  Home,
  LayoutGrid,
  Loader2Icon,
  MenuIcon,
  MoonIcon,
  PlusIcon,
  SearchIcon,
  SettingsIcon,
  Trash2Icon,
  UserIcon,
  XIcon,
} from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { DemoCard } from "@/shared/ui/demo-card"
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from "@/shared/ui/_legacy/tooltip"
import { Toaster } from "@/shared/ui/sonner"
import { Button } from "@/shared/ui/button"
import { ToggleGroup, ToggleGroupItem } from "@/shared/ui/toggle-group"
import { Input } from "@/shared/ui/input"
import { Textarea } from "@/shared/ui/textarea"
import { Label } from "@/shared/ui/label"
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from "@/shared/ui/_legacy/select"
import { Checkbox } from "@/shared/ui/checkbox"
import { RadioGroup, RadioGroupItem } from "@/shared/ui/radio-group"
import { Switch } from "@/shared/ui/switch"
import { Slider } from "@/shared/ui/slider"
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/shared/ui/input-otp"
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/shared/ui/_legacy/form"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/shared/ui/dialog"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/shared/ui/sheet"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/shared/ui/popover"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/dropdown-menu"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/shared/ui/context-menu"
import { HoverCard, HoverCardContent, HoverCardTrigger } from "@/shared/ui/hover-card"
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
} from "@/shared/ui/alert-dialog"
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/shared/ui/drawer"
import {
  Combobox,
  ComboboxInput,
  ComboboxContent,
  ComboboxList,
  ComboboxItem,
  ComboboxGroup,
  ComboboxLabel,
  ComboboxEmpty,
} from "@/shared/ui/combobox"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/shared/ui/card"
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"
import { Badge } from "@/shared/ui/badge"
import {
  Avatar,
  AvatarFallback,
  AvatarGroup,
  AvatarGroupCount,
  AvatarImage,
} from "@/shared/ui/avatar"
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/shared/ui/accordion"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/tabs"
import { Separator } from "@/shared/ui/separator"
import { ScrollArea } from "@/shared/ui/scroll-area"
import { Skeleton } from "@/shared/ui/skeleton"
import { AspectRatio } from "@/shared/ui/aspect-ratio"
import {
  Item,
  ItemContent,
  ItemDescription,
  ItemMedia,
  ItemTitle,
  ItemActions,
  ItemGroup,
  ItemSeparator,
} from "@/shared/ui/item"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/shared/ui/empty"
import { Kbd, KbdGroup } from "@/shared/ui/kbd"
import { Alert, AlertDescription, AlertTitle } from "@/shared/ui/alert"
import { Progress } from "@/shared/ui/progress"
import { Spinner } from "@/shared/ui/spinner"
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/shared/ui/breadcrumb"
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/shared/ui/pagination"
import {
  NavigationMenu,
  NavigationMenuContent,
  NavigationMenuItem,
  NavigationMenuLink,
  NavigationMenuList,
  NavigationMenuTrigger,
} from "@/shared/ui/navigation-menu"
import {
  Menubar,
  MenubarContent,
  MenubarItem,
  MenubarMenu,
  MenubarSeparator,
  MenubarTrigger,
} from "@/shared/ui/menubar"
import {
  Command,
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandShortcut,
} from "@/shared/ui/command"
import { Toggle } from "@/shared/ui/toggle"
import { ResizableHandle, ResizablePanel, ResizablePanelGroup } from "@/shared/ui/resizable"
import {
  Sidebar,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
} from "@/shared/ui/sidebar"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/shared/ui/collapsible"
import { DirectionProvider } from "@/shared/ui/direction"
import { StatusBadge } from "@/shared/ui/status-badge"
import { RunIdChip } from "@/shared/ui/run-id-chip"
import { ModeToggle } from "@/shared/ui/mode-toggle"
import { useTheme } from "@/app/theme-provider"

export const Route = createFileRoute("/components")({
  component: ComponentsShowcase,
})

function Section({
  number,
  title,
  subtitle,
  children,
}: {
  number: string
  title: string
  subtitle?: string
  children: ReactNode
}) {
  return (
    <section className="border-t border-border py-5">
      <header className="mb-2 flex items-baseline gap-3">
        <span className="font-mono text-sm font-semibold text-primary">{number}</span>
        <h2 className="font-mono text-xl font-semibold tracking-tight text-foreground">{title}</h2>
      </header>
      {subtitle ? (
        <p className="mb-4 font-mono text-sm text-muted-foreground">{subtitle}</p>
      ) : null}
      <div className="grid grid-cols-[repeat(auto-fill,minmax(264px,1fr))] gap-3">
        {children}
      </div>
    </section>
  )
}

function ComponentsShowcase() {
  return (
    <AppShell>
      <TooltipProvider delayDuration={200}>
        <Toaster richColors position="top-right" />

        <div className="mx-auto max-w-[1180px]">
          <ButtonsShowcase />
          <FormsShowcase />
          <OverlaysShowcase />
          <DataDisplayShowcase />
          <FeedbackShowcase />
          <NavigationShowcase />
          <LayoutShowcase />
          <ComukiShowcase />
        </div>
      </TooltipProvider>
    </AppShell>
  )
}

// ─── SECTION 1: BUTTONS ─────────────────────────────────────────────────────

function ButtonsShowcase() {
  return (
    <Section number="3.1" title="Buttons" subtitle="Primary actions, toggles, and group patterns.">
      <DemoCard label="Variant">
        <Button variant="default">Default</Button>
        <Button variant="outline">Outline</Button>
        <Button variant="secondary">Secondary</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="destructive">Destructive</Button>
        <Button variant="link">Link</Button>
      </DemoCard>

      <DemoCard label="Size">
        <Button size="sm">xs</Button>
        <Button size="sm">sm</Button>
        <Button size="default">Default</Button>
        <Button size="lg">lg</Button>
      </DemoCard>

      <DemoCard label="Icon Sizes">
        <Button size="icon-sm" aria-label="Close"><XIcon /></Button>
        <Button size="icon-sm" aria-label="Settings"><SettingsIcon /></Button>
        <Button size="icon" aria-label="Add"><PlusIcon /></Button>
        <Button size="icon-lg" aria-label="Delete"><Trash2Icon /></Button>
      </DemoCard>

      <DemoCard label="With Icons">
        <Button><PlusIcon />New task</Button>
        <Button variant="outline"><CopyIcon />Copy ID</Button>
        <Button variant="secondary"><BellIcon />Notifications</Button>
        <Button variant="ghost"><SearchIcon />Search</Button>
      </DemoCard>

      <DemoCard label="States">
        <Button disabled>Disabled</Button>
        <Button aria-invalid>Invalid</Button>
      </DemoCard>

      <DemoCard label="Toggle Group">
        <ToggleGroup type="single" defaultValue="left">
          <ToggleGroupItem value="left" aria-label="Left align"><MenuIcon /></ToggleGroupItem>
          <ToggleGroupItem value="center" aria-label="Center align"><LayoutGrid /></ToggleGroupItem>
          <ToggleGroupItem value="right" aria-label="Right align"><SettingsIcon /></ToggleGroupItem>
        </ToggleGroup>
      </DemoCard>
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
    <Section number="3.2" title="Forms" subtitle="Inputs, selects, checkboxes, sliders, and Form pattern with RHF + Zod.">
      {/* Input */}
      <DemoCard label="Input">
        <div className="space-y-1">
          <Label htmlFor="input-default" className="text-xs">Default</Label>
          <Input id="input-default" placeholder="Enter text..." />
        </div>
        <div className="space-y-1">
          <Label htmlFor="input-disabled" className="text-xs">Disabled</Label>
          <Input id="input-disabled" placeholder="Disabled" disabled />
        </div>
        <div className="space-y-1">
          <Label htmlFor="input-error" className="text-xs">Invalid</Label>
          <Input id="input-error" aria-invalid placeholder="Invalid" defaultValue="bad" />
        </div>
      </DemoCard>

      {/* Textarea */}
      <DemoCard label="Textarea">
        <Textarea placeholder="Describe the issue..." rows={3} className="max-w-sm" />
      </DemoCard>

      {/* Select */}
      <DemoCard label="Select">
        <Select value={selectValue} onValueChange={setSelectValue}>
          <SelectTrigger className="w-48">
            <SelectValue placeholder="Select role" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectLabel>User roles</SelectLabel>
              {roles.map((r) => (
                <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>
              ))}
            </SelectGroup>
          </SelectContent>
        </Select>
        {selectValue && <span className="text-xs text-muted-foreground">Selected: {selectValue}</span>}
      </DemoCard>

      {/* Combobox */}
      <DemoCard label="Combobox">
        <Combobox
          open={comboboxOpen}
          onOpenChange={setComboboxOpen}
          value={comboboxValue}
          onValueChange={(val) => { setComboboxValue(typeof val === "string" ? val : "") }}
        >
          <ComboboxInput placeholder="Search exchange..." className="w-56" />
          <ComboboxContent>
            <ComboboxList>
              <ComboboxGroup>
                <ComboboxLabel>Exchanges</ComboboxLabel>
                <ComboboxItem value="Binance">Binance</ComboboxItem>
                <ComboboxItem value="OKX">OKX</ComboboxItem>
                <ComboboxItem value="Bybit">Bybit</ComboboxItem>
                <ComboboxItem value="Bitget">Bitget</ComboboxItem>
                <ComboboxItem value="Gate.io">Gate.io</ComboboxItem>
              </ComboboxGroup>
            </ComboboxList>
            <ComboboxEmpty>No exchange found.</ComboboxEmpty>
          </ComboboxContent>
        </Combobox>
        {comboboxValue && <span className="text-xs text-muted-foreground">Selected: {comboboxValue}</span>}
      </DemoCard>

      {/* Checkbox & Switch */}
      <DemoCard label="Checkbox & Switch">
        <div className="flex items-center gap-2">
          <Checkbox id="checkbox-optin" />
          <Label htmlFor="checkbox-optin" className="font-normal text-xs">Email notifications</Label>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="switch-enabled" />
          <Label htmlFor="switch-enabled" className="font-normal text-xs">Enabled</Label>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="switch-sm" size="sm" />
          <Label htmlFor="switch-sm" className="font-normal text-xs">Small</Label>
        </div>
      </DemoCard>

      {/* RadioGroup */}
      <DemoCard label="Radio Group">
        <RadioGroup defaultValue="medium">
          <div className="flex items-center gap-6">
            <div className="flex items-center gap-2">
              <RadioGroupItem value="low" id="radio-low" />
              <Label htmlFor="radio-low" className="font-normal text-xs">Low</Label>
            </div>
            <div className="flex items-center gap-2">
              <RadioGroupItem value="medium" id="radio-med" />
              <Label htmlFor="radio-med" className="font-normal text-xs">Medium</Label>
            </div>
            <div className="flex items-center gap-2">
              <RadioGroupItem value="high" id="radio-high" />
              <Label htmlFor="radio-high" className="font-normal text-xs">High</Label>
            </div>
          </div>
        </RadioGroup>
      </DemoCard>

      {/* Slider */}
      <DemoCard label="Slider">
        <div className="w-64 space-y-2">
          <Slider value={sliderValue} onValueChange={setSliderValue} max={100} step={1} />
          <span className="text-xs text-muted-foreground">Value: {sliderValue[0]}</span>
        </div>
      </DemoCard>

      {/* InputOTP */}
      <DemoCard label="InputOTP">
        <div className="space-y-2">
          <InputOTP maxLength={6} value={otpValue} onChange={(value) => setOtpValue(value)}>
            <InputOTPGroup>
              <InputOTPSlot index={0} />
              <InputOTPSlot index={1} />
              <InputOTPSlot index={2} />
              <InputOTPSlot index={3} />
              <InputOTPSlot index={4} />
              <InputOTPSlot index={5} />
            </InputOTPGroup>
          </InputOTP>
          <span className="text-xs text-muted-foreground">Entered: {otpValue || "(empty)"}</span>
        </div>
      </DemoCard>

      {/* Full Form */}
      <DemoCard label="Form (RHF + Zod)" alignStart>
        <div className="max-w-sm rounded-lg border border-border p-4">
          <Form {...form}>
            <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
              <FormField
                control={form.control}
                name="username"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Username</FormLabel>
                    <FormControl><Input placeholder="agent-001" {...field} /></FormControl>
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
                    <FormControl><Input placeholder="op@comuki.io" type="email" {...field} /></FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="enabled"
                render={({ field }) => (
                  <FormItem className="flex flex-row items-center gap-2">
                    <FormControl><Switch checked={field.value} onCheckedChange={field.onChange} /></FormControl>
                    <FormLabel className="font-normal mt-0">Enable agent</FormLabel>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <div className="flex gap-2">
                <Button type="submit" size="sm">Submit</Button>
                <Button type="button" variant="outline" size="sm" onClick={() => form.reset()}>Reset</Button>
              </div>
            </form>
          </Form>
        </div>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 3: OVERLAYS ────────────────────────────────────────────────────

function OverlaysShowcase() {
  const [sliderValue, setSliderValue] = useState([60])

  return (
    <Section number="3.3" title="Overlays" subtitle="Dialogs, sheets, popovers, dropdowns, tooltips, context menus, hover cards, and drawers.">
      {/* Dialog */}
      <DemoCard label="Dialog">
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="outline">Open Dialog</Button>
          </DialogTrigger>
          <DialogContent showCloseButton>
            <DialogHeader>
              <DialogTitle>Confirm action</DialogTitle>
              <DialogDescription>This will permanently update the task status. Continue?</DialogDescription>
            </DialogHeader>
            <DialogFooter showCloseButton>
              <Button variant="outline">Cancel</Button>
              <Button variant="default">Confirm</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </DemoCard>

      {/* Sheet */}
      <DemoCard label="Sheet (Slide-in)">
        <Sheet>
          <SheetTrigger asChild><Button variant="outline" size="sm">Right sheet</Button></SheetTrigger>
          <SheetContent side="right" showCloseButton>
            <SheetHeader>
              <SheetTitle>Task details</SheetTitle>
              <SheetDescription>Review and update the current task configuration.</SheetDescription>
            </SheetHeader>
          </SheetContent>
        </Sheet>
        <Sheet>
          <SheetTrigger asChild><Button variant="outline" size="sm">Bottom sheet</Button></SheetTrigger>
          <SheetContent side="bottom" showCloseButton>
            <SheetHeader>
              <SheetTitle>Filters</SheetTitle>
              <SheetDescription>Narrow results by status, role, or date.</SheetDescription>
            </SheetHeader>
          </SheetContent>
        </Sheet>
      </DemoCard>

      {/* Popover */}
      <DemoCard label="Popover">
        <Popover>
          <PopoverTrigger asChild><Button variant="outline" size="sm">Filter options</Button></PopoverTrigger>
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
      </DemoCard>

      {/* Tooltip */}
      <DemoCard label="Tooltip">
        <Tooltip>
          <TooltipTrigger asChild><Button size="icon-sm" variant="ghost"><BellIcon /></Button></TooltipTrigger>
          <TooltipContent>Notifications</TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger asChild><Button size="icon-sm" variant="ghost"><SettingsIcon /></Button></TooltipTrigger>
          <TooltipContent>Settings</TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger asChild><Button size="icon-sm" variant="ghost"><GitBranchIcon /></Button></TooltipTrigger>
          <TooltipContent><span>Deploy</span><Kbd>K</Kbd></TooltipContent>
        </Tooltip>
      </DemoCard>

      {/* DropdownMenu */}
      <DemoCard label="DropdownMenu">
        <DropdownMenu>
          <DropdownMenuTrigger asChild><Button variant="outline" size="sm">Open menu</Button></DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            <DropdownMenuLabel>Agent actions</DropdownMenuLabel>
            <DropdownMenuItem><UserIcon />View profile</DropdownMenuItem>
            <DropdownMenuItem><FileTextIcon />View logs</DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive"><Trash2Icon />Remove agent</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </DemoCard>

      {/* ContextMenu */}
      <DemoCard label="ContextMenu (right-click)">
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
      </DemoCard>

      {/* HoverCard */}
      <DemoCard label="HoverCard">
        <HoverCard>
          <HoverCardTrigger asChild><Button variant="outline" size="sm">Hover to preview</Button></HoverCardTrigger>
          <HoverCardContent align="start" sideOffset={8}>
            <div className="space-y-1">
              <p className="text-xs font-medium">agent-alpha-01</p>
              <p className="text-xs text-muted-foreground">Running since 09:42 UTC. 847 tasks completed.</p>
              <Badge variant="outline" className="mt-1">Active</Badge>
            </div>
          </HoverCardContent>
        </HoverCard>
      </DemoCard>

      {/* AlertDialog */}
      <DemoCard label="AlertDialog">
        <AlertDialog>
          <AlertDialogTrigger asChild><Button variant="destructive" size="sm">Dangerous action</Button></AlertDialogTrigger>
          <AlertDialogContent size="sm">
            <AlertDialogHeader>
              <AlertDialogTitle>Delete agent?</AlertDialogTitle>
              <AlertDialogDescription>This action cannot be undone. All queued tasks will be lost.</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Cancel</AlertDialogCancel>
              <AlertDialogAction variant="destructive">Delete</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </DemoCard>

      {/* Drawer */}
      <DemoCard label="Drawer" alignStart>
        <Drawer>
          <DrawerTrigger asChild><Button variant="outline" size="sm">Open drawer</Button></DrawerTrigger>
          <DrawerContent>
            <DrawerHeader>
              <DrawerTitle>Quick settings</DrawerTitle>
              <DrawerDescription>Adjust agent parameters before launch.</DrawerDescription>
            </DrawerHeader>
            <div className="space-y-4 px-4">
              <div className="space-y-2">
                <Label className="text-xs">Timeout (ms)</Label>
                <Slider value={sliderValue} onValueChange={setSliderValue} max={300} step={10} />
                <span className="text-xs text-muted-foreground">{sliderValue[0]}ms</span>
              </div>
            </div>
            <DrawerFooter>
              <Button variant="outline">Cancel</Button>
              <Button>Apply</Button>
            </DrawerFooter>
          </DrawerContent>
        </Drawer>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 4: DATA DISPLAY ────────────────────────────────────────────────

function DataDisplayShowcase() {
  return (
    <Section number="3.4" title="Data Display" subtitle="Cards, tables, badges, avatars, accordions, tabs, and more.">
      {/* Card */}
      <DemoCard label="Card" alignStart>
        <div className="grid gap-4 sm:grid-cols-2">
          <Card className="border-t-2 border-t-primary">
            <CardHeader className="bg-primary/5">
              <CardTitle>Task queue</CardTitle>
              <CardDescription>Active agents processing work items.</CardDescription>
            </CardHeader>
            <CardContent>
              <p className="text-xs text-muted-foreground">12 tasks in queue, 4 running, 2 waiting for approval.</p>
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
      </DemoCard>

      {/* Table */}
      <DemoCard label="Table" alignStart>
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
      </DemoCard>

      {/* Badge */}
      <DemoCard label="Badge">
        <Badge variant="default">Default</Badge>
        <Badge variant="secondary">Secondary</Badge>
        <Badge variant="destructive">Destructive</Badge>
        <Badge variant="outline">Outline</Badge>
        <Badge variant="ghost">Ghost</Badge>
        <Badge variant="link">Link</Badge>
      </DemoCard>

      {/* Avatar */}
      <DemoCard label="Avatar">
        <AvatarGroup>
          <Avatar><AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=AA" /><AvatarFallback>AA</AvatarFallback></Avatar>
          <Avatar><AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=BB" /><AvatarFallback>BB</AvatarFallback></Avatar>
          <Avatar><AvatarImage src="https://api.dicebear.com/9.x/initials/svg?seed=CC" /><AvatarFallback>CC</AvatarFallback></Avatar>
          <AvatarGroupCount>+5</AvatarGroupCount>
        </AvatarGroup>
        <Avatar size="lg"><AvatarFallback>DS</AvatarFallback></Avatar>
        <Avatar size="sm"><AvatarFallback>SM</AvatarFallback></Avatar>
      </DemoCard>

      {/* Accordion */}
      <DemoCard label="Accordion" alignStart>
        <Accordion type="single" collapsible className="max-w-sm">
          <AccordionItem value="item-1">
            <AccordionTrigger>How does claim/lease work?</AccordionTrigger>
            <AccordionContent>Workers claim tasks atomically using SELECT FOR UPDATE SKIP LOCKED within a PostgreSQL transaction, inserting a lease row to prevent double-claim.</AccordionContent>
          </AccordionItem>
          <AccordionItem value="item-2">
            <AccordionTrigger>What is the pull model?</AccordionTrigger>
            <AccordionContent>Workers poll a queue table for available work rather than receiving push events. This keeps infrastructure simple and allows any HTTP client.</AccordionContent>
          </AccordionItem>
          <AccordionItem value="item-3">
            <AccordionTrigger>How are agents scheduled?</AccordionTrigger>
            <AccordionContent>The orchestrator decomposes tickets into tasks and places them in a priority queue. Agents pull tasks in order, executing one at a time.</AccordionContent>
          </AccordionItem>
        </Accordion>
      </DemoCard>

      {/* Tabs */}
      <DemoCard label="Tabs">
        <Tabs defaultValue="overview" className="max-w-sm">
          <TabsList className="inline-flex h-8 rounded-md bg-muted p-0.5">
            <TabsTrigger value="overview" className="rounded-sm data-[state=active]:bg-card data-[state=active]:text-foreground data-[state=active]:shadow-sm">Overview</TabsTrigger>
            <TabsTrigger value="agents" className="rounded-sm data-[state=active]:bg-card data-[state=active]:text-foreground data-[state=active]:shadow-sm">Agents</TabsTrigger>
            <TabsTrigger value="logs" className="rounded-sm data-[state=active]:bg-card data-[state=active]:text-foreground data-[state=active]:shadow-sm">Logs</TabsTrigger>
          </TabsList>
          <TabsContent value="overview">
            <p className="text-xs text-muted-foreground py-2">Platform overview — 3 active agents, 1,278 tasks completed.</p>
          </TabsContent>
          <TabsContent value="agents">
            <p className="text-xs text-muted-foreground py-2">Agent list with status, uptime, and task counts.</p>
          </TabsContent>
          <TabsContent value="logs">
            <p className="text-xs text-muted-foreground py-2">Recent log entries from all running agents.</p>
          </TabsContent>
        </Tabs>
      </DemoCard>

      {/* Separator */}
      <DemoCard label="Separator">
        <div className="space-y-2 max-w-sm">
          <p className="text-xs">Section A</p>
          <Separator />
          <p className="text-xs">Section B</p>
          <Separator />
          <p className="text-xs">Section C</p>
        </div>
      </DemoCard>

      {/* ScrollArea */}
      <DemoCard label="ScrollArea">
        <ScrollArea className="h-24 w-full max-w-xs rounded-md border border-border p-3">
          <div className="space-y-1">
            {Array.from({ length: 20 }, (_, i) => (
              <p key={i} className="text-xs text-muted-foreground">Log entry {i + 1}: Agent processed task {i + 1} in 142ms.</p>
            ))}
          </div>
        </ScrollArea>
      </DemoCard>

      {/* Skeleton */}
      <DemoCard label="Skeleton">
        <div className="flex items-center gap-3 max-w-sm">
          <Skeleton className="size-8 rounded-full" />
          <div className="space-y-1.5 flex-1">
            <Skeleton className="h-3 w-3/4" />
            <Skeleton className="h-2 w-1/2" />
          </div>
        </div>
      </DemoCard>

      {/* AspectRatio */}
      <DemoCard label="AspectRatio">
        <div className="max-w-32 rounded-md border border-border overflow-hidden">
          <AspectRatio ratio={16 / 9}>
            <div className="flex size-full items-center justify-center bg-muted text-xs text-muted-foreground">16:9</div>
          </AspectRatio>
        </div>
      </DemoCard>

      {/* Item */}
      <DemoCard label="Item">
        <ItemGroup className="max-w-sm">
          <Item>
            <ItemMedia variant="icon"><CheckIcon /></ItemMedia>
            <ItemContent>
              <ItemTitle>Task completed</ItemTitle>
              <ItemDescription>agent-alpha finished task #847 in 142ms.</ItemDescription>
            </ItemContent>
            <ItemActions>
              <Button size="icon-sm" variant="ghost"><ChevronRightIcon /></Button>
            </ItemActions>
          </Item>
          <ItemSeparator />
          <Item variant="outline">
            <ItemMedia variant="icon"><MoonIcon /></ItemMedia>
            <ItemContent>
              <ItemTitle>Agent idle</ItemTitle>
              <ItemDescription>agent-beta waiting for next task.</ItemDescription>
            </ItemContent>
            <ItemActions>
              <Badge variant="outline">Idle</Badge>
            </ItemActions>
          </Item>
        </ItemGroup>
      </DemoCard>

      {/* Empty */}
      <DemoCard label="Empty">
        <div className="relative w-full max-w-xs overflow-hidden rounded-md border border-dashed bg-muted">
          <div className="relative bg-popover/90 p-4">
          <Empty>
            <EmptyMedia variant="icon"><SearchIcon /></EmptyMedia>
            <EmptyHeader>
              <EmptyTitle>No results found</EmptyTitle>
              <EmptyDescription>Try adjusting your search or filter criteria.</EmptyDescription>
            </EmptyHeader>
            <EmptyContent>
              <Button size="sm" variant="outline">Clear filters</Button>
            </EmptyContent>
          </Empty>
          </div>
        </div>
      </DemoCard>

      {/* Kbd */}
      <DemoCard label="Kbd">
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-1">
            <Kbd>⌘</Kbd><Kbd>K</Kbd>
          </div>
          <KbdGroup><Kbd>⌘</Kbd><Kbd>⇧</Kbd><Kbd>P</Kbd></KbdGroup>
          <Kbd>Esc</Kbd>
          <Kbd>Enter</Kbd>
        </div>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 5: FEEDBACK ────────────────────────────────────────────────────

function FeedbackShowcase() {
  const [progressValue, setProgressValue] = useState(60)
  const [isLoading, setIsLoading] = useState(false)

  return (
    <Section number="3.5" title="Feedback" subtitle="Alerts, progress indicators, spinners, and toasts.">
      {/* Alert */}
      <DemoCard label="Alert">
        <div className="space-y-2 max-w-md">
          <Alert>
            <AlertTitle>Heads up!</AlertTitle>
            <AlertDescription>You can use this alert to draw attention to something important.</AlertDescription>
          </Alert>
          <Alert variant="destructive">
            <AlertTitle>Something went wrong</AlertTitle>
            <AlertDescription>Please try again later or contact support.</AlertDescription>
          </Alert>
        </div>
      </DemoCard>

      {/* Progress */}
      <DemoCard label="Progress">
        <div className="space-y-2 max-w-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono text-muted-foreground">Processing</span>
            <span className="text-xs font-mono font-medium">{progressValue}%</span>
          </div>
          <Progress value={progressValue} />
          <Slider value={[progressValue]} onValueChange={([v]) => setProgressValue(v)} max={100} step={1} />
        </div>
      </DemoCard>

      {/* Spinner */}
      <DemoCard label="Spinner">
        <Spinner />
        <Spinner className="size-6" />
        <Spinner className="size-8 text-muted-foreground" />
        <Button disabled={isLoading} onClick={() => { setIsLoading(true); setTimeout(() => setIsLoading(false), 2000) }}>
          {isLoading && <Loader2Icon className="mr-2 size-4 animate-spin" />}
          {isLoading ? "Loading..." : "Simulate async"}
        </Button>
      </DemoCard>

      {/* Sonner */}
      <DemoCard label="Sonner (toast)">
        <Button size="sm" variant="outline" onClick={() => toast.success("Task completed", { description: "run_8f3c2a91 finished in 142ms" })}>Success toast</Button>
        <Button size="sm" variant="outline" onClick={() => toast.error("Task failed", { description: "Connection timeout after 30s" })}>Error toast</Button>
        <Button size="sm" variant="outline" onClick={() => toast.warning("Task stalled", { description: "No heartbeat in 60s" })}>Warning toast</Button>
        <Button size="sm" variant="outline" onClick={() => toast.info("Task queued", { description: "Waiting for worker availability" })}>Info toast</Button>
        <Button size="sm" variant="outline" onClick={() => { const id = toast.loading("Running task..."); setTimeout(() => toast.success("Done!", { id }), 2000) }}>Loading toast</Button>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 6: NAVIGATION ─────────────────────────────────────────────────

function NavigationShowcase() {
  const [commandOpen, setCommandOpen] = useState(false)
  const [toggleBold, setToggleBold] = useState(false)
  const [toggleItalic, setToggleItalic] = useState(false)
  const [toggleUnderline, setToggleUnderline] = useState(false)
  const [alignValue, setAlignValue] = useState("left")

  return (
    <Section number="3.6" title="Navigation" subtitle="Breadcrumbs, pagination, menus, command palette, and toggles.">
      {/* Breadcrumb */}
      <DemoCard label="Breadcrumb">
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem><BreadcrumbLink href="#">Home</BreadcrumbLink></BreadcrumbItem>
            <BreadcrumbSeparator><ChevronRightIcon /></BreadcrumbSeparator>
            <BreadcrumbItem><BreadcrumbLink href="#">Agents</BreadcrumbLink></BreadcrumbItem>
            <BreadcrumbSeparator><ChevronRightIcon /></BreadcrumbSeparator>
            <BreadcrumbItem><BreadcrumbPage>agent-alpha-01</BreadcrumbPage></BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </DemoCard>

      {/* Pagination */}
      <DemoCard label="Pagination">
        <Pagination>
          <PaginationContent>
            <PaginationPrevious text="Previous" />
            <PaginationItem><PaginationLink isActive>1</PaginationLink></PaginationItem>
            <PaginationItem><PaginationLink>2</PaginationLink></PaginationItem>
            <PaginationItem><PaginationLink>3</PaginationLink></PaginationItem>
            <PaginationEllipsis />
            <PaginationItem><PaginationLink>12</PaginationLink></PaginationItem>
            <PaginationNext text="Next" />
          </PaginationContent>
        </Pagination>
      </DemoCard>

      {/* NavigationMenu */}
      <DemoCard label="NavigationMenu">
        <NavigationMenu viewport={false}>
          <NavigationMenuList>
            <NavigationMenuItem>
              <NavigationMenuTrigger>Platform</NavigationMenuTrigger>
              <NavigationMenuContent>
                <div className="grid gap-2 p-2 w-48">
                  <NavigationMenuLink>Agents</NavigationMenuLink>
                  <NavigationMenuLink>Tasks</NavigationMenuLink>
                  <NavigationMenuLink>Logs</NavigationMenuLink>
                </div>
              </NavigationMenuContent>
            </NavigationMenuItem>
            <NavigationMenuItem>
              <NavigationMenuTrigger>Settings</NavigationMenuTrigger>
              <NavigationMenuContent>
                <div className="grid gap-2 p-2 w-48">
                  <NavigationMenuLink>Preferences</NavigationMenuLink>
                  <NavigationMenuLink>Integrations</NavigationMenuLink>
                </div>
              </NavigationMenuContent>
            </NavigationMenuItem>
            <NavigationMenuItem>
              <NavigationMenuTrigger>Help</NavigationMenuTrigger>
              <NavigationMenuContent>
                <div className="grid gap-2 p-2 w-48">
                  <NavigationMenuLink>Documentation</NavigationMenuLink>
                  <NavigationMenuLink>API Reference</NavigationMenuLink>
                  <NavigationMenuLink>Contact</NavigationMenuLink>
                </div>
              </NavigationMenuContent>
            </NavigationMenuItem>
          </NavigationMenuList>
        </NavigationMenu>
      </DemoCard>

      {/* Menubar */}
      <DemoCard label="Menubar">
        <Menubar className="w-fit">
          <MenubarMenu>
            <MenubarTrigger>File</MenubarTrigger>
            <MenubarContent>
              <MenubarItem>New task</MenubarItem>
              <MenubarItem>Open run</MenubarItem>
              <MenubarSeparator />
              <MenubarItem>Save</MenubarItem>
              <MenubarSeparator />
              <MenubarItem variant="destructive">Exit</MenubarItem>
            </MenubarContent>
          </MenubarMenu>
          <MenubarMenu>
            <MenubarTrigger>View</MenubarTrigger>
            <MenubarContent>
              <MenubarItem>Toggle sidebar</MenubarItem>
              <MenubarItem>Fullscreen</MenubarItem>
            </MenubarContent>
          </MenubarMenu>
        </Menubar>
      </DemoCard>

      {/* Command */}
      <DemoCard label="Command">
        <Button variant="outline" size="sm" onClick={() => setCommandOpen(true)}>
          <SearchIcon className="mr-2 size-3.5" />
          Search commands
          <span className="ml-2 rounded border border-border px-1 font-mono text-micro text-muted-foreground">⌘K</span>
        </Button>
        <CommandDialog open={commandOpen} onOpenChange={setCommandOpen}>
          <Command>
            <CommandInput placeholder="Type a command or search..." />
            <CommandList>
              <CommandEmpty>No results found.</CommandEmpty>
              <CommandGroup heading="Suggestions">
                <CommandItem><FileTextIcon className="mr-2 size-3.5" />New task<CommandShortcut>⌘N</CommandShortcut></CommandItem>
                <CommandItem><SearchIcon className="mr-2 size-3.5" />Search runs</CommandItem>
                <CommandItem><SettingsIcon className="mr-2 size-3.5" />Preferences</CommandItem>
              </CommandGroup>
              <CommandGroup heading="Settings">
                <CommandItem><BellIcon className="mr-2 size-3.5" />Notifications</CommandItem>
                <CommandItem><MoonIcon className="mr-2 size-3.5" />Theme</CommandItem>
              </CommandGroup>
              <CommandGroup heading="Help">
                <CommandItem><HelpCircleIcon className="mr-2 size-3.5" />Documentation</CommandItem>
                <CommandItem><ExternalLinkIcon className="mr-2 size-3.5" />API reference</CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </CommandDialog>
      </DemoCard>

      {/* Toggle */}
      <DemoCard label="Toggle">
        <Toggle pressed={toggleBold} onPressedChange={setToggleBold} aria-label="Toggle bold"><span className="font-bold text-xs">B</span></Toggle>
        <Toggle pressed={toggleItalic} onPressedChange={setToggleItalic} variant="outline" aria-label="Toggle italic"><span className="italic text-xs">I</span></Toggle>
        <Toggle pressed={toggleUnderline} onPressedChange={setToggleUnderline} variant="outline" aria-label="Toggle underline"><span className="underline text-xs">U</span></Toggle>
      </DemoCard>

      {/* ToggleGroup */}
      <DemoCard label="ToggleGroup (alignment)">
        <ToggleGroup type="single" value={alignValue} onValueChange={(v) => v && setAlignValue(v)}>
          <ToggleGroupItem value="left" aria-label="Left"><MenuIcon /></ToggleGroupItem>
          <ToggleGroupItem value="center" aria-label="Center"><LayoutGrid /></ToggleGroupItem>
          <ToggleGroupItem value="right" aria-label="Right"><SettingsIcon /></ToggleGroupItem>
        </ToggleGroup>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 7: LAYOUT ──────────────────────────────────────────────────────

function LayoutShowcase() {
  const [collapsibleOpen, setCollapsibleOpen] = useState(false)
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [dirDir, setDirDir] = useState<"ltr" | "rtl">("ltr")

  return (
    <Section number="3.7" title="Layout" subtitle="Resizable panels, collapsible sections, sidebar, and text direction.">
      {/* Resizable */}
      <DemoCard label="ResizablePanelGroup">
        <ResizablePanelGroup orientation="horizontal">
          <ResizablePanel defaultSize={40}>
            <div className="flex h-20 items-center justify-center text-xs text-muted-foreground">Left panel — 40%</div>
          </ResizablePanel>
          <ResizableHandle withHandle />
          <ResizablePanel defaultSize={60}>
            <div className="flex h-20 items-center justify-center text-xs text-muted-foreground">Right panel — 60% (drag the handle)</div>
          </ResizablePanel>
        </ResizablePanelGroup>
      </DemoCard>

      {/* Collapsible */}
      <DemoCard label="Collapsible">
        <Collapsible open={collapsibleOpen} onOpenChange={setCollapsibleOpen} className="max-w-sm">
          <div className="flex items-center gap-2">
            <CollapsibleTrigger asChild>
              <Button variant="ghost" size="sm">{collapsibleOpen ? "Hide" : "Show"} agent details</Button>
            </CollapsibleTrigger>
          </div>
          <CollapsibleContent className="mt-2 rounded-md border border-border p-3">
            <p className="text-xs text-muted-foreground">agent-alpha-01 · Running since 09:42 UTC · 847 tasks completed · Memory: 128MB · CPU: 12% avg</p>
          </CollapsibleContent>
        </Collapsible>
      </DemoCard>

      {/* Sidebar */}
      <DemoCard label="Sidebar (internal state)">
        <div className="flex h-40 w-full max-w-xs items-center gap-2">
          <SidebarProvider defaultOpen={sidebarOpen}>
            <Sidebar side="left" collapsible="none" className="h-40 w-44 border">
              <SidebarGroup>
                <SidebarGroupLabel>Navigation</SidebarGroupLabel>
                <SidebarMenu>
                  <SidebarMenuItem>
                    <SidebarMenuButton isActive><Home className="size-3.5" /><span>Home</span></SidebarMenuButton>
                  </SidebarMenuItem>
                  <SidebarMenuItem>
                    <SidebarMenuButton><LayoutGrid className="size-3.5" /><span>Components</span></SidebarMenuButton>
                    <SidebarMenuBadge>8</SidebarMenuBadge>
                  </SidebarMenuItem>
                  <SidebarMenuItem>
                    <SidebarMenuButton><SettingsIcon className="size-3.5" /><span>Settings</span></SidebarMenuButton>
                  </SidebarMenuItem>
                </SidebarMenu>
              </SidebarGroup>
            </Sidebar>
          </SidebarProvider>
          <div className="flex flex-col gap-2">
            <Button size="sm" variant="outline" onClick={() => setSidebarOpen((o) => !o)}>{sidebarOpen ? "Collapse" : "Expand"} sidebar</Button>
            <span className="font-mono text-xs text-muted-foreground">state: {sidebarOpen ? "expanded" : "collapsed"}</span>
          </div>
        </div>
      </DemoCard>

      {/* Direction */}
      <DemoCard label="Direction (RTL toggle)">
        <div className="flex items-center gap-3">
          <Button size="sm" variant="outline" onClick={() => setDirDir((d) => (d === "ltr" ? "rtl" : "ltr"))}>Toggle direction</Button>
          <span className="font-mono text-xs text-muted-foreground">dir: {dirDir}</span>
        </div>
        <DirectionProvider dir={dirDir}>
          <div className="max-w-xs rounded-md border border-border p-3">
            <p className="text-xs text-muted-foreground" dir={dirDir}>The quick brown fox jumps over the lazy dog. Text flows {dirDir === "ltr" ? "left to right" : "right to left"} based on dir attribute.</p>
          </div>
        </DirectionProvider>
      </DemoCard>
    </Section>
  )
}

// ─── SECTION 8: COMUKI ──────────────────────────────────────────────────────

function ComukiShowcase() {
  const { theme } = useTheme()

  return (
    <Section number="3.8" title="Comuki" subtitle="Custom components built for the platform: StatusBadge, RunIdChip, ModeToggle.">
      {/* StatusBadge — md */}
      <DemoCard label="StatusBadge — size md">
        <StatusBadge status="running" />
        <StatusBadge status="success" />
        <StatusBadge status="failed" />
        <StatusBadge status="waiting" />
        <StatusBadge status="queued" />
        <StatusBadge status="escalated" />
      </DemoCard>

      {/* StatusBadge — sm */}
      <DemoCard label="StatusBadge — size sm">
        <StatusBadge status="running" size="sm" />
        <StatusBadge status="success" size="sm" />
        <StatusBadge status="failed" size="sm" />
        <StatusBadge status="waiting" size="sm" />
        <StatusBadge status="queued" size="sm" />
        <StatusBadge status="escalated" size="sm" />
      </DemoCard>

      {/* RunIdChip */}
      <DemoCard label="RunIdChip">
        <RunIdChip id="run_8f3c2a91" />
        <RunIdChip id="run_8f3c2a91-b7e4-4d3a-9c1f-2e8b5d7c4a9f" />
        <p className="text-xs text-muted-foreground">Click to copy — long IDs truncate at max-w-32 (8rem).</p>
      </DemoCard>

      {/* ModeToggle */}
      <DemoCard label="ModeToggle">
        <ModeToggle />
        <span className="font-mono text-xs text-muted-foreground">current theme: {theme}</span>
      </DemoCard>
    </Section>
  )
}
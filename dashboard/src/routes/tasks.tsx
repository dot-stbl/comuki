import { createFileRoute } from "@tanstack/react-router"

import { TasksPage } from "@/domains/tasks"

export const Route = createFileRoute("/tasks")({
  component: TasksPage,
})

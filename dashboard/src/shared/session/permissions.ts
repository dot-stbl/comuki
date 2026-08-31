/**
 * Roles are fixed in code and only ever assigned — there is no role editor in
 * the product, by design. These six strings are the whole vocabulary; anything
 * that reads like "an admin" in copy resolves to one of them.
 */
export type Role =
  | "viewer"
  | "member"
  | "approver"
  | "project-admin"
  | "operator"
  | "platform-admin"

/**
 * What a role is allowed to *do*, named after the act rather than the screen.
 *
 * Screens come and go — the duty list has already been three different screens
 * — but "may stop a run" is a fact about the product. Keying the matrix on acts
 * also means one permission can gate a nav item, a route and three buttons
 * without any of them agreeing on a screen name first.
 */
export type Permission =
  | "runs.view"
  | "runs.stop"
  | "queue.view"
  | "inbox.view"
  | "inbox.take"
  | "chat.use"
  | "plans.approve"
  | "knowledge.view"
  | "verify.view"
  | "sources.view"
  | "sources.edit"
  | "cost.view"
  | "settings.live"
  | "settings.git"
  | "identity.manage"
  | "projects.view"
  | "projects.create"
  | "compute.view"
  | "compute.manage"
  | "models.view"
  | "models.manage"
  | "observability.view"

/**
 * Which scope answers for a permission.
 *
 * A *project* permission is granted by a role held on the current project or
 * held platform-wide — a platform-admin does not stop being one on the way into
 * a project. A *platform* permission ignores project roles entirely: Projects,
 * Identity and Observability sit outside project scope, so being project-admin
 * of one project must never open them.
 */
const SCOPE: Record<Permission, "project" | "platform"> = {
  "runs.view": "project",
  "runs.stop": "project",
  "queue.view": "project",
  "inbox.view": "project",
  "inbox.take": "project",
  "chat.use": "project",
  "plans.approve": "project",
  "knowledge.view": "project",
  "verify.view": "project",
  "sources.view": "project",
  "sources.edit": "project",
  "cost.view": "project",
  "settings.live": "project",
  "settings.git": "project",
  "identity.manage": "platform",
  "projects.view": "platform",
  "projects.create": "platform",
  "compute.view": "platform",
  "compute.manage": "platform",
  "models.view": "platform",
  "models.manage": "platform",
  "observability.view": "platform",
}

const VIEWER: Permission[] = ["runs.view"]

const MEMBER: Permission[] = [
  ...VIEWER,
  "runs.stop",
  "queue.view",
  "inbox.view",
  "inbox.take",
  "chat.use",
  "knowledge.view",
]

const APPROVER: Permission[] = [...MEMBER, "plans.approve"]

const PROJECT_ADMIN: Permission[] = [
  ...APPROVER,
  "verify.view",
  "sources.view",
  "sources.edit",
  "cost.view",
  "settings.live",
  "settings.git",
]

/**
 * The matrix from the FE requirements, one row per role.
 *
 * Written by extension because that is how the table reads down the page — but
 * roles are emphatically *not* a chain, and `operator` is where that shows.
 * Operator is platform ops: it stops runs, turns live settings and creates
 * projects, and it cannot approve a plan. Approving is a project judgement, and
 * an operator who should make it gets an `approver` assignment on that project
 * rather than the power by default.
 */
const GRANTS: Record<Role, readonly Permission[]> = {
  viewer: VIEWER,
  member: MEMBER,
  approver: APPROVER,
  "project-admin": PROJECT_ADMIN,
  // Platform ops: the whole lower tier of the rail is this role's day. It
  // reads and turns compute, models, projects and boards — and still cannot
  // approve a plan or manage identity, which stay a project judgement and a
  // platform-admin act respectively.
  operator: [
    ...MEMBER,
    "cost.view",
    "settings.live",
    "projects.view",
    "projects.create",
    "compute.view",
    "compute.manage",
    "models.view",
    "models.manage",
    "observability.view",
  ],
  "platform-admin": Object.keys(SCOPE) as Permission[],
}

/** Every role, in the order the requirements table lists them. */
export const ROLES: readonly Role[] = [
  "viewer",
  "member",
  "approver",
  "project-admin",
  "operator",
  "platform-admin",
]

export function permissionScope(permission: Permission): "project" | "platform" {
  return SCOPE[permission]
}

export function roleGrants(role: Role, permission: Permission): boolean {
  return GRANTS[role].includes(permission)
}

/** The roles that would open a denied act — the raw material for the tooltip. */
export function rolesGranting(permission: Permission): Role[] {
  return ROLES.filter((role) => roleGrants(role, permission))
}

/**
 * The denial sentence, and the reason denial is a *sentence* rather than a
 * greyed-out control: a disabled button that says nothing teaches the operator
 * that the product is broken. Naming the role that would work teaches them the
 * shape of the system instead, and tells them what to ask for.
 */
export function needsLabel(permission: Permission, where?: string): string {
  const roles = rolesGranting(permission)
  if (roles.length === 0) {
    return "not available"
  }
  const head = roles.slice(0, -1).join(", ")
  const tail = roles[roles.length - 1]
  const list = head ? `${head} or ${tail}` : tail
  // Naming the project is the whole point once a list mixes them: the same
  // person is an approver on one and a viewer on the next, and "needs approver"
  // alone would read as a flat no rather than as a fact about this row.
  return where ? `needs ${list} on ${where}` : `needs ${list}`
}

import { RotateCw } from "lucide-react"
import { Tab, TabList, TabPanel, Tabs } from "react-aria-components"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useIdentityQuery } from "@/domains/identity/api/queries"
import { isIdentityTab, type IdentityTab } from "@/domains/identity/model/tabs"
import { GrantsPanel } from "@/domains/identity/ui/grants-panel"
import { KeysPanel } from "@/domains/identity/ui/keys-panel"
import { UsersPanel } from "@/domains/identity/ui/users-panel"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./identity-page.module.css"

const SKELETON_WIDTHS = ["52%", "74%", "46%", "66%", "58%"]

export interface IdentityPageProps {
  /** Which list is showing. In the URL, so it can be linked and returned to. */
  tab: IdentityTab
  /**
   * A subject to narrow the showing list to on arrival — the address, subject
   * or prefix a form just wrote. It seeds that panel's own text filter, so the
   * narrowing is visible in the toolbar and clears in one click.
   */
  focus?: string
  onTabChange: (tab: IdentityTab) => void
}

/**
 * Who exists, what they hold, and what acts for them.
 *
 * Three lists, and the reason they share a screen rather than three rail items:
 * none of them answers a question on its own. A grant is illegible until you
 * know whose it is; a key is not safe to leave alone until you know what it
 * opens; an account is just an address until you see what it holds. They are
 * three views of one subject graph, so they sit behind three tabs on one
 * screen and are fetched in one payload — two queries would let the screen show
 * a grant against a key the other half had already revoked.
 *
 * Tabs rather than three stacked tables because each list is virtualized and
 * owns its own scroll port: stacking them would put three scrolling regions
 * inside a fourth. One at a time, full height, one scroll.
 *
 * The showing tab lives in the URL rather than in this component. It has to:
 * every act on this screen now leaves for a form page and comes back, and a
 * tab held in local state would drop the operator on `users` after they had
 * just written a key. It also makes a tab a thing you can send somebody.
 *
 * Two product rules live under this screen and are enforced *and explained* by
 * the forms it links to: a role cannot be created — the six live in code and
 * the grant form offers exactly those and no affordance to add a seventh — and
 * a key's secret is shown once, which the create form says before it generates
 * anything rather than after the value has scrolled away.
 */
export function IdentityPage({ tab, focus, onTabChange }: IdentityPageProps) {
  const { data, isLoading, isError, error, refetch } = useIdentityQuery()

  const users = data?.users ?? []
  const grants = data?.grants ?? []
  const keys = data?.keys ?? []

  const ready = !isLoading && !isError

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "platform" }, { label: "identity" }]}
          title="Identity"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{users.length}</span> users
                {" · "}
                <span className={styles.strong}>{grants.length}</span> grants
                {" · "}
                <span className={styles.strong}>
                  {keys.filter((key) => key.status === "active").length}
                </span>{" "}
                keys in force
              </>
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="identity-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Identity did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="identity-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {ready ? (
          <Tabs
            className={styles.tabs}
            selectedKey={tab}
            onSelectionChange={(key) => {
              if (isIdentityTab(key)) {
                onTabChange(key)
              }
            }}
          >
            <TabList aria-label="Identity sections" className={styles.tabList}>
              <Tab id="users" className={styles.tab} data-test="tab-users">
                users <span className={styles.tabCount}>{users.length}</span>
              </Tab>
              <Tab id="grants" className={styles.tab} data-test="tab-grants">
                role assignments{" "}
                <span className={styles.tabCount}>{grants.length}</span>
              </Tab>
              <Tab id="keys" className={styles.tab} data-test="tab-keys">
                api keys <span className={styles.tabCount}>{keys.length}</span>
              </Tab>
            </TabList>

            {/* `focus` is handed only to the list it was written for. Switching
                tabs drops it from the URL, so a filter meant for one list can
                never appear over another. */}
            <TabPanel id="users" className={styles.tabPanel}>
              <UsersPanel
                users={users}
                initialFilter={tab === "users" ? focus : undefined}
              />
            </TabPanel>
            <TabPanel id="grants" className={styles.tabPanel}>
              <GrantsPanel
                grants={grants}
                initialFilter={tab === "grants" ? focus : undefined}
              />
            </TabPanel>
            <TabPanel id="keys" className={styles.tabPanel}>
              <KeysPanel
                keys={keys}
                initialFilter={tab === "keys" ? focus : undefined}
              />
            </TabPanel>
          </Tabs>
        ) : null}
      </div>
    </AppShell>
  )
}

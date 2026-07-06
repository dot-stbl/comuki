/* ============================================================
   Comuki Dashboard — Settings + Knowledge screens
   ============================================================ */
function SettingsScreen({ back, toast }) {
  const [sec, setSec] = React.useState('apps');
  const secs = [['apps', 'Apps', 'box'], ['rules', 'Rules', 'check-check'], ['autonomy', 'Autonomy', 'sliders'], ['routing', 'Routing', 'cpu'], ['keys', 'Keys', 'database'], ['tracker', 'Tracker', 'git-branch']];
  return (
    <div className="screen">
      <PageHead title="Settings" sub="control plane configuration"
        crumbs={<Crumbs items={[{ label: 'configure' }, { label: 'settings' }]} />} back={back} />
      <div className="screen-body">
      <div className="ui-tabs__list" style={{ marginBottom: 'var(--s4)' }}>
        {secs.map(s => (
          <button key={s[0]} className="ui-tab" aria-selected={sec === s[0]} onClick={() => setSec(s[0])}>
            <Icon name={s[2]} />{s[1]}
          </button>
        ))}
      </div>

      {sec === 'apps' && (
        <div className="panel">
          <div className="panel__h"><span>AppsRegistry</span><button className="ui-btn ui-btn--sm ui-btn--secondary"><Icon name="plus" />Add app</button></div>
          <table className="ui-table">
            <thead><tr><th>app</th><th>repo</th><th>stack</th><th>envs</th><th>deploy</th></tr></thead>
            <tbody>
              {window.APPS.map(a => (
                <tr key={a.name}>
                  <td><span className="applabel"><span className="tick" />{a.name}</span></td>
                  <td className="mono">{a.repo}</td>
                  <td>{a.stack}</td>
                  <td>{a.envs.map(e => <span key={e} className="ui-badge ui-badge--outline" style={{ marginRight: 4 }}>{e}</span>)}</td>
                  <td className="mono">{a.deploy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {sec === 'rules' && (
        <div>
          <div className="ui-alert" style={{ marginBottom: 'var(--s3)' }}><Icon name="info" /><div><div className="ui-alert__t">No conflicts found</div><div className="ui-alert__d">5 active rules · scopes don't overlap.</div></div></div>
          <div className="panel">
            <div className="panel__h"><span>RulesEditor</span><button className="ui-btn ui-btn--sm ui-btn--secondary"><Icon name="plus" />Add rule</button></div>
            <table className="ui-table">
              <thead><tr><th>rule</th><th>scope</th><th>kind</th><th>version</th><th>description</th></tr></thead>
              <tbody>
                {window.RULES.map(r => (
                  <tr key={r.id}>
                    <td className="mono">{r.id}</td>
                    <td className="mono" style={{ color: 'var(--text-muted)' }}>{r.scope}</td>
                    <td><span className={'ui-badge ' + (r.kind === 'hard' ? 'ui-badge--secondary' : 'ui-badge--outline')}>{r.kind}</span></td>
                    <td className="mono" style={{ color: 'var(--text-muted)' }}>@{r.ver}</td>
                    <td style={{ color: 'var(--text-muted)' }}>{r.desc}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {sec === 'autonomy' && (
        <div className="panel">
          <div className="panel__h"><span>AutonomyMatrix</span><span>what's auto · what needs a human</span></div>
          <table className="ui-table">
            <thead><tr><th>change class</th><th style={{ textAlign: 'right' }}>mode</th></tr></thead>
            <tbody>
              {window.AUTONOMY.map(a => (
                <tr key={a.cls}>
                  <td>{a.cls}</td>
                  <td style={{ textAlign: 'right' }}>
                    {a.mode === 'auto'
                      ? <span className="ui-badge ui-badge--default"><Icon name="zap" />auto</span>
                      : <span className="ui-badge ui-badge--outline"><Icon name="users" />human</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {sec === 'routing' && (
        <div>
          <div className="panel" style={{ marginBottom: 'var(--s3)' }}>
            <div className="panel__h"><span>ModelRouting</span><span>role → physical model</span></div>
            <table className="ui-table">
              <thead><tr><th>role</th><th>model</th><th>usage</th></tr></thead>
              <tbody>
                {window.ROUTING.map(r => (
                  <tr key={r.role}>
                    <td><span className="modelbadge"><Icon name="cpu" />{r.role}</span></td>
                    <td>{r.model}</td>
                    <td style={{ color: 'var(--text-muted)' }}>{r.use}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="ui-alert"><Icon name="chevrons-up" /><div><div className="ui-alert__t">Escalation policy</div><div className="ui-alert__d">2 failed retries on worker → escalate to lead. Red type gate → debug agent with a pinned revision.</div></div></div>
        </div>
      )}

      {sec === 'keys' && (
        <div className="panel">
          <div className="panel__h"><span>KeysPanel</span><button className="ui-btn ui-btn--sm ui-btn--secondary"><Icon name="rotate-ccw" />Rotate</button></div>
          <table className="ui-table">
            <thead><tr><th>provider</th><th>scope</th><th>rotation</th><th>status</th></tr></thead>
            <tbody>
              {window.KEYS.map(k => (
                <tr key={k.provider}>
                  <td className="mono">{k.provider}</td>
                  <td style={{ color: 'var(--text-muted)' }}>{k.scope}</td>
                  <td className="mono" style={{ color: 'var(--text-muted)' }}>{k.rotation}</td>
                  <td>{k.status === 'ok'
                    ? <span className="ui-badge ui-badge--outline"><Icon name="check" />ok</span>
                    : <span className="ui-badge ui-badge--secondary">{k.status}</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {sec === 'tracker' && (
        <div>
          <div className="ui-alert" style={{ marginBottom: 'var(--s3)' }}><Icon name="info" /><div><div className="ui-alert__t">Intake sources</div><div className="ui-alert__d">Подключённый трекер синкает тикеты в backlog · ручной ввод доступен всегда.</div></div></div>
          <div className="providers">
            {window.PROVIDERS.map(p => (
              <div key={p.id} className={'provider' + (p.connected ? ' provider--on' : '')}>
                <div className="provider__top">
                  <span className="provider__icon"><Icon name={p.icon} /></span>
                  <span className="provider__name">{p.name}</span>
                  {p.connected && <span className="provider__dot" title="connected" />}
                </div>
                <div className="provider__meta">{p.meta}</div>
                <div className="provider__foot">
                  {p.connected
                    ? <><span className="provider__last">synced {p.last}</span><button className="ui-btn ui-btn--sm ui-btn--ghost" onClick={() => toast && toast({ c: '', ic: 'rotate-ccw', t: 'Synced ' + p.name, d: 'imported new issues' })}><Icon name="rotate-ccw" />Sync</button></>
                    : <button className="ui-btn ui-btn--sm ui-btn--secondary" onClick={() => toast && toast({ c: '', ic: 'plus', t: 'Connect ' + p.name, d: 'OAuth flow…' })}><Icon name="plus" />Connect</button>}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      </div>
    </div>
  );
}

function KnowledgeScreen({ back }) {
  const k = window.KNOWLEDGE;
  const dmap = { '+': ['accent', 'check', 'improved'], '−': ['danger', 'x', 'regressed'], '=': ['muted', 'circle', 'no change'] };
  return (
    <div className="screen">
      <PageHead title="Knowledge" sub="rule set, revisions, eval harness"
        crumbs={<Crumbs items={[{ label: 'configure' }, { label: 'knowledge' }]} />} back={back} />
      <div className="screen-body">
      <div className="cost-top">
        <div className="statcard"><div className="lbl">Current revision</div><div className="val" style={{ fontSize: 18 }}>{k.revision.rules}</div><div className="sub">{k.revision.sdk} · updated {k.revision.updated}</div></div>
        <div className="statcard"><div className="lbl">Active rules</div><div className="val">{k.rulesActive}</div><div className="sub">{k.rulesHard} hard · {k.rulesSoft} soft</div></div>
        <div className="statcard"><div className="lbl">Reproducibility</div><div className="val">100<span className="unit">%</span></div><div className="sub">every run pins the rule set + SDK</div></div>
      </div>
      <div className="panel">
        <div className="panel__h"><span>EvalHarness · golden tasks</span><span>before → after on rule edits</span></div>
        <table className="ui-table">
          <thead><tr><th>task</th><th>before</th><th>after</th><th style={{ textAlign: 'right' }}>delta</th></tr></thead>
          <tbody>
            {k.eval.map(e => {
              const d = dmap[e.delta];
              return (
                <tr key={e.task}>
                  <td className="mono">{e.task}</td>
                  <td><Glyph st={e.before === 'pass' ? 'success' : 'failed'} /></td>
                  <td><Glyph st={e.after === 'pass' ? 'success' : 'failed'} /></td>
                  <td style={{ textAlign: 'right' }}>
                    <span className="ui-badge ui-badge--outline" style={{ color: 'var(--' + d[0] + ')', borderColor: 'color-mix(in oklab, var(--' + d[0] + ') 40%, transparent)' }}>
                      <Icon name={d[1]} />{d[2]}
                    </span>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      </div>
    </div>
  );
}

Object.assign(window, { SettingsScreen, KnowledgeScreen });

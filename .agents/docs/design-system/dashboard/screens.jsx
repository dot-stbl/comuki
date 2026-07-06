/* ============================================================
   Comuki Dashboard — screens
   ============================================================ */
const { useState } = React;

function CopyChip({ kind, value }) {
  const [done, setDone] = useState(false);
  return (
    <span className="chip chip--btn" onClick={(e) => { e.stopPropagation(); navigator.clipboard && navigator.clipboard.writeText(kind + '_' + value); setDone(true); setTimeout(() => setDone(false), 1000); }}>
      {done ? <Icon name="check" /> : <Icon name="copy" />}
      <span style={{ color: 'var(--text-faint)' }}>{kind}</span> {value}
    </span>
  );
}

function liveCost(run, tick) {
  const accruing = run.status === 'running' || run.status === 'escalated';
  return (run.cost + (accruing ? tick * 0.0011 : 0));
}
function liveDur(run, tick) {
  if (run.status === 'queued') return 0;
  const accruing = run.status === 'running' || run.status === 'escalated';
  return run.startSec + (accruing ? tick : 0);
}

/* ---------------- Pagination ---------------- */
function Pagination({ page, pages, total, perPage, onPage }) {
  if (total === 0) return null;
  const from = (page - 1) * perPage + 1;
  const to = Math.min(page * perPage, total);
  const nums = [];
  for (let i = 1; i <= pages; i++) {
    if (i === 1 || i === pages || Math.abs(i - page) <= 1) nums.push(i);
    else if (nums[nums.length - 1] !== '…') nums.push('…');
  }
  return (
    <div className="pagebar">
      <span className="pagebar__info">{from}–{to} of {total}</span>
      <nav className="ui-pagination">
        <button className="ui-page" disabled={page === 1} onClick={() => onPage(page - 1)} aria-label="Previous"><Icon name="chevron-left" /></button>
        {nums.map((n, i) => n === '…'
          ? <span key={'e' + i} className="ui-page" style={{ cursor: 'default' }}>…</span>
          : <button key={n} className="ui-page" aria-current={n === page ? 'page' : undefined} onClick={() => onPage(n)}>{n}</button>)}
        <button className="ui-page" disabled={page === pages} onClick={() => onPage(page + 1)} aria-label="Next"><Icon name="chevron-right" /></button>
      </nav>
    </div>
  );
}

/* ---------------- LiveRunsBoard ---------------- */
function RunCard({ run, onOpen, tick }) {
  return (
    <div className="runcard" onClick={() => onOpen(run.id)}>
      <div className="runcard__top">
        <span className="applabel"><span className="tick" />{run.app}</span>
        <span style={{ flex: 1 }} />
        <StatusBadge st={run.status} />
      </div>
      <div className="runcard__title">{run.title}</div>
      <div className="runcard__pipe"><StagePipeline stages={run.stages} current={run.current} sm /></div>
      <div className="runcard__meta">
        <CopyChip kind="run" value={run.id} />
        <Meta icon="timer">{fmtDur(liveDur(run, tick))}</Meta>
        <Meta icon="dollar-sign">{liveCost(run, tick).toFixed(2)}</Meta>
        <span style={{ flex: 1 }} />
        <Meta icon="cpu">{run.model}</Meta>
      </div>
    </div>
  );
}

function WorkerIndicator({ workers }) {
  const busy = workers.filter(w => w.state === 'busy').length;
  const stalled = workers.filter(w => w.state === 'stalled').length;
  const total = workers.length;
  const pct = Math.round((busy / total) * 100);
  return (
    <span className="wrkstat" title={busy + ' busy · ' + stalled + ' stalled · ' + total + ' workers'}>
      <Icon name="cpu" />
      <span className="wrkstat__txt"><b>{busy}</b><i>/{total}</i> busy</span>
      <span className="wrkstat__meter"><span className="wrkstat__fill" style={{ width: pct + '%' }} /></span>
      {stalled > 0 && <span className="wrkstat__stall"><span className="dot" />{stalled}</span>}
    </span>
  );
}

const RUN_STATUSES = ['all', 'running', 'waiting', 'escalated', 'queued', 'failed', 'success'];

function LiveRunsBoard({ runs, onOpenRun, tick, back }) {
  const [view, setView] = useState('grid');
  const [app, setApp] = useState('all');
  const [status, setStatus] = useState('all');
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const perPage = view === 'grid' ? 9 : 12;
  const apps = ['all', ...Array.from(new Set(window.RUNS.map(r => r.app)))];
  const ql = q.trim().toLowerCase();
  const shown = runs.filter(r =>
    (app === 'all' || r.app === app) &&
    (status === 'all' || r.status === status) &&
    (ql === '' || (r.id + ' ' + r.title + ' ' + r.app).toLowerCase().includes(ql)));
  const active = shown.filter(r => r.status === 'running' || r.status === 'escalated').length;
  const pages = Math.max(1, Math.ceil(shown.length / perPage));
  const cur = Math.min(page, pages);
  const slice = shown.slice((cur - 1) * perPage, cur * perPage);
  function pick(a) { setApp(a); setPage(1); }
  function setSt(s) { setStatus(s); setPage(1); }
  function search(v) { setQ(v); setPage(1); }
  function setV(v) { setView(v); setPage(1); }
  return (
    <div className="screen">
      <PageHead title="Live runs" sub={active + ' active · ' + shown.length + ' total'}
        crumbs={<Crumbs items={[{ label: 'observe' }, { label: 'live runs' }]} />} back={back}>
        <WorkerIndicator workers={window.WORKERS} />
        <div className="seg">
          <button aria-pressed={view === 'grid'} onClick={() => setV('grid')}><Icon name="grid" /></button>
          <button aria-pressed={view === 'table'} onClick={() => setV('table')}><Icon name="list" /></button>
        </div>
      </PageHead>
      <div className="screen-body">
        <div className="filterbar">
          <label className="filterbar__search">
            <Icon name="search" />
            <input type="text" placeholder="Search by run id, title or app…" value={q} onChange={e => search(e.target.value)} />
          </label>
          <select className="ui-select" style={{ width: 'auto', height: '30px' }} value={app} onChange={e => pick(e.target.value)}>
            {apps.map(a => <option key={a} value={a}>{a === 'all' ? 'all apps' : a}</option>)}
          </select>
          <select className="ui-select" style={{ width: 'auto', height: '30px' }} value={status} onChange={e => setSt(e.target.value)}>
            {RUN_STATUSES.map(s => <option key={s} value={s}>{s === 'all' ? 'all statuses' : s}</option>)}
          </select>
          <span className="filterbar__count">{shown.length} runs</span>
        </div>
        {shown.length === 0 ? (
          <div className="placeholder"><Icon name="search" /><h2>No matches</h2><span>Adjust search or filters.</span></div>
        ) : view === 'grid' ? (
          <div className="board">
            {slice.map(r => <RunCard key={r.id} run={r} onOpen={onOpenRun} tick={tick} />)}
          </div>
        ) : (
          <div className="panel"><table className="ui-table">
            <thead><tr><th>run</th><th>app</th><th>stage</th><th>status</th><th>time</th><th>cost</th><th></th></tr></thead>
            <tbody>
              {slice.map(r => (
                <tr key={r.id} style={{ cursor: 'pointer' }} onClick={() => onOpenRun(r.id)}>
                  <td className="mono">{r.id}</td><td>{r.app}</td>
                  <td>{(r.stages.find(s => s.key === r.current) || {}).label}</td>
                  <td><StatusBadge st={r.status} /></td>
                  <td className="mono">{fmtDur(liveDur(r, tick))}</td>
                  <td className="mono">${liveCost(r, tick).toFixed(2)}</td>
                  <td><Icon name="chevron-right" /></td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
        <Pagination page={cur} pages={pages} total={shown.length} perPage={perPage} onPage={setPage} />
      </div>
    </div>
  );
}

/* ---------------- RunTrace (timeline + diff) ---------------- */
function DiffFile({ f }) {
  const [open, setOpen] = useState(true);
  return (
    <div className="diff">
      <div className="diff__file" aria-expanded={open} onClick={() => setOpen(!open)}>
        <Icon name="chevron-down" /> {f.file}
        <span className="diff__stat"><span className="a">+{f.add}</span> <span className="d">−{f.del}</span></span>
      </div>
      {open && <div className="diff__body">
        {f.lines.map((l, i) => (
          <div key={i} className={'diff__row' + (l.ty === 'add' ? ' diff__row--add' : l.ty === 'del' ? ' diff__row--del' : '')}>
            <span className="diff__gutter">{l.n}</span>
            <span className="diff__sign">{l.ty === 'add' ? '+' : l.ty === 'del' ? '−' : ''}</span>
            <span className="diff__code">{l.text}</span>
          </div>
        ))}
      </div>}
    </div>
  );
}

function RunTrace({ run, onBack, tick }) {
  const tr = window.TRACE[run.id] || window.genericTrace(run);
  const [sel, setSel] = useState(run.current || run.stages[0].key);
  const selStage = run.stages.find(s => s.key === sel) || run.stages[0];
  const info = window.stageInfo(run, selStage.key);
  const idx = run.stages.indexOf(selStage) + 1;
  const crumbs = <Crumbs items={[{ label: 'runs', onClick: onBack }, { label: run.app }, { label: 'run_' + run.id }]} />;
  return (
    <div className="screen">
      <PageHead title={run.title} crumbs={crumbs} back={onBack}><StatusBadge st={run.status} /></PageHead>
      <div className="screen-body">
        <div className="trace__head">
          <div className="titlerow" style={{ marginBottom: 0 }}>
            <CopyChip kind="run" value={run.id} />
            <span className="modelbadge"><Icon name="cpu" />{run.model}</span>
            <span className="metaitem"><Icon name="timer" />{fmtDur(liveDur(run, tick))}</span>
            <span className="metaitem"><Icon name="dollar-sign" />{liveCost(run, tick).toFixed(2)} · {(run.tokens / 1000).toFixed(1)}k tok</span>
            <span className="metaitem"><Icon name="git-branch" />{tr.revision.rules} · {tr.revision.sdk}</span>
          </div>
          <div className="trace__brief" dangerouslySetInnerHTML={{ __html: tr.brief.replace(/`([^`]+)`/g, '<code>$1</code>') }} />
          <div className="trace__pipe" style={{ marginTop: 'var(--s4)' }}>
            <StagePipeline stages={run.stages} current={run.current} selected={sel} onSelect={setSel} />
          </div>
        </div>

        <div className="trace__grid">
          <div className="panel">
            <div className="panel__h"><span>Stage log · {selStage.label}</span><span>append-only</span></div>
            <div className="stagelog">
              {info.events.map((e, i) => (
                <div className="ev" data-st={e.st} key={i}>
                  <div className="ev__t">{e.t}</div>
                  <div className="ev__x">{e.text}</div>
                </div>
              ))}
            </div>
          </div>

          <div className="panel">
            <div className="panel__h"><span>{selStage.label} · stage {idx}/{run.stages.length}</span><StatusBadge st={selStage.status} /></div>
            <div className="panel__b">
              <div className="ins-meta">
                <span className="mb"><Icon name="cpu" />model <b>{info.role}</b></span>
                <span className="mb"><Icon name="bar-chart" /><b>{info.tokens}</b> tok</span>
                <span className="mb"><Icon name="dollar-sign" /><b>${info.cost}</b></span>
                <span className="mb"><Icon name="server" />env <b>{info.env}</b></span>
              </div>
              <div className="ins-sec">
                <div className="ins-h">Input — what fed it</div>
                <div className="iochips">
                  {info.inputs.map((it, i) => <span className="iochip" key={i}><Icon name={it[0]} />{it[1]}{it[2] && <span className="v"> · {it[2]}</span>}</span>)}
                </div>
              </div>
              {info.gate && <div className="ins-sec">
                <div className="ins-h">Verification gate</div>
                <div className="gaterow">{info.gate.map((g, i) => <span className="gchip" data-st={g.st} key={i}><Glyph st={g.st} />{g.name}</span>)}</div>
              </div>}
              <div className="ins-sec">
                <div className="ins-h">Output — what it produced</div>
                {info.files
                  ? info.files.map((f, i) => <DiffFile key={i} f={f} />)
                  : <div className="outrows">{info.outRows.map((o, i) => <div className="io-row" key={i}><Icon name={o[0]} /><span className="k">{o[1]}</span>{o[2] && <span className="val">{o[2]}</span>}</div>)}</div>}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ---------------- ApprovalQueue ---------------- */
const ATYPE = { plan: ['git-branch', 'Plan'], deploy: ['zap', 'Deploy'], baseline: ['image', 'Baseline'] };

function ApprovalCard({ a, onAction }) {
  const [open, setOpen] = useState(false);
  const run = window.RUNS.find(r => r.id === a.run);
  const ty = ATYPE[a.type];
  return (
    <div className="acard">
      <div className="acard__top">
        <span className="atype"><Icon name={ty[0]} />{ty[1]}</span>
        <span className="applabel"><span className="tick" />{a.app}</span>
        <span className="risktag" data-r={a.risk}><Icon name="triangle-alert" />{a.risk}</span>
        <span className="acard__age"><Icon name="clock" /> {a.age}</span>
      </div>
      <div className="acard__summary">{a.summary}</div>
      <div className="acard__actions">
        <button className="ui-btn ui-btn--ghost ui-btn--sm" onClick={() => setOpen(!open)}>
          <Icon name="chevron-down" /> {open ? 'Hide' : 'Details'}
        </button>
        <span className="spacer" />
        <button className="ui-btn ui-btn--sm ui-btn--ghost" onClick={() => onAction(a.id, 'review')}><Icon name="eye" /> Review</button>
        <button className="ui-btn ui-btn--sm ui-btn--danger-ghost" onClick={() => onAction(a.id, 'reject')}><Icon name="x" /> Reject</button>
        <button className="ui-btn ui-btn--sm" onClick={() => onAction(a.id, 'approve')}><Icon name="check" /> Approve</button>
      </div>
      <div className="acard__details" data-open={open || undefined}>
        {a.type === 'plan' && run && (
          <div style={{ marginBottom: 'var(--s4)', overflowX: 'auto' }}>
            <span className="ui-hint" style={{ display: 'block', marginBottom: '6px' }}>Plan — stage DAG</span>
            <StagePipeline stages={run.stages} current="" />
          </div>
        )}
        {a.type === 'baseline' && (
          <div className="vreview" style={{ marginBottom: 'var(--s4)' }}>
            <div className="vshot"><div className="vshot__h">baseline</div><div className="vshot__img"><Icon name="image" /></div></div>
            <div className="vshot"><div className="vshot__h">new</div><div className="vshot__img"><Icon name="image" /></div></div>
          </div>
        )}
        <span className="ui-hint" style={{ display: 'block', marginBottom: '6px' }}>Planner assumptions</span>
        <ul className="assume" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {a.assumptions.map((x, i) => <li key={i}><Icon name="arrow-right" /> {x}</li>)}
        </ul>
      </div>
    </div>
  );
}

function ApprovalQueue({ approvals, onAction, back }) {
  return (
    <div className="screen">
      <PageHead title="Approvals" sub={approvals.length + ' awaiting decision'}
        crumbs={<Crumbs items={[{ label: 'observe' }, { label: 'approvals' }]} />} back={back} />
      <div className="screen-body">
        {approvals.length === 0
          ? <div className="placeholder"><Icon name="check-check" /><h2>Queue empty</h2><span>Nothing awaiting a human.</span></div>
          : <div className="aqueue">{approvals.map(a => <ApprovalCard key={a.id} a={a} onAction={onAction} />)}</div>}
      </div>
    </div>
  );
}

/* ---------------- Cost ---------------- */
function CostScreen({ back }) {
  const c = window.COST;
  const max = Math.max(...c.byApp.map(a => a.spend));
  return (
    <div className="screen">
      <PageHead title="Cost & failures" sub="last 24h"
        crumbs={<Crumbs items={[{ label: 'observe' }, { label: 'cost' }]} />} back={back} />
      <div className="screen-body">
        <div className="cost-top">
          <div className="statcard"><div className="lbl">Cost per success</div><div className="val"><span className="unit">$</span>{c.perSuccess.toFixed(2)}</div><div className="sub">key business metric — per successful task, not per call</div></div>
          <div className="statcard"><div className="lbl">Per day</div><div className="val"><span className="unit">$</span>{c.totalDay.toFixed(0)}</div><div className="sub">{Math.round(c.successRate * 100)}% of tasks — green gate</div></div>
          <div className="statcard"><div className="lbl">Proxy budget</div><div className="val">{Math.round(c.budget.used / c.budget.cap * 100)}<span className="unit">%</span></div>
            <div className="ui-progress" style={{ marginTop: '8px' }}><div className="ui-progress__bar" style={{ width: (c.budget.used / c.budget.cap * 100) + '%' }} /></div>
            <div className="sub">${c.budget.used.toFixed(0)} / ${c.budget.cap.toFixed(0)} · kill-switch at cap</div></div>
        </div>
        <div className="cost-grid">
          <div className="panel">
            <div className="panel__h"><span>CostBreakdown · by app</span><span>spend</span></div>
            <div className="panel__b">
              {c.byApp.map(a => (
                <div className="barrow" key={a.app}>
                  <span className="nm">{a.app}</span>
                  <span className="track"><span className="fill" style={{ width: (a.spend / max * 100) + '%' }} /></span>
                  <span className="amt">${a.spend.toFixed(1)}</span>
                </div>
              ))}
            </div>
          </div>
          <div className="panel">
            <div className="panel__h"><span>FailureAnalytics</span><span>where it breaks</span></div>
            <div className="panel__b tests">
              {c.failures.map(f => (
                <div className="test-row" key={f.stage}>
                  <Glyph st="failed" /><span className="nm" style={{ width: '80px' }}>{f.stage}</span>
                  <span className="detail">{Math.round(f.rate * 100)}% · {f.note}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function Placeholder({ icon, title }) {
  return <div className="screen"><PageHead icon={icon} title={title} /><div className="screen-body"><div className="placeholder"><Icon name={icon} /><h2>{title}</h2><span>Section under construction.</span></div></div></div>;
}

Object.assign(window, { LiveRunsBoard, RunTrace, ApprovalQueue, CostScreen, Placeholder });

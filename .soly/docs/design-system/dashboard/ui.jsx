/* ============================================================
   Comuki Dashboard — shared React UI atoms
   ============================================================ */
const STATUS_ICON = { running:'activity', success:'check', failed:'x', waiting:'clock', queued:'circle', escalated:'chevrons-up' };

function Icon({ name, cls }) {
  return <svg className={'ic ' + (cls || '')} viewBox="0 0 24 24" aria-hidden="true"
    dangerouslySetInnerHTML={{ __html: (window.ICONS[name] || '') }} />;
}

function Glyph({ st }) {
  return <span className="glyph" data-st={st}><Icon name={STATUS_ICON[st]} /></span>;
}

function StatusBadge({ st, label }) {
  const txt = label || st.charAt(0).toUpperCase() + st.slice(1);
  return <span className="badge" data-st={st}><Glyph st={st} />{txt}</span>;
}

function fmtDur(sec) {
  const m = Math.floor(sec / 60), s = sec % 60;
  return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
}

/* StagePipeline — segments (compact, on run cards) · boxes (detail, in trace) */
function columnsOf(stages) {
  const c = [], n = stages.length; let i = 0;
  while (i < n) {
    if (stages[i].lane) { const g = []; while (i < n && stages[i].lane) { g.push(stages[i]); i++; } c.push({ lane: true, stages: g }); }
    else { c.push({ lane: false, stages: [stages[i]] }); i++; }
  }
  return c;
}

function StagePipeline({ stages, current, sm, selected, onSelect }) {
  const [tip, setTip] = React.useState(null);
  const cols = columnsOf(stages);
  const enter = (e, s) => { const r = e.currentTarget.getBoundingClientRect(); setTip({ x: r.left + r.width / 2, y: r.top, label: s.label, status: s.status, dur: s.dur }); };
  const leave = () => setTip(null);
  const tipEl = tip && (
    <div className="pipe-tip" style={{ left: tip.x, top: tip.y }}>
      <span className="pipe-tip__d" style={{ background: 'var(--st-' + tip.status + ')' }} /><b>{tip.label}</b> · {tip.status}{tip.dur ? ' · ' + tip.dur : ''}
    </div>
  );

  if (sm) {
    return (
      <div className="segpipe">
        <div className="segbar">
          {cols.map((col, i) => col.lane
            ? <div key={i} className={'seg seg--par' + (col.stages.some(s => s.key === current) ? ' seg--cur' : '')}>
                <div className="parinner">
                  {col.stages.map(s => <div key={s.key} className={'parbar' + (s.status === 'running' ? ' is-running' : '')} style={{ background: 'var(--st-' + s.status + ')' }} onMouseEnter={e => enter(e, s)} onMouseLeave={leave} />)}
                </div>
              </div>
            : <div key={i} className={'seg' + (col.stages[0].status === 'running' ? ' is-running' : '') + (col.stages[0].key === current ? ' seg--cur' : '')} style={{ background: 'var(--st-' + col.stages[0].status + ')' }} onMouseEnter={e => enter(e, col.stages[0])} onMouseLeave={leave} />)}
        </div>
        {tipEl}
      </div>
    );
  }

  const box = (s) => (
    <div key={s.key} className={'boxnode' + (s.key === selected ? ' is-sel' : '')} data-st={s.status}
      onClick={() => onSelect && onSelect(s.key)} onMouseEnter={e => enter(e, s)} onMouseLeave={leave}>
      <div className="boxnode__ic"><Icon name={STATUS_ICON[s.status]} /></div>
      <div className="boxnode__nm">{s.label}</div>
    </div>
  );
  return (
    <div className="boxpipe">
      <div className="dstripe">
        {cols.map((col, i) => (
          <React.Fragment key={i}>
            {i > 0 && <div className={'dlink' + (cols[i - 1].stages.every(s => s.status === 'success') ? ' dlink--done' : '')} />}
            {col.lane
              ? <div className="dlanes"><span className="dlanes__tag">∥ {col.stages.length}</span>{col.stages.map(s => box(s))}</div>
              : box(col.stages[0])}
          </React.Fragment>
        ))}
      </div>
      {tipEl}
    </div>
  );
}

/* small metric chip used on cards */
function Meta({ icon, children }) {
  return <span className="metaitem">{icon && <Icon name={icon} />}{children}</span>;
}

/* structured page header — separates page start from content */
function PageHead({ title, sub, crumbs, back, children }) {
  return (
    <header className="page-head">
      <div className="page-head__main">
        {(crumbs || back) && (
          <nav className="page-head__crumbs">
            {back && <button className="page-head__back" onClick={back} aria-label="Back"><Icon name="chevron-left" /></button>}
            {crumbs}
          </nav>
        )}
        <h1>{title}</h1>
        {sub && <div className="page-head__sub">{sub}</div>}
      </div>
      {children && <div className="page-head__tools">{children}</div>}
    </header>
  );
}

/* breadcrumbs */
function Crumbs({ items }) {
  return items.map((it, i) => (
    <React.Fragment key={i}>
      {i > 0 && <Icon name="chevron-right" />}
      {it.onClick
        ? <a onClick={it.onClick}>{it.label}</a>
        : <span className={i === items.length - 1 ? 'cur' : ''}>{it.label}</span>}
    </React.Fragment>
  ));
}

Object.assign(window, { STATUS_ICON, Icon, Glyph, StatusBadge, fmtDur, StagePipeline, Meta, PageHead, Crumbs });

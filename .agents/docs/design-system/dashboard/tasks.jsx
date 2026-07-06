/* ============================================================
   Comuki Dashboard — Tasks intake (backlog + create-task modal)
   wrapped in an IIFE so top-level consts don't collide with the
   other text/babel scripts (they share global scope).
   ============================================================ */
(function () {
  const { useState } = React;
  const PRIORITY = ['low', 'normal', 'high'];

  function SrcBadge({ src, id }) {
    return src === 'manual'
      ? <span className="srcbadge" data-src="manual"><Icon name="plus" />manual</span>
      : <span className="srcbadge" data-src="jira"><Icon name="git-branch" />{id}</span>;
  }

  function NewTaskModal({ apps, onClose, onCreate }) {
    const [title, setTitle] = useState('');
    const [app, setApp] = useState(apps[0]);
    const [priority, setPriority] = useState('normal');
    const [brief, setBrief] = useState('');
    function submit(e) {
      e.preventDefault();
      if (!title.trim()) return;
      onCreate({ title: title.trim(), app, priority, brief: brief.trim() });
    }
    return (
      <div className="modal-overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
        <form className="modal" onSubmit={submit}>
          <div className="modal__head">
            <h3>New task</h3>
            <button type="button" className="modal__close" onClick={onClose} aria-label="Close"><Icon name="x" /></button>
          </div>
          <div className="modal__body">
            <div className="ui-field">
              <label className="ui-label">Title</label>
              <input className="ui-input" autoFocus value={title} onChange={e => setTitle(e.target.value)} placeholder="Что сделать — кратко" />
            </div>
            <div className="taskform__row">
              <div className="ui-field">
                <label className="ui-label">App</label>
                <select className="ui-select" value={app} onChange={e => setApp(e.target.value)}>
                  {apps.map(a => <option key={a} value={a}>{a}</option>)}
                </select>
              </div>
              <div className="ui-field">
                <label className="ui-label">Priority</label>
                <div className="seg-pri">
                  {PRIORITY.map(p => <button type="button" key={p} aria-pressed={priority === p} onClick={() => setPriority(p)}>{p}</button>)}
                </div>
              </div>
            </div>
            <div className="ui-field">
              <label className="ui-label">Brief</label>
              <textarea className="ui-textarea" value={brief} onChange={e => setBrief(e.target.value)} placeholder="Контекст, критерии приёмки, ссылки…" />
            </div>
          </div>
          <div className="modal__foot">
            <button type="button" className="ui-btn ui-btn--sm ui-btn--ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="ui-btn ui-btn--sm" disabled={!title.trim()}><Icon name="plus" />Create &amp; queue</button>
          </div>
        </form>
      </div>
    );
  }

  function TasksScreen({ back, toast }) {
    const [tasks, setTasks] = useState(window.TASKS);
    const [modal, setModal] = useState(false);
    const apps = Array.from(new Set(window.RUNS.map(r => r.app)));

    function create(t) {
      const id = 'm-' + Math.floor(3042 + Math.random() * 900);
      setTasks(list => [{ id, source: 'manual', title: t.title, app: t.app, priority: t.priority, status: 'queued', age: 'just now' }, ...list]);
      setModal(false);
      toast && toast({ c: '', ic: 'check', t: 'Task created', d: t.title });
    }
    function dispatch(task) {
      setTasks(list => list.map(x => x.id === task.id ? { ...x, status: 'planning' } : x));
      toast && toast({ c: '', ic: 'zap', t: 'Dispatched to orchestrator', d: task.title });
    }
    const newCount = tasks.filter(t => t.status === 'new').length;

    return (
      <div className="screen">
        <PageHead title="Tasks" sub={tasks.length + ' in backlog · ' + newCount + ' new'}
          crumbs={<Crumbs items={[{ label: 'intake' }, { label: 'tasks' }]} />} back={back}>
          <button className="ui-btn ui-btn--sm" onClick={() => setModal(true)}><Icon name="plus" />New task</button>
        </PageHead>
        <div className="screen-body">
          <div className="panel">
            <div className="panel__h"><span>Backlog · {tasks.length}</span><span>Jira + manual</span></div>
            <table className="ui-table">
              <thead><tr><th>source</th><th>task</th><th>app</th><th>priority</th><th>status</th><th></th></tr></thead>
              <tbody>
                {tasks.map(t => (
                  <tr key={t.id}>
                    <td><SrcBadge src={t.source} id={t.id} /></td>
                    <td>{t.title}</td>
                    <td><span className="applabel"><span className="tick" />{t.app}</span></td>
                    <td><span className="pri" data-p={t.priority}>{t.priority}</span></td>
                    <td><span className="tstatus" data-s={t.status}>{t.status}</span></td>
                    <td style={{ textAlign: 'right' }}>
                      {t.status === 'planning'
                        ? <span className="tstatus" data-s="planning"><Icon name="activity" />planning</span>
                        : <button className="ui-btn ui-btn--sm ui-btn--ghost" onClick={() => dispatch(t)}><Icon name="zap" />Dispatch</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        {modal && <NewTaskModal apps={apps} onClose={() => setModal(false)} onCreate={create} />}
      </div>
    );
  }

  Object.assign(window, { TasksScreen });
})();

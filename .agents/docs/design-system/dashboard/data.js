/* ============================================================
   Comuki Dashboard — mock data + icon set (window globals)
   ============================================================ */
window.ICONS = {
  activity:'<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  check:'<path d="M20 6 9 17l-5-5"/>',
  'check-check':'<path d="M18 6 7 17l-5-5"/><path d="m22 10-7.5 7.5L13 16"/>',
  x:'<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
  clock:'<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
  circle:'<circle cx="12" cy="12" r="9"/>',
  'chevrons-up':'<path d="m17 11-5-5-5 5"/><path d="m17 18-5-5-5 5"/>',
  'chevron-down':'<path d="m6 9 6 6 6-6"/>','chevron-right':'<path d="m9 18 6-6-6-6"/>','chevron-left':'<path d="m15 18-6-6 6-6"/>',
  copy:'<rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
  'git-branch':'<line x1="6" x2="6" y1="3" y2="15"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/>',
  'git-commit':'<circle cx="12" cy="12" r="3"/><line x1="3" x2="9" y1="12" y2="12"/><line x1="15" x2="21" y1="12" y2="12"/>',
  'dollar-sign':'<line x1="12" x2="12" y1="2" y2="22"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>',
  cpu:'<rect width="16" height="16" x="4" y="4" rx="2"/><rect width="6" height="6" x="9" y="9" rx="1"/><path d="M15 2v2"/><path d="M15 20v2"/><path d="M2 15h2"/><path d="M2 9h2"/><path d="M20 15h2"/><path d="M20 9h2"/><path d="M9 2v2"/><path d="M9 20v2"/>',
  timer:'<line x1="10" x2="14" y1="2" y2="2"/><line x1="12" x2="12" y1="14" y2="11"/><circle cx="12" cy="14" r="8"/>',
  'triangle-alert':'<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  eye:'<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>',
  'external-link':'<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h6"/>',
  sliders:'<line x1="21" x2="14" y1="4" y2="4"/><line x1="10" x2="3" y1="4" y2="4"/><line x1="21" x2="12" y1="12" y2="12"/><line x1="8" x2="3" y1="12" y2="12"/><line x1="21" x2="16" y1="20" y2="20"/><line x1="12" x2="3" y1="20" y2="20"/><line x1="14" x2="14" y1="2" y2="6"/><line x1="8" x2="8" y1="10" y2="14"/><line x1="16" x2="16" y1="18" y2="22"/>',
  'rotate-ccw':'<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/>',
  sun:'<circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/>',
  moon:'<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/>',
  plus:'<path d="M5 12h14"/><path d="M12 5v14"/>',
  settings:'<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2Z"/><circle cx="12" cy="12" r="3"/>',
  trash:'<path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
  ellipsis:'<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
  search:'<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  info:'<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
  bell:'<path d="M10.268 21a2 2 0 0 0 3.464 0"/><path d="M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326"/>',
  power:'<path d="M12 2v10"/><path d="M18.4 6.6a9 9 0 1 1-12.77.04"/>',
  inbox:'<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>',
  'bar-chart':'<line x1="12" x2="12" y1="20" y2="10"/><line x1="18" x2="18" y1="20" y2="4"/><line x1="6" x2="6" y1="20" y2="16"/>',
  database:'<ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5V19A9 3 0 0 0 21 19V5"/><path d="M3 12A9 3 0 0 0 21 12"/>',
  layers:'<path d="m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>',
  users:'<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
  play:'<polygon points="6 3 20 12 6 21 6 3"/>',
  pause:'<rect x="14" y="4" width="4" height="16" rx="1"/><rect x="6" y="4" width="4" height="16" rx="1"/>',
  zap:'<path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/>',
  flask:'<path d="M10 2v7.31"/><path d="M14 9.3V1.99"/><path d="M8.5 2h7"/><path d="M14 9.3a6.5 6.5 0 1 1-4 0"/><path d="M5.52 16h12.96"/>',
  image:'<rect width="18" height="18" x="3" y="3" rx="2" ry="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/>',
  box:'<path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
  filter:'<polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>',
  'arrow-right':'<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
  grid:'<rect width="7" height="7" x="3" y="3" rx="1"/><rect width="7" height="7" x="14" y="3" rx="1"/><rect width="7" height="7" x="14" y="14" rx="1"/><rect width="7" height="7" x="3" y="14" rx="1"/>',
  list:'<line x1="8" x2="21" y1="6" y2="6"/><line x1="8" x2="21" y1="12" y2="12"/><line x1="8" x2="21" y1="18" y2="18"/><line x1="3" x2="3.01" y1="6" y2="6"/><line x1="3" x2="3.01" y1="12" y2="12"/><line x1="3" x2="3.01" y1="18" y2="18"/>',
  gauge:'<path d="m12 14 4-4"/><path d="M3.34 19a10 10 0 1 1 17.32 0"/>',
  command:'<path d="M15 6v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3V6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3"/>',
  lock:'<rect width="18" height="11" x="3" y="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
  file:'<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v5h5"/>',
  terminal:'<path d="m4 17 6-6-6-6"/><path d="M12 19h8"/>',
  server:'<rect width="20" height="8" x="2" y="3" rx="2"/><rect width="20" height="8" x="2" y="13" rx="2"/><path d="M6 7h.01"/><path d="M6 17h.01"/>',
  book:'<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2"/>'
};

/* ---- swarm-wide status ---- */
window.SWARM = { running: 7, waiting: 3, failed: 1, queued: 5, escalated: 1 };

/* ---- DAG stage template ---- */
window.STAGE_TEMPLATE = [
  { key:'explore',  label:'explore' },
  { key:'plan',     label:'plan' },
  { key:'contract', label:'contract' },
  { key:'back',     label:'backend',  lane:'a' },
  { key:'front',    label:'frontend', lane:'b' },
  { key:'sync',     label:'sync' },
  { key:'tests',    label:'tests' },
  { key:'deploy',   label:'deploy' },
  { key:'doc',      label:'doc' }
];

function st(map){ return window.STAGE_TEMPLATE.map(function(s){ return Object.assign({}, s, { status: map[s.key] || 'queued' }); }); }

window.RUNS = [
  { id:'8f3c2a91', app:'billing-api', title:"Идемпотентность в обработчике webhook'ов Stripe",
    status:'running', current:'back', model:'worker', cost:0.42, tokens:18400, startSec:252,
    stages: st({explore:'success',plan:'success',contract:'success',back:'running',front:'success',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'b3d8a402', app:'web-app', title:'Скелетоны загрузки на дашборде прогонов',
    status:'running', current:'front', model:'worker', cost:0.18, tokens:7200, startSec:96,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'running',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'5b1d7e40', app:'web-app', title:'Виртуализация таблицы прогонов (react-window)',
    status:'waiting', current:'deploy', model:'worker', cost:1.08, tokens:42100, startSec:775,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'waiting',doc:'queued'}) },
  { id:'2a6f1c33', app:'auth-svc', title:'Ротация JWT-ключей без даунтайма',
    status:'escalated', current:'back', model:'lead', cost:2.31, tokens:96800, startSec:540,
    stages: st({explore:'success',plan:'success',contract:'success',back:'escalated',front:'success',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'9d72b5f0', app:'docs-site', title:'Миграция на новый theme API',
    status:'failed', current:'contract', model:'worker', cost:0.19, tokens:8300, startSec:121,
    stages: st({explore:'success',plan:'success',contract:'failed',back:'queued',front:'queued',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'c40aa2e1', app:'worker-pool', title:'Ретраи с экспоненциальным бэкоффом',
    status:'success', current:'doc', model:'worker', cost:0.67, tokens:25600, startSec:510, done:true,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'success',doc:'success'}) },
  { id:'7e0b9d12', app:'billing-api', title:'Кэш идемпотентных ответов в Redis',
    status:'queued', current:'explore', model:'worker', cost:0.0, tokens:0, startSec:0,
    stages: st({explore:'queued',plan:'queued',contract:'queued',back:'queued',front:'queued',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'a1f4c8d2', app:'web-app', title:'Тёмная тема для экрана настроек',
    status:'running', current:'front', model:'worker', cost:0.24, tokens:9800, startSec:140,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'running',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'4c91e6a7', app:'worker-pool', title:'Грейсфул-шатдаун при rolling deploy',
    status:'success', current:'doc', model:'worker', cost:0.53, tokens:21300, startSec:430, done:true,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'success',doc:'success'}) },
  { id:'d8b2705f', app:'auth-svc', title:'Rate-limit на эндпоинт логина',
    status:'waiting', current:'deploy', model:'worker', cost:0.71, tokens:28900, startSec:610,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'waiting',doc:'queued'}) },
  { id:'f3e0a91b', app:'billing-api', title:'Вебхук-ретраи с дедупликацией по event_id',
    status:'running', current:'back', model:'worker', cost:0.39, tokens:16700, startSec:205,
    stages: st({explore:'success',plan:'success',contract:'success',back:'running',front:'success',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'6a7d4e22', app:'docs-site', title:'Поиск по документации (Pagefind)',
    status:'queued', current:'explore', model:'worker', cost:0.0, tokens:0, startSec:0,
    stages: st({explore:'queued',plan:'queued',contract:'queued',back:'queued',front:'queued',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'b5c89f01', app:'web-app', title:'Оптимистичные апдейты в очереди аппрувов',
    status:'escalated', current:'sync', model:'lead', cost:1.84, tokens:74200, startSec:495,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'escalated',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'2f6b1a90', app:'worker-pool', title:'Метрики heartbeat воркеров в Prometheus',
    status:'success', current:'doc', model:'worker', cost:0.44, tokens:18100, startSec:380, done:true,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'success',doc:'success'}) },
  { id:'9c3e7b44', app:'auth-svc', title:'Миграция сессий на Redis-кластер',
    status:'failed', current:'tests', model:'worker', cost:0.92, tokens:36400, startSec:520,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'failed',deploy:'queued',doc:'queued'}) },
  { id:'e7a05c18', app:'billing-api', title:'Экспорт инвойсов в CSV/PDF',
    status:'running', current:'front', model:'worker', cost:0.28, tokens:11200, startSec:165,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'running',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'1b8f3d67', app:'docs-site', title:'OG-картинки для страниц гайдов',
    status:'queued', current:'explore', model:'worker', cost:0.0, tokens:0, startSec:0,
    stages: st({explore:'queued',plan:'queued',contract:'queued',back:'queued',front:'queued',sync:'queued',tests:'queued',deploy:'queued',doc:'queued'}) },
  { id:'5d24a0f9', app:'web-app', title:'Виртуализация ленты событий трейса',
    status:'success', current:'doc', model:'worker', cost:0.61, tokens:24800, startSec:455, done:true,
    stages: st({explore:'success',plan:'success',contract:'success',back:'success',front:'success',sync:'success',tests:'success',deploy:'success',doc:'success'}) }
];

/* ---- worker pool ---- */
window.WORKERS = [
  { id:'w-01', state:'busy', run:'8f3c2a91' }, { id:'w-02', state:'busy', run:'b3d8a402' },
  { id:'w-03', state:'busy', run:'2a6f1c33' }, { id:'w-04', state:'idle' },
  { id:'w-05', state:'busy', run:'5b1d7e40' }, { id:'w-06', state:'stalled' },
  { id:'w-07', state:'idle' }, { id:'w-08', state:'busy', run:'9d72b5f0' }
];

/* ---- trace detail (rich for the focus run) ---- */
window.TRACE = {
  '8f3c2a91': {
    brief: "Сделать обработчик `POST /webhooks/stripe` идемпотентным. Ключ идемпотентности — заголовок `Idempotency-Key`; при повторе вернуть сохранённый ответ, не пере-выполняя side-effects. Ретраи Stripe (до 3 сут) должны быть безопасны.",
    rules: ['api-errors','db-tx','no-secrets'],
    revision: { rules:'rules@a1b9e0', sdk:'sdk@2.4.1' },
    events: [
      { t:'00:00', st:'success',  ic:'zap',         text:'Запрос принят · план мозга построен' },
      { t:'00:04', st:'success',  ic:'check',       text:'Стадия «изучатор» завершена — 4 файла в контексте' },
      { t:'00:31', st:'success',  ic:'check',       text:'Стадия «план» завершена · DAG 8 стадий' },
      { t:'01:02', st:'success',  ic:'check',       text:'Контракт согласован · применено 3 правила' },
      { t:'01:05', st:'success',  ic:'git-branch',  text:'Лейн «фронт» завершён (no-op, только бек)' },
      { t:'01:48', st:'running',  ic:'activity',    text:'Лейн «бек» в работе · правка handler + миграция' },
      { t:'03:10', st:'waiting',  ic:'rotate-ccw',  text:'Retry юнит-теста test_replay (1/3)' },
      { t:'04:12', st:'running',  ic:'cpu',         text:'worker · gpt-class · 18.4k токенов накоплено' }
    ],
    diff: [
      { file:'src/webhooks/stripe.ts', add:14, del:3, lines:[
        { ty:'ctx', n:'42', text:"export async function handleStripe(req: Req) {" },
        { ty:'add', n:'43', text:"  const key = req.headers['idempotency-key'];" },
        { ty:'add', n:'44', text:"  if (key) {" },
        { ty:'add', n:'45', text:"    const cached = await store.get(key);" },
        { ty:'add', n:'46', text:"    if (cached) return cached.response;" },
        { ty:'add', n:'47', text:"  }" },
        { ty:'del', n:'43', text:"  // TODO: idempotency" },
        { ty:'ctx', n:'48', text:"  const event = verify(req.body, req.sig);" },
        { ty:'add', n:'49', text:"  const res = await processEvent(event);" },
        { ty:'add', n:'50', text:"  if (key) await store.put(key, res, { ttl: 259200 });" },
        { ty:'ctx', n:'51', text:"  return res;" }
      ]},
      { file:'migrations/0042_idem_keys.sql', add:6, del:0, lines:[
        { ty:'add', n:'1', text:"CREATE TABLE idem_keys (" },
        { ty:'add', n:'2', text:"  key text PRIMARY KEY," },
        { ty:'add', n:'3', text:"  response jsonb NOT NULL," },
        { ty:'add', n:'4', text:"  created_at timestamptz DEFAULT now()" },
        { ty:'add', n:'5', text:");" }
      ]}
    ],
    tests: [
      { name:'types',      st:'success', detail:'tsc — 0 errors' },
      { name:'lint',       st:'success', detail:'eslint — clean' },
      { name:'unit',       st:'running', detail:'42/44 · retry test_replay' },
      { name:'e2e',        st:'queued',  detail:'Playwright — waiting' },
      { name:'visual',     st:'queued',  detail:'Storybook diff — waiting' }
    ]
  }
};

/* generic trace for runs without rich data */
window.genericTrace = function(run){
  var ev = [];
  run.stages.forEach(function(s,i){
    if(s.status==='queued') return;
    var mm = String(Math.floor(i*0.9)).padStart(2,'0');
    ev.push({ t:mm+':'+String((i*17)%60).padStart(2,'0'), st:s.status,
      ic: s.status==='success'?'check': s.status==='running'?'activity': s.status==='failed'?'x': s.status==='escalated'?'chevrons-up':'clock',
      text:'Stage «'+s.label+'» — '+s.status });
  });
  return { brief:run.title+'. Worker brief for the current stage.', rules:['api-errors','db-tx'],
    revision:{rules:'rules@a1b9e0',sdk:'sdk@2.4.1'}, events:ev,
    diff:[{file:'src/changes.ts', add:8, del:2, lines:[
      {ty:'ctx',n:'1',text:'// '+run.title}, {ty:'add',n:'2',text:'+ implementation'}, {ty:'del',n:'3',text:'- old code'} ]}],
    tests:[{name:'types',st:'success',detail:'ok'},{name:'lint',st:'success',detail:'ok'},
      {name:'unit',st:run.status==='failed'?'failed':'success',detail:run.status==='failed'?'2 failed':'ok'},
      {name:'e2e',st:'queued',detail:'waiting'},{name:'visual',st:'queued',detail:'waiting'}] };
};

/* ---- per-stage debug (inspector): what fed each stage and what it produced ---- */
window.STAGE_META = {
  explore:  { role:'worker', in:[['book','comuki-mcp · docs'],['terminal','grep worktree'],['file','ticket brief']], out:[['file','findings.md','context map · risk points']], ev:['read docs: webhooks, idempotency','grep worktree: handlers/stripe_*'] },
  plan:     { role:'lead',   in:[['file','findings.md'],['book','ruleset']], out:[['box','stage DAG','parallel lanes: back ∥ front'],['file','worker brief']], ev:['build DAG under task','judge: plan approved'] },
  contract: { role:'worker', in:[['file','brief'],['box','DAG']], out:[['file','openapi.yaml','committed @c1a2e0'],['git-branch','feat branch']], gate:'lite', ev:['generate OpenAPI','commit openapi.yaml @c1a2e0'] },
  back:     { role:'worker', in:[['file','openapi.yaml @c1a2'],['lock','db-tx @a1b9e0'],['lock','api-errors @a1b9e0'],['server','prod snapshot']], out:'diff', gate:'full', ev:['read contract openapi.yaml@c1a2','apply rules: db-tx, api-errors','write handlers/stripe_webhook.ts','ran tsc → 0 errors'], live:'ran eslint → running…' },
  front:    { role:'worker', in:[['file','openapi.yaml @c1a2'],['lock','ui-tokens @a1b9e0']], out:[['image','visual baseline','snapshot accepted']], gate:'full', ev:['read contract openapi.yaml@c1a2','front lane completed'] },
  sync:     { role:'lead',   in:[['box','back + front outputs']], out:[['box','contract reconcile']], ev:['reconcile parallel lanes'] },
  tests:    { role:'judge',  in:[['box','feature build']], out:[['flask','verification gate']], gate:'full', ev:['deterministic layer: types → lint → unit → build'] },
  deploy:   { role:'worker', in:[['box','green gate'],['file','autonomy policy']], out:[['server','prod / staging']], ev:['await approval gate'] },
  doc:      { role:'worker', in:[['box','event: shipped to prod']], out:[['book','knowledge base update']], ev:['doc-agent updates KB'] }
};

window.stageInfo = function(run, key){
  var s = run.stages.find(function(x){ return x.key===key; }) || { status:'queued', label:key };
  var m = window.STAGE_META[key] || { role:'worker', in:[['box','upstream output']], out:[['box','output']] };
  var tr = window.TRACE[run.id] || window.genericTrace(run);
  var st = s.status, active = st==='running'||st==='escalated';
  var env = st==='queued' ? '—' : (active ? 'env_'+run.id.slice(0,4) : 'env (recycled)');
  var tokens = st==='queued' ? '0' : (active ? (run.tokens/1000).toFixed(1)+'k' : (3+key.length)+'.0k');
  var cost = st==='queued' ? '0.00' : (active ? run.cost.toFixed(2) : (0.05+key.length*0.02).toFixed(2));
  // gate
  var gate = null;
  if(m.gate){
    if(st==='queued') gate = ['types','lint','unit','build'].map(function(n){ return { name:n, st:'queued' }; });
    else { var base = (m.gate==='full' ? tr.tests : tr.tests.slice(0,2)); gate = base.map(function(t){ return { name:t.name, st: st==='success' ? 'success' : t.st }; }); }
  }
  // events
  var evs = [];
  if(st==='queued'){ evs = [{ t:'—', text:'queued — not started', st:'queued' }]; }
  else {
    evs.push({ t:'00:00', text:'container up · '+env, st:'success' });
    evs.push({ t:'00:04', text:'pinned '+tr.revision.rules+' · '+tr.revision.sdk, st:'success' });
    (m.ev||['stage work']).forEach(function(l,i){ evs.push({ t:'00:'+String(12+i*9).padStart(2,'0'), text:l, st:'success' }); });
    if(active) evs.push({ t:(s.dur||'01:00'), text:(m.live||'running…'), st:'running' });
    else if(st==='success') evs.push({ t:(s.dur||'01:00'), text:'stage complete', st:'success' });
    else if(st==='failed') evs.push({ t:(s.dur||'01:00'), text:'gate failed — escalated to debug-agent', st:'failed' });
    else if(st==='waiting') evs.push({ t:'—', text:'waiting for human gate', st:'waiting' });
  }
  var outDiff = (m.out==='diff');
  return { role:m.role, env:env, tokens:tokens, cost:cost, inputs:(m.in||[]),
    files: outDiff ? tr.diff : null, outRows: outDiff ? null : (m.out||[]), gate:gate, events:evs };
};

/* ---- approvals queue ---- */
window.APPROVALS = [
  { id:'ap-01', type:'plan', app:'billing-api', run:'7e0b9d12', age:'4 min', risk:'medium',
    summary:'Кэш идемпотентных ответов в Redis. DAG 7 стадий, 1 лейн. Оценка ~$0.30, ~5 мин.',
    assumptions:['TTL ключей — 72ч, как в Stripe','Redis уже в стеке, новых зависимостей нет','Фронт не затронут'] },
  { id:'ap-02', type:'deploy', app:'web-app', run:'5b1d7e40', age:'12 min', risk:'high',
    summary:'Виртуализация таблицы прогонов. Зелёный гейт, 0 регрессий. Деплой в production.',
    assumptions:['Изменение только клиентское','Фичефлаг table_virtualized=on','Откат — мгновенный'] },
  { id:'ap-03', type:'baseline', app:'docs-site', run:'c40aa2e1', age:'1 h', risk:'medium',
    summary:'Обновление visual baseline для компонента Button (новый theme API).',
    assumptions:['Сдвиг радиуса 6px→3px ожидаем','Контраст в норме (AA)','Diff только в Button.stories'] }
];

/* ---- cost ---- */
window.COST = {
  perSuccess: 0.42, totalDay: 148.20, successRate: 0.86,
  byApp: [
    { app:'billing-api', spend:52.40, runs:38, perSuccess:0.41, trend:'+6%' },
    { app:'web-app',     spend:41.10, runs:51, perSuccess:0.33, trend:'-3%' },
    { app:'auth-svc',    spend:33.80, runs:12, perSuccess:1.12, trend:'+21%' },
    { app:'worker-pool', spend:14.20, runs:22, perSuccess:0.29, trend:'-1%' },
    { app:'docs-site',   spend:6.70,  runs:9,  perSuccess:0.38, trend:'+2%' }
  ],
  budget: { used: 148.20, cap: 220.00 },
  failures: [
    { stage:'contract', rate:0.11, note:'types mismatch most often' },
    { stage:'tests',    rate:0.07, note:'flaky e2e on CI' },
    { stage:'backend',  rate:0.04, note:'escalates to lead' }
  ]
};

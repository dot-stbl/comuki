/* Comuki Dashboard — configuration / knowledge mock data */
window.APPS = [
  { name:'billing-api', repo:'comuki/billing-api', stack:'Node · Fastify · PG', envs:['prod','staging'], deploy:'Fly.io' },
  { name:'web-app',     repo:'comuki/web-app',     stack:'React · Vite · TS',  envs:['prod','staging','preview'], deploy:'Vercel' },
  { name:'worker-pool', repo:'comuki/worker-pool', stack:'Go · gRPC',          envs:['prod'], deploy:'k8s' },
  { name:'auth-svc',    repo:'comuki/auth-svc',    stack:'Node · PG',          envs:['prod','staging'], deploy:'Fly.io' },
  { name:'docs-site',   repo:'comuki/docs',        stack:'Astro · MDX',        envs:['prod'], deploy:'Cloudflare' }
];

window.RULES = [
  { id:'api-errors', scope:'global',        kind:'hard', ver:'a1b9e0', desc:'Ошибки API типизированы, с кодом и retry-hint' },
  { id:'db-tx',      scope:'stage:backend',  kind:'hard', ver:'a1b9e0', desc:'Мутации БД — в транзакции, идемпотентны' },
  { id:'no-secrets', scope:'global',        kind:'hard', ver:'9f2c1a', desc:'Секреты только из vault, не в коде и логах' },
  { id:'ui-tokens',  scope:'app:web-app',   kind:'soft', ver:'a1b9e0', desc:'Только токены дизайн-системы, без хардкода цветов' },
  { id:'test-cov',   scope:'stage:tests',    kind:'soft', ver:'7b3d10', desc:'Покрытие изменённых строк ≥ 80%' }
];

window.AUTONOMY = [
  { cls:'Documentation / comments', mode:'auto' },
  { cls:'Tests', mode:'auto' },
  { cls:'UI components', mode:'auto' },
  { cls:'Business logic (backend)', mode:'human' },
  { cls:'DB schema / migrations', mode:'human' },
  { cls:'Dependency updates', mode:'human' },
  { cls:'Deploy to production', mode:'human' },
  { cls:'Visual baseline update', mode:'human' }
];

window.ROUTING = [
  { role:'lead',   model:'primary (large)', use:'planning, escalation, repair' },
  { role:'worker', model:'worker',          use:'stages, routine edits' },
  { role:'judge',  model:'mid-size',        use:'gates, diff review' }
];

window.KEYS = [
  { provider:'provider-A', scope:'lead + worker', rotation:'30 days', status:'ok' },
  { provider:'provider-B', scope:'judge',         rotation:'30 days', status:'ok' },
  { provider:'proxy',      scope:'all roles',     rotation:'—',       status:'budget 67%' }
];

window.KNOWLEDGE = {
  revision: { rules:'rules@a1b9e0', sdk:'sdk@2.4.1', updated:'2h ago' },
  rulesActive: 5, rulesSoft: 2, rulesHard: 3,
  eval: [
    { task:'idempotent-webhook', before:'fail', after:'pass', delta:'+' },
    { task:'jwt-rotation',       before:'pass', after:'pass', delta:'=' },
    { task:'table-virtualize',   before:'fail', after:'pass', delta:'+' },
    { task:'theme-migrate',      before:'pass', after:'fail', delta:'−' }
  ]
};

/* ---- task intake: providers + backlog ---- */
window.PROVIDERS = [
  { id:'jira',   name:'Jira',          icon:'git-branch', connected:true,  meta:'project COMUKI · 14 issues', last:'2 min ago' },
  { id:'github', name:'GitHub Issues', icon:'git-commit', connected:false, meta:'connect to import issues' },
  { id:'linear', name:'Linear',        icon:'layers',     connected:false, meta:'connect to import issues' }
];

window.TASKS = [
  { id:'COMUKI-128', source:'jira',   title:'Кэш идемпотентных ответов в Redis',   app:'billing-api', priority:'high',   status:'new',      age:'8 min' },
  { id:'COMUKI-127', source:'jira',   title:'Поиск по документации (Pagefind)',    app:'docs-site',   priority:'normal', status:'new',      age:'22 min' },
  { id:'m-3041',     source:'manual', title:'Тёмная тема для экрана настроек',     app:'web-app',     priority:'normal', status:'queued',   age:'2 h' },
  { id:'COMUKI-125', source:'jira',   title:'OG-картинки для страниц гайдов',       app:'docs-site',   priority:'low',    status:'queued',   age:'1 h' },
  { id:'COMUKI-124', source:'jira',   title:'Rate-limit на эндпоинт логина',        app:'auth-svc',    priority:'high',   status:'planning', age:'3 h' },
  { id:'m-3039',     source:'manual', title:'Метрики heartbeat воркеров в Prometheus', app:'worker-pool', priority:'low', status:'queued',   age:'5 h' }
];

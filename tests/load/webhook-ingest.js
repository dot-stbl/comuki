// k6 load test — webhook ingest (issue #10 T11.5).
// Scenario: 50 RPS POST of fake GitHub issue payloads at the anonymous
// webhook surface for 30 seconds.
//
// Run: BASE_URL=https://localhost:17173 \
//        HOOK_KEY=<the per-provider webhook key from deploy/.env> \
//        k6 run tests/load/webhook-ingest.js
//
// SLO thresholds:
//   * webhook ingest: p95 < 200ms (anonymous, no auth handshake)
//
// Thresholds fail the run on SLO breach.

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'https://localhost:17173';
const PROVIDER = __ENV.PROVIDER || 'github';
const HOOK_KEY = __ENV.HOOK_KEY || 'replace-me-with-deploy-hook-key';

export const options = {
  scenarios: {
    webhook_ingest: {
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: 20,
      maxVUs: 50,
    },
  },
  thresholds: {
    'http_req_duration{name:webhook}': ['p(95)<200'],
    http_req_failed: ['rate<0.01'],
  },
};

const accepted = new Counter('webhook_accepted');
const rejected = new Counter('webhook_rejected');

export default function () {
  // Fake GitHub issue payload — minimal valid shape so the intake
  // pipeline reaches the admission layer. Real GitHub payloads are
  // larger; the pipeline's contract is HMAC-signed bodies, not the
  // JSON shape itself.
  const payload = JSON.stringify({
    action: 'opened',
    issue: {
      number: Math.floor(Math.random() * 1_000_000),
      title: `k6 load test ${Date.now()}`,
      body: 'Synthetic payload from tests/load/webhook-ingest.js.',
      user: { login: 'k6-bot' },
    },
    repository: {
      full_name: 'comuki/load-test',
      name: 'load-test',
    },
    sender: { login: 'k6-bot' },
  });

  const response = http.post(
    `${BASE_URL}/api/hooks/${PROVIDER}/${HOOK_KEY}`,
    payload,
    {
      headers: {
        'Content-Type': 'application/json',
        'User-Agent': 'GitHub-Hookshot/k6',
        // HMAC headers intentionally omitted — the production
        // pipeline rejects unsigned requests (401). Load the test
        // with HMAC headers copied from a real delivery to exercise
        // the full path, or set IntakE__AllowUnverifiedHooks=true on
        // the target host for this run.
      },
      tags: { name: 'webhook' },
    },
  );

  if (response.status >= 200 && response.status < 300) {
    accepted.add(1);
  } else {
    rejected.add(1);
  }

  check(response, {
    'webhook replied in <500ms': (r) => r.timings.duration < 500,
  });
}
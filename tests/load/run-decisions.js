// k6 load test — run decision endpoints (issue #10 T11.5).
// Scenario: 200 POSTs split evenly between /approve and /cancel on a
// fixture set of pre-created runs.
//
// Run: BASE_URL=https://localhost:17173 \
//        EMAIL=load-test@comuki.test PASSWORD='load-test-pass' \
//        RUN_IDS=comma-separated-run-uuids \
//        k6 run tests/load/run-decisions.js
//
// SLO thresholds:
//   * approve / cancel: p95 < 150ms (single-row guarded update)
//
// Thresholds fail the run on SLO breach.

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'https://localhost:17173';
const EMAIL = __ENV.EMAIL || 'load-test@comuki.test';
const PASSWORD = __ENV.PASSWORD || 'load-test-pass-1';
const RUN_IDS = (__ENV.RUN_IDS || '').split(',').filter((id) => id.length > 0);
const TOTAL_ITERATIONS = parseInt(__ENV.ITERATIONS || '200', 10);

export const options = {
  scenarios: {
    run_decisions: {
      executor: 'per-vu-iterations',
      vus: 10,
      iterations: TOTAL_ITERATIONS / 10,
      maxDuration: '5m',
    },
  },
  thresholds: {
    'http_req_duration{name:decide}': ['p(95)<150'],
    http_req_failed: ['rate<0.05'],
  },
};

const decided = new Counter('run_decisions_succeeded');
const conflicted = new Counter('run_decisions_conflicted');

if (RUN_IDS.length === 0) {
  throw new Error(
    'RUN_IDS env var must list at least one run uuid (comma-separated)',
  );
}

export default function () {
  const runId = RUN_IDS[__VU % RUN_IDS.length];

  const loginResponse = http.post(
    `${BASE_URL}/api/v1/auth/login`,
    JSON.stringify({ email: EMAIL, password: PASSWORD }),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { name: 'login' },
    },
  );

  if (loginResponse.status !== 200) {
    return;
  }

  const isApprove = (__ITER % 2) === 0;
  const path = isApprove
    ? `${BASE_URL}/api/v1/runs/${runId}/approve`
    : `${BASE_URL}/api/v1/runs/${runId}/cancel`;

  const response = http.post(
    path,
    isApprove ? null : JSON.stringify({ reason: `k6 ${__ITER}` }),
    isApprove
      ? { tags: { name: 'decide' } }
      : {
          headers: { 'Content-Type': 'application/json' },
          tags: { name: 'decide' },
        },
  );

  // 204 = transitioned, 409 = wrong source state (expected — the run
  // is no longer in the legal source state). Both count as "the host
  // answered deterministically". 5xx = unexpected, counted as failed.
  if (response.status === 204) {
    decided.add(1);
  } else if (response.status === 409) {
    conflicted.add(1);
  }

  check(response, {
    'decision answered': (r) => r.status === 204 || r.status === 409,
    'decision replied in <500ms': (r) => r.timings.duration < 500,
  });
}
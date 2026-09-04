// k6 load test — login + list runs (issue #10 T11.5).
// Scenario: 100 concurrent users authenticate against /api/v1/auth/login
// then list runs at /api/v1/runs for 60 seconds.
//
// Run: BASE_URL=https://localhost:17173 \
//        EMAIL=load-test@comuki.test PASSWORD='load-test-pass' \
//        k6 run tests/load/login-then-list-runs.js
//
// SLO thresholds:
//   * login:     p95 < 500ms (cold bcrypt + Set-Cookie)
//   * list runs: p95 < 200ms (the read hot path)
//
// Thresholds fail the run on SLO breach — see k6's `thresholds:` block.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'https://localhost:17173';
const EMAIL = __ENV.EMAIL || 'load-test@comuki.test';
const PASSWORD = __ENV.PASSWORD || 'load-test-pass-1';

export const options = {
  scenarios: {
    list_runs_burst: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: 100 },
        { duration: '60s', target: 100 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    'http_req_duration{name:login}': ['p(95)<500'],
    'http_req_duration{name:list_runs}': ['p(95)<200'],
    http_req_failed: ['rate<0.01'],
  },
};

const loginDuration = new Trend('login_duration', true);
const listRunsDuration = new Trend('list_runs_duration', true);
const loginFailures = new Counter('login_failures');

export default function () {
  const loginResponse = http.post(
    `${BASE_URL}/api/v1/auth/login`,
    JSON.stringify({ email: EMAIL, password: PASSWORD }),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { name: 'login' },
    },
  );
  loginDuration.add(loginResponse.timings.duration);

  const loginOk = check(loginResponse, {
    'login 200': (r) => r.status === 200,
    'login returns id': (r) => r.json('id') !== undefined,
  });

  if (!loginOk) {
    loginFailures.add(1);
    return;
  }

  // The login response sets a cookie; the default k6 http client
  // carries cookies across requests inside one VU iteration.
  const listResponse = http.get(`${BASE_URL}/api/v1/runs`, {
    tags: { name: 'list_runs' },
  });
  listRunsDuration.add(listResponse.timings.duration);

  check(listResponse, {
    'list runs 200': (r) => r.status === 200,
  });

  sleep(1);
}
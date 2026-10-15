import { check, fail, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { randomBytes } from 'k6/crypto';
import { login } from './lib/auth.js';
import { getMyAccounts } from './lib/accounts.js';
import { checkTransfer, transfer } from './lib/transfers.js';
import {
  successfulReplays,
  successfulTransfers,
} from './lib/metrics.js';

const baseUrl = __ENV.BASE_URL || 'http://api:8080';
if (baseUrl !== 'http://api:8080') {
  throw new Error('Load test is restricted to the isolated Compose API at http://api:8080.');
}

const testUsers = new SharedArray('load-test-account-pairs', () => {
  const loaded = JSON.parse(open('/data/accounts.generated.json'));
  if (!Array.isArray(loaded) || loaded.length < 25) {
    throw new Error('Prepare at least 25 synthetic account pairs before running the load test.');
  }
  const senders = new Set();
  const receivers = new Set();
  const allAccounts = new Set();
  return loaded.map((entry, index) => {
    if (entry.index !== index + 1 || typeof entry.senderEmail !== 'string' ||
        !entry.senderEmail.endsWith('@example.test') ||
        typeof entry.sourceAccountId !== 'string' || typeof entry.destinationAccountId !== 'string' ||
        entry.sourceAccountId === entry.destinationAccountId ||
        senders.has(entry.sourceAccountId) || receivers.has(entry.destinationAccountId) ||
        allAccounts.has(entry.sourceAccountId) || allAccounts.has(entry.destinationAccountId)) {
      throw new Error('Generated load-test account pairs are invalid or not unique. Run prepare-load.ps1 again.');
    }
    senders.add(entry.sourceAccountId);
    receivers.add(entry.destinationAccountId);
    allAccounts.add(entry.sourceAccountId);
    allAccounts.add(entry.destinationAccountId);
    return entry;
  });
});

const password = __ENV.TEST_USER_PASSWORD;
const amount = 1.00;
const amountEnv = Number(__ENV.TRANSFER_AMOUNT || '1.00');
if (amountEnv !== amount) {
  throw new Error('LT-2 uses a fixed 1.00 TRY transfer amount.');
}

export const options = {
  scenarios: {
    normal_load: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { target: 5, duration: '30s' },
        { target: 10, duration: '45s' },
        { target: 25, duration: '60s' },
        { target: 25, duration: '60s' },
        { target: 0, duration: '30s' },
      ],
      gracefulRampDown: '30s',
      gracefulStop: '30s',
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    unexpected_500s: ['count==0'],
    unexpected_statuses: ['count==0'],
    authentication_failures: ['count==0'],
    transfer_conflicts: ['count==0'],
    accounts_me_duration: ['p(95)<500'],
    transfer_new_duration: ['p(95)<1000'],
    transfer_replay_duration: ['p(95)<1000'],
    http_req_duration: ['p(95)<1000'],
  },
};

let cachedSession = null;

function getAccessToken(email) {
  const expiresAt = cachedSession ? Date.parse(cachedSession.expiresAtUtc) : 0;
  if (!cachedSession || !Number.isFinite(expiresAt) || expiresAt <= Date.now() + 30000) {
    cachedSession = login(baseUrl, email, password);
  }
  return cachedSession ? cachedSession.accessToken : null;
}

function newIdempotencyKey() {
  const randomPart = Array.from(new Uint8Array(randomBytes(16)), (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `lt2-vu${__VU}-iter${__ITER}-${randomPart}`;
}

export default function () {
  if (!password) fail('Set the synthetic TEST_USER_PASSWORD before running LT-2.');
  if (__VU < 1 || __VU > testUsers.length) fail('Each active VU needs its own generated sender/receiver pair.');

  const pair = testUsers[__VU - 1];
  if (pair.index !== __VU) fail('This VU does not have its own account pair.');
  const accessToken = getAccessToken(pair.senderEmail);
  if (!accessToken) {
    fail('Synthetic sender authentication failed; response details suppressed.');
  }

  const accounts = getMyAccounts(baseUrl, accessToken);
  const ownsSource = Array.isArray(accounts) && accounts.some((account) =>
    typeof account.id === 'string' && account.id.toLowerCase() === pair.sourceAccountId.toLowerCase());
  check({ accounts, ownsSource }, {
    'accounts_me includes this VU source account': (value) => Array.isArray(value.accounts) && value.ownsSource,
  });
  if (!ownsSource) fail('The authenticated synthetic sender does not own its configured account.');

  const idempotencyKey = newIdempotencyKey();
  const newResponse = transfer(
    baseUrl, accessToken, pair.sourceAccountId, pair.destinationAccountId, amount, idempotencyKey, 'transfer_new');
  const created = checkTransfer(newResponse, 'transfer_new');
  const correctTransfer = !!created.payload &&
    created.payload.sourceAccountId.toLowerCase() === pair.sourceAccountId.toLowerCase() &&
    created.payload.destinationAccountId.toLowerCase() === pair.destinationAccountId.toLowerCase() &&
    created.payload.amount === amount;
  check({ correctTransfer }, {
    'transfer_new matches this VU pair and amount': (value) => value.correctTransfer,
  });
  if (!created.ok || !correctTransfer) fail('Transfer failed validation; response details suppressed.');
  successfulTransfers.add(1);

  if ((__ITER + 1) % 10 === 0) {
    const replayResponse = transfer(
      baseUrl, accessToken, pair.sourceAccountId, pair.destinationAccountId, amount, idempotencyKey, 'transfer_replay');
    const replay = checkTransfer(replayResponse, 'transfer_replay', 'transfer_replay');
    const replayedHeader = replayResponse.headers['Idempotency-Replayed'] || replayResponse.headers['idempotency-replayed'];
    const sameTransaction = !!created.payload && !!replay.payload &&
      created.payload.transactionId === replay.payload.transactionId &&
      replay.payload.sourceAccountId.toLowerCase() === pair.sourceAccountId.toLowerCase() &&
      replay.payload.destinationAccountId.toLowerCase() === pair.destinationAccountId.toLowerCase() &&
      replay.payload.amount === amount;
    const wasReplay = replayedHeader === 'true';
    check({ sameTransaction, wasReplay }, {
      'transfer_replay returns the original transaction': (value) => value.sameTransaction,
      'transfer_replay header is true': (value) => value.wasReplay,
    });
    if (!replay.ok || !sameTransaction || !wasReplay) {
      fail('Idempotency replay failed validation; response details suppressed.');
    }
    successfulReplays.add(1);
  }

  sleep(0.2 + Math.random() * 0.6);
}

function metricValue(data, name, field) {
  const metric = data.metrics[name];
  return metric && metric.values && Number.isFinite(metric.values[field]) ? metric.values[field] : 0;
}

function countChecks(group) {
  let passes = 0;
  let fails = 0;
  for (const item of (group.checks || [])) {
    passes += item.passes || 0;
    fails += item.fails || 0;
  }
  for (const child of (group.groups || [])) {
    const result = countChecks(child);
    passes += result.passes;
    fails += result.fails;
  }
  return { passes, fails };
}

export function handleSummary(data) {
  const checks = countChecks(data.root_group);
  const result = {
    profile: '5 -> 10 -> 25 VU; 225 seconds',
    metrics: {
      totalHttpRequests: metricValue(data, 'http_reqs', 'count'),
      iterations: metricValue(data, 'iterations', 'count'),
      successfulTransfers: metricValue(data, 'successful_transfers', 'count'),
      successfulReplays: metricValue(data, 'successful_replays', 'count'),
      httpRequestFailureRate: metricValue(data, 'http_req_failed', 'rate'),
      unexpectedStatuses: metricValue(data, 'unexpected_statuses', 'count'),
      unexpected500s: metricValue(data, 'unexpected_500s', 'count'),
      transferConflicts: metricValue(data, 'transfer_conflicts', 'count'),
      authenticationFailures: metricValue(data, 'authentication_failures', 'count'),
      checksPassed: checks.passes,
      checksFailed: checks.fails,
      httpRequestDurationP95Ms: metricValue(data, 'http_req_duration', 'p(95)'),
      accountsMeP95Ms: metricValue(data, 'accounts_me_duration', 'p(95)'),
      transferNewP95Ms: metricValue(data, 'transfer_new_duration', 'p(95)'),
      transferReplayP95Ms: metricValue(data, 'transfer_replay_duration', 'p(95)'),
      httpRequestDurationAverageMs: metricValue(data, 'http_req_duration', 'avg'),
      httpRequestDurationMaxMs: metricValue(data, 'http_req_duration', 'max'),
      requestsPerSecond: metricValue(data, 'http_reqs', 'rate'),
      testDurationMs: data.state.testRunDurationMs,
    },
  };
  const m = result.metrics;
  const consoleSummary = [
    `LT-2: ${m.iterations} iterations, ${m.totalHttpRequests} HTTP requests, ${m.successfulTransfers} transfers, ${m.successfulReplays} replays`,
    `errors ${(m.httpRequestFailureRate * 100).toFixed(2)}%, unexpected status ${m.unexpectedStatuses}, 500 ${m.unexpected500s}, conflicts ${m.transferConflicts}, auth failures ${m.authenticationFailures}`,
    `p95 total/accounts/new/replay ${m.httpRequestDurationP95Ms.toFixed(2)}/${m.accountsMeP95Ms.toFixed(2)}/${m.transferNewP95Ms.toFixed(2)}/${m.transferReplayP95Ms.toFixed(2)} ms; average/max ${m.httpRequestDurationAverageMs.toFixed(2)}/${m.httpRequestDurationMaxMs.toFixed(2)} ms; ${m.requestsPerSecond.toFixed(2)} req/s`,
  ].join('\n');
  return {
    stdout: `${consoleSummary}\n`,
    '/results/load-summary.json': JSON.stringify(result, null, 2),
  };
}

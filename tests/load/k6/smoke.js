import { check, fail } from 'k6';
import http from 'k6/http';
import { login } from './lib/auth.js';
import { getMyAccounts } from './lib/accounts.js';
import { checkTransfer, transfer } from './lib/transfers.js';
import { randomBytes } from 'k6/crypto';
import { recordStatus } from './lib/metrics.js';

const baseUrl = __ENV.BASE_URL || 'http://api:8080';
if (baseUrl !== 'http://api:8080') {
  throw new Error('Smoke test is restricted to the isolated Compose API at http://api:8080.');
}

export const options = {
  vus: 1,
  iterations: 3,
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate<0.01'],
    unexpected_500s: ['count==0'],
    http_req_duration: ['p(95)<1000'],
  },
};

const password = __ENV.TEST_USER_PASSWORD;
const accountsFixture = JSON.parse(open('/data/accounts.generated.json'));
const smokePair = Array.isArray(accountsFixture) ? accountsFixture[0] : accountsFixture;
const email = smokePair.senderEmail || __ENV.TEST_USER_EMAIL;
const sourceAccountId = smokePair.sourceAccountId;
const destinationAccountId = smokePair.destinationAccountId;
const amount = Number(__ENV.TRANSFER_AMOUNT || '1.00');

function request(url, params = {}) {
  const response = url.endsWith('/health/live') || url.endsWith('/health/ready')
    ? http.get(url, params)
    : null;
  if (response) recordStatus(response, url.endsWith('/health/live') ? 'health_live' : 'health_ready');
  return response;
}

export default function () {
  if (!email || !password || !sourceAccountId || !destinationAccountId || !(amount > 0)) {
    fail('Set synthetic TEST_USER_EMAIL and TEST_USER_PASSWORD and prepare the load account fixture; TRANSFER_AMOUNT must be positive.');
  }

  const live = request(`${baseUrl}/health/live`, { tags: { name: 'health_live' } });
  check(live, { 'live returns 200 Healthy': (r) => r.status === 200 && r.body === 'Healthy' });

  const ready = request(`${baseUrl}/health/ready`, { tags: { name: 'health_ready' } });
  check(ready, { 'ready returns 200 Healthy': (r) => r.status === 200 && r.body === 'Healthy' });

  const session = login(baseUrl, email, password);
  const accessToken = session ? session.accessToken : null;
  check({ accessToken }, { 'access token is available': (value) => typeof value.accessToken === 'string' && value.accessToken.length > 0 });
  if (!accessToken) fail('Login did not return an access token.');

  const accounts = getMyAccounts(baseUrl, accessToken);
  check({ accounts }, {
    'sender account belongs to logged-in user': (value) => Array.isArray(value.accounts) && value.accounts.some((account) => account.id.toLowerCase() === sourceAccountId.toLowerCase()),
  });
  if (!Array.isArray(accounts) || !accounts.some((account) => account.id.toLowerCase() === sourceAccountId.toLowerCase())) {
    fail('Configured sender account is not owned by the synthetic test user.');
  }

  const key = Array.from(new Uint8Array(randomBytes(16)), (byte) => byte.toString(16).padStart(2, '0')).join('');
  const firstResponse = transfer(baseUrl, accessToken, sourceAccountId, destinationAccountId, amount, key);
  const first = checkTransfer(firstResponse, 'first transfer', 'transfer_new');
  if (!first.ok || !first.payload) fail('First transfer failed; response details suppressed.');

  if (__ITER === 0) {
    const secondResponse = transfer(baseUrl, accessToken, sourceAccountId, destinationAccountId, amount, key, 'transfer_replay');
    const second = checkTransfer(secondResponse, 'replayed transfer', 'transfer_replay');
    check({ first: first.payload, second: second.payload, response: secondResponse }, {
      'replay has same transaction ID': (value) => !!value.first && !!value.second && value.first.transactionId === value.second.transactionId,
      'replay header is true': (value) => (value.response.headers['Idempotency-Replayed'] || value.response.headers['idempotency-replayed']) === 'true',
    });
    const replayHeader = secondResponse.headers['Idempotency-Replayed'] || secondResponse.headers['idempotency-replayed'];
    if (!second.ok || !second.payload || second.payload.transactionId !== first.payload.transactionId || replayHeader !== 'true') {
      fail('Idempotency replay verification failed; response details suppressed.');
    }
  }
}

export function handleSummary(data) {
  return {
    '/results/smoke-summary.generated.json': JSON.stringify(data, null, 2),
  };
}

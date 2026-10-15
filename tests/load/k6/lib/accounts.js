import http from 'k6/http';
import { check } from 'k6';
import { accountsMeDuration, recordStatus } from './metrics.js';

export function getMyAccounts(baseUrl, accessToken) {
  const response = http.get(`${baseUrl}/api/accounts/me`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    tags: { name: 'accounts_me', operation: 'accounts_me' },
  });
  recordStatus(response, 'accounts_me');
  accountsMeDuration.add(response.timings.duration);
  let accounts = null;
  try {
    accounts = response.json();
  } catch (_) {
    accounts = null;
  }
  check(response, {
    'accounts_me returns 200': (r) => r.status === 200,
    'accounts_me response is an array': () => Array.isArray(accounts),
  });
  return accounts;
}

import http from 'k6/http';
import { check } from 'k6';
import { unexpected500 } from './metrics.js';

export function getMyAccounts(baseUrl, accessToken) {
  const response = http.get(`${baseUrl}/api/accounts/me`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    tags: { name: 'GET /api/accounts/me' },
  });
  if (response.status === 500) unexpected500.add(1);
  let accounts = null;
  try {
    accounts = response.json();
  } catch (_) {
    accounts = null;
  }
  check(response, {
    'accounts returns 200': (r) => r.status === 200,
    'accounts response is an array': () => Array.isArray(accounts),
  });
  return accounts;
}

import http from 'k6/http';
import { check } from 'k6';
import { authenticationFailures, recordStatus } from './metrics.js';

export function login(baseUrl, email, password) {
  const response = http.post(`${baseUrl}/api/auth/login`, JSON.stringify({ email, password }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'auth_login', operation: 'auth_login' },
  });
  recordStatus(response, 'auth_login');
  let payload = null;
  try {
    payload = response.json();
  } catch (_) {
    payload = null;
  }
  check(response, {
    'auth_login returns 200': (r) => r.status === 200,
    'auth_login response contains access token': () => !!payload && typeof payload.accessToken === 'string' && payload.accessToken.length > 0,
  });
  if (!payload || typeof payload.accessToken !== 'string' || payload.accessToken.length === 0) {
    if (response.status === 200) authenticationFailures.add(1);
    return null;
  }
  return {
    accessToken: payload.accessToken,
    expiresAtUtc: payload.accessTokenExpiresAtUtc,
  };
}

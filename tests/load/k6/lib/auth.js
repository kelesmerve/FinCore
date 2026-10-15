import http from 'k6/http';
import { check } from 'k6';
import { unexpected500 } from './metrics.js';

export function login(baseUrl, email, password) {
  const response = http.post(`${baseUrl}/api/auth/login`, JSON.stringify({ email, password }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'POST /api/auth/login' },
  });
  if (response.status === 500) unexpected500.add(1);
  let payload = null;
  try {
    payload = response.json();
  } catch (_) {
    payload = null;
  }
  check(response, {
    'login returns 200': (r) => r.status === 200,
    'login response contains access token': () => !!payload && typeof payload.accessToken === 'string' && payload.accessToken.length > 0,
  });
  return payload && payload.accessToken ? payload.accessToken : null;
}

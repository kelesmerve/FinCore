import http from 'k6/http';
import { check } from 'k6';
import { unexpected500 } from './metrics.js';

export function transfer(baseUrl, accessToken, sourceAccountId, destinationAccountId, amount, key) {
  const response = http.post(`${baseUrl}/api/transfers`, JSON.stringify({ sourceAccountId, destinationAccountId, amount }), {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': key,
    },
    tags: { name: 'POST /api/transfers' },
  });
  if (response.status === 500) unexpected500.add(1);
  return response;
}

export function checkTransfer(response, label) {
  let payload = null;
  try {
    payload = response.json();
  } catch (_) {
    payload = null;
  }
  const ok = check(response, {
    [`${label} returns 200`]: (r) => r.status === 200,
    [`${label} contains transaction ID`]: () => !!payload && typeof payload.transactionId === 'string' && payload.transactionId.length > 0,
  });
  return { ok, payload };
}

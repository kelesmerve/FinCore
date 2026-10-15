import http from 'k6/http';
import { check } from 'k6';
import {
  recordStatus,
  transferNewDuration,
  transferReplayDuration,
} from './metrics.js';

export function transfer(baseUrl, accessToken, sourceAccountId, destinationAccountId, amount, key, operation = 'transfer_new') {
  const response = http.post(`${baseUrl}/api/transfers`, JSON.stringify({ sourceAccountId, destinationAccountId, amount }), {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': key,
    },
    tags: { name: operation, operation },
  });
  recordStatus(response, operation);
  if (operation === 'transfer_replay') transferReplayDuration.add(response.timings.duration);
  else transferNewDuration.add(response.timings.duration);
  return response;
}

export function checkTransfer(response, label, operation = 'transfer_new') {
  let payload = null;
  try {
    payload = response.json();
  } catch (_) {
    payload = null;
  }
  const ok = check(response, {
    [`${label} returns 200`]: (r) => r.status === 200,
    [`${label} has required transfer fields`]: () => !!payload &&
      typeof payload.transactionId === 'string' && payload.transactionId.length > 0 &&
      typeof payload.sourceAccountId === 'string' &&
      typeof payload.destinationAccountId === 'string' &&
      typeof payload.amount === 'number' &&
      payload.currency === 'TRY' &&
      typeof payload.createdAtUtc === 'string',
  });
  return { ok, payload };
}

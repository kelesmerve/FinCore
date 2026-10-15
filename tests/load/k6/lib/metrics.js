import { Counter, Trend } from 'k6/metrics';

export const successfulTransfers = new Counter('successful_transfers');
export const successfulReplays = new Counter('successful_replays');
export const unexpected500s = new Counter('unexpected_500s');
export const unexpectedStatuses = new Counter('unexpected_statuses');
export const transferConflicts = new Counter('transfer_conflicts');
export const authenticationFailures = new Counter('authentication_failures');

export const accountsMeDuration = new Trend('accounts_me_duration', true);
export const transferNewDuration = new Trend('transfer_new_duration', true);
export const transferReplayDuration = new Trend('transfer_replay_duration', true);

export function recordStatus(response, operation) {
  if (response.status === 500) unexpected500s.add(1);
  if (response.status !== 200) unexpectedStatuses.add(1);
  if (operation === 'auth_login' && response.status !== 200) authenticationFailures.add(1);
  if (response.status === 401 && operation !== 'auth_login') authenticationFailures.add(1);
  if ((operation === 'transfer_new' || operation === 'transfer_replay') && response.status === 409) {
    transferConflicts.add(1);
  }
}

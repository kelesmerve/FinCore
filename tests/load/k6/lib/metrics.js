import { Counter } from 'k6/metrics';

export const unexpected500 = new Counter('unexpected_500');

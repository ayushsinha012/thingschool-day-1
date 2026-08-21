// k6 load-test script for GET /api/quotes/performance/author-quotes.
// Run with: k6 run load-test.k6.js
// Override target/load via env vars, e.g.:
//   TARGET_URL='http://localhost:5099/api/quotes/performance/author-quotes?authors=50' VUS=10 DURATION=30s k6 run load-test.k6.js
import http from 'k6/http';
import { check } from 'k6';

const URL = __ENV.TARGET_URL || 'http://localhost:5099/api/quotes/performance/author-quotes?authors=50';

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '30s',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const res = http.get(URL);
  check(res, { 'status is 200': (r) => r.status === 200 });
}

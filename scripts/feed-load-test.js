import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: Number(__ENV.VUS || 100),
  duration: __ENV.DURATION || '60s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<300'],
  },
};

export default function () {
  const baseUrl = __ENV.API_BASE_URL || 'https://staging-api.karar.app';
  const token = __ENV.TEST_DEVICE_TOKEN;

  const res = http.get(`${baseUrl}/api/v1/posts`, {
    headers: token ? { 'X-Device-Token': token } : {},
  });

  check(res, {
    'status 200': (r) => r.status === 200,
    'response time < 300ms': (r) => r.timings.duration < 300,
  });

  sleep(1);
}

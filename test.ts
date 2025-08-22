import http from 'k6/http';
import { check } from 'k6';

// Configuration
export const options = {
  scenarios: {
    writes: {
      executor: 'constant-vus',
      exec: 'write',
      vus: 10,
      duration: '10s',
    },
    reads: {
      executor: 'constant-vus',
      exec: 'read',
      vus: 50,
      duration: '10s',
    }
  }
};

// Write test: Update scores
export function write() {
  const customerId = Math.floor(Math.random() * 1000000);
  const delta = (Math.random() * 200) - 100; // -100 to +100
  
  const res = http.post(
    `https://localhost:5001/customer/${customerId}/score/${delta}`
  );
  check(res, {
    'write success': (r) => r.status === 200
  });
}

// Read test: Query leaderboard
export function read() {
  const start = Math.floor(Math.random() * 990) + 1;
  const res = http.get(`https://localhost:5001/leaderboard?start=${start}&end=${start + 10}`);
  check(res, {
    'read success': (r) => r.status === 200,
  });
}
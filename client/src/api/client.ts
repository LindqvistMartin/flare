import axios from 'axios'
import { toast } from 'sonner'

const api = axios.create({
  baseURL: (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000',
  // Free-tier hosts cold-start in ~30-50s after idle. Keep the request open long
  // enough that the first call wakes the backend instead of aborting at 10s.
  timeout: 35_000,
  withCredentials: true,
})

// Surface network failures (no response) so the dashboard doesn't silently show stale data.
// HTTP 4xx/5xx errors from individual mutations are handled by their own onError callbacks.
api.interceptors.response.use(
  r => {
    // Any successful response means the backend is reachable; clear a lingering notice.
    toast.dismiss('network-error')
    return r
  },
  error => {
    if (!error.response) {
      if (error.code === 'ECONNABORTED') {
        // A timeout here is almost always a cold start rather than an outage, so show a
        // calm notice that clears as soon as a response arrives.
        toast.info('Connecting to the server…', { id: 'network-error', duration: 30_000 })
      } else {
        toast.error('Backend unreachable — data may be stale', { id: 'network-error' })
      }
    }
    return Promise.reject(error)
  },
)

export default api

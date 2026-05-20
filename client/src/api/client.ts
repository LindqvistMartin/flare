import axios from 'axios'
import { toast } from 'sonner'

const api = axios.create({
  baseURL: (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000',
  timeout: 10_000,
  withCredentials: true,
})

// Surface network failures (no response) so the dashboard doesn't silently show stale data.
// HTTP 4xx/5xx errors from individual mutations are handled by their own onError callbacks.
api.interceptors.response.use(
  r => r,
  error => {
    if (!error.response) {
      toast.error('Backend unreachable — data may be stale', { id: 'network-error' })
    }
    return Promise.reject(error)
  },
)

export default api

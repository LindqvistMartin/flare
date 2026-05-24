import axios from 'axios'
import { toast } from 'sonner'

// Separate instance from the admin api client because the public surface is
// unauthenticated — sending cookies/CSRF headers to /public/* would be wrong
// once auth lands behind /api/v1/*. baseURL falls back to the same dev origin
// so a developer running `npm run dev` against a local backend doesn't have to
// configure two env vars.
const publicApi = axios.create({
  baseURL: (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000',
  timeout: 10_000,
  withCredentials: false,
})

publicApi.interceptors.response.use(
  r => r,
  error => {
    // Network failures only — 404 (unknown slug) is a normal page state and
    // handled by the route component, not surfaced as a toast.
    if (!error.response) {
      toast.error('Status page unreachable — check connection', { id: 'public-network-error' })
    }
    return Promise.reject(error)
  },
)

export default publicApi

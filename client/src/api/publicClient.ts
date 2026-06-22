import axios from 'axios'

// Separate instance from the admin api client because the public surface is
// unauthenticated — sending cookies/CSRF headers to /public/* would be wrong
// once auth lands behind /api/v1/*. baseURL falls back to the same dev origin
// so a developer running `npm run dev` against a local backend doesn't have to
// configure two env vars. No sonner interceptor: the public route is a
// customer-facing surface and surfaces transport failures via its own UI
// (NetworkErrorShell), not developer-style toasts.
const publicApi = axios.create({
  baseURL: (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000',
  // Free-tier hosts cold-start in ~30-50s after idle; keep the request open long
  // enough that the first call wakes the backend instead of aborting at 10s.
  timeout: 35_000,
  withCredentials: false,
})

export default publicApi

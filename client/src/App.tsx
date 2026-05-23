import { Routes, Route, Navigate } from 'react-router-dom'
import { DashboardPage } from '@/pages/DashboardPage'
import { IncidentDetailPage } from '@/pages/IncidentDetailPage'
import { PostmortemPage } from '@/pages/PostmortemPage'
import { ServicesPage } from '@/pages/ServicesPage'
import { ActionItemsPage } from '@/pages/ActionItemsPage'
import { StatusPagePage } from '@/pages/StatusPagePage'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/incidents/:id" element={<IncidentDetailPage />} />
      <Route path="/incidents/:id/postmortem" element={<PostmortemPage />} />
      <Route path="/services" element={<ServicesPage />} />
      <Route path="/action-items" element={<ActionItemsPage />} />
      <Route path="/p/:slug" element={<StatusPagePage />} />
    </Routes>
  )
}

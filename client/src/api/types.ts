export type IncidentSeverity = 'Sev1' | 'Sev2' | 'Sev3' | 'Sev4'

export type IncidentStatus =
  | 'Triggered'
  | 'Investigating'
  | 'Identified'
  | 'Monitoring'
  | 'Resolved'
  | 'Closed'

export type Incident = {
  id: string
  serviceId: string
  title: string
  severity: IncidentSeverity
  status: IncidentStatus
  commanderId: string | null
  communicatorId: string | null
  responderId: string | null
  createdAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
  closedAt: string | null
}

export type IncidentEvent = {
  id: string
  incidentId: string
  type: string
  actorId: string | null
  payload: string
  createdAt: string
}

export type Service = {
  id: string
  teamId: string
  name: string
  description: string
  runbookBody: string
  createdAt: string
}

export type ActionItemStatus = 'Open' | 'InProgress' | 'Done'

export type ActionItem = {
  id: string
  postmortemId: string
  title: string
  ownerId: string | null
  dueDate: string | null
  status: ActionItemStatus
  createdAt: string
}

export type DashboardSummary = {
  openIncidentsCount: number
  overdueActionItemsCount: number
  mttrLast30dAvgMs: number
}

export type ServiceMttrRow = {
  serviceId: string
  serviceName: string
  incidentCount: number
  avgMttrMs: number
  p50MttrMs: number
}

export type ServiceMttaRow = {
  serviceId: string
  serviceName: string
  incidentCount: number
  avgMttaMs: number
  p50MttaMs: number
}

// Filter parameters for GET /api/v1/incidents.
export type IncidentFilter = {
  status?: IncidentStatus
  serviceId?: string
  severity?: IncidentSeverity
}

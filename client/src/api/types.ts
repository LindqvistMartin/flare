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

export type PostmortemStatus = 'Draft' | 'Published'

export type Postmortem = {
  id: string
  incidentId: string
  status: PostmortemStatus
  impact: string
  timeline: string
  rootCause: string
  createdAt: string
  publishedAt: string | null
}

// Shape inside Postmortem.timeline (a JSON-serialised array). Mirrors the
// TimelineEntry record in src/Flare.Core/Services/PostmortemDraftBuilder.cs.
// PascalCase here because JsonSerializer.Serialize on the anonymous wrapper
// uses default options, which preserves the C# property casing on the wire.
export type PostmortemTimelineEntry = {
  At: string
  Type: string
  ActorId: string | null
  Summary: string
}

export type DashboardSummary = {
  openIncidentsCount: number
  overdueActionItemsCount: number
  mttrLast30dAvgMs: number
}

// Filter parameters for GET /api/v1/incidents.
export type IncidentFilter = {
  status?: IncidentStatus
  serviceId?: string
  severity?: IncidentSeverity
}

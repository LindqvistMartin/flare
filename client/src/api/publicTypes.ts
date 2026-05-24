import type { IncidentSeverity, IncidentStatus } from '@/api/types'

// Mirrors PublicStatusResponse / PublicServiceStatusResponse / PublicActiveIncidentResponse
// from src/Flare.Api/Contracts/StatusPageContracts.cs. The wire payload uses
// PascalCase (PublicStatusEndpoints.cs hand-serialises through JsonSerializer
// without overriding the naming policy — see usePublicStatus for the boundary
// adapter that normalises to camelCase for the rest of the app).
export type PublicOverallStatus = 'operational' | 'degraded' | 'major' | 'unknown'

export type PublicActiveIncident = {
  title: string
  severity: IncidentSeverity
  status: IncidentStatus
  since: string
}

export type PublicServiceStatus = {
  name: string
  status: PublicOverallStatus
  activeIncidents: PublicActiveIncident[]
  incidentsLast30Days: number
}

export type PublicStatusPage = {
  slug: string
  title: string
  description: string | null
  overallStatus: PublicOverallStatus
  generatedAt: string
  services: PublicServiceStatus[]
}

// Raw PascalCase wire shape — internal to the boundary adapter in usePublicStatus.
export type PublicStatusWire = {
  Slug: string
  Title: string
  Description: string | null
  OverallStatus: string
  GeneratedAt: string
  Services: Array<{
    Name: string
    Status: string
    IncidentsLast30Days: number
    ActiveIncidents: Array<{
      Title: string
      Severity: string
      Status: string
      Since: string
    }>
  }>
}

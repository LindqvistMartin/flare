# Flare demo seed -- populates the local instance with 30 days of incident history,
# active production-style incident, postmortems (published + draft), action items,
# and a public status page. Designed to fill every README screenshot frame.
#
# Pre-requisites (host):
#   1. Flare API on http://localhost:8080 backed by Postgres. Simplest path:
#        docker compose up -d --build
#      (brings up postgres + flare-api; the API applies EF migrations on startup).
#   2. Vite dev server on http://localhost:5173 (cd client; npm run dev).
#
# Run from repo root:
#   pwsh scripts/seed-demo.ps1
#
# Idempotent: TRUNCATEs domain tables and re-seeds from scratch every run.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Force UTF-8 in/out so non-ASCII bytes piped to docker exec (psql) and HTTP
# bodies sent via Invoke-RestMethod survive the Windows codepage layer. UTF8
# *without* BOM -- psql treats a U+FEFF byte as a syntax error.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom
[Console]::InputEncoding  = $utf8NoBom
$OutputEncoding           = $utf8NoBom

$ApiBase  = 'http://localhost:8080'
$PgCntr   = 'flare-pg'
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Json {
    param([string]$Method, [string]$Uri, $Body)
    # PowerShell 5.1 Invoke-RestMethod sends string bodies in the default
    # codepage, not UTF-8. Convert to UTF-8 bytes ourselves so ASP.NET (which
    # always reads UTF-8) can parse the JSON.
    $json  = $Body | ConvertTo-Json -Compress -Depth 8
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Method $Method -Uri $Uri -ContentType 'application/json; charset=utf-8' -Body $bytes
}

function Invoke-Sql {
    param([string]$Sql)
    # Suppress NOTICE/INFO via SET client_min_messages so PowerShell 5.1 does not
    # wrap stderr into ErrorRecord (which would break the pipeline). No `2>&1`
    # redirect for the same reason.
    $prefixed = "SET client_min_messages TO WARNING;`n$Sql"
    $prefixed | docker exec -i $PgCntr psql -U postgres -d flare -q -v ON_ERROR_STOP=1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)" }
}

function Invoke-SqlScalar {
    param([string]$Sql)
    return ($Sql | docker exec -i $PgCntr psql -U postgres -d flare -t -A -q -v ON_ERROR_STOP=1)
}

function New-Uuid { return [guid]::NewGuid().Guid }

function To-Iso { param([datetime]$D) return $D.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') }

function Esc { param([string]$S) return $S -replace "'", "''" }

Write-Host '== Flare demo seed ==' -ForegroundColor Cyan
Write-Host "  api  : $ApiBase"
Write-Host "  pg   : $PgCntr"
Write-Host ''

# --- Sanity: API reachable ---
try {
    $health = Invoke-RestMethod -Uri "$ApiBase/healthz" -TimeoutSec 5
    if ($health.status -ne 'ok') { throw "healthz returned $($health | ConvertTo-Json)" }
} catch {
    throw "API not reachable at $ApiBase. Bring up the docker stack first. ($_)"
}
Write-Host '[ok] api healthy' -ForegroundColor Green

# --- Phase 0: clean slate ---
# TRUNCATE bypasses the append-only row trigger on IncidentEvents (statement-level,
# not row-level). FK CASCADE from Organizations clears teams/services/incidents/
# events/postmortems/action_items in one statement. OutboxMessages + StatusPages
# stand apart from the org cascade -- clear them explicitly. Materialized views are
# refreshed at the end of seed.
Invoke-Sql @'
TRUNCATE TABLE "Organizations" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "StatusPages"   RESTART IDENTITY CASCADE;
TRUNCATE TABLE "OutboxMessages" RESTART IDENTITY CASCADE;
'@
Write-Host '[ok] cleared previous demo data' -ForegroundColor Green

# --- Phase 1: Org + Team (no API, seed-only entities) ---
$orgId  = New-Uuid
$teamId = New-Uuid
Invoke-Sql @"
INSERT INTO "Organizations" ("Id","Name","Slug","CreatedAt")
  VALUES ('$orgId','Acme Cloud','acme', NOW() - INTERVAL '180 days');
INSERT INTO "Teams" ("Id","OrganizationId","Name","Slug","CreatedAt")
  VALUES ('$teamId','$orgId','Platform SRE','platform-sre', NOW() - INTERVAL '180 days');
"@
Write-Host '[ok] org + team' -ForegroundColor Green

# --- Phase 2: Services via API + PUT runbook ---
$serviceSpecs = @(
    @{ Name = 'Payment API';   Description = 'Stripe-backed checkout and refunds. Owns webhook ingest, idempotency keys, and PCI-scoped data.' }
    @{ Name = 'Auth Service';  Description = 'OAuth2 + SAML SSO front door. Handles session tokens, MFA, and downstream identity claims.' }
    @{ Name = 'CDN';           Description = 'Static asset and image edge cache. Multi-region with origin shield. Tier-1 customer-facing.' }
    @{ Name = 'Search';        Description = 'Elasticsearch-backed product and content search. Powers the global header search bar.' }
    @{ Name = 'Notifications'; Description = 'Email, SMS, and push delivery via SES + Twilio + FCM. Templated through Handlebars.' }
    @{ Name = 'Billing';       Description = 'Usage metering, invoice generation, dunning. Reconciles nightly against the Stripe ledger.' }
    @{ Name = 'Webhooks';      Description = 'Outbound webhook delivery for partner integrations. Per-tenant retry + signing.' }
    @{ Name = 'Admin Console'; Description = 'Internal back-office for ops, support, and finance. Auth via Okta groups.' }
)
# Five runbooks for the first five services; the rest get a placeholder so the
# services screenshot reads "runbook ready" consistently.
$runbooks = @(
@'
# Payment API runbook

## On-call action plan
1. Check Stripe status: https://status.stripe.com.
2. Look at p99 latency on the `payment_api_request_duration_seconds` panel.
3. If error rate above 1% -- page Commander, declare SEV2.

## Key dashboards
- Grafana: Payments overview, Webhook delivery queue.
- Datadog APM: Payment API service map.

## Recent changes
- 2026-05-18: enabled idempotency key trimming on retries.
- 2026-05-11: bumped Stripe SDK to 47.2.0.
'@,
@'
# Auth Service runbook

## SEV1 conditions
- Login p95 > 5s for 3 consecutive minutes.
- Any sustained 5xx above 2% on `/oauth/token`.

## First steps
1. Confirm Okta IdP status.
2. Drain the slow node pool with `kubectl drain auth-node-3 --ignore-daemonsets`.
3. Failover IdP routing to backup tenant via the LB rule `auth-failover-2026`.

## Escalation
- Primary: Platform SRE.
- Secondary: Identity team lead.
'@,
@'
# CDN runbook

## Common failure modes
- Origin shield burst exhaustion -- drain to alt-region.
- Image transform worker OOM -- bounce worker pool.
- Cache miss spike -- confirm purge wasn't broadcast accidentally.

## Useful commands
```sh
fastly purge --all              # last resort, very expensive
fastly stats --service=prod-edge --since=15m
```
'@,
@'
# Search runbook

## Common alerts
- Query p99 above 800ms -- check shard rebalancing.
- Indexer lag > 5 min -- inspect Kafka consumer offset.

## Recovery
1. Bounce the affected shard with `kubectl rollout restart statefulset/es-data-3`.
2. If rebalancing stuck, force a shard reroute via Kibana DevTools.
'@,
@'
# Notifications runbook

## Provider escalation
- SES bounce rate > 5% -- pause campaigns, check IP reputation.
- Twilio queue depth above 10k -- failover to secondary account SID.

## Templates
All templates compiled at build time; runtime errors surface as `notify_template_error_total`.
'@,
'# Billing runbook (work in progress)',
'# Webhooks runbook (work in progress)',
'# Admin Console runbook (work in progress)'
)

$serviceIds = @()
for ($i = 0; $i -lt $serviceSpecs.Length; $i++) {
    $spec = $serviceSpecs[$i]
    $svc  = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/services" -Body @{ name = $spec.Name; description = $spec.Description; teamId = $teamId }
    # Runbook via direct SQL -- PowerShell 5.1 Invoke-RestMethod has body-encoding
    # edge cases with non-trivial markdown, and the runbook is shaped offline
    # data anyway.
    Invoke-Sql "UPDATE ""Services"" SET ""RunbookBody"" = '$(Esc $runbooks[$i])' WHERE ""Id"" = '$($svc.id)';"
    $serviceIds += $svc.id
    Write-Host "[ok] service: $($spec.Name)" -ForegroundColor Green
}

# --- Phase 3: Historical incidents (direct SQL, backfilled timestamps) ---
# Each incident gets a realistic event stream: Created → Commander? → StatusChanged
# Investigating → comments → StatusChanged Identified? → StatusChanged Resolved.
# Spread across the last 30 days, distributed across services, mixed severities.

$today = (Get-Date).Date

$incidents = @(
    @{ Days=29; Hour=8;  Svc=3; Sev='Sev2'; Title='Search indexer Kafka lag above 8 min';   Min=64;  PostmortemPublished=$true;  Comments=@('Consumer lag climbing on the catalog topic', 'Identified slow Elasticsearch bulk_request - partition rebalanced', 'Throughput back to baseline; lag draining at 2k msg/s') }
    @{ Days=28; Hour=14; Svc=0; Sev='Sev3'; Title='Database connection pool exhaustion';    Min=47;  PostmortemPublished=$true;  Comments=@('Pool sitting at 100% utilization for 4 min', 'Bumped max_connections from 80 to 200 + restarted app pods', 'Recovery confirmed; reverting to pre-incident baseline') }
    @{ Days=26; Hour=23; Svc=4; Sev='Sev3'; Title='SES bounce rate climbing above 4%';      Min=38;  PostmortemPublished=$false; Comments=@('Bounce rate from US-west sender pool at 4.2%', 'Discovered list-hygiene job had stalled; manually re-ran') }
    @{ Days=25; Hour=9;  Svc=1; Sev='Sev2'; Title='OAuth provider timeout cascade';         Min=72;  PostmortemPublished=$true;  Comments=@('Okta returning 504 for ~25% of token exchanges', 'Confirmed: hot path stuck on IdP timeout (10s)', 'Cut over to backup IdP tenant; latency back to p95 180ms') }
    @{ Days=23; Hour=11; Svc=6; Sev='Sev3'; Title='Webhook retries piling up for partner X'; Min=29; PostmortemPublished=$false; Comments=@('Partner endpoint returning 502 consistently', 'Paused retries; opened ticket with partner ops team') }
    @{ Days=22; Hour=18; Svc=2; Sev='Sev4'; Title='Cache hit ratio dropped 12% on EU edge'; Min=18;  PostmortemPublished=$true;  Comments=@('Edge nodes in fra1/fra2 evicting earlier than configured', 'Bounced edge cache pools; hit rate recovering') }
    @{ Days=20; Hour=2;  Svc=5; Sev='Sev2'; Title='Billing invoice job stuck on tenant 184'; Min=87; PostmortemPublished=$true;  Comments=@('Nightly invoice run hung at 04:18 UTC on a single tenant', 'Tenant had a malformed line-item from a manual import', 'Quarantined the row; resumed the job; ledger reconciled') }
    @{ Days=19; Hour=11; Svc=0; Sev='Sev3'; Title='Idempotency key collisions on retries';  Min=28;  PostmortemPublished=$false; Comments=@('Client retry storm producing same key with new body', 'Patched middleware to hash on body+method instead of key alone') }
    @{ Days=17; Hour=13; Svc=3; Sev='Sev4'; Title='Search relevance regression on PR #4192'; Min=21; PostmortemPublished=$false; Comments=@('Click-through rate down 6% since deploy 11:40 UTC', 'Reverted PR #4192; CTR recovering') }
    @{ Days=16; Hour=3;  Svc=1; Sev='Sev1'; Title='Total authentication outage';            Min=114; PostmortemPublished=$true;  Comments=@('All login endpoints returning 503', 'IdP cert expired silently on midnight rotation', 'Issued emergency cert; sessions restored', 'All-hands postmortem scheduled') }
    @{ Days=14; Hour=20; Svc=4; Sev='Sev3'; Title='Push notification delivery delayed';     Min=19;  PostmortemPublished=$false; Comments=@('FCM queue depth above 50k', 'Bumped worker concurrency; queue drained in 9 min') }
    @{ Days=13; Hour=15; Svc=2; Sev='Sev3'; Title='European edge nodes degraded';           Min=41;  PostmortemPublished=$false; Comments=@('p95 from EU clients climbing past 800ms', 'Identified rogue worker holding connections; recycled pool') }
    @{ Days=11; Hour=4;  Svc=7; Sev='Sev3'; Title='Admin Console SSO callback flapping';    Min=33;  PostmortemPublished=$false; Comments=@('Operators reporting intermittent redirect loops', 'Found stale cookie domain after the recent DNS migration; patched and rolled out') }
    @{ Days=10; Hour=12; Svc=0; Sev='Sev2'; Title='Stripe webhook delivery delays';         Min=53;  PostmortemPublished=$true;  Comments=@('Stripe queue backed up; webhook lag 4 min and climbing', 'Confirmed Stripe-side issue; reading their status page', 'Their incident resolved; our backlog draining') }
    @{ Days=8;  Hour=17; Svc=5; Sev='Sev4'; Title='Dunning email batch ran twice';          Min=11;  PostmortemPublished=$false; Comments=@('Idempotency key not propagated through the new orchestrator', 'Squashed duplicates; finance team notified') }
    @{ Days=7;  Hour=19; Svc=1; Sev='Sev3'; Title='Slow login p95 above 2s';                Min=22;  PostmortemPublished=$false; Comments=@('Login latency creeping up over last 10 min', 'Found N+1 in session enrichment after last deploy; rolling back') }
    @{ Days=5;  Hour=10; Svc=6; Sev='Sev3'; Title='Webhook signature mismatch reports';     Min=24;  PostmortemPublished=$false; Comments=@('Three partners reporting bad signatures since 09:51 UTC', 'Key rotation propagated late; flushed cache and reissued') }
    @{ Days=4;  Hour=7;  Svc=2; Sev='Sev2'; Title='Image asset 502s spike';                 Min=35;  PostmortemPublished=$false; Comments=@('5xx rate from img.acme.com at 4%', 'Image transform worker OOMing on a new asset format', 'Worker pool bounced; rate back to baseline') }
    @{ Days=2;  Hour=16; Svc=0; Sev='Sev3'; Title='Refund processing queue backed up';      Min=16;  PostmortemPublished='draft'; Comments=@('Refund queue depth above 200 (normal: <30)', 'Found worker stuck on a poisoned message; ack-with-DLQ deployed') }
    @{ Days=1;  Hour=10; Svc=1; Sev='Sev4'; Title='Email verification delays';              Min=12;  PostmortemPublished=$false; Comments=@('Verification emails landing 4-5 min late', 'Third-party mailer queue caught up') }
)

# Track which incidents get postmortems (and which kind)
$postmortemIncidents = @()   # array of @{ IncidentId, IncidentTitle, IncidentDays, IncidentSev, Published, PostmortemId, ServiceName, PublishedAt }

foreach ($spec in $incidents) {
    $incId    = New-Uuid
    $svcId    = $serviceIds[$spec.Svc]
    $svcName  = $serviceSpecs[$spec.Svc].Name
    $created  = $today.AddDays(-$spec.Days).AddHours($spec.Hour).AddMinutes((Get-Random -Maximum 50))
    $resolved = $created.AddMinutes($spec.Min)

    Invoke-Sql @"
INSERT INTO "Incidents" ("Id","ServiceId","Title","Severity","Status","CreatedAt","AcknowledgedAt","ResolvedAt","ClosedAt","CommanderId","CommunicatorId","ResponderId")
  VALUES ('$incId','$svcId','$(Esc $spec.Title)','$($spec.Sev)','Resolved',
          '$(To-Iso $created)','$(To-Iso $created.AddMinutes(2))','$(To-Iso $resolved)', NULL, NULL, NULL, NULL);
"@

    # --- Event stream ---
    $eventsSql = New-Object System.Text.StringBuilder
    $cursorAt = $created

    # Created
    $createdPayload = (@{ Title = $spec.Title; Severity = $spec.Sev } | ConvertTo-Json -Compress)
    [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','Created', NULL, '$(Esc $createdPayload)'::jsonb, '$(To-Iso $cursorAt)');")

    # Commander assignment for SEV1/SEV2
    if ($spec.Sev -in 'Sev1','Sev2') {
        $cursorAt = $cursorAt.AddMinutes(3)
        $commanderPayload = (@{ Role = 'Commander'; UserId = (New-Uuid) } | ConvertTo-Json -Compress)
        [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','RoleAssigned', NULL, '$(Esc $commanderPayload)'::jsonb, '$(To-Iso $cursorAt)');")
    }

    # StatusChanged Investigating
    $cursorAt = $cursorAt.AddMinutes(5)
    $invPayload = (@{ To = 'Investigating' } | ConvertTo-Json -Compress)
    [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','StatusChanged', NULL, '$(Esc $invPayload)'::jsonb, '$(To-Iso $cursorAt)');")

    # Comments
    foreach ($comment in $spec.Comments) {
        $cursorAt = $cursorAt.AddMinutes(4 + (Get-Random -Maximum 8))
        if ($cursorAt -gt $resolved.AddMinutes(-1)) { $cursorAt = $resolved.AddMinutes(-1) }
        $cmtPayload = (@{ Comment = $comment } | ConvertTo-Json -Compress)
        [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','CommentAdded', NULL, '$(Esc $cmtPayload)'::jsonb, '$(To-Iso $cursorAt)');")
    }

    # StatusChanged Identified for longer incidents
    if ($spec.Min -gt 30) {
        $idCursor = $created.AddMinutes([int]($spec.Min * 0.55))
        $idPayload = (@{ To = 'Identified' } | ConvertTo-Json -Compress)
        [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','StatusChanged', NULL, '$(Esc $idPayload)'::jsonb, '$(To-Iso $idCursor)');")
    }

    # StatusChanged Resolved
    $resPayload = (@{ To = 'Resolved' } | ConvertTo-Json -Compress)
    [void]$eventsSql.AppendLine("INSERT INTO ""IncidentEvents"" (""Id"",""IncidentId"",""Type"",""ActorId"",""Payload"",""CreatedAt"") VALUES ('$(New-Uuid)','$incId','StatusChanged', NULL, '$(Esc $resPayload)'::jsonb, '$(To-Iso $resolved)');")

    Invoke-Sql $eventsSql.ToString()

    # --- Postmortem ---
    # PostmortemPublished semantics:
    #   $true   → create + Published
    #   'draft' → create + Draft (today's hero for the postmortem screenshot)
    #   $false / $null → no postmortem (realistic: not every incident gets one)
    if ($spec.PostmortemPublished -eq $true -or $spec.PostmortemPublished -eq 'draft') {
        $pmId = New-Uuid
        $pmCreated = $resolved.AddMinutes(15 + (Get-Random -Maximum 30))
        $impact = "Service: $svcName`nSeverity: $($spec.Sev)`nIncident: $($spec.Title)`nDuration: $([int]($spec.Min / 60)):$('{0:D2}' -f ($spec.Min % 60)):00"
        # Timeline: build same way PostmortemDraftBuilder would -- serialized JSON array of { At, Type, ActorId, Summary }
        $timelineEntries = @()
        $timelineEntries += @{ At = (To-Iso $created); Type = 'Created'; ActorId = $null; Summary = "Incident opened: $($spec.Title)" }
        $tlCursor = $created.AddMinutes(5)
        if ($spec.Sev -in 'Sev1','Sev2') {
            $timelineEntries += @{ At = (To-Iso $created.AddMinutes(3)); Type = 'RoleAssigned'; ActorId = $null; Summary = 'Role: Commander' }
        }
        $timelineEntries += @{ At = (To-Iso $tlCursor); Type = 'StatusChanged'; ActorId = $null; Summary = 'Status: Investigating' }
        $cmtTime = $tlCursor.AddMinutes(5)
        foreach ($c in $spec.Comments) {
            if ($cmtTime -gt $resolved.AddMinutes(-1)) { $cmtTime = $resolved.AddMinutes(-1) }
            $timelineEntries += @{ At = (To-Iso $cmtTime); Type = 'CommentAdded'; ActorId = $null; Summary = $c }
            $cmtTime = $cmtTime.AddMinutes(6)
        }
        if ($spec.Min -gt 30) {
            $timelineEntries += @{ At = (To-Iso $created.AddMinutes([int]($spec.Min * 0.55))); Type = 'StatusChanged'; ActorId = $null; Summary = 'Status: Identified' }
        }
        $timelineEntries += @{ At = (To-Iso $resolved); Type = 'StatusChanged'; ActorId = $null; Summary = 'Status: Resolved' }
        $timelineJson = $timelineEntries | ConvertTo-Json -Compress -Depth 4
        # Wrap single-item arrays from PowerShell ConvertTo-Json
        if (-not $timelineJson.StartsWith('[')) { $timelineJson = "[$timelineJson]" }

        $pmStatus = if ($spec.PostmortemPublished -eq $true) { 'Published' } else { 'Draft' }
        $pmPublishedAt = if ($spec.PostmortemPublished -eq $true) { "'$(To-Iso $pmCreated.AddHours(1))'" } else { 'NULL' }

        Invoke-Sql @"
INSERT INTO "Postmortems" ("Id","IncidentId","Status","Impact","Timeline","RootCause","CreatedAt","PublishedAt")
  VALUES ('$pmId','$incId','$pmStatus','$(Esc $impact)','$(Esc $timelineJson)'::jsonb, '', '$(To-Iso $pmCreated)', $pmPublishedAt);
"@
        $postmortemIncidents += @{
            IncidentId = $incId; IncidentTitle = $spec.Title; IncidentDays = $spec.Days; IncidentSev = $spec.Sev
            Status = $pmStatus; PostmortemId = $pmId; ServiceName = $svcName; PublishedAt = if ($pmStatus -eq 'Published') { $pmCreated.AddHours(1) } else { $pmCreated }
        }
    }

    Write-Host "[ok] historical: -$($spec.Days)d  $svcName  $($spec.Sev)  $($spec.Title)" -ForegroundColor DarkGray
}

# --- Phase 4: Action items for postmortems ---
# Realistic spread: older postmortems have more Done items; recent ones more Open/InProgress.
# Each Published postmortem gets 2-4 items. The Draft (today's hero) gets 3 items
# including a deliberate Overdue case for the /action-items screenshot.

$actionItemSpecs = @(
    # Day -29: Search indexer Kafka lag (published)
    @{ PmDay=29; Title='Add per-topic consumer-lag alert at 5m / 10m thresholds';                      Status='Done';       DueOffsetDays=-23 }
    @{ PmDay=29; Title='Document partition-rebalance procedure in Search runbook';                     Status='Done';       DueOffsetDays=-21 }
    @{ PmDay=29; Title='Migrate bulk_request to streaming bulk API';                                   Status='InProgress'; DueOffsetDays=3 }
    # Day -28: DB pool exhaustion (published)
    @{ PmDay=28; Title='Bump default pool size in app charts to 200';                                  Status='Done';       DueOffsetDays=-22 }
    @{ PmDay=28; Title='Add alert on pool_utilization > 80% for 2 min';                                Status='Done';       DueOffsetDays=-20 }
    @{ PmDay=28; Title='Document pool sizing rationale in Payment runbook';                            Status='InProgress'; DueOffsetDays=-3 }
    # Day -25: OAuth provider timeout (published)
    @{ PmDay=25; Title='Cut IdP timeout from 10s to 3s on hot path';                                   Status='Done';       DueOffsetDays=-19 }
    @{ PmDay=25; Title='Promote backup IdP tenant to warm standby (was cold)';                         Status='Done';       DueOffsetDays=-16 }
    @{ PmDay=25; Title='Add IdP health-probe panel to on-call dashboard';                              Status='Open';       DueOffsetDays=1 }
    # Day -22: Cache hit ratio (published)
    @{ PmDay=22; Title='Tune EU edge TTL -- drop from 300s to 240s for hot keys';                      Status='Done';       DueOffsetDays=-18 }
    @{ PmDay=22; Title='Add hit-ratio panel to public-facing Cloudflare board';                        Status='Done';       DueOffsetDays=-15 }
    # Day -20: Billing invoice job stuck (published)
    @{ PmDay=20; Title='Add line-item schema validation to nightly invoice job';                       Status='Done';       DueOffsetDays=-13 }
    @{ PmDay=20; Title='Quarantine queue for malformed tenants (no hard-stop)';                        Status='InProgress'; DueOffsetDays=4 }
    @{ PmDay=20; Title='Daily ledger reconciliation report to #finance-ops';                           Status='Open';       DueOffsetDays=6 }
    # Day -16: Total auth outage (SEV1, biggest postmortem)
    @{ PmDay=16; Title='Switch IdP cert rotation to ACME with 7-day overlap';                          Status='Done';       DueOffsetDays=-9 }
    @{ PmDay=16; Title='Pager alert on cert TTL < 14d, not 7d';                                        Status='Done';       DueOffsetDays=-12 }
    @{ PmDay=16; Title='Wire backup IdP into health probes (read-only auth check)';                    Status='InProgress'; DueOffsetDays=2 }
    @{ PmDay=16; Title='Externalise IdP rotation runbook to public Confluence';                        Status='Open';       DueOffsetDays=-2 }
    @{ PmDay=16; Title='Post-incident customer comms template for auth outages';                       Status='Done';       DueOffsetDays=-7 }
    # Day -10: Stripe webhook delays (published)
    @{ PmDay=10; Title='Move Stripe webhook ack to a dedicated worker pool';                           Status='Done';       DueOffsetDays=-5 }
    @{ PmDay=10; Title='Backfill missed webhooks for the 53-min window via Stripe Events API';         Status='InProgress'; DueOffsetDays=5 }
    @{ PmDay=10; Title='Lag SLO + burn-rate alert on Stripe webhook queue';                            Status='Open';       DueOffsetDays=8 }
    # Day -2: Refund queue (Draft postmortem -- today's hero)
    @{ PmDay=2;  Title='Add DLQ visualisation to Payment queue panel';                                 Status='Open';       DueOffsetDays=4 }
    @{ PmDay=2;  Title='Document poisoned-message recovery path in runbook';                           Status='InProgress'; DueOffsetDays=7 }
    @{ PmDay=2;  Title='Audit retry policy for refund worker (exponential backoff cap)';               Status='Open';       DueOffsetDays=10 }
    @{ PmDay=2;  Title='Schema-validate refund-job payload at producer';                               Status='Open';       DueOffsetDays=12 }
)

foreach ($ai in $actionItemSpecs) {
    $pm = $postmortemIncidents | Where-Object { $_.IncidentDays -eq $ai.PmDay } | Select-Object -First 1
    if ($null -eq $pm) { Write-Host "[skip action item] no postmortem for day -$($ai.PmDay)" -ForegroundColor Yellow; continue }
    $aiId = New-Uuid
    $created = $pm.PublishedAt.AddMinutes(30)
    if ($null -eq $pm.PublishedAt) { $created = $today.AddDays(-$ai.PmDay).AddHours(20) }
    $dueDate = $today.AddDays($ai.DueOffsetDays).ToString('yyyy-MM-dd')
    $ownerId = New-Uuid
    Invoke-Sql @"
INSERT INTO "ActionItems" ("Id","PostmortemId","Title","OwnerId","DueDate","Status","CreatedAt")
  VALUES ('$aiId','$($pm.PostmortemId)','$(Esc $ai.Title)','$ownerId', '$dueDate', '$($ai.Status)', '$(To-Iso $created)');
"@
}
Write-Host "[ok] action items: $($actionItemSpecs.Length)" -ForegroundColor Green

# --- Phase 5a: Strengthen the Draft postmortem's RootCause ---
# The frontend renders a "lands when the backend gets a PATCH endpoint" placeholder
# when RootCause is empty. Filling it with a real SRE paragraph is the difference
# between "demo" and "production" in the postmortem screenshot.
$draftPm = $postmortemIncidents | Where-Object { $_.Status -eq 'Draft' } | Select-Object -First 1
if ($draftPm) {
    $rc = 'Refund worker stuck on a single poisoned message after a producer-side schema change drifted out of sync with the consumer. Retry policy lacked an exponential backoff cap so the worker hot-looped on the same id; the message stayed at the head of the queue and starved every other refund job. The DLQ pattern was deployed within 12 minutes of detection but recovery hinged on a manual queue drain. Contributing factors: producer schema not version-locked, consumer-side payload validation missing, queue-depth alert paged but did not block the deploy that introduced the drift.'
    Invoke-Sql "UPDATE ""Postmortems"" SET ""RootCause"" = '$(Esc $rc)' WHERE ""Id"" = '$($draftPm.PostmortemId)';"
    Write-Host '[ok] draft postmortem root cause filled' -ForegroundColor Green
}

# --- Phase 5b: Active incidents TODAY via API (full production flow) ---
# Five active incidents across mixed severities give the dashboard a realistic
# "team is in the middle of work" read instead of one solitary row.

# Hero #1 for the incident-detail screenshot.
$activeServiceId = $serviceIds[0]  # Payment API
$active = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents" -Body @{ title = 'Payment latency p99 above 500ms'; severity = 'Sev2'; serviceId = $activeServiceId }
$activeId = $active.id
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/roles" -Body @{ role = 'Commander';    userId = (New-Uuid) } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/roles" -Body @{ role = 'Communicator'; userId = (New-Uuid) } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/roles" -Body @{ role = 'Responder';    userId = (New-Uuid) } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/transition" -Body @{ to = 'Investigating' } | Out-Null
$cmt1Payload = @{ Comment = 'p99 from US-east clients spiking - Stripe upstream looks healthy though' } | ConvertTo-Json -Compress
$cmt2Payload = @{ Comment = 'Pulled flamegraph from prod node; webhook handler holding the lock longer than expected on retries' } | ConvertTo-Json -Compress
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/events" -Body @{ type = 'CommentAdded'; payload = $cmt1Payload } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$activeId/events" -Body @{ type = 'CommentAdded'; payload = $cmt2Payload } | Out-Null
Invoke-Sql @"
UPDATE "Incidents" SET "CreatedAt" = NOW() - INTERVAL '2 hours', "AcknowledgedAt" = NOW() - INTERVAL '1 hour 58 minutes' WHERE "Id" = '$activeId';
"@
Write-Host "[ok] active hero incident: $activeId" -ForegroundColor Green

# Active #2: SEV1 Search -- biggest, on top of the table after the hero (most-recent).
$inc2 = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents" -Body @{ title = 'Search indexer Kafka lag above 9 min'; severity = 'Sev1'; serviceId = $serviceIds[3] }
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc2.id)/roles"      -Body @{ role = 'Commander'; userId = (New-Uuid) } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc2.id)/transition" -Body @{ to = 'Investigating' } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc2.id)/transition" -Body @{ to = 'Identified' } | Out-Null
$inc2Cmt = @{ Comment = 'Consumer offset frozen on the catalog-v2 topic; rebalancing manually now' } | ConvertTo-Json -Compress
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc2.id)/events" -Body @{ type = 'CommentAdded'; payload = $inc2Cmt } | Out-Null
Invoke-Sql "UPDATE ""Incidents"" SET ""CreatedAt"" = NOW() - INTERVAL '38 minutes', ""AcknowledgedAt"" = NOW() - INTERVAL '36 minutes' WHERE ""Id"" = '$($inc2.id)';"
Write-Host '[ok] active sev1 (Search)' -ForegroundColor Green

# Active #3: SEV3 Billing
$inc3 = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents" -Body @{ title = 'Invoice generation queue backed up'; severity = 'Sev3'; serviceId = $serviceIds[5] }
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc3.id)/transition" -Body @{ to = 'Investigating' } | Out-Null
Invoke-Sql "UPDATE ""Incidents"" SET ""CreatedAt"" = NOW() - INTERVAL '1 hour 14 minutes', ""AcknowledgedAt"" = NOW() - INTERVAL '1 hour 11 minutes' WHERE ""Id"" = '$($inc3.id)';"
Write-Host '[ok] active sev3 (Billing)' -ForegroundColor Green

# Active #4: SEV4 Webhooks
$inc4 = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents" -Body @{ title = 'Partner webhook retries failing intermittently'; severity = 'Sev4'; serviceId = $serviceIds[6] }
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc4.id)/transition" -Body @{ to = 'Investigating' } | Out-Null
$inc4Cmt = @{ Comment = 'Three partners reporting 502 from our outbound delivery; staggered retry queue draining' } | ConvertTo-Json -Compress
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc4.id)/events" -Body @{ type = 'CommentAdded'; payload = $inc4Cmt } | Out-Null
Invoke-Sql "UPDATE ""Incidents"" SET ""CreatedAt"" = NOW() - INTERVAL '47 minutes', ""AcknowledgedAt"" = NOW() - INTERVAL '45 minutes' WHERE ""Id"" = '$($inc4.id)';"
Write-Host '[ok] active sev4 (Webhooks)' -ForegroundColor Green

# Active #5: SEV2 Notifications
$inc5 = Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents" -Body @{ title = 'SES throttle on US-east sender pool'; severity = 'Sev2'; serviceId = $serviceIds[4] }
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc5.id)/roles"      -Body @{ role = 'Commander'; userId = (New-Uuid) } | Out-Null
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc5.id)/transition" -Body @{ to = 'Investigating' } | Out-Null
$inc5Cmt = @{ Comment = 'AWS console shows daily quota at 92%; failover to backup pool in progress' } | ConvertTo-Json -Compress
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/incidents/$($inc5.id)/events" -Body @{ type = 'CommentAdded'; payload = $inc5Cmt } | Out-Null
Invoke-Sql "UPDATE ""Incidents"" SET ""CreatedAt"" = NOW() - INTERVAL '23 minutes', ""AcknowledgedAt"" = NOW() - INTERVAL '21 minutes' WHERE ""Id"" = '$($inc5.id)';"
Write-Host '[ok] active sev2 (Notifications)' -ForegroundColor Green

# --- Phase 6: Public status page ---
Invoke-Json -Method Post -Uri "$ApiBase/api/v1/status-pages" -Body @{
    orgId       = $orgId
    slug        = 'demo'
    title       = 'Acme Cloud - Public Status'
    description = 'Real-time service health for Acme Cloud customers.'
    serviceIds  = $serviceIds
} | Out-Null
Write-Host '[ok] status page /p/demo' -ForegroundColor Green

# --- Phase 7: Refresh materialised views ---
Invoke-Sql 'REFRESH MATERIALIZED VIEW CONCURRENTLY mttr_by_service_30d;'
Invoke-Sql 'REFRESH MATERIALIZED VIEW CONCURRENTLY mtta_by_service_30d;'
Write-Host '[ok] matviews refreshed' -ForegroundColor Green

# --- Phase 8: Smoke verification ---
Write-Host ''
Write-Host '== verification ==' -ForegroundColor Cyan
# Count via direct SQL -- PowerShell 5.1 has surprising pipeline semantics in
# script context that make Invoke-RestMethod result counts unreliable. SQL is
# unambiguous and works in any host.
$svcCount      = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM "Services";')
$resolvedCount = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM "Incidents" WHERE "Status" = ''Resolved'';')
$activeCount   = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM "Incidents" WHERE "Status" NOT IN (''Resolved'', ''Closed'');')
$overdueCount  = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM "ActionItems" WHERE "Status" != ''Done'' AND "DueDate" < CURRENT_DATE;')
$dash          = Invoke-RestMethod "$ApiBase/api/v1/metrics/dashboard"
$mttrCount     = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM mttr_by_service_30d;')
$pub           = Invoke-RestMethod "$ApiBase/public/status/demo"

Write-Host ("  services       : {0} (want 8)" -f $svcCount)
Write-Host ("  resolved       : {0} (want 20)" -f $resolvedCount)
Write-Host ("  active         : {0} (want 5)" -f $activeCount)
Write-Host ("  overdue items  : {0} (want >=1)" -f $overdueCount)
Write-Host ("  dash openCount : {0} avgMttr {1}ms" -f $dash.openIncidentsCount, $dash.mttrLast30dAvgMs)
Write-Host ("  mttr rows      : {0}" -f $mttrCount)
Write-Host ("  /p/demo        : '{0}' overall={1} services={2}" -f $pub.title, $pub.overallStatus, $pub.services.Count)

if ($svcCount      -ne 8)  { throw "expected 8 services, got $svcCount" }
if ($resolvedCount -ne 20) { throw "expected 20 resolved, got $resolvedCount" }
if ($activeCount   -lt 5)  { throw "expected 5 active, got $activeCount" }
if ($overdueCount  -lt 1)  { throw "expected >=1 overdue action item, got $overdueCount" }
if ($dash.openIncidentsCount -lt 5) { throw "dashboard shows <5 open incidents" }
if ($pub.services.Count -ne 8) { throw "public status page services count mismatch" }
Write-Host ''
Write-Host 'seed complete -- ready for capture.' -ForegroundColor Green

# --- Phase 9: Trigger Playwright capture ---
Write-Host ''
Write-Host '== playwright capture ==' -ForegroundColor Cyan
Push-Location (Join-Path $RepoRoot 'client')
try {
    # Vite reads VITE_API_URL at startup (api/client.ts, publicClient.ts,
    # useFlareHub.ts). Default is :5000; our docker API is on :8080.
    # Playwright forks Vite as a child of this shell so the env propagates.
    $env:SCREENSHOT_CAPTURE = '1'
    $env:VITE_API_URL = 'http://localhost:8080'
    npx playwright test playwright/tests/screenshots.capture.spec.ts --reporter=line
    $exit = $LASTEXITCODE
    Remove-Item Env:SCREENSHOT_CAPTURE -ErrorAction SilentlyContinue
    Remove-Item Env:VITE_API_URL -ErrorAction SilentlyContinue
    if ($exit -ne 0) { throw "playwright capture failed (exit $exit)" }
} finally {
    Pop-Location
}
Write-Host 'done.' -ForegroundColor Green

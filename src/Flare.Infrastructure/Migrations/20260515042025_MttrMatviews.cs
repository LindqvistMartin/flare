using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MttrMatviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW mttr_by_service_30d AS
                SELECT
                    s."Id"   AS "ServiceId",
                    s."Name" AS "ServiceName",
                    COUNT(i.*) AS "IncidentCount",
                    COALESCE(AVG(EXTRACT(EPOCH FROM (i."ResolvedAt" - i."CreatedAt")) * 1000), 0)::bigint AS "AvgMttrMs",
                    COALESCE(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (i."ResolvedAt" - i."CreatedAt")) * 1000), 0)::bigint AS "P50MttrMs"
                FROM "Services" s
                LEFT JOIN "Incidents" i
                    ON i."ServiceId" = s."Id"
                   AND i."ResolvedAt" IS NOT NULL
                   AND i."CreatedAt" >= NOW() - INTERVAL '30 days'
                GROUP BY s."Id", s."Name"
                WITH DATA;

                CREATE UNIQUE INDEX ux_mttr_by_service_30d_service_id ON mttr_by_service_30d ("ServiceId");

                CREATE MATERIALIZED VIEW mtta_by_service_30d AS
                SELECT
                    s."Id"   AS "ServiceId",
                    s."Name" AS "ServiceName",
                    COUNT(i.*) AS "IncidentCount",
                    COALESCE(AVG(EXTRACT(EPOCH FROM (i."AcknowledgedAt" - i."CreatedAt")) * 1000), 0)::bigint AS "AvgMttaMs",
                    COALESCE(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (i."AcknowledgedAt" - i."CreatedAt")) * 1000), 0)::bigint AS "P50MttaMs"
                FROM "Services" s
                LEFT JOIN "Incidents" i
                    ON i."ServiceId" = s."Id"
                   AND i."AcknowledgedAt" IS NOT NULL
                   AND i."CreatedAt" >= NOW() - INTERVAL '30 days'
                GROUP BY s."Id", s."Name"
                WITH DATA;

                CREATE UNIQUE INDEX ux_mtta_by_service_30d_service_id ON mtta_by_service_30d ("ServiceId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP MATERIALIZED VIEW IF EXISTS mtta_by_service_30d;
                DROP MATERIALIZED VIEW IF EXISTS mttr_by_service_30d;
                """);
        }
    }
}

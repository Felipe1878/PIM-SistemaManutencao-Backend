using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaManutencao.API.Migrations
{
    /// <inheritdoc />
    public partial class OrdemServicoTipoOrigemCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_OrdensServicos_TipoOrigem",
                table: "OrdensServicos",
                sql: "([TipoManutencao] = 0 AND [OcorrenciaId] IS NOT NULL AND [PlanoManutencaoId] IS NULL) OR ([TipoManutencao] = 1 AND [PlanoManutencaoId] IS NOT NULL AND [OcorrenciaId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrdensServicos_TipoOrigem",
                table: "OrdensServicos");
        }
    }
}

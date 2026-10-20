using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SistemaManutencao.API.Data;

namespace SistemaManutencao.API.Migrations;

[DbContext(typeof(AppDbContext))]
public partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.11");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 128);
        modelBuilder.UseIdentityColumns();
        AppDbContext.ConfigurarModelo(modelBuilder);
    }
}

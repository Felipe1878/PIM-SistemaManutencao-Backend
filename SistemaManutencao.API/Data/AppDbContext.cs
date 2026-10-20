using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Equipamento> Equipamentos => Set<Equipamento>();
    public DbSet<Ocorrencia> Ocorrencias => Set<Ocorrencia>();
    public DbSet<OrdemServico> OrdensServicos => Set<OrdemServico>();
    public DbSet<Manutencao> Manutencoes => Set<Manutencao>();
    public DbSet<PlanoManutencao> PlanosManutencao => Set<PlanoManutencao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigurarModelo(modelBuilder);

    // Reutilizado no snapshot da migração inicial para evitar divergência com o mapeamento.
    public static void ConfigurarModelo(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios"); e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(180).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
        });
        b.Entity<Setor>(e =>
        {
            e.ToTable("Setores"); e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Nome).IsUnique().HasFilter("[Ativo] = 1");
        });
        b.Entity<Equipamento>(e =>
        {
            e.ToTable("Equipamentos"); e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
            e.Property(x => x.Patrimonio).HasMaxLength(60).IsRequired();
            e.Property(x => x.Fabricante).HasMaxLength(100);
            e.Property(x => x.Modelo).HasMaxLength(100);
            e.Property(x => x.NumeroSerie).HasMaxLength(100);
            e.HasIndex(x => x.Patrimonio).IsUnique();
            e.HasOne(x => x.Setor).WithMany().HasForeignKey(x => x.SetorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Ocorrencia>(e =>
        {
            e.ToTable("Ocorrencias"); e.HasKey(x => x.Id);
            e.Property(x => x.Descricao).HasMaxLength(2000).IsRequired();
            // StatusOcorrencia.Resolvida = 2. Impede duas ocorrências ativas mesmo com concorrência.
            e.HasIndex(x => x.EquipamentoId).IsUnique().HasFilter("[Status] <> 2");
            e.HasOne(x => x.Equipamento).WithMany().HasForeignKey(x => x.EquipamentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlanoManutencao>(e =>
        {
            e.ToTable("PlanosManutencao"); e.HasKey(x => x.Id);
            e.Property(x => x.Descricao).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.Equipamento).WithMany().HasForeignKey(x => x.EquipamentoId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrdemServico>(e =>
        {
            e.ToTable("OrdensServicos"); e.HasKey(x => x.Id);
            e.HasOne(x => x.Ocorrencia).WithOne(x => x!.OrdemServico)
             .HasForeignKey<OrdemServico>(x => x.OcorrenciaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OcorrenciaId).IsUnique().HasFilter("[OcorrenciaId] IS NOT NULL");
            e.HasOne(x => x.PlanoManutencao).WithMany().HasForeignKey(x => x.PlanoManutencaoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Tecnico).WithMany().HasForeignKey(x => x.TecnicoId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Manutencao>(e =>
        {
            e.ToTable("Manutencoes"); e.HasKey(x => x.Id);
            e.Property(x => x.Diagnostico).HasMaxLength(3000).IsRequired();
            e.Property(x => x.ServicoRealizado).HasMaxLength(3000).IsRequired();
            e.Property(x => x.Observacoes).HasMaxLength(3000);
            e.HasOne(x => x.OrdemServico).WithOne(x => x!.Manutencao)
             .HasForeignKey<Manutencao>(x => x.OrdemServicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OrdemServicoId).IsUnique();
        });
    }
}

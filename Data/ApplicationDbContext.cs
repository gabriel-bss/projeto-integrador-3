using Microsoft.EntityFrameworkCore;
using AtestadoMedico.Models;

namespace AtestadoMedico.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Atestado> Atestados { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuração do relacionamento entre Atestado e Usuario
            modelBuilder.Entity<Atestado>()
                .HasOne(a => a.Usuario)
                .WithMany(u => u.Atestados)
                .HasForeignKey(a => a.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
} 
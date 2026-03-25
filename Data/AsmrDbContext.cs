using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsmrAutomationEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace AsmrAutomationEngine.Data
{
    public class AsmrDbContext : DbContext
    {
        public DbSet<VideoJob> VideoJobs => Set<VideoJob>();
        // The DI container uses this constructor to pass in the SQLite connection string from Program.cs
        public AsmrDbContext(DbContextOptions<AsmrDbContext> options) : base(options)
        {
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Fallback for design-time migrations, but runtime will use DI
                optionsBuilder.UseSqlite("Data Source=asmr_engine.db");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VideoJob>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SeedIdea)
                        .IsRequired()
                        .HasMaxLength(200);

                entity.Property(e => e.Status)
                   .HasConversion<int>(); // Enforces enum is stored as integer, not string

                // Performance: Add an index on Status because our BackgroundService 
                // will constantly run: SELECT * FROM VideoJobs WHERE Status = X
                entity.HasIndex(e => e.Status);
            });
        }
    }
}
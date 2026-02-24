using DiDeduplicator.Data.Entities;
using DiDeduplicator.Enums;
using Microsoft.EntityFrameworkCore;

namespace DiDeduplicator.Data;

public class AppDbContext : DbContext
{
    public DbSet<FileRecord> Files => Set<FileRecord>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FileRecord>();

        entity.HasKey(f => f.FileId);

        // Store GUID as string for reliable lexicographic ordering
        entity.Property(f => f.FileId)
            .HasConversion<string>();

        // Store enums as strings for readability
        entity.Property(f => f.Source)
            .HasConversion<string>();

        entity.Property(f => f.Status)
            .HasConversion<string>();

        // Store nullable GUIDs as strings
        entity.Property(f => f.MergedToMasterFileId)
            .HasConversion<string>();

        entity.Property(f => f.ByteMismatchFileId)
            .HasConversion<string>();

        // Indexes
        entity.HasIndex(f => new { f.Source, f.HashSha256, f.FileSize })
            .HasDatabaseName("IX_Files_Source_Hash_Size");

        entity.HasIndex(f => new { f.HashSha256, f.Source })
            .HasDatabaseName("IX_Files_Hash_Source");

        entity.HasIndex(f => f.Status)
            .HasDatabaseName("IX_Files_Status");

        entity.HasIndex(f => f.FullPath)
            .IsUnique()
            .HasDatabaseName("IX_Files_FullPath_Unique");
    }
}

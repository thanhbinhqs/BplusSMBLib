using SmbEnterprise.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace SmbEnterprise.Persistence;

/// <summary>EF Core DbContext for persisting transfer jobs.</summary>
public sealed class SmbJobsDbContext : DbContext
{
    public SmbJobsDbContext(DbContextOptions<SmbJobsDbContext> options) : base(options) { }

    public DbSet<TransferJobEntity> Jobs => Set<TransferJobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransferJobEntity>(e =>
        {
            e.HasKey(x => x.JobId);
            e.Property(x => x.JobId).ValueGeneratedNever();
            e.Property(x => x.SourcePath).IsRequired().HasMaxLength(2000);
            e.Property(x => x.DestinationPath).IsRequired().HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.ErrorMessage).HasMaxLength(4000);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Priority);
        });
    }
}

/// <summary>Flat entity for SQLite storage (avoids complex type issues).</summary>
public sealed class TransferJobEntity
{
    public Guid JobId { get; set; }
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public JobStatus Status { get; set; }
    public JobPriority Priority { get; set; }
    public long TotalBytes { get; set; }
    public long BytesTransferred { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ChunkSize { get; set; }
    public bool VerifyAfterCopy { get; set; }

    public static TransferJobEntity FromDomain(TransferJob job) => new()
    {
        JobId = job.JobId,
        SourcePath = job.SourcePath,
        DestinationPath = job.DestinationPath,
        Status = job.Status,
        Priority = job.Priority,
        TotalBytes = job.TotalBytes,
        BytesTransferred = job.BytesTransferred,
        RetryCount = job.RetryCount,
        ErrorMessage = job.ErrorMessage,
        CorrelationId = job.CorrelationId,
        CreatedAtUtc = job.CreatedAtUtc,
        StartedAtUtc = job.StartedAtUtc,
        CompletedAtUtc = job.CompletedAtUtc,
        ChunkSize = job.Options.ChunkSize,
        VerifyAfterCopy = job.Options.VerifyAfterCopy
    };

    public TransferJob ToDomain() => new()
    {
        JobId = JobId,
        SourcePath = SourcePath,
        DestinationPath = DestinationPath,
        Status = Status,
        Priority = Priority,
        TotalBytes = TotalBytes,
        BytesTransferred = BytesTransferred,
        RetryCount = RetryCount,
        ErrorMessage = ErrorMessage,
        CorrelationId = CorrelationId,
        CreatedAtUtc = CreatedAtUtc,
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = CompletedAtUtc,
        Options = new TransferOptions
        {
            ChunkSize = ChunkSize,
            VerifyAfterCopy = VerifyAfterCopy
        }
    };
}

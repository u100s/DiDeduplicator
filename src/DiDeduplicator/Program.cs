using DiDeduplicator.Configuration;
using DiDeduplicator.Data;
using DiDeduplicator.Pipeline;
using DiDeduplicator.Services;
using DiDeduplicator.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// Bootstrap Serilog early for startup errors
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "dideduplicator-.log"),
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        retainedFileCountLimit: 31)
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Serilog
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "dideduplicator-.log"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 100 * 1024 * 1024,
            retainedFileCountLimit: 31));

    // Configuration
    builder.Services.Configure<AppSettings>(
        builder.Configuration.GetSection("DiDeduplicator"));

    // Database
    var dbPath = Path.Combine(AppContext.BaseDirectory, "dideduplicator.db");
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath};Cache=Shared",
            sqliteOptions => sqliteOptions.CommandTimeout(60))
        );

    // Services
    builder.Services.AddSingleton<IHashService, HashService>();
    builder.Services.AddSingleton<ISafeTransferService, SafeTransferService>();
    builder.Services.AddSingleton<IConsoleUI, ConsoleUI>();
    builder.Services.AddSingleton<IScanService, ScanService>();
    builder.Services.AddSingleton<IComparisonService, ComparisonService>();
    builder.Services.AddSingleton<IDeduplicationService, DeduplicationService>();
    builder.Services.AddSingleton<ICleanupService, CleanupService>();
    builder.Services.AddSingleton<PipelineOrchestrator>();

    var host = builder.Build();

    // Initialize database (EnsureCreated — no migrations)
    using (var scope = host.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Enable WAL mode + busy_timeout for parallel writers
        await db.Database.ExecuteSqlRawAsync(
            "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;");

        await db.Database.EnsureCreatedAsync();
    }

    // Run pipeline
    var orchestrator = host.Services.GetRequiredService<PipelineOrchestrator>();
    await orchestrator.ExecuteAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

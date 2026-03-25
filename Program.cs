using AsmrAutomationEngine;
using AsmrAutomationEngine.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Polly;
using Polly.Extensions.Http;
using AsmrAutomationEngine.Interfaces;
using AsmrAutomationEngine.Services;

//1. Bootstrapping Serilog
Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/engine_log.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

try
{
    Log.Information("Starting up ASMR Automation Engine...");
    var builder = Host.CreateApplicationBuilder(args);

    // 2. Replace Default Logging with Serilog
    builder.Services.AddSerilog();

    // 3. Register Database Configuration securely
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AsmrDbContext>(options=>options.UseSqlite(connectionString));

    // 4. Resilient HTTP Clients (Polly)
    // Define the Exponential Backoff Policy: Retry 3 times, waiting 2s, 4s, 8s between failures.
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Warning("HTTP request failed. Waiting {Delay} before next retry. Retry attempt {RetryAttempt}.", timespan, retryAttempt);
            });
    // Register the Typed Client with the Policy attached
    builder.Services.AddHttpClient<IGeminiClient, GeminiClient>().AddPolicyHandler(retryPolicy);
    builder.Services.AddHttpClient<IVeoClient, VeoClient>(client => 
    {
        client.BaseAddress = new Uri("https://us-central1-aiplatform.googleapis.com/");
    }).AddPolicyHandler(retryPolicy);
    
// 5. Register the YouTube Publisher (Transient because it manages stateful file streams internally)
    builder.Services.AddTransient<IYouTubePublisher, YouTubePublisher>();
    // Register the Main Execution Loop
    builder.Services.AddHostedService<Worker>();
    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
Log.Fatal(ex, "The application failed to start correctly.");
}
finally
{
    Log.CloseAndFlush();
}



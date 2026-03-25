using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AsmrAutomationEngine.Data;
using AsmrAutomationEngine.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AsmrAutomationEngine.Interfaces;
namespace AsmrAutomationEngine;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGeminiClient _geminiClient;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory,IGeminiClient geminiClient)
    {
        _logger = logger;
        _scopeFactory = scopeFactory; // Crucial for scoping DbContext
        _geminiClient = geminiClient;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AsmrAutomationEngine is starting the execution loop.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPipelineAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Global catch-all to prevent the worker from crashing entirely if the DB goes down.
                _logger.LogCritical(ex, "Fatal anomaly in the main execution loop.");
            }
            // Polling interval: Throttle the loop to prevent aggressive CPU/DB spiking.
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ProcessPipelineAsync(CancellationToken stoppingToken)
    {
        // 1. Create a fresh scope for this specific execution tick
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AsmrDbContext>();

        //2. Fetch the highest priority pending job (FIFO queueing)
        var pendingJob = await dbContext.VideoJobs
           .Where(j => j.Status != JobStatus.Published && j.Status != JobStatus.Failed)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(stoppingToken);

        if (pendingJob == null)
        {
            _logger.LogDebug("Queue is empty. Waiting for seed ideas...");
            return;
        }

        _logger.LogInformation("Picked up JobId: {Id} | State: {Status} | Seed: {SeedIdea}",
            pendingJob.Id, pendingJob.Status, pendingJob.SeedIdea);

        try
        {
            //3. The State Router (Single Responsibility applied to routing)
            switch (pendingJob.Status)
            {
                case JobStatus.PendingLLM:
                    await HandlePendingLLMAsync(pendingJob, dbContext, stoppingToken);
                    break;
                case JobStatus.PendingRender:
                    await HandlePendingRenderAsync(pendingJob, dbContext, stoppingToken);
                    break;
                case JobStatus.Rendering:
                    await HandleRenderingAsync(pendingJob, dbContext, stoppingToken);
                    break;
                case JobStatus.ReadyForUpload:
                    await HandleReadyForUploadAsync(pendingJob, dbContext, stoppingToken);
                    break;
                default:
                    _logger.LogWarning("Unknown or unhandled JobStatus for JobId: {Id}", pendingJob.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 4. Poison Message Handling
            _logger.LogError(ex, "Error processing JobId: {Id} in State: {Status}", pendingJob.Id, pendingJob.Status);

            // Mark as Failed so the queue does not infinite-loop on a broken record
            pendingJob.Status = JobStatus.Failed;
            pendingJob.ErrorMessage = ex.Message;
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
    // --- State Handlers (Currently Mocked for Logic Verification) ---

    private async Task HandlePendingLLMAsync(VideoJob job, AsmrDbContext dbContext, CancellationToken token)
    {
        _logger.LogInformation("Executing API Call -> Gemini for JobId: {Id} | Seed: {SeedIdea}", job.Id, job.SeedIdea);
        // 1. Call the Live API
        var metadata = await _geminiClient.GenerateVideoMetadataAsync(job.SeedIdea, token);

        // 2. Map the DTO to the Entity
        job.GeneratedPrompt = metadata.Prompt;
        job.Title = metadata.Title;
        job.Description = metadata.Description;
        job.Tags = metadata.Tags;
        
    // 3. Advance the State Machine
        job.Status = JobStatus.PendingRender;
        await dbContext.SaveChangesAsync(token);
        
        _logger.LogInformation("Successfully generated metadata for JobId: {Id}. Ready for Veo 3.", job.Id);
        }
    private async Task HandlePendingRenderAsync(VideoJob job, AsmrDbContext dbContext, CancellationToken token)
    {
        _logger.LogInformation("Executing API Call -> Veo 3 for JobId: {Id}", job.Id);
        // TODO: Await IVeoClient POST call here

        job.VeoJobId = $"veo-job-{Guid.NewGuid()}";
        job.Status = JobStatus.Rendering;

        await dbContext.SaveChangesAsync(token);
    }

    private async Task HandleRenderingAsync(VideoJob job, AsmrDbContext dbContext, CancellationToken token)
    {
        _logger.LogInformation("Polling API -> Veo 3 Status for JobId: {Id}", job.Id);
        // TODO: Await IVeoClient GET status call here. If complete, download to blob.

        // Mocking immediate completion
        job.VideoBlobUrl = $"/local/path/to/{job.VeoJobId}.mp4";
        job.Status = JobStatus.ReadyForUpload;

        await dbContext.SaveChangesAsync(token);
    }

    private async Task HandleReadyForUploadAsync(VideoJob job, AsmrDbContext dbContext, CancellationToken token)
    {
        _logger.LogInformation("Executing API Call -> YouTube for JobId: {Id}", job.Id);
        // TODO: Await IYouTubePublisher call here

        job.YouTubeVideoId = $"yt-{Guid.NewGuid().ToString().Substring(0, 8)}";
        job.CompletedAt = DateTime.UtcNow;
        job.Status = JobStatus.Published;

        await dbContext.SaveChangesAsync(token);
        _logger.LogInformation("JobId: {Id} successfully completed the pipeline!", job.Id);
    }
}

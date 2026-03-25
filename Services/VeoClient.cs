using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsmrAutomationEngine.Interfaces;

namespace AsmrAutomationEngine.Services;

public class VeoClient : IVeoClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VeoClient> _logger;
    private readonly string _endpoint;
    private readonly string _projectId;

    public VeoClient(HttpClient httpClient, IConfiguration config, ILogger<VeoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        _endpoint = config["ApiSettings:VertexAiEndpoint"] 
            ?? throw new ArgumentNullException("Vertex AI Endpoint missing.");
        _projectId = config["ApiSettings:GcpProjectId"] 
            ?? throw new ArgumentNullException("GCP Project ID missing.");
            
        // Setup Bearer token auth here in production (using Google.Apis.Auth)
        // _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "YOUR_GCP_TOKEN");
    }

    public async Task<string> StartVideoRenderAsync(string prompt, CancellationToken token)
    {
        _logger.LogInformation("Submitting prompt to Veo 3 Vertex API...");

        var payload = new
        {
            instances = new[] { new { prompt = prompt } },
            parameters = new { aspectRatio = "9:16", duration = 5 } // Strict Shorts format
        };

        var requestUri = $"v1/projects/{_projectId}/locations/us-central1/publishers/google/models/veo-3:predict";
        
        var response = await _httpClient.PostAsJsonAsync(requestUri, payload, token);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        
        // Extract the Operation ID from Vertex AI response
        var operationId = responseContent.GetProperty("name").GetString();
        
        if (string.IsNullOrWhiteSpace(operationId))
            throw new InvalidOperationException("Failed to retrieve Operation ID from Veo 3.");

        return operationId;
    }

    public async Task<string?> CheckStatusAndDownloadAsync(string veoJobId, CancellationToken token)
    {
        _logger.LogInformation("Polling Vertex AI for Job Status: {JobId}", veoJobId);
        
        var response = await _httpClient.GetAsync(veoJobId, token);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        var isDone = responseContent.GetProperty("done").GetBoolean();

        if (!isDone)
        {
            _logger.LogInformation("Video {JobId} is still rendering.", veoJobId);
            return null; // Signals Worker to try again next loop
        }

        // 1. Extract Video Base64 or GCS URI
        var videoUri = responseContent.GetProperty("response").GetProperty("videoUri").GetString();
        
        // 2. Download the physical .mp4 file to local disk
        var localPath = $"C:\\YouTubeAutomation\\Outputs\\{Guid.NewGuid()}.mp4";
        // TODO: Implement actual HTTP GET byte stream to save to localPath

        _logger.LogInformation("Video successfully downloaded to {LocalPath}", localPath);
        return localPath;
    }
}
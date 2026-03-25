using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AsmrAutomationEngine.Interfaces;
using AsmrAutomationEngine.Models;

namespace AsmrAutomationEngine.Services;

public class GeminiClient : IGeminiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiClient> _logger;
    private readonly string _apiKey;

    public GeminiClient(HttpClient httpClient, IConfiguration config, ILogger<GeminiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // In Azure, this pulls from Key Vault. Locally, it pulls from appsettings.json.
        _apiKey = config["ApiSettings:GeminiApiKey"] 
            ?? throw new ArgumentNullException("Gemini API Key is missing from configuration.");
            
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    }

    public async Task<VideoMetadataDto> GenerateVideoMetadataAsync(string seedIdea, CancellationToken token)
    {
        _logger.LogInformation("Requesting metadata generation for seed: '{SeedIdea}'", seedIdea);

        // 1. The Strict System Prompt
        var systemInstruction = @"You are an expert AI video prompt engineer and YouTube metadata optimizer. 
            Take the user's seed idea and generate:
            1. A hyper-detailed Veo 3 prompt (focus on physics, lighting, and ASMR sound layers).
            2. A highly clickable YouTube title.
            3. A short, engaging YouTube description.
            4. A comma-separated list of SEO tags.
            You MUST return ONLY a valid JSON object with the exact keys: 'Prompt', 'Title', 'Description', 'Tags'. No markdown formatting.";

        // 2. The Payload Structure (matches Gemini API schema)
        var payload = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = $"{systemInstruction}\n\nSeed Idea: {seedIdea}" } } }
            },
            generationConfig = new
            {
                temperature = 0.7,
                response_mime_type = "application/json" // Forces strict JSON output
            }
        };

        // 3. The API Call
        var requestUri = $"v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsJsonAsync(requestUri, payload, token);

        response.EnsureSuccessStatusCode();

        // 4. Deserialization and Parsing
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        var rawJsonString = responseContent
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(rawJsonString))
            throw new InvalidOperationException("Gemini returned an empty response.");

        var metadata = JsonSerializer.Deserialize<VideoMetadataDto>(rawJsonString, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        return metadata ?? throw new InvalidOperationException("Failed to deserialize Gemini JSON into VideoMetadataDto.");
    }
}
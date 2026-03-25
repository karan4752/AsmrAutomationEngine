using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using AsmrAutomationEngine.Interfaces;

namespace AsmrAutomationEngine.Services;

public class YouTubePublisher : IYouTubePublisher
{
    private readonly ILogger<YouTubePublisher> _logger;
    private readonly string _clientSecretsFilePath;

    public YouTubePublisher(IConfiguration config, ILogger<YouTubePublisher> logger)
    {
        _logger = logger;
        
        // This expects a downloaded OAuth 2.0 Client ID JSON file from Google Cloud Console
        _clientSecretsFilePath = config["ApiSettings:YouTubeClientSecretsFile"] 
            ?? "client_secrets.json"; 
    }

    public async Task<string> UploadVideoAsync(string localVideoPath, string title, string description, string tags, CancellationToken token)
    {
        _logger.LogInformation("Initiating YouTube OAuth 2.0 Authorization...");

        UserCredential credential;
        using (var stream = new FileStream(_clientSecretsFilePath, FileMode.Open, FileAccess.Read))
        {
            // This creates a local SQLite/Folder store for the Refresh Token.
            // On the first run, it will open your browser to log in. 
            // On subsequent headless runs, it uses the saved refresh token silently.
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                new[] { YouTubeService.Scope.YoutubeUpload },
                "user",
                token,
                new Google.Apis.Util.Store.FileDataStore("YouTube.Auth.Store")
            );
        }

        var youtubeService = new YouTubeService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "AsmrAutomationEngine"
        });

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = description,
                Tags = tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                CategoryId = "24" // 24 = Entertainment. 22 = People & Blogs.
            },
            Status = new VideoStatus
            {
                PrivacyStatus = "private", // STRICT ARCHITECTURE RULE: Always upload as private first for safety.
                MadeForKids = false
            }
        };

        _logger.LogInformation("Opening file stream for {LocalPath} (O(1) memory allocation)...", localVideoPath);

        string uploadedVideoId = string.Empty;

        // The using statement guarantees the file lock is released, even if the upload crashes
        using (var fileStream = new FileStream(localVideoPath, FileMode.Open, FileAccess.Read))
        {
            // 1. Configure the Resumable Upload Request
            var insertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
            insertRequest.ChunkSize = ResumableUpload.MinimumChunkSize * 4; // Upload in 1MB chunks

            // 2. Wire up Event Handlers for Observability
            insertRequest.ProgressChanged += progress =>
            {
                if (progress.Status == UploadStatus.Uploading)
                    _logger.LogDebug("{BytesSent} bytes sent...", progress.BytesSent);
                else if (progress.Status == UploadStatus.Failed)
                    _logger.LogError(progress.Exception, "YouTube upload failed.");
            };

            insertRequest.ResponseReceived += responseVideo =>
            {
                uploadedVideoId = responseVideo.Id;
                _logger.LogInformation("Upload SUCCESS! YouTube Video ID: {VideoId}", uploadedVideoId);
            };

            // 3. Execute the Upload
            await insertRequest.UploadAsync(token);
        }

        // 4. Disk Cleanup 
        if (!string.IsNullOrEmpty(uploadedVideoId))
        {
            _logger.LogInformation("Cleaning up local disk space. Deleting {LocalPath}", localVideoPath);
            File.Delete(localVideoPath);
            return uploadedVideoId;
        }

        throw new InvalidOperationException("Upload completed but no Video ID was returned from YouTube.");
    }
}
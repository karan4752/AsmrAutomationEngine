using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AsmrAutomationEngine.Models
{
    public enum JobStatus
    {
        PendingLLM = 0,     // Waiting for Gemini prompt generation
        PendingRender = 1,  // Prompt ready, waiting to be sent to Veo 3
        Rendering = 2,      // Sent to Veo 3, polling for completion
        ReadyForUpload = 3, // Mp4 downloaded, waiting for YouTube push
        Published = 4,      // Successfully on YouTube
        Failed = 5          // Caught an exception, requires manual intervention
    }
    public class VideoJob
    {
        public int Id { get; set; }
        public string SeedIdea { get; set; } = string.Empty;

        // Metadata (Populated by LLM)
        public string? GeneratedPrompt { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Tags { get; set; }

        // Execution State (Populated by Veo/YouTube)
        public string? VeoJobId { get; set; }
        public string? VideoBlobUrl { get; set; }
        public string? YouTubeVideoId { get; set; }

        // State Tracking
        public JobStatus Status { get; set; } = JobStatus.PendingLLM;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; } // Crucial for observability
    }
}
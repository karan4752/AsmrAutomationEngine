using System.Threading;
using System.Threading.Tasks;
using AsmrAutomationEngine.Models;

namespace AsmrAutomationEngine.Interfaces;

public interface IGeminiClient
{
    Task<VideoMetadataDto> GenerateVideoMetadataAsync(string seedIdea, CancellationToken token);
}
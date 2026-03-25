using System.Threading;
using System.Threading.Tasks;

namespace AsmrAutomationEngine.Interfaces;

public interface IVeoClient
{
    // Returns the remote JobId
    Task<string> StartVideoRenderAsync(string prompt, CancellationToken token);
    
    // Returns the local file path to the downloaded .mp4, or null if still rendering
    Task<string?> CheckStatusAndDownloadAsync(string veoJobId, CancellationToken token);
}
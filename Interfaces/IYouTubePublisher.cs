using System.Threading;
using System.Threading.Tasks;

namespace AsmrAutomationEngine.Interfaces;

public interface IYouTubePublisher
{
    // Returns the generated YouTube Video ID
    Task<string> UploadVideoAsync(string localVideoPath, string title, string description, string tags, CancellationToken token);
}
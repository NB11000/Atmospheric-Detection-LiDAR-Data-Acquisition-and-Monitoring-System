using System.Threading.Tasks;

namespace WebAPI.Service;

public interface ISignalRHubPublisher
{
    Task PublishStateChangedAsync(string eventType, string source, string reason, string message);
}

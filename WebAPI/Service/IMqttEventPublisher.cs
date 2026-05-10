using System.Threading.Tasks;

namespace WebAPI.Service;

public interface IMqttEventPublisher
{
    Task PublishStateChangedAsync(string eventType, string source, string reason, string message);
}

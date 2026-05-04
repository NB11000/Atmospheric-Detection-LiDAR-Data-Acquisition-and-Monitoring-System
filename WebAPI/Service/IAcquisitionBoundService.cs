namespace WebAPI.Service
{
    public interface IAcquisitionBoundService
    {
        bool RequiresMqttConnection { get; }
        void Start();
        void Stop();
    }
}

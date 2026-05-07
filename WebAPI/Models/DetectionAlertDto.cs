namespace WebAPI.Models;

public record DetectionAlertDto
{
    public string AlarmType { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public long Timestamp { get; init; }
    public double CH1 { get; init; }
    public double CH2 { get; init; }
}

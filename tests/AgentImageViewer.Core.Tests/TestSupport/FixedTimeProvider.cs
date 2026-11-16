namespace AgentImageViewer.Core.Tests.TestSupport;

public sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => zone;

    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

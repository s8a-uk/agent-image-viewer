using AgentImageViewer.Services;

namespace AgentImageViewer.Tests.Services;

public sealed class NotificationSoundTests
{
    [Fact]
    public void The_generated_tone_is_a_wave_file_the_sound_player_accepts()
    {
        // The constructor loads the tone; SoundPlayer throws for a header it cannot parse.
        using var sound = new NotificationSound();
    }
}

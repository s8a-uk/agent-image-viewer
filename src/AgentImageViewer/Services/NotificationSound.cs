using System.IO;
using System.Media;
using AgentImageViewer.Core.Audio;

namespace AgentImageViewer.Services;

/// <summary>Plays <see cref="NotificationTone"/> when a card arrives.</summary>
public sealed class NotificationSound : IDisposable
{
    private readonly MemoryStream _wave = new(NotificationTone.CreateWave());
    private readonly SoundPlayer _player;

    public NotificationSound()
    {
        _player = new SoundPlayer(_wave);
        _player.Load();
    }

    public void Play()
    {
        _wave.Position = 0;
        _player.Play();
    }

    public void Dispose()
    {
        _player.Dispose();
        _wave.Dispose();
    }
}

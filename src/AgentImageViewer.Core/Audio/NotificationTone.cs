namespace AgentImageViewer.Core.Audio;

/// <summary>A short, quiet double "pip-pip" for when a card arrives. Generated in memory, so no asset is needed.</summary>
public static class NotificationTone
{
    private const int SampleRate = 44100;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;
    private const short PcmFormat = 1;
    private const int HeaderSize = 44;
    private const double FrequencyHz = 2000;
    private const double PipSec = 0.05;
    private const double GapSec = 0.05;
    private const int PipCount = 2;
    private const double Volume = 0.12;
    private const double FadeSec = 0.008;

    /// <summary>Returns a WAV file (16-bit mono PCM): sine pips with short fades (no clicks) separated by silence.</summary>
    public static byte[] CreateWave()
    {
        var pip = (int)(SampleRate * PipSec);
        var gap = (int)(SampleRate * GapSec);
        var samples = pip * PipCount + gap * (PipCount - 1);
        var fade = (int)(SampleRate * FadeSec);
        var dataSize = samples * BytesPerSample * Channels;

        using var stream = new MemoryStream(HeaderSize + dataSize);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(HeaderSize - 8 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(PcmFormat);
        writer.Write(Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * BytesPerSample * Channels);
        writer.Write((short)(BytesPerSample * Channels));
        writer.Write(BitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);

        for (var i = 0; i < samples; i++)
        {
            var position = i % (pip + gap);
            var value = 0.0;
            if (position < pip)
            {
                var envelope = Math.Min(1.0, Math.Min(position, pip - 1 - position) / (double)fade);
                value = Math.Sin(2 * Math.PI * FrequencyHz * position / SampleRate) * Volume * envelope;
            }

            writer.Write((short)(value * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}

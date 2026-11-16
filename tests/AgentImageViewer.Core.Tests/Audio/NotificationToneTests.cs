using System.Text;
using AgentImageViewer.Core.Audio;

namespace AgentImageViewer.Core.Tests.Audio;

public sealed class NotificationToneTests
{
    private const int HeaderSize = 44;
    private const int SampleRate = 44100;

    private readonly byte[] _wave = NotificationTone.CreateWave();

    [Fact]
    public void The_header_describes_16_bit_mono_pcm()
    {
        using var reader = new BinaryReader(new MemoryStream(_wave));

        Assert.Equal("RIFF", Tag(reader));
        Assert.Equal(_wave.Length - 8, reader.ReadInt32());
        Assert.Equal("WAVE", Tag(reader));
        Assert.Equal("fmt ", Tag(reader));
        Assert.Equal(16, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt16());
        Assert.Equal(1, reader.ReadInt16());
        Assert.Equal(SampleRate, reader.ReadInt32());
        Assert.Equal(SampleRate * 2, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt16());
        Assert.Equal(16, reader.ReadInt16());
        Assert.Equal("data", Tag(reader));
        Assert.Equal(_wave.Length - HeaderSize, reader.ReadInt32());
        Assert.Equal(HeaderSize, reader.BaseStream.Position);
    }

    [Fact]
    public void The_tone_is_two_pips_with_a_gap_between_them()
    {
        var samples = Samples();
        var section = SampleRate / 20;

        Assert.Equal(3 * section, samples.Length);
        Assert.Contains(samples[..section], s => s != 0);
        Assert.All(samples[section..(2 * section)], s => Assert.Equal(0, s));
        Assert.Contains(samples[(2 * section)..], s => s != 0);
        Assert.Equal(samples[..section], samples[(2 * section)..]);
    }

    [Fact]
    public void The_tone_is_quiet()
    {
        var peak = Samples().Max(s => Math.Abs((int)s));

        Assert.InRange(peak, short.MaxValue * 0.05, short.MaxValue * 0.12);
    }

    [Fact]
    public void Each_pip_fades_in_and_out_so_it_does_not_click()
    {
        var samples = Samples();
        var section = SampleRate / 20;

        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[section - 1]);
    }

    [Fact]
    public void The_tone_is_the_same_every_time()
    {
        Assert.Equal(_wave, NotificationTone.CreateWave());
    }

    private short[] Samples()
    {
        var samples = new short[(_wave.Length - HeaderSize) / 2];
        Buffer.BlockCopy(_wave, HeaderSize, samples, 0, samples.Length * 2);
        return samples;
    }

    private static string Tag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}

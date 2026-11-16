using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentImageViewer.Core.Ipc;

/// <summary>Names and wire format shared by the resident viewer and its command-line client.</summary>
public static class PipeProtocol
{
    public const string OkResponse = "ok";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Per user and per logon session, like <see cref="MutexName"/>: a user signed in twice (say, at the console and over
    /// Remote Desktop) runs one viewer in each session, and each session's command line must reach its own.
    /// </summary>
    public static string PipeName => $"AgentImageViewer-{Environment.UserName}-{SessionId}";

    /// <summary>Name of the mutex the resident viewer holds for its whole lifetime.</summary>
    public static string MutexName => $@"Local\AgentImageViewer-{Environment.UserName}";

    private static int SessionId
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return process.SessionId;
        }
    }

    /// <summary>Serializes a message to the single line of JSON sent over the pipe.</summary>
    public static string Serialize(ViewerMessage message) => JsonSerializer.Serialize(message, JsonOptions);

    /// <exception cref="JsonException">The line is not a valid message.</exception>
    public static ViewerMessage? Deserialize(string line) => JsonSerializer.Deserialize<ViewerMessage>(line, JsonOptions);
}

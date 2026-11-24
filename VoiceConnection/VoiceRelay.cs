namespace VoiceConnection;

public sealed record VoiceRelay(string Host, int Port, string Username, string Password)
{
    private static VoiceRelay? configured;
    public static void Configure(VoiceRelay? relay)
    {
        if (relay != null && (string.IsNullOrWhiteSpace(relay.Host) || relay.Port is < 1 or > 65535 || string.IsNullOrEmpty(relay.Username) || string.IsNullOrEmpty(relay.Password)))
            throw new ArgumentException("TURN host, port, username and password are required.", nameof(relay));
        Volatile.Write(ref configured, relay);
    }

    public static VoiceRelay? Default()
    {
        if (Volatile.Read(ref configured) is { } current) return current;
        var host = Environment.GetEnvironmentVariable("WATCHTORRENT_TURN_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            var username = Environment.GetEnvironmentVariable("WATCHTORRENT_TURN_USERNAME") ?? throw new InvalidOperationException("TURN username is missing.");
            var password = Environment.GetEnvironmentVariable("WATCHTORRENT_TURN_PASSWORD") ?? throw new InvalidOperationException("TURN password is missing.");
            int port = int.TryParse(Environment.GetEnvironmentVariable("WATCHTORRENT_TURN_PORT"), out var value) ? value : 3478;
            if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid TURN port.");
            return new(host, port, username, password);
        }
        return null;
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Observatory.Core;

namespace Observatory.Agents;

public sealed class AgentTransportAccess
{
    public const string HeaderName = "X-Observatory-A2A-Key";
    public const string SecuritySchemeName = "observatory-a2a-key";
    private readonly string? _sharedSecret;
    private readonly byte[]? _secretHash;

    public bool AllowRemote { get; }

    public AgentTransportAccess(IConfiguration configuration)
    {
        AllowRemote = configuration.GetValue<bool>("Agents:AllowRemote");
        if (!AllowRemote) return;
        var secret = configuration["Agents:SharedSecret"];
        if (secret is null || secret.Length is < 32 or > 256 || secret.Any(character => character is < '!' or > '~'))
            throw new DomainException("agent_transport_secret_required",
                "Agents:AllowRemote richiede Agents:SharedSecret: 32-256 caratteri ASCII visibili, senza spazi. Nessun fallback anonimo.");
        _sharedSecret = secret;
        _secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https")
            || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0
            || endpoint.AbsolutePath != "/"
            || _sharedSecret is not null && endpoint.AbsoluteUri.Contains(_sharedSecret, StringComparison.Ordinal))
            throw new DomainException("a2a_invalid_endpoint", "Agents:Endpoints:{role} deve essere un'origine HTTP(S), senza credenziali, percorsi, query o fragment.");
        if (!endpoint.IsLoopback && !AllowRemote)
            throw new DomainException("a2a_loopback_required", "Endpoint non-loopback bloccato: richiede Agents:AllowRemote=true e un segreto valido.");
    }

    public bool IsAuthorized(IPAddress? remoteAddress, string? suppliedKey)
    {
        if (!AllowRemote)
        {
            if (remoteAddress?.IsIPv4MappedToIPv6 == true) remoteAddress = remoteAddress.MapToIPv4();
            return remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
        }
        if (suppliedKey is null || suppliedKey.Length is < 32 or > 256) return false;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, _secretHash!);
    }

    public void AuthenticateClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(HeaderName);
        if (AllowRemote) client.DefaultRequestHeaders.Add(HeaderName, _sharedSecret);
    }

    public HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = timeout ?? TimeSpan.FromMinutes(3) };
        AuthenticateClient(client);
        return client;
    }

    public string Redact(string? text) => SafeTelemetry.Text(_sharedSecret is null
        ? text : text?.Replace(_sharedSecret, "[REDACTED]", StringComparison.Ordinal));
}

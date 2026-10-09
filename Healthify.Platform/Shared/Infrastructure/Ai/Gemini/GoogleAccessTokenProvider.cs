using Google.Apis.Auth.OAuth2;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Gemini;

/// <summary>OAuth access token for Vertex AI. Only used when <c>Ai:Gemini:Mode = VertexAi</c>.</summary>
public interface IGoogleAccessTokenProvider
{
    /// <exception cref="InvalidOperationException">No Application Default Credentials are available.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Application Default Credentials (<c>GOOGLE_APPLICATION_CREDENTIALS</c>, <c>gcloud auth application-default
///     login</c>, or the service account of the machine on Google Cloud). Nothing is read until the first Vertex call,
///     so the API starts without credentials while AI is off. The library caches and refreshes the token.
/// </summary>
public sealed class GoogleApplicationDefaultTokenProvider : IGoogleAccessTokenProvider
{
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITokenAccess? _credential;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credential = _credential;
        if (credential is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                credential = _credential ??=
                    (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken))
                    .CreateScoped(CloudPlatformScope);
            }
            finally
            {
                _gate.Release();
            }
        }

        return await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}

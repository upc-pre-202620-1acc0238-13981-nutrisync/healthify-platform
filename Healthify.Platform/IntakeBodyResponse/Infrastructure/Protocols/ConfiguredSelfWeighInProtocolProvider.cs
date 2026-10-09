using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Protocols;

/// <summary>
///     IN-3. Reads <c>Intake:SelfWeighInProtocol</c> (a list of Fasted, SameTimeOfDay, SameScale) and falls back
///     to <see cref="SelfWeighInProtocol.Default" /> (fasted only) when the key is missing or invalid.
/// </summary>
/// <remarks>
///     An invalid list is not half applied: a protocol that silently dropped a condition would let readings
///     into the trend that the configuration meant to keep out, so the whole list falls back with a warning.
/// </remarks>
public class ConfiguredSelfWeighInProtocolProvider(
    IConfiguration configuration,
    ILogger<ConfiguredSelfWeighInProtocolProvider> logger) : ISelfWeighInProtocolProvider
{
    private const string Key = "Intake:SelfWeighInProtocol";

    public SelfWeighInProtocol Current
    {
        get
        {
            var configured = configuration.GetSection(Key).GetChildren()
                .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
            if (configured.Count == 0) return SelfWeighInProtocol.Default;

            try
            {
                return new SelfWeighInProtocol(configured);
            }
            catch (ArgumentException)
            {
                logger.LogWarning("Ignoring {Key}: it holds an unknown condition. Using the default protocol.",
                    Key);
                return SelfWeighInProtocol.Default;
            }
        }
    }
}

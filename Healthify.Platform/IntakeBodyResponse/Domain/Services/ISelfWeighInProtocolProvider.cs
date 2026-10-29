using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Services;

/// <summary>
///     The protocol a self weigh-in must follow to smooth the trend, as configured.
/// </summary>
/// <remarks>
///     Business rule: Only Protocol Compliant Weigh Ins Smooth The Trend (Subflow 4.5). The aggregate applies
///     the protocol; this only says which one is in force, so the questions removed from it can come back
///     without a code change.
/// </remarks>
public interface ISelfWeighInProtocolProvider
{
    /// <summary>The protocol in force. Never null: <see cref="SelfWeighInProtocol.Default" /> when unset.</summary>
    SelfWeighInProtocol Current { get; }
}

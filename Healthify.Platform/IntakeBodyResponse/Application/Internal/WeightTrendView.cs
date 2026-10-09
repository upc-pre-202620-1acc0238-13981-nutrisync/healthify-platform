using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>IN-5. The trend together with the summary of its last weeks, as the chart (PT13) reads it.</summary>
/// <param name="Trend">The whole smoothed series.</param>
/// <param name="Range">Slope, change and excluded readings count over the last weeks.</param>
public record WeightTrendView(WeightTrend Trend, WeightTrendRange Range);

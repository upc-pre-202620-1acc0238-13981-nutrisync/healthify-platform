using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>Self weigh-ins as EF would load them (identity assigned), for the IN-3/IN-4/IN-5 tests.</summary>
public static class WeighIns
{
    public static SelfWeighIn Reading(int id, int patientId, DateOnly day, decimal kg, bool fasted = true,
        bool? sameTimeOfDay = null, bool? sameScale = null)
    {
        var moment = new DateTimeOffset(day.ToDateTime(new TimeOnly(7, 30)), TimeSpan.FromHours(-5));
        var reading = new SelfWeighIn(patientId, new WeightKg(kg), new LocalTimestamp(moment),
            new ProtocolCompliance(fasted, sameTimeOfDay, sameScale));
        return Identity.Assign(reading, new SelfWeighInId(id));
    }
}

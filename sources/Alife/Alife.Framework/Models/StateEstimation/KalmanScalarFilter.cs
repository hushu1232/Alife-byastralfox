using System;

namespace Alife.Framework.Models.StateEstimation;

public sealed record KalmanScalarFilter(double Value, double Uncertainty)
{
    const double MinimumNoise = 0.000001;

    public KalmanScalarFilter Predict(double processNoise)
    {
        return new KalmanScalarFilter(
            Clamp01(Value),
            ClampUncertainty(Uncertainty + Math.Max(MinimumNoise, processNoise)));
    }

    public KalmanScalarFilter Update(double observation, double observationNoise)
    {
        double priorValue = Clamp01(Value);
        double priorUncertainty = ClampUncertainty(Uncertainty);
        double safeObservationNoise = Math.Max(MinimumNoise, observationNoise);
        double gain = priorUncertainty / (priorUncertainty + safeObservationNoise);
        double updatedValue = priorValue + gain * (Clamp01(observation) - priorValue);
        double updatedUncertainty = (1.0 - gain) * priorUncertainty;

        return new KalmanScalarFilter(
            Clamp01(updatedValue),
            ClampUncertainty(updatedUncertainty));
    }

    static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;

        return Math.Clamp(value, 0.0, 1.0);
    }

    static double ClampUncertainty(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 1.0;

        return Math.Clamp(Math.Max(MinimumNoise, value), MinimumNoise, 1.0);
    }
}

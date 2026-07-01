using Alife.Framework.Models.StateEstimation;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class KalmanScalarFilterTests
{
    [Test]
    public void UpdateMovesValueTowardObservationWithoutJumpingAllTheWay()
    {
        KalmanScalarFilter filter = new(0.20, 0.50);

        KalmanScalarFilter updated = filter.Predict(0.05).Update(0.90, 0.20);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Value, Is.GreaterThan(0.20));
            Assert.That(updated.Value, Is.LessThan(0.90));
            Assert.That(updated.Uncertainty, Is.GreaterThan(0.0));
            Assert.That(updated.Uncertainty, Is.LessThan(0.55));
        });
    }

    [Test]
    public void HighObservationNoiseTrustsThePriorMoreThanTheObservation()
    {
        KalmanScalarFilter filter = new(0.30, 0.20);

        KalmanScalarFilter updated = filter.Predict(0.02).Update(1.00, 2.00);

        Assert.That(updated.Value, Is.LessThan(0.45));
    }

    [Test]
    public void ValuesAndUncertaintyStayInValidRange()
    {
        KalmanScalarFilter filter = new(-3.00, -1.00);

        KalmanScalarFilter updated = filter.Predict(-4.00).Update(8.00, -2.00);

        Assert.Multiple(() =>
        {
            Assert.That(updated.Value, Is.InRange(0.0, 1.0));
            Assert.That(updated.Uncertainty, Is.GreaterThan(0.0));
            Assert.That(updated.Uncertainty, Is.LessThanOrEqualTo(1.0));
        });
    }
}

using Xunit;
using LertWheelbarrows;

public class LogisticsRulesTests
{
    [Theory]
    [InlineData(5,2,10,2)] [InlineData(0,2,10,0)] [InlineData(10,0,100,100)]
    [InlineData(-1,2,10,0)] [InlineData(100,2,3,3)] [InlineData(5,2,0,0)]
    public void CapacityNeverOverloadsOrDividesByZero(float free,float mass,int count,int expected) => Assert.Equal(expected,LogisticsRules.MaxUnits(free,mass,count));

    [Theory]
    [InlineData(2,CartKind.Wood,1.6)] [InlineData(2,CartKind.Steel,2)] [InlineData(2,CartKind.Plasteel,2)]
    [InlineData(8.74,CartKind.Wood,6.992)] [InlineData(8.74,CartKind.Steel,4.6)] [InlineData(8.74,CartKind.Plasteel,8.74)]
    public void SpeedPreservesInjuryAndAppliesMaterial(float speed,CartKind kind,float expected) => Assert.Equal(expected,LogisticsRules.Speed(speed,kind),3);

    [Fact] public void DeliveryUsesPathCost() => Assert.Equal(2,LogisticsRules.NearestDestination(new[]{new DestinationScore(1,80),new DestinationScore(2,20)}));
    [Fact] public void ParkingUsesPriorityBeforeDistance() => Assert.Equal(2,LogisticsRules.BestParking(new[]{new ParkingScore(1,20,2),new ParkingScore(2,80,3)}));
    [Fact] public void NoDestinationMeansReturn() => Assert.Null(LogisticsRules.NearestDestination(Array.Empty<DestinationScore>()));
    [Fact] public void EqualRoutesAreDeterministic() => Assert.Equal(1,LogisticsRules.NearestDestination(new[]{new DestinationScore(2,20),new DestinationScore(1,20)}));
}

using System;
using System.Collections.Generic;
using System.Linq;
namespace LertWheelbarrows
{
    public enum CartKind { Wood, Steel, Plasteel }
    public struct DestinationScore { public int Id; public float Cost; public DestinationScore(int id,float cost){Id=id;Cost=cost;} }
    public struct ParkingScore { public int Id; public float Cost; public int Priority; public ParkingScore(int id,float cost,int priority){Id=id;Cost=cost;Priority=priority;} }
    public static class LogisticsRules
    {
        public static int MaxUnits(float freeKg,float unitKg,int available)
        {
            if (available<=0 || freeKg<0 || float.IsNaN(freeKg) || float.IsNaN(unitKg)) return 0;
            if (unitKg<=0) return available;
            return (int)Math.Min(available,Math.Floor((freeKg+0.00001f)/unitKg));
        }
        public static float Speed(float nativeSpeed,CartKind kind) => kind==CartKind.Wood ? nativeSpeed*0.8f : kind==CartKind.Steel ? Math.Min(nativeSpeed,4.6f) : nativeSpeed;
        public static int? NearestDestination(IEnumerable<DestinationScore> candidates) => candidates.OrderBy(c=>c.Cost).ThenBy(c=>c.Id).Select(c=>(int?)c.Id).FirstOrDefault();
        public static int? BestParking(IEnumerable<ParkingScore> candidates) => candidates.OrderByDescending(c=>c.Priority).ThenBy(c=>c.Cost).ThenBy(c=>c.Id).Select(c=>(int?)c.Id).FirstOrDefault();
    }
}

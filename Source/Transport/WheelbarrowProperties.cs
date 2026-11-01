using Verse;
using RimWorld;
namespace LertWheelbarrows
{
    public class WheelbarrowProperties : DefModExtension
    {
        public CartKind kind;
        public float bonusKg;
    }
    [DefOf]
    public static class WBDefOf
    {
        public static JobDef LERT_WB_Transport;
        static WBDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WBDefOf)); }
    }
}

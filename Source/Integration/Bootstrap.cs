using HarmonyLib;
using Verse;
namespace LertWheelbarrows
{
    public class WheelbarrowsMod : Mod
    {
        public WheelbarrowsMod(ModContentPack content):base(content)
        {
            var harmony=new Harmony("lert.wheelbarrows");
            harmony.PatchAll();
            CommonSenseCompatibility.Apply(harmony);
            AllowToolCompatibility.Apply(harmony);
            Log.Message("[LertWheelbarrows] 0.3.0 loaded (RimWorld 1.6).");
        }
    }
}

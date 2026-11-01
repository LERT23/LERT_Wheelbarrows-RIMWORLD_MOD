using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace LertWheelbarrows
{
    [HarmonyPatch(typeof(Selector),"SelectableObjectsUnderMouse")]
    public static class CartSelection
    {
        public static IEnumerable<object> Postfix(IEnumerable<object> values)
        {
            var portrait=Find.ColonistBar.ColonistOrCorpseAt(UI.MousePositionOnUIInverted);
            if(Find.CurrentMap!=null && !(portrait?.SpawnedOrAnyParentSpawned??false) && UI.MouseCell().InBounds(Find.CurrentMap))
            {
                Vector3 mouse=UI.MouseMapPosition();
                foreach(var cart in Find.CurrentMap.GetComponent<MapComponent_Wheelbarrows>().ActiveCarts)
                    if(cart.User?.Spawned==true && (cart.DrawPos-mouse).MagnitudeHorizontal()<0.45f)yield return cart;
            }
            foreach(var item in values)yield return item;
        }
    }
    [HarmonyPatch(typeof(Selector),nameof(Selector.SelectableObjectsAt))]
    public static class CartSelectionAtCell
    {
        public static IEnumerable<object> Postfix(IEnumerable<object> values,IntVec3 c,Map map)
        {
            foreach(var cart in map.GetComponent<MapComponent_Wheelbarrows>().ActiveCarts)
                if(cart.User?.Spawned==true && (cart.DrawPos.ToIntVec3()==c || cart.User.Position==c))yield return cart;
            foreach(var item in values)yield return item;
        }
    }
}

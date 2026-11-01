using UnityEngine;
using RimWorld;
using Verse;
namespace LertWheelbarrows
{
    // Keep the owner's original top sprite; side sprites are authored facing east.
    public class Graphic_Wheelbarrow : Graphic_Single
    {
        private Material side,fullTop,fullSide;
        public override void Init(GraphicRequest req)
        {
            base.Init(req);
            side=GraphicDatabase.Get<Graphic_Single>(req.path+"_Side",req.shader,req.drawSize,req.color,req.colorTwo).MatSingle;
            string fullPath=req.path.Replace("Things/LertWheelbarrows/","Things/LertWheelbarrowsfull/");
            fullTop=GraphicDatabase.Get<Graphic_Single>(fullPath+"_full",req.shader,req.drawSize,req.color,req.colorTwo).MatSingle;
            fullSide=GraphicDatabase.Get<Graphic_Single>(fullPath+"_Side_full",req.shader,req.drawSize,req.color,req.colorTwo).MatSingle;
        }
        public override Material MatAt(Rot4 rot,Thing thing=null)
        {
            bool loaded=thing is Thing_Wheelbarrow cart && cart.Cargo.Any;
            return rot.IsHorizontal?(loaded?fullSide:side):(loaded?fullTop:mat);
        }
        public override Material MatSingleFor(Thing thing)=>MatAt(Rot4.North,thing);
        public override Material MatEast=>side;
        public override Material MatWest=>side;
        public override Mesh MeshAt(Rot4 rot)=>rot==Rot4.West?MeshPool.GridPlaneFlip(drawSize):MeshPool.GridPlane(drawSize);
        public override void DrawWorker(Vector3 loc,Rot4 rot,ThingDef def,Thing thing,float extraRotation)
        {
            var angle=rot==Rot4.South?180f:0f;
            DrawMeshInt(MeshAt(rot),loc+DrawOffset(rot),Quaternion.AngleAxis(angle+extraRotation,Vector3.up),MatAt(rot,thing));
        }
        public override void Print(SectionLayer layer,Thing thing,float extraRotation)
        {
            Rot4 rot=thing.Rotation;
            bool flip=rot==Rot4.West;
            Material material=MatAt(rot,thing);
            TryGetTextureAtlasReplacementInfo(material,thing.def.category.ToAtlasGroup(),flip,true,out material,out var uvs,out var tint);
            Printer_Plane.PrintPlane(layer,thing.TrueCenter()+DrawOffset(rot),drawSize,material,(rot==Rot4.South?180f:0f)+extraRotation,flip,uvs,new[]{tint,tint,tint,tint});
        }
        public override void TryInsertIntoAtlas(TextureAtlasGroup group)
        {
            base.TryInsertIntoAtlas(group);
            GlobalTextureAtlasManager.TryInsertStatic(group,(Texture2D)side.mainTexture,null);
            GlobalTextureAtlasManager.TryInsertStatic(group,(Texture2D)fullTop.mainTexture,null);
            GlobalTextureAtlasManager.TryInsertStatic(group,(Texture2D)fullSide.mainTexture,null);
        }
        public override Graphic GetColoredVersion(Shader shader,Color first,Color second)=>GraphicDatabase.Get<Graphic_Wheelbarrow>(path,shader,drawSize,first,second,data);
    }
}

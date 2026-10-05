using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartFinalIslandSetup
    {
        private const string BoatPrefab="Assets/Low Poly Boats & Raft/Low Poly Row Boats/Prefabs/NormalBoat.prefab";
        private const string ContainerPrefab="Assets/FREE Low Poly Shipping Container/Prefabs/Low Poly Shipping Container.prefab";
        private static PropAnchor House(string name,int id,float x,float z,float yaw)
        {return new PropAnchor(name,Houses+"House_"+id+".prefab",x,z,yaw,new Vector3(9,8,8));}
        private static PropAnchor Rock(string name,int id,float x,float z,float yaw,Vector3 size)
        {return new PropAnchor(name,Nature+"Cliff "+id+".prefab",x,z,yaw,size);}
        private static void ComposeEnvironment(Transform environment,Palette p)
        {
            foreach(Transform section in environment)Clear(section);
            Transform terrain=Section("Terrain",environment),water=Section("Water",environment),harbor=Section("Harbor",environment),
                village=Section("Village",environment),beach=Section("Beach",environment),canyon=Section("Canyon",environment),
                cave=Section("Cave",environment),cliff=Section("Cliff",environment),vegetation=Section("Vegetation",environment);
            p.Rock.SetFloat("_Cull",0);p.Cave.SetFloat("_Cull",0);p.Rock.SetFloat("_Smoothness",.08f);
            CoherentTerrain(terrain,p);
            GameObject ocean=Solid("Ocean_Visual",PrimitiveType.Cube,water,new Vector3(0,WaterLevel-.5f,0),new Vector3(1600,1,1600),p.Ocean);
            ocean.layer=LayerMask.NameToLayer("Water");UnityEngine.Object.DestroyImmediate(ocean.GetComponent<Collider>());
            ComposeHarbor(harbor,p);
            // Four west street houses, four east/alley houses, two plaza houses, two south street houses.
            PropAnchor[] villagePlan={
                House("West_1",1,-293,110,90),House("West_2",2,-306,85,90),
                House("West_3",3,-302,58,90),House("West_4",4,-301,30,90),
                House("East_1",5,-195,100,270),House("East_2",6,-199,75,270),
                House("Alley_1",7,-203,48,270),House("Alley_2",1,-205,15,270),
                House("Plaza_1",2,-242,145,180),House("Plaza_2",3,-267,143,180),
                House("South_1",4,-260,-6,0),House("South_2",5,-281,-14,0)};
            PlaceAnchors(villagePlan,village,p);
            StartArch(village,Node(19),p);
            PlacePlaza(village,p);
            PlaceAnchors(new[]{
                Rock("NorthRidge_A",1,70,267,15,new Vector3(32,19,24)),
                Rock("SouthRidge_A",2,55,202,185,new Vector3(36,19,28)),
                Rock("NorthRidge_B",3,-22,275,10,new Vector3(34,18,22)),
                Rock("SouthRidge_B",4,-25,213,170,new Vector3(33,21,25)),
                Rock("NorthRidge_C",5,-101,247,30,new Vector3(28,18,23)),
                Rock("SouthRidge_C",6,-100,191,210,new Vector3(26,16,22)),
                Rock("HairpinOuter",7,-180,180,85,new Vector3(27,20,28)),
                Rock("HairpinInner",1,-125,179,260,new Vector3(24,17,22)),
                Rock("DescentRidge",2,-162,95,15,new Vector3(29,16,25)),
                Rock("CanyonCrown",3,11,177,50,new Vector3(43,23,32))},canyon,p);
            RockGate(canyon,Node(14),p);
            // One lower branch tunnel, ~55m long, 15m internal width and >8m headroom.
            CaveVolume(cave,"LowerCave",branches[2].points.Skip(18).Take(25).ToArray(),9,p);
            PlaceAnchors(new[]{
                Rock("MountainEntranceMass",4,252,176,35,new Vector3(24,21,26)),
                Rock("MountainExitMass",6,168,157,210,new Vector3(29,22,25))},cave,p);
            PlaceAnchors(new[]{
                Rock("InnerCliffSouth",1,270,-50,10,new Vector3(30,21,45)),
                Rock("InnerCliffMiddle",3,276,16,5,new Vector3(25,24,35)),
                Rock("InnerCliffNorth",5,269,76,20,new Vector3(23,23,36))},cliff,p);
            ComposeBridge(cliff,p);
            // Every cluster center AND its local offsets are authored, with no fit-search or scattering.
            Vector2[] palmClusters={
                new Vector2(30,-275),new Vector2(89,-210),new Vector2(135,-268),new Vector2(179,-246),
                new Vector2(211,-157),new Vector2(257,-181),new Vector2(272,-136),new Vector2(-62,-269)};
            Vector2[] offsets={new Vector2(-6,-3),new Vector2(3,0),new Vector2(8,6),new Vector2(-2,9)};
            float[] heights={10,12,9,11},yaws={15,110,215,305};
            for(int i=0;i<palmClusters.Length;i++)for(int j=0;j<offsets.Length;j++)
            {
                Vector2 point=palmClusters[i]+offsets[j];
                PlaceAnchor(new PropAnchor("BeachPalm_"+i+"_"+j,Tropical+"PalmTree_05.prefab",point.x,point.y,yaws[j],new Vector3(6,heights[j],6),false),vegetation,p);
            }
            PlaceAnchors(new[]{
                new PropAnchor("BeachRockLandmark",Tropical+"Rock_01.prefab",225,-238,25,new Vector3(16,8,13)),
                new PropAnchor("ViewpointRock",Tropical+"Rock_01.prefab",290,-161,80,new Vector3(12,6,11)),
                new PropAnchor("BeachPlantWest",Tropical+"Plant_01.prefab",77,-224,0,new Vector3(5,2.5f,5),false),
                new PropAnchor("BeachPlantEast",Tropical+"Plant_01.prefab",205,-168,35,new Vector3(5,2.5f,5),false)},beach,p);
        }
        private static void ComposeHarbor(Transform harbor,Palette p)
        {
            StartArch(harbor,0,p);
            PlaceAnchors(new[]{
                new PropAnchor("Pier_A",Tropical+"Pier_02.prefab",-340,-225,90,new Vector3(8,2,38),false,SurfaceKind.Dock),
                new PropAnchor("Pier_B",Tropical+"Pier_02.prefab",-335,-185,90,new Vector3(8,2,32),false,SurfaceKind.Dock),
                new PropAnchor("Boat_A",BoatPrefab,-362,-240,75,new Vector3(4,2,10),false,SurfaceKind.Water),
                new PropAnchor("Boat_B",BoatPrefab,-365,-214,100,new Vector3(4,2,10),false,SurfaceKind.Water),
                new PropAnchor("Boat_C",BoatPrefab,-354,-175,65,new Vector3(4,2,10),false,SurfaceKind.Water),
                new PropAnchor("Boat_D",BoatPrefab,-374,-190,90,new Vector3(4,2,10),false,SurfaceKind.Water),
                new PropAnchor("Lighthouse",Houses+"Lighthouse.prefab",-350,-110,0,new Vector3(9,27,9)),
                House("HarborHouse_A",3,-274,-145,180),House("HarborHouse_B",4,-250,-125,180),House("HarborHouse_C",5,-225,-108,180),
                new PropAnchor("CargoBarrels_A",Tropical+"Barrel_04.prefab",-268,-203,0,new Vector3(1.4f,2,1.4f)),
                new PropAnchor("CargoBarrels_B",Tropical+"Barrel_04.prefab",-264,-203,0,new Vector3(1.4f,2,1.4f))},harbor,p);
            PropAnchor[] cargo={
                new PropAnchor("CargoStack_A",ContainerPrefab,-250,-190,90,new Vector3(10,3,4)),
                new PropAnchor("CargoStack_B",ContainerPrefab,-235,-187,90,new Vector3(10,3,4)),
                new PropAnchor("CargoStack_C",ContainerPrefab,-250,-172,90,new Vector3(10,3,4))};
            foreach(PropAnchor anchor in cargo)
            {
                GameObject lower=PlaceAnchor(anchor,harbor,p);if(lower==null)continue;
                GameObject upper=UnityEngine.Object.Instantiate(lower,harbor);upper.name=anchor.name+"_Upper";
                Transform foundation=upper.transform.Find("Foundation");if(foundation!=null)UnityEngine.Object.DestroyImmediate(foundation.gameObject);
                upper.transform.position+=Vector3.up*3;
            }
        }
        private static void PlacePlaza(Transform village,Palette p)
        {
            Vector3 center=new Vector3(-254,0,139);
            if(!LandHit(center,out RaycastHit hit)){Debug.LogWarning("Village plaza skipped: no dry land.");return;}
            SafeSolid("PlazaPaving",village,new Vector3(center.x,hit.point.y+.08f,center.z),new Vector3(12,.16f,10),Quaternion.identity,p.Stone);
            PlaceAnchors(new[]{
                new PropAnchor("MarketTable",Houses+"Table.prefab",-254,139,0,new Vector3(3,1,2),false),
                new PropAnchor("MarketParasol",Houses+"Parasol.prefab",-254,139,0,new Vector3(5,4,5),false),
                new PropAnchor("VillageLamp_A",Houses+"Lamppost.prefab",-283,105,0,new Vector3(1,4,1),false),
                new PropAnchor("VillageLamp_B",Houses+"Lamppost.prefab",-290,52,0,new Vector3(1,4,1),false)},village,p);
        }
        private static void RockGate(Transform parent,int node,Palette p)
        {
            Vector3 f=Vector3.ProjectOnPlane(Route[node+1]-Route[node-1],Vector3.up).normalized,r=Vector3.Cross(Vector3.up,f);
            Quaternion yaw=Quaternion.LookRotation(f);
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 basePoint=Route[node]+r*side*11;
                if(LandHit(basePoint,out RaycastHit hit))SafeSolid("RockArchPillar",parent,new Vector3(basePoint.x,hit.point.y+7,basePoint.z),new Vector3(4,14,5),yaw,p.Rock);
            }
            SafeSolid("RockArchLintel",parent,Route[node]+Vector3.up*14,new Vector3(26,4,6),yaw,p.Rock);
        }
        private static void ComposeBridge(Transform cliff,Palette p)
        {
            // Deck is part of MainRoad; all support meshes are strictly below it.
            for(int i=Node(8)+2;i<Node(9)-2;i++)
            {
                Vector3 a=Route[i],b=Route[i+1],f=(b-a).normalized,r=Vector3.Cross(Vector3.up,f).normalized;
                for(int side=-1;side<=1;side+=2)
                    SafeSolid("BridgeGirder",cliff,(a+b)*.5f+r*side*(Widths[i]*.5f-1)-Vector3.up*1.3f,
                        new Vector3(.8f,1.2f,Vector3.Distance(a,b)+.1f),Quaternion.LookRotation(f),p.Wood);
                if(i==Node(8)+3||i==Node(8)+8)for(int side=-1;side<=1;side+=2)
                {
                    Vector3 foot=a+r*side*4;float top=a.y-.8f,bottom=-3.5f;
                    SafeSolid("BridgePile",cliff,new Vector3(foot.x,(top+bottom)*.5f,foot.z),new Vector3(1,top-bottom,1),Quaternion.identity,p.Wood);
                }
            }
        }
        private static void AddTwoSidedQuad(List<int> triangles,int a,int b,int c,int d)
        {
            // Both materials disable culling. Duplicating reversed faces here would cancel vertex normals.
            triangles.AddRange(new[]{a,b,c,c,b,d});
        }

        private static void CaveVolume(Transform parent,string name,Vector3[] path,float width,Palette p)
        {
            // Hollow, closed-roof arch with a second exterior skin and open portal mouths.
            // Side walls remain outside half-width + 2m; crown provides 12m camera clearance.
            float radius=width*.5f+3;
            Vector2[] profile={new Vector2(-radius,-1),new Vector2(-radius,7),new Vector2(-radius*.72f,11),new Vector2(0,13),
                new Vector2(radius*.72f,11),new Vector2(radius,7),new Vector2(radius,-1)};
            var vertices=new List<Vector3>();var inside=new List<int>();var outside=new List<int>();
            for(int i=0;i<path.Length;i++)
            {
                Vector3 f=Vector3.ProjectOnPlane(path[Mathf.Min(path.Length-1,i+1)]-path[Mathf.Max(0,i-1)],Vector3.up).normalized,r=Vector3.Cross(Vector3.up,f);
                foreach(Vector2 q in profile)vertices.Add(path[i]+r*q.x+Vector3.up*q.y);
                foreach(Vector2 q in profile)vertices.Add(path[i]+r*(q.x*1.8f)+Vector3.up*(q.y+7));
            }
            for(int i=0;i<path.Length-1;i++)for(int j=0;j<6;j++)
            {
                int k=i*14+j;AddTwoSidedQuad(inside,k,k+14,k+1,k+15);AddTwoSidedQuad(outside,k+7,k+21,k+8,k+22);
            }
            foreach(int end in new[]{0,path.Length-1})for(int j=0;j<6;j++)
            {int k=end*14+j;AddTwoSidedQuad(outside,k,k+1,k+7,k+8);}
            Mesh mesh=new Mesh{name=name,subMeshCount=2};mesh.SetVertices(vertices);mesh.SetTriangles(inside,0);mesh.SetTriangles(outside,1);mesh.RecalculateNormals();mesh.RecalculateBounds();
            MeshObject(name,parent,SaveMesh(mesh,name),p.Cave,true);parent.Find(name).GetComponent<MeshRenderer>().sharedMaterials=new[]{p.Cave,p.Rock};
            for(int i=4;i<path.Length-3;i+=9)
            {
                Light light=new GameObject(name+"_AmberLamp").AddComponent<Light>();light.transform.SetParent(parent,false);light.transform.position=path[i]+Vector3.up*8;
                light.type=LightType.Point;light.color=new Color(1,.58f,.22f);light.range=25;light.intensity=2.5f;light.shadows=LightShadows.None;
            }
        }


    }
}

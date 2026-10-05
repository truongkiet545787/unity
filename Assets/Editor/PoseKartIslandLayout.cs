using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartFinalIslandSetup
    {
        private const float WaterLevel=-1.2f;
        private static Vector3[] NewIslandRoute()
        {
            // Clockwise: harbor -> beach -> cliff -> cave -> canyon -> village -> harbor.
            return SampleSpline(new[]{
                new Vector3(-295,1,-225),new Vector3(-220,1,-235),new Vector3(-120,1,-245),new Vector3(0,1,-250),
                new Vector3(110,1,-240),new Vector3(190,1.5f,-215),new Vector3(235,2,-170),
                new Vector3(285,8,-110),new Vector3(310,13,-30),new Vector3(300,17,60),
                new Vector3(265,19,120),new Vector3(220,22,175),new Vector3(145,20,210),
                new Vector3(60,21,235),new Vector3(-25,19,245),new Vector3(-110,17,220),
                new Vector3(-155,15,180),new Vector3(-105,13,145),new Vector3(-165,9,120),
                new Vector3(-225,6,120),new Vector3(-275,5.5f,85),new Vector3(-230,5,45),
                new Vector3(-285,4.5f,15),new Vector3(-315,3,-50),new Vector3(-325,2,-125),
                new Vector3(-300,1,-190)},true);
        }
        private static Vector3[] SampleSpline(Vector3[] anchors,bool closed)
        {
            var points=new List<Vector3>();int count=anchors.Length,spans=closed?count:count-1;
            for(int i=0;i<spans;i++)
            {
                Vector3 a=anchors[i],b=anchors[(i+1)%count];
                Vector3 prev=closed?anchors[(i+count-1)%count]:anchors[Mathf.Max(0,i-1)];
                Vector3 next=closed?anchors[(i+2)%count]:anchors[Mathf.Min(count-1,i+2)];
                Vector3 ta=(b-prev).normalized*Mathf.Min(Vector3.Distance(a,prev),Vector3.Distance(a,b))*.85f;
                Vector3 tb=(next-a).normalized*Mathf.Min(Vector3.Distance(a,b),Vector3.Distance(b,next))*.85f;
                if(!closed&&i==0)ta=(b-a)*.75f;
                if(!closed&&i==spans-1)tb=(b-a)*.75f;
                for(int step=0;step<SamplesPerSpan;step++)
                {
                    float t=step/(float)SamplesPerSpan,t2=t*t,t3=t2*t;
                    points.Add((2*t3-3*t2+1)*a+(t3-2*t2+t)*ta+(-2*t3+3*t2)*b+(t3-t2)*tb);
                }
            }
            if(!closed)points.Add(anchors[count-1]);
            return points.ToArray();
        }
        private static float AnchorWidth(int a)
        {
            if(a<=3||a>=23)return 15;
            if(a<=6)return 15;
            if(a<=10)return 12;
            if(a<=12)return 10;
            if(a<=18)return 11;
            return 9.5f;
        }
        private static float RoadWidth(int node)
        {
            int a=node/SamplesPerSpan;
            return Mathf.Lerp(AnchorWidth(a),AnchorWidth((a+1)%26),Mathf.SmoothStep(0,1,(node%SamplesPerSpan)/(float)SamplesPerSpan));
        }
        private static Material RoadMaterial(int node,Palette p)
        {
            int a=node/SamplesPerSpan;
            if(a<2||a>=24||a==8)return p.Wood;
            if(a<7)return p.Dirt;
            if(a<13)return p.Asphalt;
            if(a<19)return p.Dirt;
            if(a<23)return p.Stone;
            return p.Asphalt;
        }
        private static readonly Vector2[] Coast={
            new Vector2(-378,-100),new Vector2(-351,-150),new Vector2(-329,-190),new Vector2(-322,-246),
            new Vector2(-250,-278),new Vector2(-135,-287),new Vector2(-20,-280),new Vector2(105,-295),
            new Vector2(219,-275),new Vector2(287,-215),new Vector2(323,-145),new Vector2(343,-90),
            new Vector2(335,-25),new Vector2(350,50),new Vector2(326,128),new Vector2(280,201),
            new Vector2(200,263),new Vector2(104,283),new Vector2(15,294),new Vector2(-79,279),
            new Vector2(-156,248),new Vector2(-184,201),new Vector2(-250,175),new Vector2(-315,141),
            new Vector2(-333,72),new Vector2(-323,3),new Vector2(-358,-42)};
        private static float CoastDistance(float x,float z)
        {
            Vector2 point=new Vector2(x,z);bool inside=false;float distance=float.PositiveInfinity;
            for(int i=0,j=Coast.Length-1;i<Coast.Length;j=i++)
            {
                Vector2 a=Coast[j],b=Coast[i],d=b-a;
                if((a.y>z)!=(b.y>z)&&x<(b.x-a.x)*(z-a.y)/(b.y-a.y)+a.x)inside=!inside;
                float t=Mathf.Clamp01(Vector2.Dot(point-a,d)/d.sqrMagnitude);
                distance=Mathf.Min(distance,(point-a-d*t).magnitude);
            }
            return inside?distance:-distance;
        }
        private static float Hill(float x,float z,float cx,float cz,float rx,float rz)
        {return Mathf.Exp(-Mathf.Pow((x-cx)/rx,2)-Mathf.Pow((z-cz)/rz,2));}
        private static float LandHeight(float x,float z)
        {
            // Authored overlapping ridges and terraces, no noise or random height/scatter.
            float coast=CoastDistance(x,z);
            float h=1+15*Hill(x,z,-30,198,180,92)+23*Hill(x,z,200,165,86,80);
            h+=12*Hill(x,z,-90,60,115,95)+8*Hill(x,z,270,-5,58,123);
            h+=5*Hill(x,z,-241,87,72,73);
            h=Mathf.Lerp(-5,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-3,20,coast)));
            // The central cove is a narrow branching inlet, not an empty circular lagoon.
            float cove=Mathf.Max(Hill(x,z,100,-58,65,78),Hill(x,z,128,28,36,85));
            h=Mathf.Lerp(h,-3.5f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,.9f,cove)));
            // A shelf supports the fixed village plan, including its plaza.
            float village=Hill(x,z,-252,81,93,87);
            h=Mathf.Lerp(h,5.1f,Mathf.Clamp01((village-.27f)*2)*Mathf.Clamp01(coast/12));
            // Harbor: authored reclaimed cargo/building terrace and lighthouse headland.
            h=Mathf.Lerp(h,1.6f,Mathf.Clamp01((Hill(x,z,-251,-158,67,57)-.15f)*3)*Mathf.Clamp01(coast/10));
            h=Mathf.Lerp(h,2.5f,Mathf.Clamp01((Hill(x,z,-350,-110,16,19)-.15f)*2.5f));
            for(int i=0;i<Route.Length;i++)
            {
                int n=(i+1)%Route.Length;Vector3 a=Route[i],b=Route[n];
                Vector2 d=new Vector2(b.x-a.x,b.z-a.z),q=new Vector2(x-a.x,z-a.z);
                float t=Mathf.Clamp01(Vector2.Dot(q,d)/Mathf.Max(.001f,d.sqrMagnitude)),distance=(q-d*t).magnitude;
                float half=Mathf.Max(Widths[i],Widths[n])*.5f;
                if(distance<half+16)h=Mathf.Lerp(Mathf.Lerp(a.y,b.y,t)-.25f,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(half+4,half+16,distance)));
            }
            h=FitBranchGround(x,z,h);
            // A fixed east-facing tidal cleft under the actual road bridge.
            float cleft=Mathf.Clamp01((x-284)/15)*Mathf.Clamp01((18-Mathf.Abs(z-12))/7);
            return Mathf.Lerp(h,-3.2f,cleft);
        }
        private static void CoherentTerrain(Transform parent,Palette p)
        {
            const int nx=257,nz=201;var vertices=new Vector3[nx*nz];var uv=new Vector2[vertices.Length];
            var groups=new[]{new List<int>(),new List<int>(),new List<int>()};
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                float wx=-384+x*3,wz=-300+z*3;int index=z*nx+x;
                vertices[index]=new Vector3(wx,LandHeight(wx,wz),wz);uv[index]=new Vector2(wx*.04f,wz*.04f);
            }
            for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++)
            {
                int a=z*nx+x,b=a+nx;Vector3 center=(vertices[a]+vertices[b+1])*.5f;
                float rise=Mathf.Abs(vertices[a].y-vertices[b+1].y);
                int material=center.y<1.9f||(center.z<-175&&center.x>0)?0:center.y>15||rise>1.7f?2:1;
                groups[material].AddRange(new[]{a,b,a+1,a+1,b,b+1});
            }
            Mesh mesh=new Mesh{name="AuthoredIsland",vertices=vertices,uv=uv,subMeshCount=3};
            for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();
            MeshObject("AuthoredIsland",parent,SaveMesh(mesh,"UnifiedIsland"),p.Sand,true);
            Transform land=parent.Find("AuthoredIsland");land.gameObject.layer=EnsureTerrainLayer();
            land.GetComponent<MeshRenderer>().sharedMaterials=new[]{p.Sand,p.Grass,p.Rock};
            terrainSurface=land.GetComponent<MeshCollider>();Physics.SyncTransforms();
        }
        private static void ValidateAuthoredRoutes()
        {
            if(Route.Length<3||Widths.Length!=Route.Length)throw new InvalidOperationException("Main road data is invalid.");
            foreach(int cp in CheckpointNodes)if(cp<0||cp>=Route.Length)throw new InvalidOperationException("Checkpoint outside main route.");
            if(branches.Length!=3)throw new InvalidOperationException("Expected exactly three authored branches.");
            foreach(Branch b in branches)
            {
                if(b.points.Length<2||Vector3.Distance(b.points[0],Route[b.start])>.01f||
                    Vector3.Distance(b.points[b.points.Length-1],Route[b.end])>.01f)
                    throw new InvalidOperationException("Invalid junction endpoints: "+b.name);
                if(CheckpointNodes.Any(cp=>cp>=b.start&&cp<=b.end))
                    throw new InvalidOperationException("A mandatory checkpoint lies inside a fork: "+b.name);
            }
        }
        private static void ValidateRoadSurface()
        {
            if(drivingSurface==null||drivingSurface.sharedMesh==null||drivingSurface.sharedMesh.vertexCount<6)
                throw new InvalidOperationException("MainRoad was not generated.");
            Physics.SyncTransforms();
            foreach(int node in CheckpointNodes)
                if(!drivingSurface.Raycast(new Ray(Route[node]+Vector3.up*50,Vector3.down),out RaycastHit hit,100))
                    throw new InvalidOperationException("Checkpoint is not over MainRoad: "+node);
        }
    }
}

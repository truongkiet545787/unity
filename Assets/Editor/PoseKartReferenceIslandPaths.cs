using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceIslandArt
    {
        private sealed class ArtShortcut
        {
            internal string name;
            internal Vector3[] points,edges;
            internal float width;
        }
        private static ArtShortcut[] shortcuts=Array.Empty<ArtShortcut>();
        private static Vector3[] mainEdges;
        private static MeshCollider mainSurface;
        private static Material looseSand,wornSand;

        private static void CreateSurfaceMaterials()
        {
            looseSand=Mat("Loose sand shoulders",new Color(.86f,.67f,.38f),.01f);
            wornSand=Mat("Worn sandy wheel lines",new Color(.72f,.53f,.30f),.01f);
            Texture2D grain=SurfaceTexture("Sand fine grain",false,false),sandNormal=SurfaceTexture("Sand grain normal",true,false);
            foreach(Material material in new[]{road,looseSand,wornSand,sand})
            {
                material.SetTexture("_BaseMap",grain);material.SetTexture("_BumpMap",sandNormal);
                material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",.25f);EditorUtility.SetDirty(material);
            }
            Texture2D stone=SurfaceTexture("Weathered limestone strata",false,true),stoneNormal=SurfaceTexture("Limestone relief normal",true,true);
            foreach(Material material in cliffPalette)
            {
                material.SetTexture("_BaseMap",stone);material.SetTexture("_BumpMap",stoneNormal);
                material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",.5f);material.SetFloat("_Smoothness",.025f);EditorUtility.SetDirty(material);
            }
        }
        private static float Grain(int x,int y,bool stone)
        {
            float broad=Mathf.PerlinNoise(x*.036f+7,y*.036f+13),fine=Mathf.PerlinNoise(x*.42f+21,y*.42f+5);
            if(!stone)return broad*.45f+fine*.55f;
            float strata=Mathf.Abs(Mathf.Sin(y*.19f+broad*1.5f));
            return broad*.55f+fine*.25f+Mathf.SmoothStep(0,1,strata)*.2f;
        }
        private static Texture2D SurfaceTexture(string name,bool normal,bool stone)
        {
            string path=Folder+"/"+name+".asset";Texture2D texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture!=null)return texture;
            const int size=256;texture=new Texture2D(size,size,TextureFormat.RGBA32,true,normal){name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
            var colors=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                if(normal)
                {
                    Vector3 n=new Vector3((Grain(x-1,y,stone)-Grain(x+1,y,stone))*.65f,(Grain(x,y-1,stone)-Grain(x,y+1,stone))*.65f,1).normalized;
                    // Alpha=1 preserves X for both RGB and URP RG/AG normal unpacking.
                    colors[y*size+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
                }
                else
                {
                    float value=Mathf.Lerp(stone ? .65f : .82f,1,Grain(x,y,stone));colors[y*size+x]=new Color(value,value,value,1);
                }
            }
            texture.SetPixels(colors);texture.Apply(true,false);AssetDatabase.CreateAsset(texture,path);return texture;
        }
        private static void CreateShortcuts()
        {
            shortcuts=new[]{
                Shortcut("Village garden alley",40,70,9.5f,new[]{P(137,210,21.28f),P(174,189,26.88f)}),
                Shortcut("Canyon ridge cut",95,130,10f,new[]{P(300,107,39.2f),P(347,95,42.56f)}),
                Shortcut("Beach inner sand trail",215,260,10f,new[]{P(333,372,3.47f),P(272,365,3.02f)})};
        }
        private static ArtShortcut Shortcut(string name,int split,int merge,float width,Vector3[] interior)
        {
            for(int i=0;i<interior.Length;i++)
            {
                Vector3 point=interior[i];point.y=Mathf.Lerp(route[split].y,route[merge].y,(i+1f)/(interior.Length+1f));interior[i]=point;
            }
            var anchors=new List<Vector3>{route[split]};anchors.AddRange(interior);anchors.Add(route[merge]);
            var points=new List<Vector3>();
            for(int i=0;i<anchors.Count-1;i++)
            {
                Vector3 a=anchors[i],b=anchors[i+1],ta=i==0?(route[split+1]-route[split-1]).normalized:(b-anchors[i-1]).normalized;
                Vector3 tb=i==anchors.Count-2?(route[merge+1]-route[merge-1]).normalized:(anchors[i+2]-a).normalized;
                float handle=Vector3.Distance(a,b)*.65f;
                for(int j=0;j<14;j++)
                {
                    float t=j/14f,t2=t*t,t3=t2*t;
                    Vector3 point=(2*t3-3*t2+1)*a+(t3-2*t2+t)*ta*handle+(-2*t3+3*t2)*b+(t3-t2)*tb*handle;
                    point.y=Mathf.Lerp(a.y,b.y,t);
                    points.Add(point);
                }
            }
            points.Add(route[merge]);
            for(int i=0;i<points.Count;i++)points[i]=MatchMainRoadHeight(points[i]);
            var path=new ArtShortcut{name=name,points=points.ToArray(),width=width,edges=new Vector3[points.Count*2]};
            for(int i=0;i<points.Count;i++)
            {
                Vector3 f=points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(0,i-1)];f.y=0;Vector3 r=Vector3.Cross(Vector3.up,f.normalized)*width*.5f;
                path.edges[i*2]=MatchMainRoadHeight(points[i]-r);path.edges[i*2+1]=MatchMainRoadHeight(points[i]+r);
            }
            return path;
        }
        private static Vector3 MatchMainRoadHeight(Vector3 point)
        {
            float nearest=float.PositiveInfinity,height=point.y,half=0;
            for(int i=0;i<route.Length;i++)
            {
                Vector3 a=route[i],b=route[(i+1)%route.Length];Vector2 d=new Vector2(b.x-a.x,b.z-a.z),q=new Vector2(point.x-a.x,point.z-a.z);
                float t=Mathf.Clamp01(Vector2.Dot(q,d)/Mathf.Max(.001f,d.sqrMagnitude));float distance=(q-d*t).magnitude;
                if(distance<nearest){nearest=distance;height=Mathf.Lerp(a.y,b.y,t);half=Width(i)*.5f;}
            }
            point.y=Mathf.Lerp(height,point.y,Mathf.SmoothStep(0,1,Mathf.InverseLerp(half+1,half+12,nearest)));return point;
        }
        internal static bool NearShortcut(Vector3 point)=>ShortcutDistance(new Vector3(point.x/RaceStretch.x,point.y/RaceStretch.y,point.z/RaceStretch.z)/RaceScale)<3f;
        internal static PoseKart.UI.PoseKartMinimap.Path[] MinimapPaths(Vector3[] main)
        {
            var paths=new List<PoseKart.UI.PoseKartMinimap.Path>{new PoseKart.UI.PoseKartMinimap.Path{points=main,closed=true}};
            foreach(ArtShortcut shortcut in shortcuts)paths.Add(new PoseKart.UI.PoseKartMinimap.Path{points=shortcut.points.Select(RacePoint).ToArray(),closed=false});
            return paths.ToArray();
        }
        private static float ShortcutDistance(Vector3 world)
        {
            float nearest=float.PositiveInfinity;
            foreach(ArtShortcut path in shortcuts)for(int i=0;i<path.points.Length-1;i++)
            {
                Vector3 d=path.points[i+1]-path.points[i],q=world-path.points[i];d.y=0;q.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(q,d)/d.sqrMagnitude);
                nearest=Mathf.Min(nearest,(q-d*t).magnitude-path.width*.5f);
            }
            return nearest;
        }
        private static float FitShortcutTerrain(Vector3 world,float height)
        {
            float nearest=float.PositiveInfinity,target=height,half=4;
            foreach(ArtShortcut path in shortcuts)for(int i=0;i<path.points.Length-1;i++)
            {
                Vector3 a=path.points[i],b=path.points[i+1],d=b-a,q=world-a;d.y=0;q.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(q,d)/d.sqrMagnitude),distance=(q-d*t).magnitude;
                if(distance<nearest){nearest=distance;target=Mathf.Lerp(a.y,b.y,t)-.04f;half=path.width*.5f;}
            }
            if(nearest<half+18)
            {
                float fitted=Mathf.Lerp(target,height,Mathf.SmoothStep(0,1,Mathf.InverseLerp(half+3,half+18,nearest)));
                height=MainRoadDistance(world)<1?Mathf.Min(height,fitted):fitted;
            }
            return height;
        }
        private static void BuildShortcutMeshes(Transform track)
        {
            Transform parent=new GameObject("Optional sandy shortcuts").transform.WithParent(track);
            foreach(ArtShortcut path in shortcuts)
            {
                var vertices=new List<Vector3>();var indices=new List<int>();
                for(int i=0;i<path.points.Length-1;i++)
                {
                    AppendOutsideMain(path.edges[i*2],path.edges[i*2+2],path.edges[i*2+1],vertices,indices);
                    AppendOutsideMain(path.edges[i*2+1],path.edges[i*2+2],path.edges[i*2+3],vertices,indices);
                }
                Mesh mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
                mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x,v.z)*.125f).ToList());mesh.RecalculateNormals();
                MeshObject(path.name,parent,mesh,new[]{road},true);
                BuildPathShoulder(parent,path.points,path.edges,false);
                AddWornTracks(parent,path.points,false,1.6f);
            }
        }
        // Clip the main-road footprint out of each branch. The fork is a union, with no stacked road colliders.
        private static void AppendOutsideMain(Vector3 a,Vector3 b,Vector3 c,List<Vector3> vertices,List<int> indices)
        {
            var fragments=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
            for(int i=0;i<route.Length&&fragments.Count>0;i++)
            {
                int n=(i+1)%route.Length;Vector3[] clip={mainEdges[i*2],mainEdges[n*2],mainEdges[n*2+1],mainEdges[i*2+1]};
                var remaining=new List<List<Vector3>>();float winding=Mathf.Sign(Cross2(clip[1]-clip[0],clip[2]-clip[1]));
                if(winding==0)continue;
                foreach(List<Vector3> fragment in fragments)
                {
                    List<Vector3> inside=fragment;
                    for(int edge=0;edge<4&&inside.Count>=3;edge++)
                    {
                        Vector3 p=clip[edge],d=clip[(edge+1)%4]-p;
                        List<Vector3> outside=HalfPlane(inside,p,d,winding,false);if(outside.Count>=3)remaining.Add(outside);
                        inside=HalfPlane(inside,p,d,winding,true);
                    }
                }
                fragments=remaining;
            }
            foreach(List<Vector3> polygon in fragments)
            {
                int first=vertices.Count;
                foreach(Vector3 value in polygon)
                {
                    Vector3 v=MatchMainRoadHeight(value);if(mainSurface.Raycast(new Ray(new Vector3(v.x,100,v.z),Vector3.down),out RaycastHit hit,200))v.y=hit.point.y;
                    vertices.Add(v);
                }
                for(int i=1;i<polygon.Count-1;i++)if(Vector3.Cross(vertices[first+i]-vertices[first],vertices[first+i+1]-vertices[first]).sqrMagnitude>.000001f)
                    indices.AddRange(new[]{first,first+i,first+i+1});
            }
        }
        private static float Cross2(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
        private static List<Vector3> HalfPlane(List<Vector3> polygon,Vector3 a,Vector3 d,float winding,bool positive)
        {
            var result=new List<Vector3>();
            for(int i=0;i<polygon.Count;i++)
            {
                Vector3 p=polygon[i],q=polygon[(i+1)%polygon.Count];float dp=Cross2(d,p-a)*winding,dq=Cross2(d,q-a)*winding;
                bool ip=positive?dp>=0:dp<=0,iq=positive?dq>=0:dq<=0;
                if(ip)result.Add(p);if(ip!=iq)result.Add(Vector3.LerpUnclamped(p,q,dp/(dp-dq)));
            }
            return result;
        }
        private static void BuildSoftShoulders(Transform parent)=>BuildPathShoulder(parent,route,mainEdges,true);
        private static void BuildPathShoulder(Transform parent,Vector3[] points,Vector3[] edges,bool closed)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<points.Length-(closed?0:1);i++)
            {
                int n=(i+1)%points.Length;if(closed&&i>=182&&i<204)continue;
                for(int side=0;side<2;side++)
                {
                    Vector3 a=edges[i*2+side],b=edges[n*2+side],outA=a-points[i],outB=b-points[n];outA.y=outB.y=0;
                    Vector3 c=a+outA.normalized*(1.1f+.3f*Mathf.Sin(i*.7f)),d=b+outB.normalized*(1.1f+.3f*Mathf.Sin(n*.7f));
                    Vector3 mid=(a+b+c+d)*.25f;
                    if(closed?ShortcutDistance(mid)<.2f:MainRoadDistance(mid)<.2f)continue;
                    c.y=Height(c)+.035f;d.y=Height(d)+.035f;a.y-=.02f;b.y-=.02f;int k=vertices.Count;
                    vertices.AddRange(new[]{a,b,c,d});triangles.AddRange(side==0?new[]{k,k+2,k+1,k+1,k+2,k+3}:new[]{k,k+1,k+2,k+1,k+3,k+2});
                }
            }
            Mesh mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x,v.z)*.125f).ToList());mesh.RecalculateNormals();
            MeshObject("Irregular soft sand shoulders",parent,mesh,new[]{looseSand});
        }
        private static float MainRoadDistance(Vector3 p)
        {
            float result=float.PositiveInfinity;
            for(int i=0;i<route.Length;i++)
            {
                Vector3 d=route[(i+1)%route.Length]-route[i],q=p-route[i];d.y=0;q.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(q,d)/d.sqrMagnitude);result=Mathf.Min(result,(q-d*t).magnitude-Width(i)*.5f);
            }
            return result;
        }
        private static void AddWornTracks(Transform parent,Vector3[] points,bool closed,float offset)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            for(int i=1;i<points.Length-2;i++)
            {
                if(i%9>5||(closed&&i>=180&&i<=205)||(!closed&&(i<6||i>points.Length-8)))continue;
                Vector3 a=points[i],b=points[i+1],f=b-a;f.y=0;Vector3 r=Vector3.Cross(Vector3.up,f.normalized);
                foreach(int side in new[]{-1,1})
                {
                    float width=.22f+.08f*Mathf.Sin(i*.8f);Vector3 p=a+r*(side*offset+.1f*Mathf.Sin(i*.35f))+Vector3.up*.018f;
                    Vector3 q=b+r*(side*offset+.1f*Mathf.Sin((i+1)*.35f))+Vector3.up*.018f;
                    if(!closed&&MainRoadDistance((p+q)*.5f)<.5f)continue;
                    int k=vertices.Count;vertices.AddRange(new[]{p-r*width,p+r*width,q-r*width,q+r*width});indices.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});
                }
            }
            Mesh mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.SetUVs(0,vertices.Select(p=>new Vector2(p.x,p.z)*.125f).ToList());mesh.RecalculateNormals();
            MeshObject("Soft interrupted wheel wear",parent,mesh,new[]{wornSand});
        }
    }
}

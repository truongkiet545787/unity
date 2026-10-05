using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceRoadRepair
    {
        private const float CellSize=16f;
        private struct RoadFace
        {
            public Vector3 a,b,c;
            public float winding;
            public bool HeightAt(Vector3 p,out float y)
            {
                float area=CrossXZ(b-a,c-a);
                float u=CrossXZ(p-a,c-a)/area,v=CrossXZ(b-a,p-a)/area;
                y=a.y+u*(b.y-a.y)+v*(c.y-a.y);
                return u>=-.0001f&&v>=-.0001f&&u+v<=1.0001f;
            }
        }
        private static float CrossXZ(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
        private static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));

        private static void CutTerrainUnderRoads(Mesh terrainMesh,Transform transform,MeshCollider[] roads)
        {
            var faces=new List<RoadFace>();var bins=new Dictionary<Vector2Int,List<int>>();
            foreach(MeshCollider road in roads)
            {
                Vector3[] v=road.sharedMesh.vertices;int[] t=road.sharedMesh.triangles;
                for(int i=0;i<t.Length;i+=3)
                {
                    var f=new RoadFace{a=road.transform.TransformPoint(v[t[i]]),b=road.transform.TransformPoint(v[t[i+1]]),c=road.transform.TransformPoint(v[t[i+2]])};
                    float area=CrossXZ(f.b-f.a,f.c-f.a);if(Mathf.Abs(area)<.00001f)continue;
                    f.winding=Mathf.Sign(area);int id=faces.Count;faces.Add(f);
                    Vector2Int lo=Cell(Vector3.Min(f.a,Vector3.Min(f.b,f.c))),hi=Cell(Vector3.Max(f.a,Vector3.Max(f.b,f.c)));
                    for(int z=lo.y;z<=hi.y;z++)for(int x=lo.x;x<=hi.x;x++)
                    {
                        var key=new Vector2Int(x,z);
                        if(!bins.TryGetValue(key,out List<int> bucket)){bucket=new List<int>();bins.Add(key,bucket);}
                        bucket.Add(id);
                    }
                }
            }
            Vector3[] source=terrainMesh.vertices;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();
            var submeshes=new List<int>[terrainMesh.subMeshCount];
            for(int sub=0;sub<submeshes.Length;sub++)
            {
                submeshes[sub]=new List<int>();int[] indices=terrainMesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    Vector3 a=transform.TransformPoint(source[indices[i]]),b=transform.TransformPoint(source[indices[i+1]]),c=transform.TransformPoint(source[indices[i+2]]);
                    Vector2Int lo=Cell(Vector3.Min(a,Vector3.Min(b,c))),hi=Cell(Vector3.Max(a,Vector3.Max(b,c)));
                    var candidates=new HashSet<int>();
                    for(int z=lo.y;z<=hi.y;z++)for(int x=lo.x;x<=hi.x;x++)
                        if(bins.TryGetValue(new Vector2Int(x,z),out List<int> bucket))foreach(int id in bucket)candidates.Add(id);
                    var fragments=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
                    foreach(int id in candidates.OrderBy(value=>value))
                    {
                        RoadFace face=faces[id];
                        // Preserve terrain far below a bridge; only carve the road bed.
                        if(Mathf.Max(a.y,Mathf.Max(b.y,c.y))<Mathf.Min(face.a.y,Mathf.Min(face.b.y,face.c.y))-2f)continue;
                        var remaining=new List<List<Vector3>>();
                        foreach(List<Vector3> polygon in fragments)
                        {
                            List<Vector3> inside=polygon;
                            Vector3[] clip={face.a,face.b,face.c};
                            for(int edge=0;edge<3&&inside.Count>=3;edge++)
                            {
                                Vector3 p=clip[edge],d=clip[(edge+1)%3]-p;
                                List<Vector3> outside=CutHalf(inside,p,d,face.winding,false);
                                if(outside.Count>=3)remaining.Add(outside);
                                inside=CutHalf(inside,p,d,face.winding,true);
                            }
                        }
                        fragments=remaining;if(fragments.Count==0)break;
                    }
                    foreach(List<Vector3> polygon in fragments)
                    {
                        int start=vertices.Count;
                        foreach(Vector3 point in polygon)
                        {
                            Vector3 world=point;
                            // Match newly created boundary vertices to the road lip.
                            foreach(int id in candidates)
                                if(faces[id].HeightAt(world,out float height)&&Mathf.Abs(world.y-height)<2f)
                                    world.y=Mathf.Min(world.y,height-.035f);
                            Vector3 local=transform.InverseTransformPoint(world);
                            vertices.Add(local);uv.Add(new Vector2(local.x,local.z)*.125f);
                        }
                        for(int k=1;k<polygon.Count-1;k++)
                            if(Vector3.Cross(vertices[start+k]-vertices[start],vertices[start+k+1]-vertices[start]).sqrMagnitude>.00000001f)
                                submeshes[sub].AddRange(new[]{start,start+k,start+k+1});
                    }
                }
            }
            terrainMesh.Clear();terrainMesh.indexFormat=IndexFormat.UInt32;
            terrainMesh.SetVertices(vertices);terrainMesh.SetUVs(0,uv);terrainMesh.subMeshCount=submeshes.Length;
            for(int i=0;i<submeshes.Length;i++)terrainMesh.SetTriangles(submeshes[i],i);
        }

        private static List<Vector3> CutHalf(List<Vector3> polygon,Vector3 a,Vector3 d,float winding,bool positive)
        {
            var result=new List<Vector3>();
            for(int i=0;i<polygon.Count;i++)
            {
                Vector3 p=polygon[i],q=polygon[(i+1)%polygon.Count];
                float dp=CrossXZ(d,p-a)*winding,dq=CrossXZ(d,q-a)*winding;
                bool ip=positive?dp>=0:dp<=0,iq=positive?dq>=0:dq<=0;
                if(ip)result.Add(p);
                if(ip!=iq)result.Add(Vector3.LerpUnclamped(p,q,dp/(dp-dq)));
            }
            return result;
        }
    }
}

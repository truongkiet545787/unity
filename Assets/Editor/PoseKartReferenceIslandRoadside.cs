using System.Collections.Generic;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceIslandArt
    {
        private static void Boulder(Transform parent,Vector3 bottom,Vector3 size,float yaw)
        {
            if (ImportedRock(parent, bottom, size, yaw)) return;
            // Subdivided, asymmetrically weathered stone, replacing the repeated straight seven-sided columns.
            float phi=(1+Mathf.Sqrt(5))*.5f;
            var points=new List<Vector3>{new Vector3(-1,phi,0),new Vector3(1,phi,0),new Vector3(-1,-phi,0),new Vector3(1,-phi,0),
                new Vector3(0,-1,phi),new Vector3(0,1,phi),new Vector3(0,-1,-phi),new Vector3(0,1,-phi),new Vector3(phi,0,-1),new Vector3(phi,0,1),new Vector3(-phi,0,-1),new Vector3(-phi,0,1)};
            for(int i=0;i<points.Count;i++)points[i]=points[i].normalized;
            int[] baseFaces={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
                3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
            var faces=new List<int>();var midpoints=new Dictionary<long,int>();
            for(int i=0;i<baseFaces.Length;i+=3)
            {
                int a=baseFaces[i],b=baseFaces[i+1],c=baseFaces[i+2];
                int ab=RockMidpoint(a,b,points,midpoints),bc=RockMidpoint(b,c,points,midpoints),ca=RockMidpoint(c,a,points,midpoints);
                faces.AddRange(new[]{a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});
            }
            float seed=yaw*.073f+bottom.x*.17f+bottom.z*.11f;
            for(int i=0;i<points.Count;i++)
            {
                Vector3 n=points[i];float h=(n.y+1)*.5f;
                float erosion=.9f+.09f*Mathf.Sin(n.x*7+n.z*5+seed)+.05f*Mathf.Sin(n.y*11-seed);
                float lean=(h-.35f)*.14f;
                Vector3 p=new Vector3((n.x*erosion*.5f+lean)*size.x,h*size.y,(n.z*erosion*.5f-lean*.6f)*size.z);
                p.x=Mathf.Clamp(p.x,-size.x*.47f,size.x*.49f);
                p.z=Mathf.Clamp(p.z,-size.z*.48f,size.z*.46f);
                // Inclined bedding and chipped flat faces give each boulder a geological direction.
                float cap=size.y*(.94f+.045f*n.x-.025f*n.z);
                p.y=Mathf.Min(p.y,cap)+Mathf.Sin(h*12+seed)*size.y*.022f;
                points[i]=p;
            }
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var groups=new[]{new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>()};
            for(int i=0;i<faces.Count;i+=3)
            {
                Vector3 a=points[faces[i]],b=points[faces[i+1]],c=points[faces[i+2]],normal=Vector3.Cross(b-a,c-a).normalized;
                float level=(a.y+b.y+c.y)/(3*size.y);
                int material=normal.y>.5f?(level>.68f?4:3):normal.z<-.35f?1:level<.28f?2:0;
                int k=vertices.Count;vertices.AddRange(new[]{a,b,c});groups[material].AddRange(new[]{k,k+1,k+2});
                foreach(Vector3 p in new[]{a,b,c})uv.Add(new Vector2(Mathf.Abs(normal.x)>.65f?p.z:p.x,p.y)*.19f);
            }
            Mesh mesh=new Mesh{subMeshCount=5};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            for(int g=0;g<5;g++)mesh.SetTriangles(groups[g],g);mesh.RecalculateNormals();mesh.RecalculateBounds();
            GameObject go=MeshObject("Weathered layered limestone",parent,mesh,cliffPalette.ToArray());go.transform.SetPositionAndRotation(bottom,Quaternion.Euler(0,yaw,0));
        }
        private static int RockMidpoint(int a,int b,List<Vector3> vertices,Dictionary<long,int> cache)
        {
            long key=((long)Mathf.Min(a,b)<<32)+(uint)Mathf.Max(a,b);
            if(cache.TryGetValue(key,out int found))return found;
            int index=vertices.Count;vertices.Add((vertices[a]+vertices[b]).normalized);cache[key]=index;return index;
        }
        private static void BuildRoadsideScenery()
        {
            Transform parent=Group("Roadside • canyon walls ferns palms fences and fork signs");
            GameObject palmTemplate=CreatePalm(parent),shrubTemplate=CreateShrub(parent);
            // Fixed stations serve the driver's view: low foreground plants, middle-distance rocks,
            // then taller formations behind. No position-search or density quota is used.
            int[] stations={8,16,24,32,39,47,55,63,71,79,87,95,103,111,119,127,135,143,174,181,207,214,222,230,238,246,254,262,270};
            foreach(int node in stations)
            {
                Vector3 f=route[(node+1)%route.Length]-route[(node+route.Length-1)%route.Length];f.y=0;f.Normalize();Vector3 right=Vector3.Cross(Vector3.up,f);
                foreach(int side in new[]{-1,1})
                {
                    Vector3 near=route[node]+right*side*(Width(node)*.5f+2.8f);
                    PlaceRoadsidePlant(shrubTemplate,parent,near,.9f+(node%4)*.13f,node*21+side*25);
                    PlaceRoadsidePlant(shrubTemplate,parent,near+f*3.1f,1.1f,node*13);
                    Vector3 mid=route[node]+right*side*(Width(node)*.5f+7.5f);
                    bool canyon=node>=80&&node<=144;
                    if(canyon)
                    {
                        if(node%3==0)PlaceRoadRock(parent,mid,new Vector3(4,2.2f,4),node*19+side*41);
                        PlaceRoadsidePlant(palmTemplate,parent,mid,1.1f,node*31);
                        PlaceRoadsidePlant(shrubTemplate,parent,mid+f*4,1.7f,node*19);
                    }
                    else
                    {
                        if(node%3==0)PlaceRoadRock(parent,mid,new Vector3(5,4+node%4,4),node*23);
                        PlaceRoadsidePlant(palmTemplate,parent,mid+f*3,1+node%3*.12f,node*33+side*17);
                    }
                    // Loose stones remain beyond the usable shoulder, not on the racing surface.
                    PlaceRoadRock(parent,near-f*2,new Vector3(.8f,.55f,1),node*9);
                    PlaceRoadRock(parent,near+f*1.3f+right*side,new Vector3(1.2f,.8f,.9f),node*15);
                }
                if(node<80||node>207)RoadsideFence(parent,node,right,f);
            }
            foreach(ArtShortcut path in shortcuts)
            {
                for(int i=7;i<path.points.Length-6;i+=9)
                {
                    Vector3 f=path.points[i+1]-path.points[i-1];f.y=0;Vector3 right=Vector3.Cross(Vector3.up,f.normalized);
                    foreach(int side in new[]{-1,1})
                    {
                        Vector3 pos=path.points[i]+right*side*(path.width*.5f+2.4f);
                        PlaceRoadsidePlant(shrubTemplate,parent,pos,1.15f,i*37);
                        if(path.name.Contains("Canyon"))PlaceRoadRock(parent,pos+right*side*5,new Vector3(6,13,7),i*29);
                    }
                }
                foreach(int end in new[]{5,path.points.Length-6})ForkSign(parent,path,end);
            }
            UnityEngine.Object.DestroyImmediate(palmTemplate);UnityEngine.Object.DestroyImmediate(shrubTemplate);
        }
        private static void PlaceRoadRock(Transform parent,Vector3 requested,Vector3 size,float yaw)
        {
            Vector2 image=ImagePoint(requested);
            float radius=new Vector2(size.x,size.z).magnitude*.5f;
            if(RoadDistance(requested)<radius+.85f||!Ground(image.x,image.y,out Vector3 point))return;
            // Embed the foot to avoid floating stones on a sloping bank.
            Boulder(parent,point-Vector3.up*Mathf.Min(2,size.y*.12f),size,yaw);
        }
        private static void PlaceRoadsidePlant(GameObject template,Transform parent,Vector3 requested,float scale,float yaw)
        {
            Vector2 image=ImagePoint(requested);if(RoadDistance(requested)<2||!Ground(image.x,image.y,out Vector3 point))return;
            GameObject plant=UnityEngine.Object.Instantiate(template,parent);plant.name=template.name.Contains("Palm")?"Roadside palm":"Roadside fern";
            plant.transform.SetPositionAndRotation(point,Quaternion.Euler(0,yaw,0));plant.transform.localScale=Vector3.one*scale;plant.SetActive(true);
        }
        private static GameObject CreateShrub(Transform parent)
        {
            Transform shrub=new GameObject("Fern geometry template").transform.WithParent(parent);
            var vertices=new List<Vector3>();var light=new List<int>();var dark=new List<int>();
            for(int leaf=0;leaf<9;leaf++)
            {
                float angle=leaf*Mathf.PI*2/9;Vector3 f=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),r=Vector3.Cross(Vector3.up,f);
                float height=.9f+(leaf%3)*.25f;
                for(int segment=0;segment<4;segment++)
                {
                    float a=segment/4f,b=(segment+1)/4f;
                    Vector3 p=f*a*1.2f+Vector3.up*(Mathf.Sin(a*Mathf.PI*.85f)*height),q=f*b*1.2f+Vector3.up*(Mathf.Sin(b*Mathf.PI*.85f)*height);
                    float wa=Mathf.Sin(a*Mathf.PI)*.28f,wb=Mathf.Sin(b*Mathf.PI)*.28f;int k=vertices.Count;
                    vertices.AddRange(new[]{p-r*wa,p+Vector3.up*.06f,p+r*wa,q-r*wb,q+Vector3.up*.06f,q+r*wb});
                    light.AddRange(new[]{k,k+3,k+1,k+1,k+3,k+4});dark.AddRange(new[]{k+1,k+4,k+2,k+2,k+4,k+5});
                }
            }
            Mesh mesh=new Mesh{subMeshCount=2};mesh.SetVertices(vertices);mesh.SetTriangles(light,0);mesh.SetTriangles(dark,1);mesh.RecalculateNormals();
            MeshObject("Broad tropical fern leaves",shrub,mesh,new[]{grassLight,grass});shrub.gameObject.SetActive(false);return shrub.gameObject;
        }
        private static void RoadsideFence(Transform parent,int node,Vector3 right,Vector3 forward)
        {
            int side=node%2==0?1:-1;Vector3 center=route[node]+right*side*(Width(node)*.5f+2.1f);
            Vector3 previous=Vector3.zero;bool previousValid=false;
            for(int i=0;i<4;i++)
            {
                Vector3 anchor=center+forward*(i-1.5f)*2.6f;Vector2 image=ImagePoint(anchor);
                if(RoadDistance(anchor)<1.5f||!Ground(image.x,image.y,out Vector3 pos)){previousValid=false;continue;}
                Cube("Fence post",parent,pos+Vector3.up*.65f,new Vector3(.18f,1.5f,.18f),wood);
                if(previousValid)
                {
                    Vector3 mid=(previous+pos)*.5f;
                    if(RoadDistance(mid)>1.5f){Beam(parent,previous+Vector3.up*.65f,pos+Vector3.up*.65f,.12f,woodLight);Beam(parent,previous+Vector3.up*1.2f,pos+Vector3.up*1.2f,.12f,woodLight);}
                }
                previous=pos;previousValid=true;
            }
        }
        private static void ForkSign(Transform parent,ArtShortcut path,int index)
        {
            Vector3 f=path.points[index+1]-path.points[index-1];f.y=0;f.Normalize();Vector3 r=Vector3.Cross(Vector3.up,f);
            Vector3 requested=path.points[index]+r*(path.width*.5f+2.4f);Vector2 image=ImagePoint(requested);
            if(RoadDistance(requested)<1.7f||!Ground(image.x,image.y,out Vector3 point))return;
            Cube(path.name+" signpost",parent,point+Vector3.up*1.1f,new Vector3(.16f,2.3f,.16f),wood);
            Quaternion facing=Quaternion.LookRotation(-f);
            Cube("Optional path wooden sign",parent,point+Vector3.up*2,new Vector3(2,.7f,.18f),woodLight,facing);
            Vector3 center=point+Vector3.up*2-f*.11f;
            Vector3[] vertices={center-r*.62f-Vector3.up*.09f,center+r*.25f-Vector3.up*.09f,center+r*.25f-Vector3.up*.24f,
                center+r*.7f,center+r*.25f+Vector3.up*.24f,center+r*.25f+Vector3.up*.09f,center-r*.62f+Vector3.up*.09f};
            Mesh mesh=new Mesh{vertices=vertices,triangles=new[]{0,1,5,0,5,6,2,3,4}};mesh.RecalculateNormals();MeshObject("Ivory shortcut arrow",parent,mesh,new[]{white});
        }
    }
}

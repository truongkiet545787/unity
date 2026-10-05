using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceIslandArt
    {
        private static bool Ground(float imageX,float imageY,out Vector3 position)
        {
            position=P(imageX,imageY);
            if(terrain!=null&&terrain.Raycast(new Ray(position+Vector3.up*220,Vector3.down),out RaycastHit hit,300)&&hit.point.y>-.45f)
            {position.y=hit.point.y;return true;}return false;
        }
        private static float RoadDistance(Vector3 position)
        {
            float distance=float.PositiveInfinity;
            for(int i=0;i<route.Length;i++)
            {
                Vector3 a=route[i],d=route[(i+1)%route.Length]-a,q=position-a;d.y=0;q.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(q,d)/d.sqrMagnitude);
                distance=Mathf.Min(distance,(q-d*t).magnitude-Width(i)*.5f);
            }
            return Mathf.Min(distance,ShortcutDistance(position));
        }
        private static void BuildHarbor()
        {
            Transform harbor=Group("Harbor • boardwalk boats and lighthouse");
            Dock(harbor,P(115,402,1.5f),P(55,448,1.5f),8);
            Dock(harbor,P(159,405,1.5f),P(164,480,1.5f),6);
            Dock(harbor,P(201,407,1.5f),P(208,449,1.5f),5);
            Dock(harbor,P(73,312,4),P(31,347,1.5f),6);
            Dock(harbor,P(156,417,1.5f),P(204,421,1.5f),5);
            House(harbor,153,390,100,7,6,6);House(harbor,188,398,0,6,5,5);House(harbor,93,361,35,5,4,5);
            Boat(harbor,70,460,-35);Boat(harbor,126,479,-15);Boat(harbor,219,458,5);Boat(harbor,29,375,40);
            Lighthouse(harbor,83,227);
            foreach(Vector2 point in new[]{new Vector2(132,413),new Vector2(140,417),new Vector2(172,417),new Vector2(188,420)})
            {
                Vector3 p=P(point.x,point.y,2.1f);Cube("Cargo crate",harbor,p,new Vector3(1.5f,1.5f,1.5f),woodLight);
                Cube("Crate strap",harbor,p+Vector3.up*.03f,new Vector3(1.58f,.22f,1.58f),wood);
            }
            Vector3 finish=route[0],f=(route[1]-route[route.Length-1]).normalized,r=Vector3.Cross(Vector3.up,f);
            foreach(int side in new[]{-1,1})Cube("Start arch post",harbor,finish+r*side*6+Vector3.up*4,new Vector3(.5f,8,.5f),wood);
            Cube("Start arch beam",harbor,finish+Vector3.up*8,new Vector3(12.5f,.65f,.65f),woodLight,Quaternion.LookRotation(f));
            Pennants(harbor,finish-r*5.7f+Vector3.up*7.5f,finish+r*5.7f+Vector3.up*7.5f);
        }
        private static void Dock(Transform parent,Vector3 a,Vector3 b,float width)
        {
            Vector3 f=(b-a).normalized,r=Vector3.Cross(Vector3.up,f).normalized;int count=Mathf.CeilToInt(Vector3.Distance(a,b)/.8f);
            for(int i=0;i<count;i++)
            {
                Vector3 p=Vector3.Lerp(a,b,(i+.5f)/count);
                Cube("Boardwalk plank",parent,p,new Vector3(width,.24f,Vector3.Distance(a,b)/count-.045f),i%4==0?woodLight:wood,Quaternion.LookRotation(f));
                if(i%5==0)foreach(int side in new[]{-1,1})
                {
                    Vector3 foot=p+r*(width*.5f-.15f)*side;
                    Cube("Dock timber pile",parent,new Vector3(foot.x,(foot.y-3)*.5f,foot.z),new Vector3(.36f,foot.y+3,.36f),wood);
                    Cube("Mooring bollard",parent,foot+Vector3.up*.5f,new Vector3(.4f,1,.4f),woodLight);
                }
            }
        }
        private static void Boat(Transform parent,float x,float y,float yaw)
        {
            // Built as a low-poly hull in a Y-up wrapper; its keel is below the fixed sea level.
            Transform boat=new GameObject("Moored fishing boat").transform.WithParent(parent);boat.position=P(x,y,-1.15f);boat.rotation=Quaternion.Euler(0,yaw,0);
            Vector3[] v={new Vector3(-1.3f,.65f,-3),new Vector3(1.3f,.65f,-3),new Vector3(1.5f,.65f,1.5f),new Vector3(0,.65f,4),new Vector3(-1.5f,.65f,1.5f),
                new Vector3(-.65f,-.35f,-2.6f),new Vector3(.65f,-.35f,-2.6f),new Vector3(.7f,-.35f,1.1f),new Vector3(0,-.15f,3),new Vector3(-.7f,-.35f,1.1f)};
            var t=new List<int>();for(int i=0;i<5;i++){int n=(i+1)%5;t.AddRange(new[]{i,n,5+i,n,5+n,5+i});}t.AddRange(new[]{5,9,6,6,9,7,7,9,8});
            Mesh mesh=new Mesh{vertices=v,triangles=t.ToArray()};mesh.RecalculateNormals();MeshObject("Carved boat hull",boat,mesh,new[]{cream});
            for(int i=0;i<3;i++){GameObject seat=Cube("Boat seat",boat,Vector3.zero,new Vector3(2.3f,.18f,.6f),wood);seat.transform.localPosition=new Vector3(0,.3f,-1.8f+i*1.4f);seat.transform.localRotation=Quaternion.identity;}
            GameObject mast=Cube("Short mast",boat,Vector3.zero,new Vector3(.14f,4,.14f),woodLight);mast.transform.localPosition=new Vector3(0,2,0);
            Mesh sail=new Mesh{vertices=new[]{new Vector3(.08f,.7f,0),new Vector3(.08f,3.8f,0),new Vector3(2,.7f,0)},triangles=new[]{0,1,2}};sail.RecalculateNormals();MeshObject("Furled ivory sail",boat,sail,new[]{white});
        }
        private static void BuildVillage()
        {
            Transform village=Group("Village • terracotta terraces and market square");
            // Authored streets flank the west road, stepping up towards the canyon.
            float[,] houses={{90,201,30,6,6,6},{116,194,-10,7,7,7},{141,195,-20,6,5,6},{166,195,25,7,6,6},
                {128,247,160,7,6,6},{154,248,170,6,7,6},{176,241,120,5,5,5},{120,165,15,6,6,6},
                {150,161,-15,7,6,7},{176,169,25,6,7,6},{179,140,-10,6,6,6},{227,184,90,6,5,6},
                {226,158,75,5,6,5},{241,224,20,6,6,6},{260,215,-15,5,5,5},{151,280,15,5,5,5}};
            for(int i=0;i<houses.GetLength(0);i++)House(village,houses[i,0],houses[i,1],houses[i,2],houses[i,3],houses[i,4],houses[i,5]);
            if(Ground(151,179,out Vector3 plaza))
            {
                Cube("Market square",village,plaza+Vector3.up*.08f,new Vector3(12,.16f,9),sand);
                for(int i=0;i<3;i++)
                {
                    Vector3 p=plaza+new Vector3(-4+i*4,0,0);Cube("Market counter",village,p+Vector3.up*.8f,new Vector3(2.6f,1.6f,1.4f),woodLight);
                    foreach(int side in new[]{-1,1})Cube("Awning post",village,p+new Vector3(side*1.3f,1.6f,0),new Vector3(.13f,3.2f,.13f),wood);
                    for(int stripe=0;stripe<6;stripe++)Cube("Striped market awning",village,p+new Vector3((stripe-2.5f)*.5f,3.1f,0),new Vector3(.5f,.12f,2.7f),(stripe+i)%2==0?white:red);
                }
            }
            if(Ground(180,202,out Vector3 a)&&Ground(147,203,out Vector3 b))Pennants(village,a+Vector3.up*6,b+Vector3.up*6);
        }
        private static void House(Transform parent,float x,float y,float yaw,float width,float height,float depth)
        {
            if(!Ground(x,y,out Vector3 ground))return;
            if(RoadDistance(ground)<Mathf.Max(width,depth)*.55f+.5f)return;
            Transform house=new GameObject("Terracotta house "+x+" "+y).transform.WithParent(parent);house.position=ground;house.rotation=Quaternion.Euler(0,yaw,0);
            // Grounded support remains part of the architecture, not a plinth beneath every plant.
            LocalCube("Stone foundation",house,new Vector3(0,-.3f,0),new Vector3(width+.2f,.8f,depth+.2f),rock);
            LocalCube("Warm stucco walls",house,new Vector3(0,height*.5f,0),new Vector3(width,height,depth),cream);
            float e=.6f,w=width*.5f+e,d=depth*.5f+e,rise=2.3f;
            Vector3[] v={new Vector3(-w,height,-d),new Vector3(w,height,-d),new Vector3(0,height+rise,-d),new Vector3(-w,height,d),new Vector3(w,height,d),new Vector3(0,height+rise,d)};
            Mesh roofMesh=new Mesh{vertices=v,triangles=new[]{0,2,1,3,4,5,0,3,2,2,3,5,2,5,1,1,5,4}};roofMesh.RecalculateNormals();MeshObject("Pitched terracotta roof",house,roofMesh,new[]{roof});
            for(int row=0;row<4;row++)
            {
                float t=(row+1)/5f;foreach(int side in new[]{-1,1})LocalCube("Roof tile course",house,new Vector3(side*w*t,height+rise*(1-t)+.035f,0),new Vector3(.08f,.09f,depth+1.25f),red);
            }
            LocalCube("Chimney",house,new Vector3(-width*.26f,height+1.2f,depth*.24f),new Vector3(.7f,2,.75f),cream);
            LocalCube("Front door",house,new Vector3(0,1.25f,-depth*.5f-.04f),new Vector3(1.1f,2.5f,.15f),wood);
            foreach(int side in new[]{-1,1})
            {
                Vector3 p=new Vector3(side*width*.3f,height*.55f,-depth*.5f-.1f);
                LocalCube("Window surround",house,p,new Vector3(1.6f,1.7f,.15f),white);
                LocalCube("Blue shutter",house,p+Vector3.back*.1f,new Vector3(1.25f,1.35f,.12f),ocean);
                LocalCube("Window mullion",house,p+Vector3.back*.18f,new Vector3(.10f,1.45f,.10f),woodLight);
                LocalCube("Window sill",house,p+new Vector3(0,-.87f,-.15f),new Vector3(1.8f,.18f,.4f),wood);
            }
        }
        private static void LocalCube(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {GameObject go=Cube(name,parent,Vector3.zero,size,material);go.transform.localPosition=position;go.transform.localRotation=Quaternion.identity;}
        private static void Lighthouse(Transform parent,float x,float y)
        {
            if(!Ground(x,y,out Vector3 ground))return;
            Transform tower=new GameObject("Red and ivory lighthouse").transform.WithParent(parent);tower.position=ground;
            for(int band=0;band<7;band++)
            {
                GameObject cylinder=GameObject.CreatePrimitive(PrimitiveType.Cylinder);cylinder.name="Lighthouse band";cylinder.transform.SetParent(tower,false);
                cylinder.transform.localPosition=Vector3.up*(band*2+1);cylinder.transform.localScale=new Vector3(4.8f-band*.22f,1,4.8f-band*.22f);
                cylinder.GetComponent<Renderer>().sharedMaterial=band%2==0?white:red;UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            }
            LocalCube("Lantern housing",tower,new Vector3(0,15,0),new Vector3(3,2.1f,3),glow);
            LocalCube("Lantern roof",tower,new Vector3(0,16.3f,0),new Vector3(4.2f,.5f,4.2f),red);
        }
        private static void BuildCliffsAndCave()
        {
            Transform cliffs=Group("Cliffs • layered lagoon escarpments");
            // Rock buttresses are fitted to the concave shore. They form the exposed sides of the upland.
            Vector2 center=new Vector2(292,291);
            for(int i=0;i<Bay.Length;i++)
            {
                Vector2 a=Bay[i],b=Bay[(i+1)%Bay.Length];int pieces=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/11));
                for(int j=0;j<pieces;j++)
                {
                    Vector2 point=Vector2.Lerp(a,b,(j+.5f)/pieces),outside=(point-center).normalized;
                    Vector2 cap=point+outside*10;Vector3 world=P(cap.x,cap.y);float top=Height(world);
                    if(top<4||RoadDistance(world)<5)continue;
                    Vector3 foot=P(point.x+outside.x*3,point.y+outside.y*3,-3.5f);
                    if(RoadDistance(foot)>9)
                        if(j%3==0)Boulder(cliffs,foot,new Vector3(8,Mathf.Min(top+5,7),7),i*31+j*17);
                }
            }
            float[,] masses={{220,76,11,14,12},{249,65,15,20,14},{277,76,10,15,12},{308,79,13,13,10},
                {332,58,12,18,11},{367,61,13,17,12},{402,62,13,15,12},{442,83,11,16,13},
                {257,161,12,17,14},{289,173,11,18,14},{329,163,14,16,12},{363,146,12,16,11},
                {462,113,13,21,12},{494,143,12,18,14},{514,190,10,17,13},
                {509,251,12,16,12},{492,285,10,18,12},{453,323,12,17,13},{428,355,9,15,10}};
            // The large inland masses now come from Height(), not stacked rock prefabs.
            // Small offshore stacks at northwest and southeast complete the island silhouette.
            Boulder(cliffs,P(58,93,-3),new Vector3(18,17,15),12);Boulder(cliffs,P(83,73,-3),new Vector3(13,22,15),35);
            Boulder(cliffs,P(37,131,-3),new Vector3(9,11,10),5);Boulder(cliffs,P(451,477,-3),new Vector3(11,13,10),60);
            Boulder(cliffs,P(474,426,-3),new Vector3(9,15,11),25);
            BuildReferenceCave(cliffs);
            DressCaveMountain(cliffs);
        }
        private static void BuildReferenceCave(Transform parent)
        {
            var vertices=new List<Vector3>();var inside=new List<int>();var outside=new List<int>();
            Vector2[] profile={new Vector2(-7.8f,-1),new Vector2(-7.8f,7),new Vector2(-5.7f,11.5f),new Vector2(0,14),new Vector2(5.7f,11.5f),new Vector2(7.8f,7),new Vector2(7.8f,-1)};
            const int first=148,last=169;
            for(int i=first;i<=last;i++)
            {
                Vector3 f=route[i+1]-route[i-1];f.y=0;Vector3 r=Vector3.Cross(Vector3.up,f.normalized);
                foreach(Vector2 q in profile)vertices.Add(route[i]+r*q.x+Vector3.up*q.y);
                foreach(Vector2 q in profile)vertices.Add(route[i]+r*q.x*1.8f+Vector3.up*(q.y+5.5f+(i%4)*.5f));
            }
            for(int i=0;i<last-first;i++)for(int j=0;j<6;j++)
            {int k=i*14+j;inside.AddRange(new[]{k,k+14,k+1,k+1,k+14,k+15});outside.AddRange(new[]{k+7,k+21,k+8,k+8,k+21,k+22});}
            foreach(int i in new[]{0,last-first})for(int j=0;j<6;j++)
            {int k=i*14+j;outside.AddRange(new[]{k,k+1,k+7,k+7,k+1,k+8});}
            Mesh mesh=new Mesh{subMeshCount=2};mesh.SetVertices(vertices);mesh.SetTriangles(inside,0);mesh.SetTriangles(outside,1);mesh.RecalculateNormals();
            MeshObject("Rock mountain with enclosed cave",parent,mesh,new[]{rockDark,rock});
            for(int i=first+2;i<last;i+=6)
            {
                Vector3 f=route[i+1]-route[i-1];f.y=0;Vector3 r=Vector3.Cross(Vector3.up,f.normalized);
                Vector3 pos=route[i]+r*6.6f+Vector3.up*3.5f;
                Cube("Amber cave lantern",parent,pos,new Vector3(.5f,.8f,.5f),glow);
                Light light=new GameObject("Warm cave light").AddComponent<Light>();light.transform.SetParent(parent,false);light.transform.position=pos;
                light.color=new Color(1,.43f,.09f);light.intensity=4;light.range=16;light.shadows=LightShadows.None;
            }
        }
        private static void BuildVegetation()
        {
            Transform foliage=Group("Vegetation • authored palm groves");GameObject template=CreatePalm(foliage);
            Vector2[] groves={new Vector2(79,178),new Vector2(101,153),new Vector2(144,132),new Vector2(175,110),
                new Vector2(163,266),new Vector2(208,252),new Vector2(241,201),new Vector2(269,183),new Vector2(323,195),
                new Vector2(299,63),new Vector2(359,73),new Vector2(421,80),new Vector2(488,118),new Vector2(518,222),
                new Vector2(477,300),new Vector2(438,351),new Vector2(414,401),new Vector2(389,444),new Vector2(348,454),
                new Vector2(304,453),new Vector2(263,428),new Vector2(236,363),new Vector2(296,366),new Vector2(351,371),new Vector2(92,328)};
            Vector2[] offsets={new Vector2(-8,-4),new Vector2(5,-2),new Vector2(-1,9),new Vector2(12,10)};
            for(int i=0;i<groves.Length;i++)for(int j=0;j<offsets.Length;j++)
            {
                Vector2 q=groves[i]+offsets[j];if(!Ground(q.x,q.y,out Vector3 pos)||RoadDistance(pos)<1.8f)continue;
                GameObject palm=UnityEngine.Object.Instantiate(template,foliage);palm.name="Palm grove "+i+" tree "+j;
                palm.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,i*37+j*81,0));palm.transform.localScale=Vector3.one*(.8f+j*.095f);palm.SetActive(true);
            }
            for(int x=75;x<=505;x+=13)for(int y=65;y<=460;y+=15)
            {
                if((x+y)%5==0)continue;
                float jitter=Mathf.Sin(x*1.73f+y*.39f)*5f;
                if(!Ground(x+jitter,y-jitter,out Vector3 pos)||pos.y<3f||pos.y>100f||RoadDistance(pos)<7f)continue;
                // Leave the village and harbor clear of new trees.
                if(x<210&&y>170&&y<290)continue;
                float slopeX=Mathf.Abs(Height(pos+Vector3.right)-Height(pos-Vector3.right));
                float slopeZ=Mathf.Abs(Height(pos+Vector3.forward)-Height(pos-Vector3.forward));
                if(slopeX+slopeZ>2.2f)continue;
                GameObject palm=UnityEngine.Object.Instantiate(template,foliage);palm.name="Hillside grove tree";
                palm.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,x*13+y*7,0));palm.transform.localScale=Vector3.one*(.85f+.25f*Mathf.Abs(jitter)/5f);palm.SetActive(true);
            }
            UnityEngine.Object.DestroyImmediate(template);
        }
        private static GameObject CreatePalm(Transform parent)
        {
            Transform tree=new GameObject("Palm geometry template").transform.WithParent(parent);
            var v=new List<Vector3>();var t=new List<int>();
            for(int ring=0;ring<8;ring++)for(int side=0;side<7;side++)
            {
                float a=side*Mathf.PI*2/7,r=.34f-ring*.021f;
                v.Add(new Vector3(Mathf.Cos(a)*r+Mathf.Sin(ring*.2f)*.9f,ring*1.25f,Mathf.Sin(a)*r));
            }
            for(int ring=0;ring<7;ring++)for(int side=0;side<7;side++)
            {int a=ring*7+side,b=ring*7+(side+1)%7;t.AddRange(new[]{a,b,a+7,a+7,b,b+7});}
            Mesh trunk=new Mesh();trunk.SetVertices(v);trunk.SetTriangles(t,0);trunk.RecalculateNormals();MeshObject("Curved palm trunk",tree,trunk,new[]{woodLight});
            var leaves=new List<Vector3>();var lightIndices=new List<int>();var darkIndices=new List<int>();Vector3 crown=new Vector3(.9f,8.8f,0);
            for(int leaf=0;leaf<9;leaf++)
            {
                float a=leaf*Mathf.PI*2/9;Vector3 f=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),r=Vector3.Cross(Vector3.up,f);
                float length=leaf%2==0?5:4.3f;
                for(int segment=0;segment<6;segment++)
                {
                    float u=segment/6f,w=(segment+1)/6f;
                    Vector3 ca=crown+f*(u*length)+Vector3.up*(Mathf.Sin(u*Mathf.PI)*1.6f-u*u*1.5f),cb=crown+f*(w*length)+Vector3.up*(Mathf.Sin(w*Mathf.PI)*1.6f-w*w*1.5f);
                    float wa=Mathf.Sin(u*Mathf.PI)*.72f+.03f,wb=Mathf.Sin(w*Mathf.PI)*.72f+.03f;int k=leaves.Count;
                    leaves.AddRange(new[]{ca-r*wa,ca+Vector3.up*.12f,ca+r*wa,cb-r*wb,cb+Vector3.up*.12f,cb+r*wb});
                    lightIndices.AddRange(new[]{k,k+3,k+1,k+1,k+3,k+4});darkIndices.AddRange(new[]{k+1,k+4,k+2,k+2,k+4,k+5});
                }
            }
            Mesh canopy=new Mesh{subMeshCount=2};canopy.SetVertices(leaves);canopy.SetTriangles(lightIndices,0);canopy.SetTriangles(darkIndices,1);canopy.RecalculateNormals();MeshObject("Sculpted palm fronds",tree,canopy,new[]{grassLight,grass});
            tree.gameObject.SetActive(false);return tree.gameObject;
        }
        private static void BuildDetails()
        {
            Transform details=Group("Shore details • surf stone gardens and roadside markers");
            float[,] stones={{246,340,5,4,5},{285,309,7,6,5},{321,291,6,5,5},{349,323,5,7,5},
                {379,277,6,8,5},{174,348,6,5,5},{328,478,7,6,5},{395,464,7,5,6},
                {422,426,7,8,6},{66,277,4,5,4},{224,276,5,6,4},{304,256,7,8,5}};
            for(int i=0;i<stones.GetLength(0);i++)
            {
                Vector3 pos=P(stones[i,0],stones[i,1]);float h=Height(pos);pos.y=h<0?-2:h-.8f;
                if(RoadDistance(pos)>stones[i,2]*.5f+.5f)Boulder(details,pos,new Vector3(stones[i,2],stones[i,3],stones[i,4]),i*43);
            }
            // Short white foam arcs around the fitted coastline and lagoon shore.
            foreach(Vector2[] polygon in new[]{Shore,Bay})for(int i=0;i<polygon.Length;i++)
            {
                Vector2 a=polygon[i],b=polygon[(i+1)%polygon.Length];
                for(int j=0;j<3;j++)
                {
                    Vector2 point=Vector2.Lerp(a,b,(j+.3f)/3),dir=(b-a).normalized;
                    Vector3 p=P(point.x,point.y,-.92f);if(Height(p)>-.8f)continue;
                    Vector3 end=P(point.x+dir.x*5,point.y+dir.y*5,-.92f);Beam(details,p,end,.15f,foam);
                }
            }
            // A few directional chevrons, matching the bright roadside accents of the concept.
            foreach(int i in new[]{57,113,219,250})
            {
                Vector3 f=route[i+1]-route[i-1];f.y=0;Vector3 r=Vector3.Cross(Vector3.up,f.normalized),p=route[i]+r*(Width(i)*.5f+1.3f);
                Cube("Roadside marker post",details,p+Vector3.up*1.2f,new Vector3(.16f,2.4f,.16f),wood);
                Cube("Turquoise direction sign",details,p+Vector3.up*2.1f,new Vector3(1.7f,.9f,.15f),shallow,Quaternion.LookRotation(f));
            }
        }
        private static void Pennants(Transform parent,Vector3 a,Vector3 b)
        {
            Beam(parent,a,b,.05f,wood);Vector3 f=(b-a).normalized;
            for(int i=0;i<12;i++)
            {
                Vector3 p=Vector3.Lerp(a,b,(i+.5f)/12);Mesh mesh=new Mesh{vertices=new[]{p-f*.35f,p+f*.35f,p-Vector3.up*.9f},triangles=new[]{0,1,2}};
                mesh.RecalculateNormals();MeshObject("Harbor bunting",parent,mesh,new[]{i%3==0?red:i%3==1?white:grassLight});
            }
        }
    }
}

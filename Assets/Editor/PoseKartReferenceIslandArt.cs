using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    // Art-only map. Coordinates are traced against the 580 x 526 reference map, not the old race layout.
    public static partial class PoseKartReferenceIslandArt
    {
        private const string ScenePath="Assets/Scenes/PoseKart_ReferenceIsland.unity";
        private const string Folder="Assets/PoseKart/ReferenceIslandArt";
        private const float Scale=.82f;
        internal const float RaceScale=1.5f;
        internal static readonly Vector3 RaceStretch=new Vector3(1f,1f,1.65f);
        internal static Vector3 RacePoint(Vector3 point)=>Vector3.Scale(point,RaceStretch)*RaceScale;
        internal static Vector3[] RaceRoute()=>TraceRoute().Select(RacePoint).ToArray();
        private static readonly Vector2[] Shore={
            new Vector2(47,331),new Vector2(42,285),new Vector2(46,233),new Vector2(78,181),
            new Vector2(134,127),new Vector2(184,95),new Vector2(210,57),new Vector2(276,49),
            new Vector2(324,55),new Vector2(362,41),new Vector2(413,54),new Vector2(459,76),
            new Vector2(493,111),new Vector2(520,165),new Vector2(540,207),new Vector2(526,249),
            new Vector2(509,295),new Vector2(476,329),new Vector2(451,395),new Vector2(432,453),
            new Vector2(391,485),new Vector2(336,493),new Vector2(279,481),new Vector2(235,463),
            new Vector2(221,424),new Vector2(180,422),new Vector2(132,429),new Vector2(109,402),new Vector2(87,369)};
        private static readonly Vector2[] Bay={
            new Vector2(107,322),new Vector2(157,304),new Vector2(181,280),new Vector2(221,271),
            new Vector2(252,244),new Vector2(292,247),new Vector2(324,227),new Vector2(360,225),
            new Vector2(393,212),new Vector2(430,201),new Vector2(459,215),new Vector2(450,249),
            new Vector2(414,271),new Vector2(376,296),new Vector2(362,333),new Vector2(337,356),
            new Vector2(302,344),new Vector2(268,325),new Vector2(225,333),new Vector2(190,340),new Vector2(149,337)};
        private static readonly Vector2[] SeaChannel={new Vector2(426,256),new Vector2(491,284),new Vector2(505,318),new Vector2(454,319),new Vector2(410,287)};
        private static Vector3[] route;
        private static MeshCollider terrain;
        private static Transform root;
        private static Material sand,grass,grassLight,rock,rockDark,road,white,wood,woodLight,ocean,shallow,foam,red,cream,roof,glow;
        private static readonly List<Material> cliffPalette=new List<Material>();
        private static int meshId;
        private static Vector3 P(float x,float imageY,float height=0)=>new Vector3((x-290)*Scale,height,(270-imageY)*Scale);
        private static Vector2 ImagePoint(Vector3 p)=>new Vector2(p.x/Scale+290,270-p.z/Scale);

        [MenuItem("PoseKart/Map/Build Reference Island Art")]
        public static void Build()
        {
            if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EnsureFolder(Folder);meshId=0;CreateArtMaterials();CreateSurfaceMaterials();route=TraceRoute();CreateShortcuts();
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            root=new GameObject("ReferenceIsland_ART_ONLY").transform;
            BuildTerrain();BuildWater();BuildRoad();BuildHarbor();BuildVillage();BuildCliffsAndCave();BuildVegetation();BuildDetails();BuildRoadsideScenery();
            root.localScale=RaceStretch*RaceScale;
            Light sun=new GameObject("Tropical afternoon sun").AddComponent<Light>();sun.type=LightType.Directional;
            sun.color=new Color(1,.91f,.76f);sun.intensity=1.6f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-35,0);
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.63f,.81f,.88f);
            RenderSettings.ambientEquatorColor=new Color(.42f,.59f,.55f);RenderSettings.ambientGroundColor=new Color(.27f,.28f,.23f);RenderSettings.fog=false;
            UnityEngine.Camera camera=new GameObject("Map Presentation Camera").AddComponent<UnityEngine.Camera>();
            camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=300*RaceScale;camera.nearClipPlane=.3f;camera.farClipPlane=2000;
            camera.transform.position=new Vector3(0,405,-300)*RaceScale;camera.transform.LookAt(new Vector3(0,5,0));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.34f,.48f);
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new InvalidOperationException("Unable to save reference art scene.");
            AssetDatabase.SaveAssets();Selection.activeGameObject=root.gameObject;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(new Vector3(0,8,0),Quaternion.Euler(58,0,0),219,true);
            Debug.Log("Reference island artwork saved: "+ScenePath+". Art only; no race systems or runtime tests.");
        }
        private static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }
        private static Material Mat(string name,Color color,float smooth=.08f)
        {
            string path=Folder+"/"+name+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)throw new InvalidOperationException("URP Lit shader missing.");
            if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}else m.shader=shader;
            m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",0);m.SetFloat("_Cull",0);EditorUtility.SetDirty(m);return m;
        }
        private static void CreateArtMaterials()
        {
            sand=Mat("Warm coral sand",new Color(.88f,.70f,.38f));grass=Mat("Palm grass",new Color(.30f,.49f,.12f));
            grassLight=Mat("Sunlit grass",new Color(.49f,.62f,.19f));rock=Mat("Warm limestone",new Color(.39f,.38f,.29f));rockDark=Mat("Basalt shadows",new Color(.21f,.28f,.28f));
            road=Mat("Packed golden sand",new Color(.77f,.56f,.30f),.015f);white=Mat("Ivory markings",new Color(.95f,.91f,.76f));
            wood=Mat("Teak boards",new Color(.40f,.20f,.075f));woodLight=Mat("Honey boards",new Color(.60f,.34f,.13f));
            ocean=Mat("Deep turquoise",new Color(.025f,.36f,.51f),.6f);shallow=Mat("Lagoon turquoise",new Color(.08f,.64f,.65f),.48f);
            foam=Mat("Sea foam",new Color(.66f,.88f,.80f),.3f);red=Mat("Lighthouse vermilion",new Color(.82f,.21f,.095f));
            cream=Mat("Plaster walls",new Color(.96f,.79f,.49f));roof=Mat("Terracotta roofs",new Color(.69f,.23f,.075f));
            glow=Mat("Amber lanterns",new Color(1,.56f,.12f));glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",new Color(1,.30f,.04f)*3);
            cliffPalette.Clear();cliffPalette.Add(rock);cliffPalette.Add(rockDark);cliffPalette.Add(Mat("Limestone ochre",new Color(.48f,.44f,.31f)));
            cliffPalette.Add(Mat("Limestone light",new Color(.55f,.52f,.39f)));cliffPalette.Add(Mat("Limestone moss",new Color(.36f,.43f,.23f)));
        }
        private static Vector3[] TraceRoute()
        {
            Vector3[] a={P(134,385,2),P(102,341,4),P(69,296,7),P(66,258,10),P(103,223,14),P(154,224,17),
                P(192,219,21),P(204,167,24),P(211,119,28),P(241,108,30),P(282,134,30),P(316,139,32),
                P(352,110,35),P(391,91,35),P(434,111,33),P(464,158,28),P(495,194,22),P(494,228,18),
                P(474,266,14),P(433,287,12),P(404,312,10),P(395,356,5),P(363,394,2),P(325,398,1.7f),
                P(283,384,2),P(245,395,2),P(204,376,2),P(164,368,2)};
            var result=new List<Vector3>();
            for(int i=0;i<a.Length;i++)for(int j=0;j<10;j++)
            {
                Vector3 p0=a[(i+a.Length-1)%a.Length],p1=a[i],p2=a[(i+1)%a.Length],p3=a[(i+2)%a.Length];
                float t=j/10f,t2=t*t,t3=t*t*t;
                result.Add(.5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t2+(-p0+3*p1-3*p2+p3)*t3));
            }
            for(int i=0;i<result.Count;i++)
            {
                Vector3 point=result[i];point.y*=1.22f;
                // Smooth crests produce sustained descents without introducing road seams.
                foreach(var crest in new[]{new Vector3(22,12,5),new Vector3(151,14,10)})
                {
                    float distance=Mathf.Abs(i-crest.x)/crest.y;
                    if(distance<1f)point.y+=crest.z*Mathf.Pow(.5f+.5f*Mathf.Cos(distance*Mathf.PI),2f);
                }
                result[i]=point;
            }
            // Long acceleration zones, with tangent-matched approaches and exits.
            StraightSection(result, 142, 150, 176, 181);
            StraightSection(result, 68, 74, 85, 91);
            StraightSection(result, 218, 224, 239, 245);

            SmoothRouteHeight(result, 2, 0.25f);

            return result.ToArray();
        }
        private static void SmoothRouteHeight(
    List<Vector3> points,
    int passes,
    float amount)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                float[] smoothed = new float[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    int previous = (i - 1 + points.Count) % points.Count;
                    int next = (i + 1) % points.Count;

                    float average =
                        (points[previous].y +
                         points[i].y * 2f +
                         points[next].y) / 4f;

                    smoothed[i] = Mathf.Lerp(
                        points[i].y,
                        average,
                        amount);
                }

                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 point = points[i];
                    point.y = smoothed[i];
                    points[i] = point;
                }
            }
        }
        private static void StraightSection(List<Vector3> points,int entry,int start,int end,int exit)
        {
            Vector3 a=points[start],b=points[end],direction=(b-a).normalized;
            Vector3 entryPoint=points[entry],exitPoint=points[exit];
            Vector3 entryDirection=(points[entry]-points[entry-1]).normalized;
            Vector3 exitDirection=(points[exit+1]-points[exit]).normalized;
            Vector3 Curve(Vector3 p,Vector3 q,Vector3 u,Vector3 v,float t)
            {
                float handle=Vector3.Distance(p,q)*.8f,t2=t*t,t3=t2*t;
                return (2*t3-3*t2+1)*p+(t3-2*t2+t)*u*handle+(-2*t3+3*t2)*q+(t3-t2)*v*handle;
            }
            for(int i=entry;i<start;i++)points[i]=Curve(entryPoint,a,entryDirection,direction,(i-entry)/(float)(start-entry));
            for(int i=start;i<=end;i++)points[i]=Vector3.Lerp(a,b,(i-start)/(float)(end-start));
            for(int i=end+1;i<=exit;i++)points[i]=Curve(b,exitPoint,direction,exitDirection,(i-end)/(float)(exit-end));
        }
        private static float SignedDistance(Vector2 p,Vector2[] polygon)
        {
            bool inside=false;float d=float.PositiveInfinity;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                Vector2 a=polygon[j],b=polygon[i],edge=b-a;
                if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
                d=Mathf.Min(d,Vector2.Distance(p,a+edge*Mathf.Clamp01(Vector2.Dot(p-a,edge)/edge.sqrMagnitude)));
            }
            return inside?d:-d;
        }
        private static float Hill(Vector2 p,float x,float y,float rx,float ry)=>Mathf.Exp(-Mathf.Pow((p.x-x)/rx,2)-Mathf.Pow((p.y-y)/ry,2));
        private static float Width(int node)=>(node>=220?9:node<30?8:node<80?7.8f:8)*1.5f;
        private static float Height(Vector3 world)
        {
            Vector2 p=ImagePoint(world);float shore=SignedDistance(p,Shore),bay=SignedDistance(p,Bay),channel=SignedDistance(p,SeaChannel);
            float h=2+36*Hill(p,315,120,140,105)+16*Hill(p,155,205,90,80)+14*Hill(p,475,231,54,80);
            // Broad, connected mountain ridges sculpted into the island mesh itself.
            // Distributed low ridges around the outer coast; max avoids stacked peaks.
            float ridge=Mathf.Max(34*Hill(p,130,167,65,78),38*Hill(p,224,96,74,63));
            ridge=Mathf.Max(ridge,42*Hill(p,350,83,93,62));
            ridge=Mathf.Max(ridge,36*Hill(p,464,145,63,83));
            ridge=Mathf.Max(ridge,28*Hill(p,492,259,48,75));
            ridge=Mathf.Max(ridge,22*Hill(p,421,404,58,60));
            ridge=Mathf.Max(ridge,16*Hill(p,319,452,76,40));
            h+=ridge;
            // Steep shore escarpments and grass-capped uplands replace the former flat disc.
            h=Mathf.Lerp(-4,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-4,12,shore)));
            h=Mathf.Lerp(h,-4,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-5,3,bay)));
            h=Mathf.Lerp(h,-4,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-3,2,channel)));
            float nearest=float.PositiveInfinity,target=0,half=4;
            for(int i=0;i<route.Length;i++)
            {
                Vector3 a=route[i],b=route[(i+1)%route.Length],d=b-a;d.y=0;Vector3 q=world-a;q.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(q,d)/d.sqrMagnitude),distance=(q-d*t).magnitude;
                if(distance<nearest){nearest=distance;target=Mathf.Lerp(a.y,b.y,t)-.04f;half=Width(i)*.5f;}
            }
            // The eastern timber span crosses open lagoon water; there is no land embankment beneath it.
            if(channel<0&&nearest<half+24)h=Mathf.Lerp(target,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(half+4,half+24,nearest)));
            return FitShortcutTerrain(world,h);
        }
        private static Transform Group(string name)=>new GameObject(name).transform.WithParent(root);
        private static void BuildTerrain()
        {
            Transform parent=Group("Terrain • sculpted headlands and lagoon cliffs");var vertices=new List<Vector3>();var triangles=new List<int>();var materials=new List<int>();
            const float step=1.4f;
            for(float z=-200;z<200;z+=step)for(float x=-215;x<215;x+=step)
            {
                Vector3 a=new Vector3(x,0,z),b=new Vector3(x,0,z+step),c=new Vector3(x+step,0,z),d=new Vector3(x+step,0,z+step);
                a.y=Height(a);b.y=Height(b);c.y=Height(c);d.y=Height(d);
                AddTerrainFace(a,b,c,vertices,triangles,materials);AddTerrainFace(c,b,d,vertices,triangles,materials);
            }
            var mesh=new Mesh{name="Reference coast terrain",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,vertices.Select(p=>new Vector2(p.x,p.z)*.125f).ToList());mesh.subMeshCount=5;
            for(int m=0;m<5;m++){var indices=new List<int>();for(int i=0;i<materials.Count;i++)if(materials[i]==m)indices.AddRange(new[]{i*3,i*3+1,i*3+2});mesh.SetTriangles(indices,m);}
            mesh.RecalculateNormals();mesh.RecalculateBounds();GameObject go=MeshObject("Sculpted island",parent,mesh,new[]{sand,grass,grassLight,rock,rockDark},true);terrain=go.GetComponent<MeshCollider>();
            Physics.SyncTransforms();
        }
        private static void AddTerrainFace(Vector3 a,Vector3 b,Vector3 c,List<Vector3> vertices,List<int> triangles,List<int> materials)
        {
            Vector3 center=(a+b+c)/3,normal=Vector3.Cross(b-a,c-a).normalized;Vector2 p=ImagePoint(center);
            int material=normal.y<.72f?3:center.y<3.2f||p.y>360?0:((Mathf.FloorToInt(p.x/27)+Mathf.FloorToInt(p.y/23))%3==0?2:1);
            if(normal.y<.38f)material=4;vertices.Add(a);vertices.Add(b);vertices.Add(c);materials.Add(material);
        }
        private static GameObject MeshObject(string name,Transform parent,Mesh mesh,Material[] materials,bool collision=false)
        {
            mesh.name=name;string path=Folder+"/"+(meshId++).ToString("000")+"_"+name.Replace('/','_')+".asset";
            Mesh saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
            GameObject go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=saved;go.GetComponent<MeshRenderer>().sharedMaterials=materials;
            if(collision)go.AddComponent<MeshCollider>().sharedMesh=saved;return go;
        }
        private static void BuildWater()
        {
            Transform water=Group("Water • turquoise lagoon and shallow shoreline");
            Cube("Ocean",water,new Vector3(0,-1.2f,0),new Vector3(1400,.2f,1400),ocean);
            // A fitted shallow-water mesh follows the actual shores, not another rectangular plane.
            var v=new List<Vector3>();var t=new List<int>();
            for(float z=-220;z<205;z+=3)for(float x=-237;x<237;x+=3)
            {
                Vector3 c=new Vector3(x+1.5f,0,z+1.5f);Vector2 p=ImagePoint(c);
                float coast=SignedDistance(p,Shore);if(coast< -24)continue;
                if(Height(c)>-.8f)continue;
                int k=v.Count;v.Add(new Vector3(x,-1.04f,z));v.Add(new Vector3(x,-1.04f,z+3));v.Add(new Vector3(x+3,-1.04f,z));v.Add(new Vector3(x+3,-1.04f,z+3));t.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
            }
            Mesh mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();MeshObject("Clear shallow lagoon",water,mesh,new[]{shallow});
        }
        private static void BuildRoad()
        {
            Transform track =
                Group("Track • reference silhouette");

            const int verticesAcross = 7;
            const float roadLift = 0.04f;
            const float crownHeight = 0.035f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();

            mainEdges = new Vector3[route.Length * 2];

            for (int i = 0; i < route.Length; i++)
            {
                int previous =
                    (i - 1 + route.Length) % route.Length;

                int next =
                    (i + 1) % route.Length;

                Vector3 forward =
                    route[next] - route[previous];

                forward.y = 0f;
                forward.Normalize();

                Vector3 right =
                    Vector3.Cross(Vector3.up, forward);

                float halfWidth = Width(i) * 0.5f;

                mainEdges[i * 2] =
                    route[i] - right * halfWidth;

                mainEdges[i * 2 + 1] =
                    route[i] + right * halfWidth;

                for (int lane = 0; lane < verticesAcross; lane++)
                {
                    float t =
                        lane / (float)(verticesAcross - 1);

                    float across =
                        Mathf.Lerp(-halfWidth, halfWidth, t);

                    Vector3 point =
                        route[i] + right * across;

                    float centerAmount =
                        1f - Mathf.Abs(t * 2f - 1f);

                    point.y += roadLift;
                    point.y += crownHeight * centerAmount;

                    vertices.Add(point);
                    uv.Add(new Vector2(t, i / 10f));
                }
            }

            for (int i = 0; i < route.Length; i++)
            {
                int next =
                    (i + 1) % route.Length;

                int currentStart =
                    i * verticesAcross;

                int nextStart =
                    next * verticesAcross;

                for (int lane = 0; lane < verticesAcross - 1; lane++)
                {
                    int a = currentStart + lane;
                    int b = nextStart + lane;
                    int c = currentStart + lane + 1;
                    int d = nextStart + lane + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                }
            }

            Mesh mesh = new Mesh
            {
                name = "Continuous sand and dirt road",
                indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mainSurface =
                MeshObject(
                    "Continuous sand and dirt road",
                    track,
                    mesh,
                    new[] { road },
                    true
                ).GetComponent<MeshCollider>();

            Physics.SyncTransforms();

            BuildSoftShoulders(track);
            BuildShortcutMeshes(track);
            AddWornTracks(track, route, true, 2.2f);

            // Giữ nguyên phần bridge và finish checker hiện tại ở dưới đây.
        }
        private static GameObject Cube(string name,Transform parent,Vector3 center,Vector3 size,Material material,Quaternion? rotation=null)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(center,rotation??Quaternion.identity);go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        private static void Beam(Transform parent,Vector3 a,Vector3 b,float size,Material material)=>Cube("Timber rail",parent,(a+b)*.5f,new Vector3(size,size,Vector3.Distance(a,b)),material,Quaternion.LookRotation(b-a));
    }
    internal static class ReferenceArtTransformExtensions
    {
        internal static Transform WithParent(this Transform transform,Transform parent){transform.SetParent(parent,false);return transform;}
    }
}

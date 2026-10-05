using System;
using System.Collections.Generic;
using System.Linq;
using PoseKart.Race;
using PoseKart.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.Editor
{
    public static partial class PoseKartFinalIslandSetup
    {
        private sealed class Branch
        {
            public string name;
            public Vector3[] points,edges;
            public float width;
            public int start,end;
        }
        private static Branch[] branches=Array.Empty<Branch>();
        private static MeshCollider terrainSurface;
        private const string SelectionScene="Assets/Scenes/PoseKart_VehicleSelect.unity";

        private static void PrepareBranches()
        {
            branches=new[]{
                AuthoredBranch("Village_Alley",Node(19)+3,Node(22),7.5f,new[]{
                    new Vector3(-230,5.8f,90),new Vector3(-224,5.4f,65),new Vector3(-245,4.8f,28)}),
                AuthoredBranch("Beach_Inside",Node(3)+8,Node(6),8,new[]{
                    new Vector3(120,1.3f,-220),new Vector3(175,1.6f,-190)}),
                AuthoredBranch("Canyon_LowerTunnel",Node(10),Node(12),9,new[]{
                    new Vector3(245,16,151),new Vector3(217,16,164),new Vector3(186,17,183)})};
        }
        private static Branch AuthoredBranch(string name,int start,int end,float width,Vector3[] interior)
        {
            var anchors=new List<Vector3>{Route[start]};anchors.AddRange(interior);anchors.Add(Route[end]);
            Vector3[] points=SampleSpline(anchors.ToArray(),false);
            // Matching the road tangents at both ends makes junctions smoothly diverge and merge.
            Vector3 entry=(Route[start+1]-Route[start-1]).normalized,exit=(Route[end+1]-Route[end-1]).normalized;
            points[1]=points[0]+entry*Vector3.Distance(points[0],points[1]);
            points[points.Length-2]=points[points.Length-1]-exit*Vector3.Distance(points[points.Length-2],points[points.Length-1]);
            var branch=new Branch{name=name,width=width,points=points,edges=new Vector3[points.Length*2],start=start,end=end};
            for(int i=0;i<points.Length;i++)
            {
                Vector3 tangent=Vector3.ProjectOnPlane(points[Mathf.Min(points.Length-1,i+1)]-points[Mathf.Max(0,i-1)],Vector3.up).normalized;
                Vector3 side=Vector3.Cross(Vector3.up,tangent)*width*.5f;
                branch.edges[i*2]=points[i]-side+Vector3.up*.16f;
                branch.edges[i*2+1]=points[i]+side+Vector3.up*.16f;
            }
            return branch;
        }
        private static bool BranchCorridorIntersects(Bounds bounds,float margin)
        {
            foreach(Branch branch in branches)
                for(int i=0;i<branch.points.Length-1;i++)
                {
                    if(bounds.max.y<Mathf.Min(branch.points[i].y,branch.points[i+1].y)-.3f||bounds.min.y>Mathf.Max(branch.points[i].y,branch.points[i+1].y)+9)continue;
                    if(TriangleBox(branch.edges[i*2],branch.edges[i*2+2],branch.edges[i*2+1],bounds,margin)
                        ||TriangleBox(branch.edges[i*2+1],branch.edges[i*2+2],branch.edges[i*2+3],bounds,margin))return true;
                }
            return false;
        }
        private static float FitBranchGround(float x,float z,float height)
        {
            foreach(Branch branch in branches)
                for(int i=0;i<branch.points.Length-1;i++)
                {
                    Vector3 a=branch.points[i],b=branch.points[i+1];Vector2 d=new Vector2(b.x-a.x,b.z-a.z),q=new Vector2(x-a.x,z-a.z);
                    float t=Mathf.Clamp01(Vector2.Dot(q,d)/Mathf.Max(.001f,d.sqrMagnitude)),dist=(q-d*t).magnitude;
                    if(dist<branch.width*.5f+7)height=Mathf.Lerp(Mathf.Min(height,Mathf.Lerp(a.y,b.y,t)-.25f),height,
                        Mathf.SmoothStep(0,1,Mathf.InverseLerp(branch.width*.5f+3,branch.width*.5f+7,dist)));
                }
            return height;
        }
        private static void BuildBranches(Transform road,Palette palette)
        {
            Physics.SyncTransforms();
            foreach(Branch branch in branches)
            {
                var vertices=new List<Vector3>();var indices=new List<int>();
                for(int i=0;i<branch.points.Length-1;i++)
                {
                    AppendBranchTriangle(branch.edges[i*2],branch.edges[i*2+2],branch.edges[i*2+1],vertices,indices);
                    AppendBranchTriangle(branch.edges[i*2+1],branch.edges[i*2+2],branch.edges[i*2+3],vertices,indices);
                }
                Mesh mesh=new Mesh{name=branch.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
                mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x*.1f,v.z*.1f)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();
                MeshObject(branch.name,road,SaveMesh(mesh,branch.name),branch.name.Contains("Village")?palette.Stone:branch.name.Contains("Tunnel")?palette.Cave:palette.Dirt,true);
            }
        }
        // Subtract the primary road footprint from branch triangles. Junctions share edges instead of stacked colliders.
        private static void AppendBranchTriangle(Vector3 a,Vector3 b,Vector3 c,List<Vector3> vertices,List<int> indices)
        {
            var fragments=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
            for(int i=0;i<Route.Length&&fragments.Count>0;i++)
            {
                int n=(i+1)%Route.Length;
                Vector3[] clip={roadEdges[i*2],roadEdges[n*2],roadEdges[n*2+1],roadEdges[i*2+1]};
                var outside=new List<List<Vector3>>();
                foreach(List<Vector3> polygon in fragments)SubtractConvex(polygon,clip,outside);
                fragments=outside;
            }
            foreach(List<Vector3> polygon in fragments)
            {
                if(polygon.Count<3)continue;
                int first=vertices.Count;
                foreach(Vector3 value in polygon)
                {
                    Vector3 v=value;
                    if(drivingSurface.Raycast(new Ray(new Vector3(v.x,100,v.z),Vector3.down),out RaycastHit hit,200))v.y=hit.point.y;
                    vertices.Add(v);
                }
                for(int i=1;i<polygon.Count-1;i++)
                {
                    if(Vector3.Cross(vertices[first+i]-vertices[first],vertices[first+i+1]-vertices[first]).sqrMagnitude<.000001f)continue;
                    indices.Add(first);indices.Add(first+i);indices.Add(first+i+1);
                }
            }
        }
        private static float CrossXZ(Vector3 a,Vector3 b){return a.x*b.z-a.z*b.x;}
        private static void SubtractConvex(List<Vector3> polygon,Vector3[] clip,List<List<Vector3>> output)
        {
            float winding=Mathf.Sign(CrossXZ(clip[1]-clip[0],clip[2]-clip[1]));
            if(winding==0){output.Add(polygon);return;}
            List<Vector3> inside=polygon;
            for(int edge=0;edge<clip.Length&&inside.Count>=3;edge++)
            {
                Vector3 a=clip[edge],d=clip[(edge+1)%clip.Length]-a;
                List<Vector3> rejected=ClipHalf(inside,a,d,winding,false);
                if(rejected.Count>=3)output.Add(rejected);
                inside=ClipHalf(inside,a,d,winding,true);
            }
        }
        private static List<Vector3> ClipHalf(List<Vector3> polygon,Vector3 a,Vector3 d,float winding,bool positive)
        {
            var result=new List<Vector3>();
            for(int i=0;i<polygon.Count;i++)
            {
                Vector3 p=polygon[i],q=polygon[(i+1)%polygon.Count];float dp=CrossXZ(d,p-a)*winding,dq=CrossXZ(d,q-a)*winding;
                bool ip=positive?dp>=0:dp<=0,iq=positive?dq>=0:dq<=0;
                if(ip)result.Add(p);
                if(ip!=iq)result.Add(Vector3.LerpUnclamped(p,q,dp/(dp-dq)));
            }
            return result;
        }
        private static Canvas NewCanvas(string name)
        {
            GameObject go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            Canvas canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            return canvas;
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {
            GameObject go=new GameObject(name,typeof(RectTransform));RectTransform rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
        }
        private static Text Label(string name,Transform parent,string text,Vector2 position,Vector2 size,int fontSize)
        {
            RectTransform rect=Rect(name,parent,new Vector2(.5f,.5f),position,size);Text label=rect.gameObject.AddComponent<Text>();
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text=text;label.fontSize=fontSize;label.alignment=TextAnchor.MiddleCenter;label.color=Color.white;label.raycastTarget=false;return label;
        }
        private static void InstallMinimap()
        {
            GameObject old=GameObject.Find("PoseKart_Minimap");if(old!=null)UnityEngine.Object.DestroyImmediate(old);
            Canvas canvas=NewCanvas("PoseKart_Minimap");canvas.sortingOrder=20;
            RectTransform panel=Rect("MapPanel",canvas.transform,Vector2.one,new Vector2(-156,-156),new Vector2(264,264));
            panel.gameObject.AddComponent<Image>().color=new Color(.025f,.08f,.105f,.9f);
            RectTransform map=Rect("Network",panel,new Vector2(.5f,.5f),new Vector2(0,0),new Vector2(244,218));
            var paths=new List<PoseKartMinimap.Path>{new PoseKartMinimap.Path{points=Route,closed=true}};
            paths.AddRange(branches.Select(b=>new PoseKartMinimap.Path{points=b.points,closed=false}));
            map.gameObject.AddComponent<PoseKartMinimap>().Configure(paths.ToArray(),UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None));
            Label("Heading",panel,"ISLAND ROUTES",new Vector2(0,118),new Vector2(244,22),18);
            Label("Legend",panel,"WHITE  MAIN  /  GOLD  SHORTCUT",new Vector2(0,-119),new Vector2(250,22),11);
        }
        private static Renderer[] BodyRenderers(Transform root)
        {
            return root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.Equals("body",StringComparison.OrdinalIgnoreCase)||r.name.Contains("roadster_body")).ToArray();
        }
        private static void InstallPlayerSelection()
        {
            RaceParticipant player=UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None).First(r=>r.IsPlayer);
            PoseKartVehiclePaint paint=player.GetComponent<PoseKartVehiclePaint>()??player.gameObject.AddComponent<PoseKartVehiclePaint>();
            paint.Configure(CreateSelectableModels(player.transform.Find("Visual"),player.GetComponent<BoxCollider>(),true),true);
        }
        private static GameObject[] CreateSelectableModels(Transform parent,BoxCollider fit,bool keepExisting=false)
        {
            GameObject existing=keepExisting&&parent.childCount>0?parent.GetChild(0).gameObject:null;
            var models=new GameObject[SelectableVehiclePrefabs.Length];
            for(int i=0;i<SelectableVehiclePrefabs.Length;i++)
            {
                if(i==2&&existing!=null)models[i]=existing;
                else
                {
                    models[i]=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SelectableVehiclePrefabs[i]),parent);
                    PrefabUtility.UnpackPrefabInstance(models[i],PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    models[i].name="Selectable_"+PoseKartVehiclePaint.VehicleNames[i];
                    foreach(Joint value in models[i].GetComponentsInChildren<Joint>(true))UnityEngine.Object.DestroyImmediate(value);
                    foreach(Collider value in models[i].GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(value);
                    foreach(Rigidbody value in models[i].GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(value);
                    foreach(MonoBehaviour value in models[i].GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(value);
                    foreach(Animator value in models[i].GetComponentsInChildren<Animator>(true))value.enabled=false;
                    foreach(Renderer value in models[i].GetComponentsInChildren<Renderer>(true))value.sharedMaterials=value.sharedMaterials.Select(VehicleMaterial).ToArray();
                }
                models[i].transform.localPosition=Vector3.zero;models[i].transform.localRotation=Quaternion.identity;models[i].transform.localScale=Vector3.one;
                Bounds bounds=VisualBounds(models[i].transform);float targetLength=fit!=null?fit.size.z:4f;
                models[i].transform.localScale*=targetLength/Mathf.Max(.01f,bounds.size.z);
                bounds=new Bounds(Vector3.Scale(bounds.center,models[i].transform.localScale),Vector3.Scale(bounds.size,models[i].transform.localScale));
                Vector3 center=fit!=null?fit.center:Vector3.zero;float bottom=fit!=null?fit.center.y-fit.size.y*.5f:0;
                models[i].transform.localPosition+=new Vector3(center.x-bounds.center.x,bottom-bounds.min.y,center.z-bounds.center.z);models[i].SetActive(i==0);
            }
            return models;
        }
        private static void RegisterScene(string path)
        {
            var scenes=EditorBuildSettings.scenes.ToList();int existing=scenes.FindIndex(s=>s.path==path);
            if(existing>=0)scenes[existing]=new EditorBuildSettingsScene(path,true);else scenes.Add(new EditorBuildSettingsScene(path,true));
            EditorBuildSettings.scenes=scenes.ToArray();
        }

        [MenuItem("PoseKart/Final/Build Vehicle Select Scene")]
        public static void BuildVehicleSelectScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            CreatePalette();
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GameObject preview=new GameObject("SelectedVehiclePreview");
            GameObject[] models=CreateSelectableModels(preview.transform,null);
            preview.transform.rotation=Quaternion.Euler(0,-25,0);
            PoseKartVehiclePaint paint=preview.AddComponent<PoseKartVehiclePaint>();paint.Configure(models,false);
            GameObject cameraGo=new GameObject("Main Camera");cameraGo.tag="MainCamera";
            UnityEngine.Camera camera=cameraGo.AddComponent<UnityEngine.Camera>();cameraGo.AddComponent<AudioListener>();camera.transform.position=new Vector3(6,3,7);camera.transform.LookAt(new Vector3(0,1,0));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.09f,.13f);camera.fieldOfView=38;
            Light sun=new GameObject("PreviewKeyLight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.62f,.67f);
            Canvas canvas=NewCanvas("VehicleSelectUI");
            Label("Title",canvas.transform,"SELECT VEHICLE",new Vector2(0,420),new Vector2(900,90),54);
            Label("Subtitle",canvas.transform,"POSE KART  /  TROPICAL ISLAND RACE",new Vector2(0,355),new Vector2(900,45),22);
            Text vehicleName=Label("VehicleName",canvas.transform,"TYPE  /  RALLY",new Vector2(0,-245),new Vector2(650,55),34);
            Button previousVehicle=SelectionButton(canvas.transform,"PreviousVehicle","<",new Vector2(-430,-245),new Vector2(90,58));
            Button nextVehicle=SelectionButton(canvas.transform,"NextVehicle",">",new Vector2(430,-245),new Vector2(90,58));
            Text colorName=Label("ColorName",canvas.transform,"COLOR  /  CORAL",new Vector2(0,-315),new Vector2(650,55),30);
            Button previousColor=SelectionButton(canvas.transform,"PreviousColor","<",new Vector2(-430,-315),new Vector2(90,58));
            Button nextColor=SelectionButton(canvas.transform,"NextColor",">",new Vector2(430,-315),new Vector2(90,58));
            Button start=SelectionButton(canvas.transform,"StartRace","START RACE",new Vector2(0,-395),new Vector2(330,78));
            canvas.gameObject.AddComponent<PoseKartVehicleSelect>().Configure(paint,vehicleName,colorName,previousVehicle,nextVehicle,previousColor,nextColor,start);
            GameObject events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            if(!EditorSceneManager.SaveScene(scene,SelectionScene))throw new InvalidOperationException("Unable to save Vehicle Select scene.");
            RegisterScene(SelectionScene);RegisterScene("Assets/Scenes/PoseKart_ReferenceIsland.unity");
            var buildScenes=EditorBuildSettings.scenes.ToList();var selection=buildScenes.First(s=>s.path==SelectionScene);buildScenes.Remove(selection);buildScenes.Insert(0,selection);EditorBuildSettings.scenes=buildScenes.ToArray();
            AssetDatabase.SaveAssets();
        }
        [MenuItem("PoseKart/Final/Build Vehicle Select Scene",true)]
        private static bool CanBuildSelect(){return !EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling;}
        private static Button SelectionButton(Transform parent,string name,string text,Vector2 position,Vector2 size)
        {
            RectTransform rect=Rect(name,parent,new Vector2(.5f,.5f),position,size);Image image=rect.gameObject.AddComponent<Image>();image.color=new Color(.05f,.38f,.44f);
            Button button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            Label(name+"Label",rect,text,Vector2.zero,size,28);return button;
        }
    }
}

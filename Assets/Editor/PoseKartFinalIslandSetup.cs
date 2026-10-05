using System;
using System.Linq;
using System.Collections.Generic;
using PoseKart.AI;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    /// <summary>One-pass, repeatable production builder for the final island race scene.</summary>
    public static partial class PoseKartFinalIslandSetup
    {
        private const string SourceScene = "Assets/Scenes/Milestone6_BuggyBeach.unity";
        private const string FinalScene = "Assets/Scenes/PoseKart_FinalIsland.unity";
        private const int TotalLaps = 3;
        private const string BuggyPrefab = "Assets/Free Adventure Vehicles/Prefabs/Vehicle19.prefab";
        private static readonly string[] SelectableVehiclePrefabs = {
            "Assets/Free Adventure Vehicles/Prefabs/Vehicle14.prefab",
            "Assets/Free Adventure Vehicles/Prefabs/Vehicle16.prefab",
            "Assets/Free Adventure Vehicles/Prefabs/Vehicle19.prefab"
        };
        private const string MeshFolder = "Assets/PoseKart/GeneratedMeshes";
        private static MeshCollider drivingSurface;
        private static Vector3[] roadEdges;
        private static readonly HashSet<Material> convertedMaterials = new HashSet<Material>();
        private const string Tropical = "Assets/LowPolyTropicalEnvironment_LITE/Prefabs/";
        private const string Houses = "Assets/BrokenVector/LowPolyBrickHouses/Prefabs/";
        private const string Nature = "Assets/Skyden_Games/Low Poly Environment/Prefabs/";

        // Authored world-space layout; never scales or reuses the rejected SVG ribbon.
        private const int SamplesPerSpan = 12;
        private static int Node(int anchor) => anchor * SamplesPerSpan;
        private static readonly Vector3[] Route = NewIslandRoute();
        private static readonly float[] Widths = Enumerable.Range(0, Route.Length).Select(RoadWidth).ToArray();
        // Every alternative splits and rejoins strictly BETWEEN these shared gates.
        private static readonly int[] CheckpointNodes = { 0, Node(2), Node(3), Node(7), Node(9), Node(13),
            Node(15), Node(17), Node(19), Node(23), Node(24), Node(25) };

        private sealed class Palette
        {
            public Material Asphalt, Stone, Dirt, Cave, Sand, Grass, Rock, Ocean, Wood, Paint, Metal, Glow;
        }

        [MenuItem("PoseKart/Final/Build Final Island Map")]
        public static void BuildFinalIslandMap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
                throw new InvalidOperationException("The validated Milestone 6 source scene is missing.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FinalScene) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScene, FinalScene))
                    throw new InvalidOperationException("Unable to create the final scene from Milestone 6.");
                AssetDatabase.ImportAsset(FinalScene, ImportAssetOptions.ForceSynchronousImport);
            }

            Scene scene = EditorSceneManager.OpenScene(FinalScene, OpenSceneMode.Single);
            Build(scene);
        }

        [MenuItem("PoseKart/Final/Build Final Island Map", true)]
        private static bool ValidateBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        private static void Build(Scene scene)
        {
            GameObject environment = GameObject.Find("Environment");
            GameObject road = GameObject.Find("Track/MainRoad") ?? GameObject.Find("Track/Road") ?? GameObject.Find("Environment/Road");
            GameObject barriers = GameObject.Find("Track/Barriers") ?? GameObject.Find("Environment/Barriers");
            GameObject race = GameObject.Find("Race");
            if (environment == null || road == null || barriers == null || race == null)
                throw new InvalidOperationException("The preserved race scene is missing its Environment, Track, or Race root.");

            Palette p = CreatePalette();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BuggyPrefab) == null)
                throw new InvalidOperationException("Required Adventure Vehicle19 prefab is missing.");
            Transform track = (GameObject.Find("Track") ?? new GameObject("Track")).transform;
            road.name = "MainRoad";
            road.transform.SetParent(track, true);
            Transform branchRoot=Section("Branches",track); Clear(branchRoot);
            road.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity); road.transform.localScale=Vector3.one;
            barriers.transform.SetParent(track, true);
            Transform oldArt = environment.transform.Find("FinalIsland");
            if (oldArt != null)
            {
                while (oldArt.childCount > 0) oldArt.GetChild(0).SetParent(environment.transform, true);
                UnityEngine.Object.DestroyImmediate(oldArt.gameObject);
            }
            string[] sections = { "Terrain", "Water", "Harbor", "Village", "Beach", "Canyon", "Cave", "Cliff", "Vegetation", "DistantScenery" };
            // Remove only obsolete environment from the copied milestone, never racer/race roots.
            foreach (Transform child in environment.transform.Cast<Transform>().ToArray())
                if (!sections.Contains(child.name)) UnityEngine.Object.DestroyImmediate(child.gameObject);
            Clear(road.transform); Clear(barriers.transform);

            Transform island = environment.transform;
            Transform terrain = Section("Terrain", island);
            Transform water = Section("Water", island);
            Transform harbor = Section("Harbor", island);
            Transform village = Section("Village", island);
            Transform beach = Section("Beach", island);
            Transform canyon = Section("Canyon", island);
            Transform cave = Section("Cave", island);
            Transform cliff = Section("Cliff", island);
            Transform vegetation = Section("Vegetation", island);
            Transform distant = Section("DistantScenery", island);

            convertedMaterials.Clear();
            PrepareBranches();
            ValidateAuthoredRoutes();
            BuildRoad(road.transform, barriers.transform, p);
            BuildBranches(branchRoot, p);
            ValidateRoadSurface();
            ComposeEnvironment(island, p);
            // Geometry changed to the reference: update placements using the existing gameplay components.
            ConfigureRace(race.transform);
            ConfigureResets(track);
            OrganizeRace(race.transform, track);
            InstallMinimap();
            InstallPlayerSelection();
            RegisterScene(FinalScene);
            ConfigureLighting(p);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(ConvertMaterial).ToArray();
            foreach (string helpers in new[] { "Checkpoints", "ResetZones", "RacingLine" })
            {
                Transform helper = track.Find(helpers);
                if (helper != null) foreach (Renderer renderer in helper.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, FinalScene)) throw new InvalidOperationException("Unable to save final island scene.");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = island.gameObject;
            Debug.Log("Pose Kart final island saved. Converted/reused " + convertedMaterials.Count + " unique URP replacement materials. " + FinalScene, island);
        }

        private static Palette CreatePalette()
        {
            const string folder = "Assets/PoseKart/Materials";
            if (!AssetDatabase.IsValidFolder("Assets/PoseKart")) AssetDatabase.CreateFolder("Assets", "PoseKart");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PoseKart", "Materials");
            return new Palette {
                Asphalt = MaterialAt(folder,"Final_Asphalt",new Color(.075f,.09f,.1f)), Stone = MaterialAt(folder,"Final_VillageStone",new Color(.32f,.28f,.22f)),
                Dirt = MaterialAt(folder,"Final_CanyonDirt",new Color(.35f,.19f,.09f)), Cave = MaterialAt(folder,"Final_CaveStone",new Color(.10f,.065f,.045f)),
                Sand = MaterialAt(folder,"Final_Sand",new Color(.82f,.65f,.36f)), Grass = MaterialAt(folder,"Final_Grass",new Color(.12f,.42f,.18f)),
                Rock = MaterialAt(folder,"Final_Rock",new Color(.28f,.22f,.16f)), Ocean = MaterialAt(folder,"Final_Ocean",new Color(.025f,.31f,.52f),.25f),
                Wood = MaterialAt(folder,"Final_Wood",new Color(.28f,.13f,.045f)), Paint = MaterialAt(folder,"Final_Accent",new Color(.95f,.34f,.08f)),
                Metal = MaterialAt(folder,"Final_Metal",new Color(.22f,.29f,.31f),.65f), Glow = MaterialAt(folder,"Final_CaveGlow",new Color(1f,.48f,.10f),0f,true)
            };
        }

        private static Material MaterialAt(string folder, string name, Color color, float metallic = 0f, bool emission = false)
        {
            string path = folder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { name = name };
            m.color = color; m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", .35f);
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 2.5f); }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        private static Material ConvertMaterial(Material source)
        {
            string shaderName = source != null && source.shader != null ? source.shader.name : "";
            if (source != null && source.shader != null && source.shader.isSupported && !ShaderUtil.ShaderHasError(source.shader)
                && (shaderName.StartsWith("Universal Render Pipeline/") || shaderName.StartsWith("UI/") || shaderName.StartsWith("TextMeshPro/"))) return source;
            const string folder = "Assets/PoseKart/Materials/Converted";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PoseKart/Materials", "Converted");
            string key = "Missing";
            if (source != null)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id);
                key = string.IsNullOrEmpty(guid) ? source.name.Replace('/', '_') : guid + "_" + id;
            }
            string path = folder + "/" + key + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null || result.shader == null || !result.shader.isSupported || ShaderUtil.ShaderHasError(result.shader)
                || !result.shader.name.StartsWith("Universal Render Pipeline/"))
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("URP Lit shader is unavailable.");
                bool create = result == null;
                if(create) result = new Material(shader) { name = "PoseKart_" + (source != null ? source.name : "MissingMaterial") };
                else { result.shader = shader; result.shaderKeywords = Array.Empty<string>(); }
                if (source != null)
                {
                    SerializedObject serialized = new SerializedObject(source);
                    SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                    string baseKey = shaderName.StartsWith("Universal") ? "_BaseMap" : "_MainTex";
                    CopyTexture(textures, result, baseKey, "_BaseMap");
                    if (result.GetTexture("_BaseMap") == null) CopyTexture(textures, result, baseKey == "_MainTex" ? "_BaseMap" : "_MainTex", "_BaseMap");
                    CopyTexture(textures,result,"_BumpMap","_BumpMap");
                    CopyTexture(textures,result,"_MetallicGlossMap","_MetallicGlossMap");
                    CopyTexture(textures,result,"_EmissionMap","_EmissionMap");
                    Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
                    result.SetColor("_BaseColor", color);
                    result.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0);
                    result.SetFloat("_Smoothness", Mathf.Min(.35f, source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : .15f));
                    result.SetFloat("_BumpScale", source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1);
                    if (result.GetTexture("_BumpMap") != null) result.EnableKeyword("_NORMALMAP");
                    if (result.GetTexture("_MetallicGlossMap") != null) result.EnableKeyword("_METALLICSPECGLOSSMAP");
                    float mode = source.HasProperty("_Mode") ? source.GetFloat("_Mode") : 0;
                    bool cutout = mode == 1 || source.IsKeywordEnabled("_ALPHATEST_ON") || source.GetTag("RenderType",false)=="TransparentCutout";
                    if (cutout)
                    {
                        result.SetFloat("_AlphaClip",1); result.EnableKeyword("_ALPHATEST_ON"); result.SetFloat("_Cutoff",source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f);
                        result.SetOverrideTag("RenderType","TransparentCutout"); result.renderQueue=2450; result.SetFloat("_Cull",0);
                    }
                    else if (mode >= 2 || source.GetTag("RenderType",false)=="Transparent")
                    {
                        result.SetFloat("_Surface",1); result.SetFloat("_Blend",0); result.SetFloat("_SrcBlend",5); result.SetFloat("_DstBlend",10); result.SetFloat("_ZWrite",0);
                        result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); result.SetOverrideTag("RenderType","Transparent"); result.renderQueue=3000;
                    }
                    if (source.IsKeywordEnabled("_EMISSION"))
                    {
                        result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor",source.HasProperty("_EmissionColor")?source.GetColor("_EmissionColor"):Color.black);
                    }
                }
                if(create) AssetDatabase.CreateAsset(result,path);
                else EditorUtility.SetDirty(result);
            }
            convertedMaterials.Add(result); return result;
        }

        private static void CopyTexture(SerializedProperty entries, Material target, string oldName, string newName)
        {
            if (entries == null) return;
            for (int i=0;i<entries.arraySize;i++)
            {
                SerializedProperty entry=entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue != oldName) continue;
                SerializedProperty data=entry.FindPropertyRelative("second");
                target.SetTexture(newName,data.FindPropertyRelative("m_Texture").objectReferenceValue as Texture);
                target.SetTextureScale(newName,data.FindPropertyRelative("m_Scale").vector2Value);
                target.SetTextureOffset(newName,data.FindPropertyRelative("m_Offset").vector2Value); return;
            }
        }

        // Conservative triangle/AABB separating-axis test in XZ; includes actual mitered road edges.
        private static bool CorridorIntersects(Bounds bounds, float margin = 1.6f)
        {
            if (BranchCorridorIntersects(bounds, margin)) return true;
            for (int i=0;i<Route.Length;i++)
            {
                int n=(i+1)%Route.Length;
                if (bounds.max.y < Mathf.Min(Route[i].y,Route[n].y)-.3f || bounds.min.y > Mathf.Max(Route[i].y,Route[n].y)+9) continue;
                if (TriangleBox(roadEdges[i*2],roadEdges[n*2],roadEdges[i*2+1],bounds,margin)
                    || TriangleBox(roadEdges[i*2+1],roadEdges[n*2],roadEdges[n*2+1],bounds,margin)) return true;
            }
            return false;
        }

        private static bool TriangleBox(Vector3 a, Vector3 b, Vector3 c, Bounds box, float margin)
        {
            Vector2[] v={new Vector2(a.x,a.z),new Vector2(b.x,b.z),new Vector2(c.x,c.z)};
            Vector2 center=new Vector2(box.center.x,box.center.z), extent=new Vector2(box.extents.x+margin,box.extents.z+margin);
            Vector2[] axes={Vector2.right,Vector2.up,new Vector2(-(b.z-a.z),b.x-a.x),new Vector2(-(c.z-b.z),c.x-b.x),new Vector2(-(a.z-c.z),a.x-c.x)};
            foreach(Vector2 axis in axes)
            {
                float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
                foreach(Vector2 point in v){float dot=Vector2.Dot(point,axis);lo=Mathf.Min(lo,dot);hi=Mathf.Max(hi,dot);}
                float mid=Vector2.Dot(center,axis),r=Mathf.Abs(axis.x)*extent.x+Mathf.Abs(axis.y)*extent.y;
                if(hi<mid-r || lo>mid+r)return false;
            }
            return true;
        }

        private static void BuildEdgeFurniture(Transform road, Transform barriers, Palette p)
        {
            var shoulderVertices=new List<Vector3>(); var shoulderTriangles=new List<int>();
            for(int i=0;i<Route.Length;i++)
            {
                int n=(i+1)%Route.Length;
                for(int side=0;side<2;side++)
                {
                    Vector3 a=roadEdges[i*2+side],b=roadEdges[n*2+side];
                    Vector3 outA=Vector3.ProjectOnPlane(a-Route[i],Vector3.up).normalized;
                    Vector3 outB=Vector3.ProjectOnPlane(b-Route[n],Vector3.up).normalized;
                    Bounds shoulderBounds=new Bounds((a+b)*.5f,Vector3.zero); shoulderBounds.Encapsulate(a+outA*1.7f); shoulderBounds.Encapsulate(b+outB*1.7f);
                    bool junction=BranchCorridorIntersects(shoulderBounds,1.6f);
                    int k=shoulderVertices.Count;
                    shoulderVertices.Add(a-Vector3.up*.03f);shoulderVertices.Add(b-Vector3.up*.03f);
                    shoulderVertices.Add(a+outA*1.7f-Vector3.up*.12f);shoulderVertices.Add(b+outB*1.7f-Vector3.up*.12f);
                    if(!junction && side==0) shoulderTriangles.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});
                    else if(!junction) shoulderTriangles.AddRange(new[]{k,k+1,k+2,k+1,k+3,k+2});
                    Vector3 ra=a+outA*2.4f,rb=b+outB*2.4f;
                    int pieces=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(ra,rb)/6f));
                    for(int j=0;j<pieces;j++)
                    {
                        Vector3 start=Vector3.Lerp(ra,rb,j/(float)pieces),end=Vector3.Lerp(ra,rb,(j+1f)/pieces);
                        GameObject beam=Solid("EdgeRail_"+i+"_"+side+"_"+j,PrimitiveType.Cube,barriers,(start+end)*.5f+Vector3.up*.6f,
                            new Vector3(.28f,.9f,Vector3.Distance(start,end)+.05f),i<Node(4)||i>=Node(23)?p.Wood:p.Metal);
                        beam.transform.rotation=Quaternion.LookRotation(end-start,Vector3.up);
                        // Reject unsafe corners/endcaps against ALL route segments, not just their own segment.
                        if(CorridorIntersects(beam.GetComponent<Renderer>().bounds)) UnityEngine.Object.DestroyImmediate(beam);
                    }
                }
            }
            Mesh mesh=new Mesh{name="RoadShoulders"};mesh.SetVertices(shoulderVertices);mesh.SetTriangles(shoulderTriangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            MeshObject("Shoulders",road,SaveMesh(mesh,"Shoulders"),p.Sand,false);
        }

        private static void MeshObject(string name, Transform parent, Mesh mesh, Material material, bool solid)
        {
            GameObject go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=material;if(solid)go.AddComponent<MeshCollider>().sharedMesh=mesh;
        }

        private static void StartArch(Transform parent,int node,Palette p)
        {
            Vector3 f=Vector3.ProjectOnPlane(Route[(node+1)%Route.Length]-Route[node],Vector3.up).normalized,r=Vector3.Cross(Vector3.up,f);
            float span=Widths[node]+8;
            for(int side=-1;side<=1;side+=2)
            {
                GameObject post=Solid("FinishPost",PrimitiveType.Cube,parent,Route[node]+r*(span*.5f*side)+Vector3.up*4,new Vector3(.7f,8,.7f),p.Wood);
                if(CorridorIntersects(post.GetComponent<Renderer>().bounds))UnityEngine.Object.DestroyImmediate(post);
            }
            GameObject banner=Solid("FinishBanner",PrimitiveType.Cube,parent,Route[node]+Vector3.up*9.8f,new Vector3(span,1.2f,.5f),p.Paint);
            banner.transform.rotation=Quaternion.LookRotation(f);UnityEngine.Object.DestroyImmediate(banner.GetComponent<Collider>());
        }

        private static void BuildRoad(Transform road, Transform barriers, Palette p)
        {
            // A persisted, watertight strip is both the visible road and its ONLY driving collider.
            // Track/Road intentionally does not activate the legacy runtime Environment/Road patch.
            var vertices = new Vector3[Route.Length * 2];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < Route.Length; i++)
            {
                int prev = (i + Route.Length - 1) % Route.Length;
                Vector3 incoming = Vector3.ProjectOnPlane(Route[i] - Route[prev], Vector3.up).normalized;
                Vector3 outgoing = Vector3.ProjectOnPlane(Route[(i + 1) % Route.Length] - Route[i], Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, outgoing);
                Vector3 miter = (Vector3.Cross(Vector3.up, incoming) + right).normalized;
                float extent = Mathf.Max(Widths[i], Widths[prev]) * .5f / Mathf.Max(.5f, Vector3.Dot(miter, right));
                vertices[i * 2] = Route[i] + Vector3.up * .16f - miter * extent;
                vertices[i * 2 + 1] = Route[i] + Vector3.up * .16f + miter * extent;
                uv[i * 2] = new Vector2(0, i); uv[i * 2 + 1] = new Vector2(1, i);
            }
            Mesh mesh = new Mesh { name = "FinalDrivingSurface", vertices = vertices, uv = uv, subMeshCount = Route.Length };
            Material[] materials = new Material[Route.Length];
            for (int i=0;i<Route.Length;i++)
            {
                int n=(i+1)%Route.Length; Vector3 a=Route[i], b=Route[n], d=(b-a).normalized; float length=Vector3.Distance(a,b);
                Material mat = RoadMaterial(i, p);
                mesh.SetTriangles(new[] { i*2, n*2, i*2+1, i*2+1, n*2, n*2+1 }, i);
                materials[i] = mat;
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh = SaveMesh(mesh, "Road");
            GameObject surface = new GameObject("DrivingSurface"); surface.transform.SetParent(road, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterials = materials;
            drivingSurface = surface.AddComponent<MeshCollider>(); drivingSurface.sharedMesh = mesh;
            roadEdges = vertices;
            BuildEdgeFurniture(road, barriers, p);
        }

        private static void ConfigureRace(Transform raceRoot)
        {
            foreach(Checkpoint cp in UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(cp.gameObject);
            RacingLine old=UnityEngine.Object.FindFirstObjectByType<RacingLine>(); if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Checkpoint[] cps=new Checkpoint[CheckpointNodes.Length];
            for(int i=0;i<cps.Length;i++) { int node=CheckpointNodes[i]; Vector3 d=(Route[(node+1)%Route.Length]-Route[node]).normalized; GameObject go=new GameObject(i==0?"StartFinish":"CP"+i.ToString("00"));go.transform.SetParent(raceRoot,false);go.transform.position=Route[node]+Vector3.up*2;go.transform.rotation=Quaternion.LookRotation(d); BoxCollider box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.size=new Vector3(Widths[node]+2,4,2);cps[i]=go.AddComponent<Checkpoint>(); }
            GameObject lineGo=new GameObject("RacingLine");lineGo.transform.SetParent(raceRoot,false);RacingLine line=lineGo.AddComponent<RacingLine>();RacingWaypoint[] points=new RacingWaypoint[Route.Length];
            for(int i=0;i<Route.Length;i++){GameObject w=new GameObject("Waypoint_"+i.ToString("00"));w.transform.SetParent(lineGo.transform,false);w.transform.position=Route[i]+Vector3.up*.35f;RacingWaypoint wp=w.AddComponent<RacingWaypoint>();wp.Configure(i,Speed(i));points[i]=wp;} line.Configure(points);
            RaceParticipant[] racers=UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(r=>r.IsPlayer?0:1).ThenBy(r=>r.name).ToArray();
            if(racers.Length!=4) throw new InvalidOperationException("Final map expects the preserved player and three AI racers.");
            Physics.SyncTransforms();
            for (int i = 0; i < racers.Length; i++)
            {
                if (racers[i].transform.Find("Visual/Imported_AdventureBuggy") == null) IntegrateBuggy(racers[i]);
                PlaceOnGrid(racers[i], i);
                AIRacerBrain ai = racers[i].GetComponent<AIRacerBrain>();
                if (ai != null)
                {
                    // Replace the line reference only; retain all existing AI tuning and input wiring.
                    SerializedObject serialized = new SerializedObject(ai);
                    serialized.FindProperty("racingLine").objectReferenceValue = line;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            var camera = UnityEngine.Object.FindFirstObjectByType<PoseKart.Camera.ChaseCameraController>();
            if (camera != null) { camera.SetTarget(racers[0].transform); camera.SnapToTarget(); }
            RaceManager rm=raceRoot.GetComponentInChildren<RaceManager>(true);CheckpointManager cm=raceRoot.GetComponentInChildren<CheckpointManager>(true);RacePositionCalculator pc=raceRoot.GetComponentInChildren<RacePositionCalculator>(true);cm.ConfigureRacers(rm,cps,racers);pc.Configure(cm,racers);rm.ConfigureRacers(cm,racers,pc,TotalLaps);
        }

        private static float Speed(int i) { Vector3 a=Route[(i+Route.Length-1)%Route.Length],b=Route[i],c=Route[(i+1)%Route.Length]; return Mathf.Lerp(1.02f,.68f,Mathf.Clamp01(Vector3.Angle(b-a,c-b)/90f)); }
        private static void ConfigureResets(Transform environment)
        {
            Transform reset = Section("ResetZones", environment); Clear(reset);
            Transform water = GameObject.Find("Environment/Water").transform;
            Transform old = water.Find("Ocean_ResetTrigger");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            // Existing VehicleResetZone is player-specific: one trigger per racer, no runtime redesign.
            RaceParticipant[] racers = UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform oceanReset = Section("Ocean_ResetTrigger", water);
            foreach (RaceParticipant racer in racers)
            {
                CarResetController recovery = racer.GetComponent<CarResetController>();
                if (racer.IsPlayer && recovery == null) throw new InvalidOperationException("Player reset controller missing.");
                // AI already recovers through AIRacerBrain; no keyboard reset component is added to AI.
                if (recovery == null) continue;
                AddReset(oceanReset, racer, recovery, new Vector3(0,-4,12), new Vector3(1100,6,840));
                AddReset(reset, racer, recovery, new Vector3(0,-18,12), new Vector3(1200,20,1200));
            }
        }
        private static void ConfigureLighting(Palette p)
        {
            Light sun=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l=>l.type==LightType.Directional);if(sun!=null){sun.color=new Color(1f,.91f,.72f);sun.intensity=1.25f;sun.transform.rotation=Quaternion.Euler(48,-32,0);sun.shadows=LightShadows.Soft;} RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.61f,.62f);RenderSettings.fog=false;
        }
        private static void IntegrateBuggy(RaceParticipant racer)
        {
            Transform root = racer.transform;
            // Keep the physics root and every gameplay component. Only replace rendered children.
            Transform previous = root.Find("Visual");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                if (collider.transform != root) collider.enabled = false;
            BoxCollider body = root.GetComponent<BoxCollider>();
            if (body == null) throw new InvalidOperationException("Expected existing root BoxCollider on " + root.name);
            body.enabled = true; body.isTrigger = false;
            Transform visual = Section("Visual", root);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BuggyPrefab), visual);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Imported_AdventureBuggy";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (Joint joint in model.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(joint);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
            foreach (MonoBehaviour script in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(script);
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            // Vehicle19's front marker is at +Z (2.17046), rear at -Z (-2.16588).
            // Read those markers in prefab space so the alignment remains independent of the physics root.
            Transform front = model.GetComponentsInChildren<Transform>().First(t => t.name == "font_suspension_marker");
            Transform rear = model.GetComponentsInChildren<Transform>().First(t => t.name == "rear_suspension_marker");
            Vector3 forward = Vector3.ProjectOnPlane(visual.InverseTransformPoint(front.position) - visual.InverseTransformPoint(rear.position), Vector3.up);
            model.transform.localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
            Bounds bounds = VisualBounds(visual);
            float scale = Mathf.Min(body.size.x / bounds.size.x, body.size.z / bounds.size.z);
            model.transform.localScale *= scale;
            bounds = VisualBounds(visual);
            model.transform.localPosition += new Vector3(body.center.x - bounds.center.x,
                body.center.y - body.size.y * .5f - bounds.min.y, body.center.z - bounds.center.z);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(VehicleMaterial).ToArray();
            // Remove only the known old placeholder visual objects, leaving unrelated gameplay children alone.
            string[] obsolete = { "Body_Visual", "Cabin_Visual", "Wheel_FrontLeft", "Wheel_FrontRight", "Wheel_RearLeft", "Wheel_RearRight" };
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
                if (child != null && obsolete.Contains(child.name) && !child.IsChildOf(visual)) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        private static Bounds VisualBounds(Transform space)
        {
            bool initialized = false; Bounds result = default;
            foreach (Renderer renderer in space.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = renderer.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c&1)==0?-1:1, (c&2)==0?-1:1, (c&4)==0?-1:1));
                    corner = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!initialized) { result = new Bounds(corner, Vector3.zero); initialized = true; } else result.Encapsulate(corner);
                }
            }
            if (!initialized || result.size.x < .01f || result.size.z < .01f) throw new InvalidOperationException("Imported buggy has no usable mesh bounds.");
            return result;
        }

        private static Material VehicleMaterial(Material source)
        {
            if (source == null) return null;
            string path = "Assets/PoseKart/Materials/Adventure_" + source.name + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Adventure_" + source.name };
            result.SetTexture("_BaseMap", source.mainTexture);
            result.SetColor("_BaseColor", source.color);
            result.SetFloat("_Smoothness", .25f);
            result.SetFloat("_Metallic", .1f);
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        private static void PlaceOnGrid(RaceParticipant racer, int index)
        {
            float remaining = 7f + index * 5f;
            int end = 0; int start = Route.Length - 1;
            while (remaining > Vector3.Distance(Route[start], Route[end]))
            {
                remaining -= Vector3.Distance(Route[start], Route[end]); end = start; start = (start + Route.Length - 1) % Route.Length;
            }
            Vector3 tangent = Vector3.ProjectOnPlane(Route[end] - Route[start], Vector3.up).normalized;
            Vector3 position = Vector3.Lerp(Route[end], Route[start], remaining / Vector3.Distance(Route[start], Route[end]));
            position += Vector3.Cross(Vector3.up, tangent) * (index % 2 == 0 ? -2.4f : 2.4f);
            Quaternion rotation = Quaternion.LookRotation(tangent);
            BoxCollider collider = racer.GetComponent<BoxCollider>();
            if(collider==null||racer.Body==null)throw new InvalidOperationException("Missing vehicle body at grid placement: "+racer.name);
            float rootY = float.NegativeInfinity;
            // Sample all four bottom corners as well as the center on the actual generated mesh.
            for (int c = 0; c < 5; c++)
            {
                Vector3 offset = collider.center - Vector3.up * collider.size.y * .5f;
                if (c < 4) offset += new Vector3((c&1)==0 ? -collider.size.x*.5f : collider.size.x*.5f, 0, (c&2)==0 ? -collider.size.z*.5f : collider.size.z*.5f);
                offset = rotation * Vector3.Scale(offset, racer.transform.lossyScale);
                Vector3 sample = position + offset;
                if (!drivingSurface.Raycast(new Ray(new Vector3(sample.x, 100, sample.z), Vector3.down), out RaycastHit hit, 200))
                    throw new InvalidOperationException("Grid footprint leaves the road: " + racer.name);
                rootY = Mathf.Max(rootY, hit.point.y - offset.y + .2f);
            }
            position.y = rootY;
            racer.transform.SetPositionAndRotation(position, rotation);
            racer.Body.position = position; racer.Body.rotation = rotation;
            racer.Body.linearVelocity = Vector3.zero; racer.Body.angularVelocity = Vector3.zero;
        }

        private static void AddReset(Transform parent, RaceParticipant racer, CarResetController recovery, Vector3 center, Vector3 size)
        {
            GameObject go = new GameObject(racer.name + "_Reset"); go.transform.SetParent(parent, false); go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>(); box.size = size; box.isTrigger = true;
            go.AddComponent<VehicleResetZone>().Configure(racer.Body, recovery);
        }

        private static void OrganizeRace(Transform race, Transform track)
        {
            Transform checkpoints = Section("Checkpoints", track);
            foreach (Checkpoint cp in race.GetComponentsInChildren<Checkpoint>(true)) cp.transform.SetParent(checkpoints, true);
            RacingLine line = race.GetComponentInChildren<RacingLine>(true); if (line != null) line.transform.SetParent(track, true);
            Transform racers = (GameObject.Find("Racers") ?? new GameObject("Racers")).transform;
            racers.SetParent(null, true);
            foreach (RaceParticipant participant in UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform oldParent = participant.transform.parent;
                participant.transform.SetParent(racers, true);
                if (oldParent != null && oldParent != racers && oldParent.childCount == 0 && oldParent.GetComponents<Component>().Length == 1)
                    UnityEngine.Object.DestroyImmediate(oldParent.gameObject);
            }
        }

        private static Mesh SaveMesh(Mesh mesh, string key)
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/PoseKart", "GeneratedMeshes");
            string path = MeshFolder + "/" + key + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing); return existing;
        }

        private static Transform Section(string name,Transform parent){Transform existing=parent.Find(name);if(existing!=null)return existing;Transform t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private static GameObject Solid(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat){GameObject go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.position=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;return go;}
        private static void Clear(Transform parent){for(int i=parent.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);}
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using PoseKart.AI;
using PoseKart.Race;
using PoseKart.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    // Scene construction is manual only. Reloading scripts must never replace
    // the user's open scene or discard unsaved UI work.
    public static partial class PoseKartFinalIslandSetup
    {
        private const string ReferenceMainScene="Assets/Scenes/PoseKart_ReferenceIsland.unity";
        private const string NitroModelPath="Assets/PoseKart/Asset Them Tu Be Ngoai/nitro-pickups/source/nitro.fbx";
        private static readonly string[] GameplayRoots={"Racers","Race","UI","Main Camera","PoseKart_Minimap"};

        [MenuItem("PoseKart/Reference/Use As Main Race Scene")]
        public static void MakeReferenceIslandMain()
        {
            if(EditorApplication.isPlaying||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
            {
                Debug.LogWarning("PoseKart: Reference Island setup waits until Unity returns to Edit Mode.");
                return;
            }
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            PoseKartReferenceIslandArt.Build();
            Scene reference=EditorSceneManager.OpenScene(ReferenceMainScene,OpenSceneMode.Single);
            foreach(GameObject root in reference.GetRootGameObjects().Where(value=>GameplayRoots.Contains(value.name)||value.name=="Track"||value.name=="Map Presentation Camera").ToArray())
                UnityEngine.Object.DestroyImmediate(root);

            Scene source=EditorSceneManager.OpenScene(FinalScene,OpenSceneMode.Additive);
            foreach(string name in GameplayRoots)
            {
                GameObject root=source.GetRootGameObjects().FirstOrDefault(value=>value.name==name);
                if(root==null)throw new InvalidOperationException("Final Island is missing gameplay root: "+name);
                SceneManager.MoveGameObjectToScene(root,reference);
            }
            EditorSceneManager.CloseScene(source,true);
            SceneManager.SetActiveScene(reference);
            ConfigureReferenceRace();
            BuildReferenceSafetyAndItems();
            BuildReferenceNitroPickups();
            PoseKartReferenceRoadRepair.Repair(GameObject.Find("ReferenceIsland_ART_ONLY").transform);
            BuildReferenceBoostPads();
            InstallPlayerSelection();
            new GameObject("PoseKart_MainSceneConfigured_V14");
            EditorSceneManager.MarkSceneDirty(reference);
            if(!EditorSceneManager.SaveScene(reference,ReferenceMainScene))throw new InvalidOperationException("Unable to save Reference Island main scene.");
            RegisterScene(ReferenceMainScene);
            BuildVehicleSelectScene();
            Debug.Log("PoseKart: Reference Island is now the main race scene with vehicle type and color selection.");
        }

        [MenuItem("PoseKart/Reference/Add Nitro Pickups To Current Scene")]
        public static void AddNitroPickupsToCurrentScene()
        {
            if(EditorApplication.isPlaying||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
            {
                Debug.LogWarning("PoseKart: Nitro pickup placement waits until Unity returns to Edit Mode.");
                return;
            }

            Scene active=SceneManager.GetActiveScene();
            if(!active.IsValid()||active.name!="PoseKart_ReferenceIsland")
            {
                EditorUtility.DisplayDialog("PoseKart Nitro Pickups", "Open PoseKart_ReferenceIsland first, then run this command again.", "OK");
                return;
            }

            BuildReferenceNitroPickups();
            EditorSceneManager.MarkSceneDirty(active);
            if(EditorSceneManager.SaveScene(active))
                Debug.Log("PoseKart: Nitro pickups were placed on the Reference Island racing line.");
        }

        private static void ConfigureReferenceRace()
        {
            Vector3[] path=ReferenceRoute();
            foreach(Checkpoint value in UnityEngine.Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include,FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(value.gameObject);
            RacingLine oldLine=UnityEngine.Object.FindFirstObjectByType<RacingLine>();if(oldLine!=null)UnityEngine.Object.DestroyImmediate(oldLine.gameObject);
            RaceParticipant[] racers=UnityEngine.Object.FindObjectsByType<RaceParticipant>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(value=>value.IsPlayer?0:1).ThenBy(value=>value.name).ToArray();
            Transform race=GameObject.Find("Race").transform;
            int[] checkpointNodes={0,35,70,105,140,175,210,245};
            var checkpoints=new Checkpoint[checkpointNodes.Length];
            for(int i=0;i<checkpointNodes.Length;i++)
            {
                int node=checkpointNodes[i];Vector3 forward=(path[(node+1)%path.Length]-path[node]).normalized;
                GameObject go=new GameObject(i==0?"StartFinish":"CP"+i.ToString("00"));go.transform.SetParent(race,false);
                go.transform.position=path[node]+Vector3.up*2;go.transform.rotation=Quaternion.LookRotation(forward);
                BoxCollider trigger=go.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(20,8,2);checkpoints[i]=go.AddComponent<Checkpoint>();
            }
            GameObject lineObject=new GameObject("ReferenceIsland_RacingLine");lineObject.transform.SetParent(race,false);
            RacingLine line=lineObject.AddComponent<RacingLine>();var waypoints=new RacingWaypoint[path.Length];
            for(int i=0;i<path.Length;i++)
            {
                GameObject go=new GameObject("Waypoint_"+i.ToString("000"));go.transform.SetParent(lineObject.transform,false);go.transform.position=path[i]+Vector3.up*.35f;
                Vector3 a=path[(i+path.Length-1)%path.Length],b=path[i],c=path[(i+1)%path.Length];float speed=Mathf.Lerp(1.02f,.68f,Mathf.Clamp01(Vector3.Angle(b-a,c-b)/90f));
                waypoints[i]=go.AddComponent<RacingWaypoint>();waypoints[i].Configure(i,speed);
            }
            line.Configure(waypoints);
            for(int i=0;i<racers.Length;i++)
            {
                int node=(path.Length-i*4)%path.Length;Vector3 forward=(path[(node+1)%path.Length]-path[node]).normalized;
                Vector3 side=Vector3.Cross(Vector3.up,forward)*(i%2==0?-1.8f:1.8f);Vector3 position=path[node]+side+Vector3.up*.65f;
                racers[i].transform.SetPositionAndRotation(position,Quaternion.LookRotation(forward));racers[i].Body.position=position;racers[i].Body.rotation=Quaternion.LookRotation(forward);
                AIRacerBrain ai=racers[i].GetComponent<AIRacerBrain>();if(ai!=null){SerializedObject serialized=new SerializedObject(ai);serialized.FindProperty("racingLine").objectReferenceValue=line;serialized.ApplyModifiedPropertiesWithoutUndo();}
            }
            RaceManager manager=race.GetComponentInChildren<RaceManager>(true);CheckpointManager checkpointManager=race.GetComponentInChildren<CheckpointManager>(true);RacePositionCalculator calculator=race.GetComponentInChildren<RacePositionCalculator>(true);
            checkpointManager.ConfigureRacers(manager,checkpoints,racers);calculator.Configure(checkpointManager,racers);manager.ConfigureRacers(checkpointManager,racers,calculator,TotalLaps);
            var camera=UnityEngine.Object.FindFirstObjectByType<PoseKart.Camera.ChaseCameraController>();if(camera!=null){camera.SetTarget(racers[0].transform);camera.SnapToTarget();}
            PoseKartMinimap minimap=UnityEngine.Object.FindFirstObjectByType<PoseKartMinimap>();if(minimap!=null)minimap.Configure(PoseKartReferenceIslandArt.MinimapPaths(path),racers);
        }

        private static Vector3[] ReferenceRoute()
        {
            return PoseKartReferenceIslandArt.RaceRoute();
        }

        private static void BuildReferenceSafetyAndItems()
        {
            Vector3[] path=ReferenceRoute();Transform safety=new GameObject("Reference Track Safety").transform;
            for(int i=0;i<path.Length;i+=2)
            {
                int next=(i+2)%path.Length;Vector3 a=path[i],b=path[next],delta=b-a;float length=delta.magnitude;
                Vector3 flatForward=Vector3.ProjectOnPlane(delta,Vector3.up).normalized;Vector3 right=Vector3.Cross(Vector3.up,flatForward);
                foreach(int side in new[]{-1,1})
                {
                    float lateralScale=1f/Mathf.Sqrt(right.x*right.x+right.z*right.z/(PoseKartReferenceIslandArt.RaceStretch.z*PoseKartReferenceIslandArt.RaceStretch.z));
                    Vector3 edge=(a+b)*.5f+right*side*10.5f*PoseKartReferenceIslandArt.RaceScale*lateralScale;
                    if(PoseKartReferenceIslandArt.NearShortcut(edge))continue;
                    GameObject wall=new GameObject("Invisible edge barrier");wall.transform.SetParent(safety,false);wall.transform.position=edge+Vector3.up*1.25f;wall.transform.rotation=Quaternion.LookRotation(flatForward);
                    BoxCollider barrier=wall.AddComponent<BoxCollider>();barrier.size=new Vector3(.35f,3f,Vector3.ProjectOnPlane(delta,Vector3.up).magnitude+.6f);
                    barrier.isTrigger=false;
                    wall.name="Solid track guard - permits outside re-entry";
                    wall.AddComponent<PoseKart.Vehicle.TrackReturnBoundary>().Configure(right*side);
                }
            }
            Transform itemRoot=new GameObject("Track Gift Boxes").transform;
            Material boxMaterial=MaterialAt("Assets/PoseKart/Materials","GiftBox_Cyan",new Color(.08f,.72f,.9f),.15f,true);
            Material ribbonMaterial=MaterialAt("Assets/PoseKart/Materials","GiftRibbon_Gold",new Color(1f,.68f,.08f),.2f,true);
            for(int i=18;i<path.Length;i+=42)
            {
                Vector3 forward=(path[(i+1)%path.Length]-path[i]).normalized;Vector3 right=Vector3.Cross(Vector3.up,Vector3.ProjectOnPlane(forward,Vector3.up).normalized);
                for(int lane=0;lane<4;lane++)
                {
                    GameObject item=new GameObject("Random Gift Box "+(lane+1));item.transform.SetParent(itemRoot,false);item.transform.position=path[i]+right*((lane-1.5f)*3.5f)+Vector3.up*.9f;item.transform.rotation=Quaternion.LookRotation(forward);
                    BoxCollider trigger=item.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(1.35f,1.35f,1.35f);item.AddComponent<KartItemPickup>();
                    GiftPart("Box",item.transform,Vector3.zero,new Vector3(1.2f,1.05f,1.2f),boxMaterial);
                    GiftPart("RibbonVertical",item.transform,new Vector3(0,.02f,0),new Vector3(.18f,1.12f,1.24f),ribbonMaterial);
                    GiftPart("RibbonCross",item.transform,new Vector3(0,.02f,0),new Vector3(1.24f,1.12f,.18f),ribbonMaterial);
                    GiftPart("BowLeft",item.transform,new Vector3(-.2f,.68f,0),new Vector3(.38f,.25f,.16f),ribbonMaterial,Quaternion.Euler(0,0,28));
                    GiftPart("BowRight",item.transform,new Vector3(.2f,.68f,0),new Vector3(.38f,.25f,.16f),ribbonMaterial,Quaternion.Euler(0,0,-28));
                }
            }
            foreach(PoseKart.Vehicle.CarController controller in UnityEngine.Object.FindObjectsByType<PoseKart.Vehicle.CarController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(controller.GetComponent<PoseKart.Vehicle.VehicleWheelAnimator>()==null)controller.gameObject.AddComponent<PoseKart.Vehicle.VehicleWheelAnimator>();
                if(controller.GetComponent<PoseKart.Vehicle.CarResetController>()==null)controller.gameObject.AddComponent<PoseKart.Vehicle.CarResetController>();
                SerializedObject serialized=new SerializedObject(controller);serialized.FindProperty("steeringDegreesPerSecond").floatValue=125f;serialized.FindProperty("steeringAtMaxSpeedMultiplier").floatValue=.6f;serialized.FindProperty("steeringResponse").floatValue=7f;serialized.FindProperty("uphillAssist").floatValue=8f;serialized.ApplyModifiedPropertiesWithoutUndo();
                serialized.FindProperty("maxForwardSpeed").floatValue=38f;
                serialized.FindProperty("acceleration").floatValue=20f;
                serialized.FindProperty("coastingResistance").floatValue=.45f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildReferenceNitroPickups()
        {
            GameObject old=GameObject.Find("Track Nitro Pickups");
            if(old!=null)
                UnityEngine.Object.DestroyImmediate(old);

            GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>(NitroModelPath);
            if(model==null)
            {
                Debug.LogWarning("PoseKart: Could not find the Nitro model at "+NitroModelPath+". Nitro pickup gameplay was not placed.");
                return;
            }

            Vector3[] path=ReferenceRoute();
            Transform root=new GameObject("Track Nitro Pickups").transform;
            int pickupIndex=0;
            for(int i=36;i<path.Length;i+=42)
            {
                int lane=(pickupIndex%3)-1;
                Vector3 forward=(path[(i+1)%path.Length]-path[i]).normalized;
                Vector3 flatForward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
                if(flatForward.sqrMagnitude<.001f)flatForward=Vector3.forward;
                Vector3 right=Vector3.Cross(Vector3.up,flatForward);
                GameObject pickup=(GameObject)PrefabUtility.InstantiatePrefab(model);
                pickup.name="Nitro Pickup "+(pickupIndex+1).ToString("00");
                pickup.transform.SetParent(root,false);
                pickup.transform.SetPositionAndRotation(path[i]+right*(lane*3.5f)+Vector3.up*1.1f,Quaternion.LookRotation(flatForward));
                BoxCollider trigger=pickup.AddComponent<BoxCollider>();
                trigger.isTrigger=true;
                trigger.size=new Vector3(1.4f,1.8f,1.4f);
                trigger.center=new Vector3(0f,.9f,0f);
                PoseKartNitroPickup collectible=pickup.AddComponent<PoseKartNitroPickup>();
                collectible.Configure(.75f,6f);
                pickupIndex++;
            }
        }

        private static void GiftPart(string name,Transform parent,Vector3 position,Vector3 scale,Material material,Quaternion rotation=default)
        {
            GameObject part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;part.transform.localRotation=rotation==default?Quaternion.identity:rotation;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());part.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}

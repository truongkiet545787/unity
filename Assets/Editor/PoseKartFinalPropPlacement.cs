using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartFinalIslandSetup
    {
        private enum SurfaceKind { Land, Water, Dock }
        private readonly struct PropAnchor
        {
            public readonly string name,prefab;
            public readonly Vector3 position,size;
            public readonly float yaw;
            public readonly bool solid;
            public readonly SurfaceKind surface;
            public PropAnchor(string name,string prefab,float x,float z,float yaw,Vector3 size,bool solid=true,SurfaceKind surface=SurfaceKind.Land)
            {this.name=name;this.prefab=prefab;position=new Vector3(x,0,z);this.yaw=yaw;this.size=size;this.solid=solid;this.surface=surface;}
        }
        // The boat prefab's root is Y-up; its mesh children already contain the vendor -90 X conversion.
        // Do not apply that conversion a second time. House prefab authored root rotation is retained too.
        private static readonly Quaternion BoatCorrectionRotation=Quaternion.identity;
        private static int terrainMask;
        private static int EnsureTerrainLayer()
        {
            int layer=LayerMask.NameToLayer("PoseKartLand");
            if(layer<0)
            {
                var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                SerializedProperty layers=settings.FindProperty("layers");
                for(int i=8;i<32;i++)if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {layers.GetArrayElementAtIndex(i).stringValue="PoseKartLand";settings.ApplyModifiedPropertiesWithoutUndo();layer=i;break;}
            }
            if(layer<0){Debug.LogWarning("No free land layer; grounding remains restricted to the terrain collider instance.");layer=0;}
            terrainMask=1<<layer;return layer;
        }
        private static bool LandHit(Vector3 point,out RaycastHit hit)
        {
            hit=default;
            // Direct collider raycast is stricter than Physics.Raycast with a mask: water, racers,
            // checkpoints and reset triggers cannot be candidates even if their layers are misconfigured.
            return terrainSurface!=null&&(terrainMask&(1<<terrainSurface.gameObject.layer))!=0&&
                terrainSurface.Raycast(new Ray(new Vector3(point.x,100,point.z),Vector3.down),out hit,200)&&hit.point.y>WaterLevel+.2f;
        }
        private static Bounds WorldBounds(GameObject go)
        {
            // Transform local renderer bounds explicitly; never depend on deferred renderer.bounds updates.
            Bounds local=VisualBounds(go.transform);bool initialized=false;Bounds result=default;
            for(int i=0;i<8;i++)
            {
                Vector3 corner=local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                corner=go.transform.TransformPoint(corner);
                if(!initialized){result=new Bounds(corner,Vector3.zero);initialized=true;}else result.Encapsulate(corner);
            }
            return result;
        }
        private static bool FootprintClear(Bounds bounds)
        {
            bounds.center=new Vector3(bounds.center.x,0,bounds.center.z);
            bounds.size=new Vector3(bounds.size.x,400,bounds.size.z);
            return !CorridorIntersects(bounds,1.6f);
        }
        private static GameObject PlaceAnchor(PropAnchor anchor,Transform parent,Palette palette)
        {
            GameObject go=null;
            try
            {
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(anchor.prefab);
                if(prefab==null){Debug.LogWarning("Skipping missing decorative prefab: "+anchor.prefab);return null;}
                go=new GameObject(anchor.name);go.transform.SetParent(parent,false);
                GameObject visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,go.transform);
                PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                visual.name="Visual";visual.transform.localPosition=Vector3.zero;
                if(anchor.surface==SurfaceKind.Water)visual.transform.localRotation=BoatCorrectionRotation*visual.transform.localRotation;
                foreach(Joint joint in visual.GetComponentsInChildren<Joint>(true))UnityEngine.Object.DestroyImmediate(joint);
                foreach(Collider collider in visual.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(body);
                foreach(MonoBehaviour script in visual.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(script);
                foreach(Animator animator in visual.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                Bounds local=VisualBounds(go.transform);
                go.transform.localScale=new Vector3(anchor.size.x/local.size.x,anchor.size.y/local.size.y,anchor.size.z/local.size.z);
                go.transform.rotation=Quaternion.Euler(0,anchor.yaw,0);
                go.transform.position=anchor.position-go.transform.TransformVector(new Vector3(local.center.x,local.min.y,local.center.z));
                Bounds bounds=WorldBounds(go);
                if(!FootprintClear(bounds))return SkipAnchor(go,"road/camera corridor");
                if(anchor.surface==SurfaceKind.Water)
                {
                    // Fixed harbor water rectangle and underwater terrain; never ground a boat on terrain.
                    if(bounds.min.x<-380||bounds.max.x>-330||bounds.min.z<-260||bounds.max.z>-155||
                        LandHeight(anchor.position.x,anchor.position.z)>WaterLevel-.3f)return SkipAnchor(go,"not harbor water");
                    go.transform.position+=Vector3.up*(WaterLevel-.25f-bounds.min.y);
                }
                else if(anchor.surface==SurfaceKind.Dock)
                    go.transform.position+=Vector3.up*(.65f-bounds.max.y); // explicit dock deck elevation
                else
                {
                    float low=float.PositiveInfinity,high=float.NegativeInfinity;
                    for(int i=0;i<5;i++)
                    {
                        Vector3 point=bounds.center;
                        if(i<4)point+=new Vector3((i&1)==0?-bounds.extents.x:bounds.extents.x,0,(i&2)==0?-bounds.extents.z:bounds.extents.z);
                        if(Mathf.Abs(point.x)>380||Mathf.Abs(point.z)>300||!LandHit(point,out RaycastHit hit))return SkipAnchor(go,"no dry terrain under footprint");
                        low=Mathf.Min(low,hit.point.y);high=Mathf.Max(high,hit.point.y);
                    }
                    bool rock=anchor.prefab.StartsWith(Nature)||anchor.prefab.Contains("Rock_");
                    float baseY=rock?low-.35f:high;
                    go.transform.position+=Vector3.up*(baseY-bounds.min.y);
                    if(!rock&&high-low>.1f)
                    {
                        GameObject foundation=Solid("Foundation",PrimitiveType.Cube,parent,new Vector3(bounds.center.x,(high+low)*.5f-.06f,bounds.center.z),
                            new Vector3(bounds.size.x,high-low+.12f,bounds.size.z),palette.Stone);
                        if(!anchor.solid)UnityEngine.Object.DestroyImmediate(foundation.GetComponent<Collider>());
                        foundation.transform.SetParent(go.transform,true);
                    }
                }
                if(anchor.solid){BoxCollider box=go.AddComponent<BoxCollider>();box.center=local.center;box.size=local.size;}
                foreach(Renderer renderer in go.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(ConvertMaterial).ToArray();
                return go;
            }
            catch(Exception exception)
            {
                if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                Debug.LogWarning("Skipping authored decoration "+anchor.name+": "+exception.Message);return null;
            }
        }
        private static GameObject SkipAnchor(GameObject go,string reason)
        {Debug.LogWarning("Skipping fixed anchor "+go.name+": "+reason+". Anchor is not relocated.");UnityEngine.Object.DestroyImmediate(go);return null;}
        private static void PlaceAnchors(PropAnchor[] anchors,Transform parent,Palette palette)
        {foreach(PropAnchor anchor in anchors)PlaceAnchor(anchor,parent,palette);}
        private static void SafeSolid(string name,Transform parent,Vector3 position,Vector3 size,Quaternion rotation,Material material)
        {
            GameObject go=Solid(name,PrimitiveType.Cube,parent,position,size,material);go.transform.rotation=rotation;
            if(CorridorIntersects(WorldBounds(go))){Debug.LogWarning("Skipping intersecting landmark: "+name);UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}

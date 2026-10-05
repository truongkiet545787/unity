using System.Linq;
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceRoadRepair
    {
        [MenuItem("PoseKart/Reference/Repair Road Surface (Keep Scene)")]
        public static void RepairCurrentScene()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
            {Debug.LogWarning("Stop Play Mode and wait for compilation before repairing the road.");return;}
            GameObject art=GameObject.Find("ReferenceIsland_ART_ONLY");
            if(art==null){Debug.LogWarning("Open PoseKart_ReferenceIsland first.");return;}
            Repair(art.transform);
            EditorSceneManager.MarkSceneDirty(art.scene);
            Debug.Log("Reference road repaired: overlapping terrain triangles cut out of the road bed. UI and scenery preserved; save with Ctrl+S. No Play Mode test was run.");
        }

        internal static void Repair(Transform art)
        {
            Physics.SyncTransforms();
            MeshCollider[] roads=art.GetComponentsInChildren<MeshCollider>(true).Where(c=>
                c.name=="Continuous sand and dirt road"||c.name=="Village garden alley"||
                c.name=="Canyon ridge cut"||c.name=="Beach inner sand trail").ToArray();
            MeshCollider ground=art.GetComponentsInChildren<MeshCollider>(true).FirstOrDefault(c=>c.name=="Sculpted island");
            if(roads.Length==0||ground==null)return;
            // Project against the actual road triangles, not the nearest centre-line height.
            // A curved, banked strip can differ from that approximation and expose terrain.
            Mesh mesh=Object.Instantiate(ground.sharedMesh);
            mesh.name="Road-fitted island terrain";
            Vector3[] vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=ground.transform.TransformPoint(vertices[i]);
                if(RoadHeight(roads,p,out float y))
                {
                    p.y=Mathf.Min(p.y,y-.18f);
                    vertices[i]=ground.transform.InverseTransformPoint(p);
                }
            }
            mesh.vertices=vertices;
            // Vertex lowering alone cannot prevent the interior of a large terrain
            // triangle from piercing a curved road. Remove the overlapping footprint.
            CutTerrainUnderRoads(mesh,ground.transform,roads);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            // Keep the original generated mesh intact so this targeted repair is reversible.
            const string path="Assets/PoseKart/ReferenceIslandArt/RoadFittedTerrain.asset";
            Mesh saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
            else{Undo.RegisterCompleteObjectUndo(saved,"Fit terrain below road");EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
            Undo.RecordObject(ground,"Repair terrain collision");
            MeshFilter filter=ground.GetComponent<MeshFilter>();Undo.RecordObject(filter,"Repair terrain render mesh");
            ground.sharedMesh=null;ground.sharedMesh=saved;filter.sharedMesh=saved;

            int removed=0;
            foreach(TrackReturnBoundary boundary in Object.FindObjectsByType<TrackReturnBoundary>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(boundary.gameObject.scene!=art.gameObject.scene)continue;
                BoxCollider box=boundary.GetComponent<BoxCollider>();if(box==null)continue;
                bool crosses=false;
                int samples=Mathf.Max(2,Mathf.CeilToInt(box.size.z*box.transform.lossyScale.z/.5f));
                for(int i=0;i<=samples&&!crosses;i++)for(int side=-1;side<=1&&!crosses;side++)
                {
                    Vector3 p=box.transform.TransformPoint(box.center+new Vector3(side*(box.size.x*.5f+1f),0,Mathf.Lerp(-box.size.z*.5f,box.size.z*.5f,i/(float)samples)));
                    crosses=RoadHeight(roads,p,out float y)&&y>=box.bounds.min.y-1f&&y<=box.bounds.max.y+1f;
                }
                if(!crosses)continue;
                // Only overlapping sections are disabled. The outer track guards stay solid.
                Undo.RecordObject(box,"Remove cross-road barrier");box.enabled=false;removed++;
            }
            foreach(Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(checkpoint.gameObject.scene!=art.gameObject.scene)continue;
                BoxCollider trigger=checkpoint.GetComponent<BoxCollider>();
                Undo.RecordObject(trigger,"Keep checkpoint non-solid");trigger.isTrigger=true;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Fitted island terrain below {roads.Length} road surfaces; disabled {removed} overlapping guard sections.");
        }

        private static bool RoadHeight(MeshCollider[] roads,Vector3 p,out float height)
        {
            height=float.PositiveInfinity;bool found=false;
            foreach(MeshCollider road in roads)
            {
                Bounds b=road.bounds;
                if(p.x<b.min.x||p.x>b.max.x||p.z<b.min.z||p.z>b.max.z)continue;
                if(!road.Raycast(new Ray(new Vector3(p.x,b.max.y+2,p.z),Vector3.down),out RaycastHit hit,b.size.y+4))continue;
                height=Mathf.Min(height,hit.point.y);found=true;
            }
            return found;
        }
    }
}

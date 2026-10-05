using PoseKart.Race;
using UnityEditor;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartFinalIslandSetup
    {
        private static void BuildReferenceBoostPads()
        {
            Vector3[] path=ReferenceRoute();
            Transform parent=new GameObject("Boost lanes and stunt ramps").transform;
            Material cyan=MaterialAt("Assets/PoseKart/Materials","BoostLane_Cyan",new Color(.05f,.85f,1f),.35f,true);
            Material gold=MaterialAt("Assets/PoseKart/Materials","JumpLane_Gold",new Color(1f,.5f,.05f),.35f,true);
            foreach(int node in new[]{12,42,72,108,133,205})
            {
                Vector3 forward=(path[node+1]-path[node]).normalized;
                GameObject pad=new GameObject("Uphill boost lane");pad.transform.SetParent(parent,false);
                pad.transform.SetPositionAndRotation(path[node],Quaternion.LookRotation(forward));
                BoxCollider trigger=pad.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.center=Vector3.up*.65f;trigger.size=new Vector3(10,1.6f,3);
                pad.AddComponent<KartBoostPad>().Configure(false,Vector3.zero);
                for(int stripe=0;stripe<3;stripe++)GiftPart("Boost stripe",pad.transform,new Vector3(0,.045f,(stripe-1)*.85f),new Vector3(10,.035f,.36f),cyan);
            }
            // Narrow ramps leave an unobstructed bypass on both sides of the wider road.
            foreach(int node in new[]{158})
            {
                if(path[node+3].y>=path[node].y||path[node+12].y>=path[node+3].y)continue;
                Vector3 straight=(path[176]-path[150]).normalized;
                for(int check=150;check<176;check++)
                    if(Vector3.Angle(straight,path[check+1]-path[check])>1f)throw new System.InvalidOperationException("Stunt ramp requires a straight downhill landing corridor.");
                const int segments=3;
                float lipHeight=Mathf.Max(1.3f,(path[node].y-path[node+segments].y)*.6f+.65f);
                var vertices=new Vector3[(segments+1)*2];var triangles=new int[segments*6];
                for(int i=0;i<=segments;i++)
                {
                    int n=node+i;
                    Vector3 forward=(path[n+1]-path[n]).normalized;
                    Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
                    Vector3 center=path[n]+Vector3.up*(.025f+lipHeight*Mathf.Pow(i/(float)segments,2));
                    vertices[i*2]=center-right*2.6f;vertices[i*2+1]=center+right*2.6f;
                    if(i==segments)continue;
                    int k=i*6,v=i*2;triangles[k]=v;triangles[k+1]=v+2;triangles[k+2]=v+1;triangles[k+3]=v+1;triangles[k+4]=v+2;triangles[k+5]=v+3;
                }
                Mesh mesh=new Mesh{name="Stunt ramp "+node};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
                string asset="Assets/PoseKart/ReferenceIslandArt/StuntRamp_"+node+".asset";
                Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
                if(existing==null)AssetDatabase.CreateAsset(mesh,asset);
                else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(existing);}
                GameObject ramp=new GameObject("Gold stunt ramp");ramp.transform.SetParent(parent,false);
                ramp.AddComponent<MeshFilter>().sharedMesh=mesh;ramp.AddComponent<MeshRenderer>().sharedMaterial=gold;ramp.AddComponent<MeshCollider>().sharedMesh=mesh;
                int tip=node+segments;
                GameObject launch=new GameObject("Stunt takeoff trigger");launch.transform.SetParent(parent,false);
                launch.transform.SetPositionAndRotation(path[tip]+Vector3.up*lipHeight,Quaternion.LookRotation(path[tip+1]-path[tip]));
                BoxCollider trigger=launch.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.center=Vector3.up*.65f;trigger.size=new Vector3(5.2f,1.8f,1.1f);
                launch.AddComponent<KartBoostPad>().Configure(true,path[node+12]+Vector3.up*.65f);
                for(int stripe=0;stripe<3;stripe++)
                    GiftPart("Takeoff stripe",launch.transform,new Vector3(0,.045f,-stripe*.4f),new Vector3(5,.035f,.18f),cyan);
            }
            // Paint the descent edges so the long run-up and landing zone are unmistakable.
            for(int n=150;n<176;n+=2)
            {
                Vector3 forward=(path[n+1]-path[n]).normalized,right=Vector3.Cross(Vector3.up,forward).normalized;
                foreach(int side in new[]{-1,1})
                    GiftPart("Downhill runway edge",parent,path[n]+right*side*7f+Vector3.up*.06f,new Vector3(.3f,.04f,3f),cyan,Quaternion.LookRotation(forward));
            }
            AssetDatabase.SaveAssets();
        }
    }
}

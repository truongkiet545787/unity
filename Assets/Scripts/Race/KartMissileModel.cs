using System.Collections.Generic;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class KartMissileModel : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;

        public static KartMissileModel Attach(GameObject target)
        {
            if (target == null) return null;
            var model = target.GetComponent<KartMissileModel>();
            if (model == null) model = target.AddComponent<KartMissileModel>();
            return model;
        }

        private void Awake()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();
            float[] z={-.85f,-.65f,.55f,1.15f};float[] radius={.16f,.23f,.23f,0f};
            const int sides=12;
            for(int ring=0;ring<4;ring++)for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;
                vertices.Add(new Vector3(Mathf.Cos(angle)*radius[ring],Mathf.Sin(angle)*radius[ring],z[ring]));
                colors.Add(ring>=2?new Color(.95f,.18f,.08f):new Color(.8f,.85f,.9f));
            }
            for(int ring=0;ring<3;ring++)for(int i=0;i<sides;i++)
            {
                int a=ring*sides+i,b=ring*sides+(i+1)%sides,c=a+sides,d=b+sides;
                triangles.AddRange(new[]{a,b,c,b,d,c});
            }
            for(int fin=0;fin<4;fin++)
            {
                Quaternion turn=Quaternion.Euler(0,0,fin*90);int start=vertices.Count;
                vertices.Add(turn*new Vector3(.16f,0,-.8f));vertices.Add(turn*new Vector3(.65f,0,-.85f));vertices.Add(turn*new Vector3(.2f,0,-.12f));
                colors.Add(Color.red);colors.Add(Color.red);colors.Add(Color.red);
                triangles.AddRange(new[]{start,start+1,start+2,start+2,start+1,start});
            }
            mesh=new Mesh{name="Rocket body nose and four fins"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.9f,.25f,.08f);material.SetFloat("_Metallic",.5f);material.SetFloat("_Smoothness",.65f);
            gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private void OnDestroy(){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
    }
}

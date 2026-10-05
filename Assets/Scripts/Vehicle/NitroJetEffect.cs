using UnityEngine;

namespace PoseKart.Vehicle
{
    public sealed class NitroJetEffect : MonoBehaviour
    {
        private CarController car;
        private LineRenderer[] jets;
        private Material material;
        private Material coreMaterial;
        private Light glow;
        private void Start()
        {
            car=GetComponent<CarController>();
            material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor",new Color(.15f,1.5f,3f,1f));
            coreMaterial=new Material(material);coreMaterial.SetColor("_BaseColor",new Color(2f,2.5f,3f,1f));
            jets=new LineRenderer[4];
            for(int i=0;i<jets.Length;i++)
            {
                GameObject go=new GameObject(i%2==0?"Nitro blue plume":"Nitro white core");go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>();jets[i]=line;line.sharedMaterial=i%2==0?material:coreMaterial;
                line.useWorldSpace=false;line.positionCount=4;line.numCapVertices=4;
                line.widthCurve=new AnimationCurve(new Keyframe(0,.45f),new Keyframe(.25f,.65f),new Keyframe(1,0));
                line.widthMultiplier=i%2==0?.65f:.24f;
                line.startColor=i%2==0?Color.cyan:Color.white;line.endColor=new Color(.08f,.2f,1f,1f);line.enabled=false;
            }
            GameObject lamp=new GameObject("Nitro glow");lamp.transform.SetParent(transform,false);lamp.transform.localPosition=new Vector3(0,.5f,-1.5f);
            glow=lamp.AddComponent<Light>();glow.color=Color.cyan;glow.range=5;glow.intensity=0;glow.shadows=LightShadows.None;
        }
        private void LateUpdate()
        {
            bool active=car!=null&&car.IsBoosting;
            glow.intensity=active?4f:0f;
            for(int i=0;i<jets.Length;i++)
            {
                LineRenderer line=jets[i];line.enabled=active;if(!active)continue;
                float length=(i%2==0?3.8f:2.2f)*(1f+.12f*Mathf.Sin(Time.time*43f+i));
                float x=i<2?-.42f:.42f;
                line.SetPosition(0,new Vector3(x,.45f,-1.1f));
                line.SetPosition(1,new Vector3(x,.45f,-1.5f));
                line.SetPosition(2,new Vector3(x,.47f,-1.1f-length*.6f));
                line.SetPosition(3,new Vector3(x,.45f,-1.1f-length));
            }
        }
        private void OnDestroy(){if(material!=null)Destroy(material);if(coreMaterial!=null)Destroy(coreMaterial);}
    }
}

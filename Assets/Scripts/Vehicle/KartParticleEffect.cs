using UnityEngine;

namespace PoseKart.Vehicle
{
    public sealed class KartParticleEffect : MonoBehaviour
    {
        private Material material;
        private Texture2D texture;
        public static ParticleSystem Create(string label,Transform parent,Color color,float lifetime,float size)
        {
            GameObject go=new GameObject(label);go.transform.SetParent(parent,false);
            ParticleSystem particles=go.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=lifetime;main.startSize=new ParticleSystem.MinMaxCurve(size*.6f,size);main.startColor=color;main.startSpeed=0f;main.maxParticles=400;
            var emission=particles.emission;emission.enabled=false;
            var shape=particles.shape;shape.enabled=false;
            var fade=particles.colorOverLifetime;fade.enabled=true;
            Gradient gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=gradient;
            var grow=particles.sizeOverLifetime;grow.enabled=true;grow.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,.35f,1,1.2f));
            var owner=go.AddComponent<KartParticleEffect>();
            Shader shader=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Sprites/Default");
            owner.material=new Material(shader);
            owner.texture=new Texture2D(32,32,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[32*32];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float radius=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
                pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),1.4f));
            }
            owner.texture.SetPixels(pixels);owner.texture.Apply();
            owner.material.mainTexture=owner.texture;
            if(owner.material.HasProperty("_BaseMap"))owner.material.SetTexture("_BaseMap",owner.texture);
            owner.material.SetFloat("_Surface",1);owner.material.SetFloat("_SrcBlend",5);owner.material.SetFloat("_DstBlend",10);owner.material.SetFloat("_ZWrite",0);
            owner.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");owner.material.renderQueue=3000;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=owner.material;
            particles.Play();return particles;
        }
        public static void Burst(Vector3 position,Color color,int count=36)
        {
            ParticleSystem particles=Create("Kart impact sparkle",null,color,.65f,.22f);
            particles.transform.position=position;
            for(int i=0;i<count;i++)particles.Emit(new ParticleSystem.EmitParams{position=position,velocity=Random.onUnitSphere*Random.Range(2f,6f)+Vector3.up*2f},1);
            Destroy(particles.gameObject,1.1f);
        }
        private void OnDestroy(){if(material!=null)Destroy(material);if(texture!=null)Destroy(texture);}
    }
}

using System.Linq;
using PoseKart.Vehicle;
using UnityEngine;

namespace PoseKart.Race
{
    public sealed class KartItemPickup : MonoBehaviour
    {
        public enum ItemType { Boost, HomingMissile, Oil }
        [SerializeField] private ItemType itemType;
        [SerializeField] private float respawnSeconds=6f;
        private Collider trigger;
        private Renderer[] visuals;
        private void Awake(){trigger=GetComponent<Collider>();visuals=GetComponentsInChildren<Renderer>(true);}
        public void Configure(ItemType type){itemType=type;}
        private void OnTriggerEnter(Collider other)
        {
            CarController car=other.attachedRigidbody!=null?other.attachedRigidbody.GetComponent<CarController>():null;if(car==null||!trigger.enabled)return;
            KartItemInventory inventory=car.GetComponent<KartItemInventory>()??car.gameObject.AddComponent<KartItemInventory>();
            if(!inventory.TryCollect((ItemType)Random.Range(0,3)))return;
            KartParticleEffect.Burst(transform.position,new Color(1f,.78f,.16f,1f),48);
            trigger.enabled=false;foreach(Renderer value in visuals)value.enabled=false;Invoke(nameof(Respawn),respawnSeconds);
        }
        private void Respawn(){trigger.enabled=true;foreach(Renderer value in visuals)value.enabled=true;}
        public static void Activate(CarController car,ItemType type)
        {
            if(type==ItemType.Boost)car.ApplySpeedBoost();
            else if(type==ItemType.HomingMissile)LaunchMissile(car);
            else DropOil(car);
        }
        private static void LaunchMissile(CarController owner)
        {
            CarController target=FindObjectsByType<CarController>(FindObjectsSortMode.None).Where(value=>value!=owner&&Vector3.Dot(owner.transform.forward,value.transform.position-owner.transform.position)>.1f).OrderBy(value=>(value.transform.position-owner.transform.position).sqrMagnitude).FirstOrDefault();
            if(target==null){owner.ApplySpeedBoost(1.2f,4f);return;}
            GameObject projectile=new GameObject("AutoAim_Missile");projectile.transform.position=owner.transform.position+owner.transform.forward*2.5f+Vector3.up*.8f;projectile.transform.rotation=Quaternion.LookRotation(target.transform.position-projectile.transform.position);
            projectile.AddComponent<KartMissileModel>();
            SphereCollider collider=projectile.AddComponent<SphereCollider>();collider.radius=.35f;collider.isTrigger=true;Rigidbody body=projectile.AddComponent<Rigidbody>();body.useGravity=false;body.isKinematic=true;
            projectile.AddComponent<HomingKartMissile>().Configure(owner,target);
        }
        private static void DropOil(CarController owner)
        {
            GameObject oil=new GameObject("Oil_Slick");oil.transform.position=owner.transform.position-owner.transform.forward*2+Vector3.up*.04f;
            SphereCollider trigger=oil.AddComponent<SphereCollider>();trigger.isTrigger=true;trigger.radius=1.35f;
            Material material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.035f,.025f,.018f,1);material.SetColor("_BaseColor",new Color(.035f,.025f,.018f,1));material.SetFloat("_Smoothness",.82f);
            for(int i=0;i<5;i++)
            {
                GameObject patch=GameObject.CreatePrimitive(PrimitiveType.Cylinder);Destroy(patch.GetComponent<Collider>());patch.transform.SetParent(oil.transform,false);
                float angle=i*1.9f;patch.transform.localPosition=new Vector3(Mathf.Cos(angle)*.55f,.005f,Mathf.Sin(angle)*.42f);patch.transform.localScale=new Vector3(.75f+i*.08f,.012f,.5f+i*.05f);patch.transform.localRotation=Quaternion.Euler(0,i*37f,0);patch.GetComponent<Renderer>().sharedMaterial=material;
            }
            oil.AddComponent<OilSlick>().Configure(owner);Destroy(oil,8f);
        }
    }
}

using System.Linq;
using UnityEngine;

namespace PoseKart.UI
{
    public sealed class PoseKartVehiclePaint : MonoBehaviour
    {
        public const string VehiclePreferenceKey="PoseKart.SelectedVehicle";
        public const string ColorPreferenceKey="PoseKart.SelectedColor";
        public static readonly string[] VehicleNames={"Rally","Sport","Buggy"};
        public static readonly string[] ColorNames={"Coral","Azure","Sunshine"};
        private static readonly Color[] Colors={new Color(1,.22f,.12f),new Color(.18f,.58f,1),new Color(1,.84f,.14f)};
        [SerializeField] private GameObject[] vehicleModels;
        [SerializeField] private bool useSavedSelection=true;
        private int vehicleIndex,colorIndex;
        public int VehicleCount=>vehicleModels==null?0:vehicleModels.Length;
        public int ColorCount=>Colors.Length;
        public void Configure(GameObject[] models,bool saved){vehicleModels=models;useSavedSelection=saved;}
        private void Start(){if(useSavedSelection)Apply(PlayerPrefs.GetInt(VehiclePreferenceKey,0),PlayerPrefs.GetInt(ColorPreferenceKey,0));}
        public void Apply(int selectedVehicle,int selectedColor)
        {
            if(vehicleModels==null||vehicleModels.Length==0)return;
            vehicleIndex=Mathf.Clamp(selectedVehicle,0,vehicleModels.Length-1);colorIndex=Mathf.Clamp(selectedColor,0,Colors.Length-1);
            for(int i=0;i<vehicleModels.Length;i++)if(vehicleModels[i]!=null)vehicleModels[i].SetActive(i==vehicleIndex);
            GameObject active=vehicleModels[vehicleIndex];if(active==null)return;
            var properties=new MaterialPropertyBlock();
            foreach(Renderer renderer in active.GetComponentsInChildren<Renderer>(true).Where(IsPaintableBody))
            {renderer.GetPropertyBlock(properties);properties.SetColor("_BaseColor",Colors[colorIndex]);properties.SetColor("_Color",Colors[colorIndex]);renderer.SetPropertyBlock(properties);}
        }
        private static bool IsPaintableBody(Renderer renderer)
        {string value=renderer.name.ToLowerInvariant();return value=="body"||value.Contains("body")||value.Contains("roadster");}
    }
}

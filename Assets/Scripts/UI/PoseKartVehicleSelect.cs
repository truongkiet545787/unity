using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace PoseKart.UI
{
    public sealed class PoseKartVehicleSelect : MonoBehaviour
    {
        private const string RaceScene="PoseKart_ReferenceIsland";
        [SerializeField] private PoseKartVehiclePaint preview;
        [SerializeField] private Text vehicleName,colorName;
        [SerializeField] private Button previousVehicle,nextVehicle,previousColor,nextColor,startRace;
        private int selectedVehicle,selectedColor;
        public void Configure(PoseKartVehiclePaint paint,Text vehicleLabel,Text colorLabel,Button vehicleLeft,Button vehicleRight,Button colorLeft,Button colorRight,Button start)
        {preview=paint;vehicleName=vehicleLabel;colorName=colorLabel;previousVehicle=vehicleLeft;nextVehicle=vehicleRight;previousColor=colorLeft;nextColor=colorRight;startRace=start;}
        private void Start()
        {
            selectedVehicle=Mathf.Clamp(PlayerPrefs.GetInt(PoseKartVehiclePaint.VehiclePreferenceKey,0),0,Mathf.Max(0,preview.VehicleCount-1));
            selectedColor=Mathf.Clamp(PlayerPrefs.GetInt(PoseKartVehiclePaint.ColorPreferenceKey,0),0,preview.ColorCount-1);
            previousVehicle.onClick.AddListener(PreviousVehicle);nextVehicle.onClick.AddListener(NextVehicle);
            previousColor.onClick.AddListener(PreviousColor);nextColor.onClick.AddListener(NextColor);startRace.onClick.AddListener(StartRace);Refresh();
        }
        private void PreviousVehicle(){selectedVehicle=(selectedVehicle+preview.VehicleCount-1)%preview.VehicleCount;Refresh();}
        private void NextVehicle(){selectedVehicle=(selectedVehicle+1)%preview.VehicleCount;Refresh();}
        private void PreviousColor(){selectedColor=(selectedColor+preview.ColorCount-1)%preview.ColorCount;Refresh();}
        private void NextColor(){selectedColor=(selectedColor+1)%preview.ColorCount;Refresh();}
        private void Refresh(){preview.Apply(selectedVehicle,selectedColor);vehicleName.text="TYPE  /  "+PoseKartVehiclePaint.VehicleNames[selectedVehicle];colorName.text="COLOR  /  "+PoseKartVehiclePaint.ColorNames[selectedColor];}
        private void StartRace()
        {
            if(!Application.CanStreamedLevelBeLoaded(RaceScene)){vehicleName.text="Reference Island is not in Build Settings";return;}
            PlayerPrefs.SetInt(PoseKartVehiclePaint.VehiclePreferenceKey,selectedVehicle);PlayerPrefs.SetInt(PoseKartVehiclePaint.ColorPreferenceKey,selectedColor);PlayerPrefs.Save();SceneManager.LoadScene(RaceScene);
        }
        private void OnDestroy()
        {
            if(previousVehicle!=null)previousVehicle.onClick.RemoveListener(PreviousVehicle);if(nextVehicle!=null)nextVehicle.onClick.RemoveListener(NextVehicle);
            if(previousColor!=null)previousColor.onClick.RemoveListener(PreviousColor);if(nextColor!=null)nextColor.onClick.RemoveListener(NextColor);if(startRace!=null)startRace.onClick.RemoveListener(StartRace);
        }
    }
}

using PoseKart.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseKart.Race
{
    public sealed class KartItemInventory : MonoBehaviour
    {
        private CarController car;
        private RaceParticipant racer;
        private KartItemPickup.ItemType held;
        private bool hasItem;
        private float aiUseTime;
        private void Awake(){car=GetComponent<CarController>();racer=GetComponent<RaceParticipant>();}
        public bool TryCollect(KartItemPickup.ItemType item)
        {
            if(hasItem)return false;
            held=item;hasItem=true;aiUseTime=Time.time+Random.Range(1.5f,4f);return true;
        }
        private void Update()
        {
            if(!hasItem||car==null||!car.ControlEnabled)return;
            bool player=racer!=null&&racer.IsPlayer;
            if(player?Keyboard.current!=null&&Keyboard.current.spaceKey.wasPressedThisFrame:Time.time>=aiUseTime)UseItem();
        }
        public void UseItem()
        {
            if(!hasItem||car==null||!car.ControlEnabled)return;
            hasItem=false;KartItemPickup.Activate(car,held);
        }
        private void OnGUI()
        {
            if(racer==null||!racer.IsPlayer)return;
            bool hands=car!=null&&car.InputProviderSource is PoseKart.Input.VirtualWheelInput;
            string itemKey=hands?"SKILL (Python)":"SPACE";
            GUI.Box(new Rect(20,Screen.height-65,260,45),hasItem?itemKey+": "+held:"Collect a gift box — "+itemKey+" to use");
            if(car!=null)GUI.Box(new Rect(20,Screen.height-112,260,42),"NITRO "+Mathf.RoundToInt(car.StoredNitro/2.5f*100f)+"% — "+(car.NitroReady?(hands?"NITRO button in Python":"E: activate"):(hands?"DRIFT button + steer to charge":"Hold SHIFT + turn to charge")));
        }
    }
}

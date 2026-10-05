using UnityEngine;

namespace PoseKart.Input
{
    public sealed partial class VirtualWheelInput
    {
        // No webcam, skeleton or wheel is drawn over the game. All three live in Python.
        private void OnGUI()
        {
            if(car==null||tracker==null)return;
            if(!cameraMode)
            {
                if(GUI.Button(new Rect(20,65,240,30),"Connect Python hand controls"))EnableCamera();
                return;
            }
            float width=Mathf.Min(440,Screen.width-40);
            GUI.Box(new Rect(15,60,width+10,132),"");
            if(GUI.Button(new Rect(20,65,width*.48f,27),"Keyboard / Disconnect")){KeyboardMode();return;}
            if(GUI.Button(new Rect(24+width*.48f,65,width*.5f-4,27),"Reconnect Python")){EnableCamera();return;}
            string state=!started?"READY: raise 1 or 2 hands in Python":paused?"PAUSED: restore a steering hand":"DRIVING: "+tracker.Mode;
            GUI.Label(new Rect(20,96,width,23),state);
            GUI.Label(new Rect(20,119,width,40),tracker.Status);
            if(!started)
            {
                bool old=GUI.enabled;GUI.enabled=tracker.TrackingValid&&tracker.HandCount>0;
                if(GUI.Button(new Rect(20,159,width,27),"START HAND DRIVING"))
                {
                    started=true;recovery=0;lastNitro=tracker.NitroSequence;lastSkill=tracker.SkillSequence;lastReset=tracker.ResetSequence;
                }
                GUI.enabled=old;
            }
            else GUI.Label(new Rect(20,162,width,24),$"Hands: {tracker.HandCount} | {tracker.HandFps:0} FPS | Drift / Nitro / Skill / R in Python");
        }
    }
}

using UnityEngine;

namespace PoseKart.Input
{
    public sealed partial class VirtualWheelInput
    {
        // No webcam, skeleton or wheel is drawn over the game. All three live in Python.
        private void OnGUI()
        {
            if(car==null||tracker==null)return;
            float panelY = Mathf.Max(20f, Screen.height - 225f);
            if(!cameraMode)
            {
                GUI.Box(new Rect(15, panelY, 310, 62), "");
                GUI.Label(new Rect(25, panelY + 8, 280, 20), "HAND CONTROL — DISCONNECTED");
                if(GUI.Button(new Rect(25, panelY + 30, 280, 25),"CONNECT PYTHON HAND CONTROLS"))EnableCamera();
                return;
            }
            float width=Mathf.Min(440,Screen.width-40);
            float height = started ? 132f : 164f;
            GUI.Box(new Rect(15, panelY, width+10, height),"");
            GUI.Label(new Rect(25, panelY + 8, width, 20), "HAND CONTROL — " + tracker.Mode.ToUpperInvariant());
            if(GUI.Button(new Rect(20, panelY + 32, width*.48f,27),"Keyboard / Disconnect")){KeyboardMode();return;}
            if(GUI.Button(new Rect(24+width*.48f, panelY + 32, width*.5f-4,27),"Reconnect Python")){EnableCamera();return;}
            string state=!started?"READY: raise 1 or 2 hands in Python":paused?"PAUSED: restore a steering hand":"DRIVING: "+tracker.Mode;
            GUI.Label(new Rect(20, panelY + 64, width, 23),state);
            GUI.Label(new Rect(20, panelY + 87, width, 40),tracker.Status);
            if(!started)
            {
                bool old=GUI.enabled;GUI.enabled=tracker.TrackingValid&&tracker.HandCount>0;
                if(GUI.Button(new Rect(20, panelY + 128,width,27),"START HAND DRIVING"))
                {
                    started=true;recovery=0;lastNitro=tracker.NitroSequence;lastSkill=tracker.SkillSequence;lastReset=tracker.ResetSequence;
                }
                GUI.enabled=old;
            }
            else GUI.Label(new Rect(20, panelY + 110,width,24),$"Hands: {tracker.HandCount} | {tracker.HandFps:0} FPS | Drift / Nitro / Skill / R in Python");
        }
    }
}

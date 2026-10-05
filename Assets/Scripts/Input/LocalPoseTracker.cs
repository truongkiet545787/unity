using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PoseKart.Input
{
    // Legacy component name retained for scene compatibility. Protocol 2 carries controls only.
    public sealed class LocalPoseTracker : MonoBehaviour
    {
        private const string Endpoint="ws://127.0.0.1:8765";
        public string Status { get; private set; }="Start the Python hand server";
        public bool TrackingValid { get; private set; }
        public float Steering { get; private set; }
        public bool Drift { get; private set; }
        public int NitroSequence { get; private set; }
        public int SkillSequence { get; private set; }
        public int ResetSequence { get; private set; }
        public int HandCount { get; private set; }
        public float HandFps { get; private set; }
        public string Mode { get; private set; }="Disconnected";
        public bool Fresh=>Time.unscaledTime-lastPacket<.4f;
        private float lastPacket=-100,receivedAt,requestMs;
        private ClientWebSocket socket;
        private CancellationTokenSource lifetime;
        private int generation;
        private string pending,serverError;

        [Serializable] private sealed class Packet
        {
            public int protocol=0,seq=0,hand_count=0,nitro_seq=0,skill_seq=0,reset_seq=0;
            public bool tracking_valid=false,drift=false,error=false;
            public float age_ms=0,steering=0,hand_fps=0;
            public string status="",mode="";
        }

        public void OpenCamera()
        {
            CloseCamera();
            int session=++generation;
            lifetime=new CancellationTokenSource();socket=new ClientWebSocket();
            Status="Connecting to Python Hands on port 8765...";
            _=ReceiveLoop(socket,lifetime.Token,session);
        }

        private async Task ReceiveLoop(ClientWebSocket connection,CancellationToken token,int session)
        {
            try
            {
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
                {timeout.CancelAfter(8000);await connection.ConnectAsync(new Uri(Endpoint),timeout.Token);}
                if(session!=generation)return;
                byte[] request=Encoding.UTF8.GetBytes("next"),buffer=new byte[4096];
                while(!token.IsCancellationRequested)
                {
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(15000);
                    await connection.SendAsync(new ArraySegment<byte>(request),WebSocketMessageType.Text,true,timeout.Token);
                    using var message=new MemoryStream();
                    WebSocketReceiveResult part;
                    do
                    {
                        part=await connection.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
                        if(part.MessageType==WebSocketMessageType.Close)throw new IOException("Python closed the connection.");
                        if(part.MessageType!=WebSocketMessageType.Text||message.Length+part.Count>16384)
                            throw new IOException("Invalid control packet. Restart the updated Python server.");
                        message.Write(buffer,0,part.Count);
                    }while(!part.EndOfMessage);
                    if(session!=generation)return;
                    pending=Encoding.UTF8.GetString(message.ToArray());
                    receivedAt=Time.unscaledTime;requestMs=(float)clock.Elapsed.TotalMilliseconds;
                    // Unity synchronization context; one outstanding request, no queue of old commands.
                    while(pending!=null&&!token.IsCancellationRequested)await Task.Yield();
                }
            }
            catch(Exception e)
            {
                if(session!=generation)return;
                ClearControl();Mode="Disconnected";
                Status=string.IsNullOrEmpty(serverError)?e.Message+" Restart Python, then Reconnect.":serverError;
            }
            finally{connection.Dispose();if(session==generation)socket=null;}
        }

        private void Update()
        {
            if(pending!=null)
            {
                string json=pending;pending=null;
                try
                {
                    Packet p=JsonUtility.FromJson<Packet>(json);
                    if(p==null||p.protocol!=2)throw new IOException("Old Python server: stop it and reopen Start_Pose_Server.bat.");
                    if(p.error){serverError=p.status;ClearControl();Status=serverError;return;}
                    if(!Finite(p.steering)||!Finite(p.age_ms)||p.age_ms<0)throw new IOException("Invalid hand controls");
                    Status=p.status;Mode=p.mode;HandCount=p.hand_count;HandFps=p.hand_fps;
                    float age=Mathf.Max(p.age_ms,requestMs)*.001f+Time.unscaledTime-receivedAt;
                    lastPacket=Time.unscaledTime-age;
                    TrackingValid=p.tracking_valid&&Fresh;
                    Steering=TrackingValid?Mathf.Clamp(p.steering,-1,1):0;
                    Drift=TrackingValid&&p.drift;
                    NitroSequence=p.nitro_seq;SkillSequence=p.skill_seq;ResetSequence=p.reset_seq;
                }
                catch(Exception e){serverError=e.Message;ClearControl();Status=e.Message;}
            }
            if(!Fresh){TrackingValid=false;Drift=false;Steering=0;}
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private void ClearControl(){TrackingValid=false;Drift=false;Steering=0;lastPacket=-100;HandCount=0;}
        public void CloseCamera()
        {
            generation++;lifetime?.Cancel();socket?.Abort();socket?.Dispose();lifetime?.Dispose();
            socket=null;lifetime=null;pending=null;serverError=null;ClearControl();HandFps=0;Mode="Disconnected";
        }
        private void OnDisable()=>CloseCamera();
    }
}

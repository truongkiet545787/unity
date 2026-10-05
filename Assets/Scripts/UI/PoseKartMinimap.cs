using System;
using UnityEngine;
using UnityEngine.UI;
using PoseKart.Race;

namespace PoseKart.UI
{
    /// <summary>North-up track-network graphic; no camera, layers or RenderTexture dependency.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PoseKartMinimap : MaskableGraphic
    {
        [Serializable] public class Path { public Vector3[] points; public bool closed; }
        [SerializeField] private Path[] roads;
        [SerializeField] private RaceParticipant[] racers;
        [SerializeField] private Vector2 minimum, maximum;
        private static readonly Color[] AiColors = { new Color(.15f,.85f,1),new Color(.8f,.4f,1),new Color(.35f,1,.5f) };

        public void Configure(Path[] paths,RaceParticipant[] participants)
        {
            if(GetComponent<CanvasRenderer>()==null)gameObject.AddComponent<CanvasRenderer>();
            roads=paths;racers=participants==null?null:(RaceParticipant[])participants.Clone();raycastTarget=false;
            minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            maximum=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            if(paths!=null)foreach(Path path in paths)
            {
                if(path?.points==null)continue;
                foreach(Vector3 p in path.points)
                {
                    minimum=Vector2.Min(minimum,new Vector2(p.x,p.z));
                    maximum=Vector2.Max(maximum,new Vector2(p.x,p.z));
                }
            }
            if(float.IsInfinity(minimum.x)){minimum=Vector2.zero;maximum=Vector2.one;}
            if(racers!=null)Array.Sort(racers,(a,b)=>string.CompareOrdinal(a==null?"":a.name,b==null?"":b.name));
            SetVerticesDirty();
        }

        private void LateUpdate(){if(roads!=null&&racers!=null)SetVerticesDirty();}
        private Vector2 Map(Vector3 world)
        {
            Rect rect=rectTransform.rect;
            Vector2 extent=Vector2.Max(maximum-minimum,Vector2.one);
            float scale=Mathf.Max(0,Mathf.Min((rect.width-24)/extent.x,(rect.height-24)/extent.y));
            Vector2 size=extent*scale,origin=rect.center-size*.5f;
            float normalizedX=(world.x-minimum.x)/extent.x,normalizedY=(world.z-minimum.y)/extent.y;
            return origin+new Vector2(normalizedX*size.x,normalizedY*size.y);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(roads==null||roads.Length==0)return;
            for(int k=0;k<roads.Length;k++)
            {
                Path path=roads[k];if(path?.points==null||path.points.Length<2)continue;
                float width=k==0?3.4f:2.6f;Color color=k==0?new Color(.94f,.97f,1):new Color(1,.72f,.12f);
                for(int i=0;i<path.points.Length-(path.closed?0:1);i++)
                    Line(vh,Map(path.points[i]),Map(path.points[(i+1)%path.points.Length]),width,color);
                // Round joins close cracks at tight bends. Branch ends remain clearly highlighted.
                foreach(Vector3 point in path.points)Dot(vh,Map(point),width*.5f,color);
                if(k>0){Dot(vh,Map(path.points[0]),2.5f,color);Dot(vh,Map(path.points[path.points.Length-1]),2.5f,color);}
            }
            if(roads[0]?.points!=null&&roads[0].points.Length>0)
            {
                Vector2 finish=Map(roads[0].points[0]);
                for(int y=0;y<3;y++)for(int x=0;x<4;x++)
                    Square(vh,finish+new Vector2((x-1.5f)*2.5f,(y-1)*2.5f),2.5f,(x+y)%2==0?Color.white:Color.black);
            }
            if(racers==null)return;
            int ai=0;
            foreach(RaceParticipant racer in racers)
            {
                if(racer==null||racer.IsPlayer)continue;
                Vector2 point=Map(racer.transform.position);Dot(vh,point,4.1f,new Color(.02f,.04f,.08f));
                Dot(vh,point,2.9f,AiColors[ai++%AiColors.Length]);
            }
            foreach(RaceParticipant racer in racers)
            {
                if(racer==null||!racer.IsPlayer)continue;
                Vector2 point=Map(racer.transform.position);Vector3 f=racer.transform.forward;
                Vector2 forward=new Vector2(f.x,f.z).normalized;if(forward.sqrMagnitude<.1f)forward=Vector2.up;
                Arrow(vh,point,forward,8,Color.black);Arrow(vh,point,forward,6,new Color(1,.28f,.12f));
            }
        }

        private static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color color)
        {
            if((b-a).sqrMagnitude<.0001f)return;
            Vector2 d=(b-a).normalized,n=new Vector2(-d.y,d.x)*width*.5f;int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);
            vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        private static void Dot(VertexHelper vh,Vector2 point,float radius,Color color)
        {
            int first=vh.currentVertCount;vh.AddVert(point,color,Vector2.zero);
            for(int i=0;i<8;i++){float angle=-i*Mathf.PI*.25f;vh.AddVert(point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);}
            for(int i=0;i<8;i++)vh.AddTriangle(first,first+1+i,first+1+(i+1)%8);
        }
        private static void Square(VertexHelper vh,Vector2 center,float size,Color color)
        {
            int i=vh.currentVertCount;float h=size*.5f;
            vh.AddVert(center+new Vector2(-h,-h),color,Vector2.zero);vh.AddVert(center+new Vector2(-h,h),color,Vector2.zero);
            vh.AddVert(center+new Vector2(h,h),color,Vector2.zero);vh.AddVert(center+new Vector2(h,-h),color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
        private static void Arrow(VertexHelper vh,Vector2 center,Vector2 forward,float size,Color color)
        {
            Vector2 right=new Vector2(forward.y,-forward.x);int i=vh.currentVertCount;
            vh.AddVert(center+forward*size,color,Vector2.zero);
            vh.AddVert(center-forward*size*.65f+right*size*.65f,color,Vector2.zero);
            vh.AddVert(center-forward*size*.3f,color,Vector2.zero);
            vh.AddVert(center-forward*size*.65f-right*size*.65f,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}

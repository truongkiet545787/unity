Shader "PoseKart/WebcamNormalize"
{
    Properties { _MainTex("Camera",2D)="white"{} }
    SubShader { Pass {
        ZTest Always Cull Off ZWrite Off
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex;float _Angle;float _Flip;
        fixed4 frag(v2f_img i):SV_Target
        {
            float2 p=i.uv-.5;float s=sin(_Angle),c=cos(_Angle);
            float2 uv=float2(c*p.x-s*p.y,s*p.x+c*p.y)+.5;
            if(_Flip>.5)uv.y=1-uv.y;
            fixed4 color=tex2D(_MainTex,uv);
            return color;
        }
        ENDCG
    } }
}

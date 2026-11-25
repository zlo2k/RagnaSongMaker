Shader "Ragnarock/Fjord Horizon" {
 Properties { _MainTex("Panorama",2D)="white" {} _Exposure("Exposure",Float)=1 _Rotation("Rotation",Float)=0 }
 SubShader {
  Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
  Cull Off ZWrite Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   sampler2D _MainTex; float _Exposure,_Rotation;
   struct Input { float4 pos:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Output { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
   Output vert(Input v) {
    UNITY_SETUP_INSTANCE_ID(v); Output o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos=UnityObjectToClipPos(v.pos); o.dir=v.pos.xyz; return o;
   }
   half4 frag(Output i):SV_Target {
    float3 d=normalize(i.dir);
    float2 uv=float2(frac(.5-atan2(d.z,d.x)/(2*UNITY_PI)+_Rotation/360),.5+asin(clamp(d.y,-1,1))/UNITY_PI);
    half3 sky=tex2D(_MainTex,uv).rgb*_Exposure;
    return half4(lerp(unity_FogColor.rgb,sky,smoothstep(.015,.22,d.y)),1);
   }
   ENDCG
  }
 }
}

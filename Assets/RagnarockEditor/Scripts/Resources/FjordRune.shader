Shader "Ragnarock/Rune Fade" {
 Properties { _BaseColor("Rune",Color)=(1,1,1,1) }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct Input { float4 pos:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Output { float4 pos:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
   Output vert(Input v) {
    UNITY_SETUP_INSTANCE_ID(v);
    Output o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.pos=TransformObjectToHClip(v.pos.xyz); return o;
   }
   half4 frag(Output i):SV_Target { return _BaseColor; }
   ENDHLSL
  }
 }
}

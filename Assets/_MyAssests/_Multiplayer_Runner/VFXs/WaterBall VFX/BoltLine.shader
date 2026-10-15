Shader "Custom/BoltLine"
{
    Properties
    {
        [HDR] _Color ("Glow Color", Color) = (3, 0.6, 0.12, 1)
        [HDR] _HotColor ("Core Color", Color) = (5, 3, 1.8, 1)
        _Intensity ("Intensity", Range(0, 5)) = 1.3
        _CoreSharp ("Core Sharpness", Range(4, 60)) = 18
        _GlowFalloff ("Glow Falloff", Range(1, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Bolt"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _HotColor;
                float _Intensity;
                float _CoreSharp;
                float _GlowFalloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                ZERO_INITIALIZE(Varyings, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // uv.y runs across the ribbon width: 0.5 is the center
                float d = abs(IN.uv.y - 0.5) * 2.0;
                float core = exp(-d * d * _CoreSharp);
                float glow = exp(-d * _GlowFalloff);
                float3 col = (_HotColor.rgb * core + _Color.rgb * glow * 0.5) * IN.color.a * _Intensity;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}

Shader "VCR/P0/TintPulse"
{
    Properties
    {
        [MainTexture] _MainTex("Base Texture", 2D) = "white" {}
        [MainColor] _Color("Base Color", Color) = (1,1,1,1)
        _TintColor("Tint Color", Color) = (0.6,0.85,1,1)
        _TintStrength("Tint Strength", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _TintColor;
                half _TintStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.uv =
                    TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseColor =
                    SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        input.uv) *
                    _Color;

                baseColor.rgb =
                    lerp(
                        baseColor.rgb,
                        baseColor.rgb * _TintColor.rgb,
                        saturate(_TintStrength));

                return baseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}

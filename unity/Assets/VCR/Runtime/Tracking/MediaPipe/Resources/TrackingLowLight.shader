Shader "Hidden/VCR/TrackingLowLight"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Exposure ("Exposure", Float) = 1.0
        _Gamma ("Gamma", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Exposure;
                float _Gamma;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz);
                output.uv =
                    input.uv *
                    _MainTex_ST.xy +
                    _MainTex_ST.zw;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color =
                    SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        input.uv);

                color.rgb =
                    saturate(
                        color.rgb *
                        _Exposure);

                color.rgb =
                    pow(
                        max(
                            color.rgb,
                            half3(0.0001h, 0.0001h, 0.0001h)),
                        rcp(
                            max(
                                _Gamma,
                                0.0001h)));

                return color;
            }
            ENDHLSL
        }
    }
}

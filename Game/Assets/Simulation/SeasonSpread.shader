// The zoomed-out map while a new season spreads across it (#131): each
// pixel is a map cell, drawn from this season's map once the spread has
// reached it and from last season's until then. _Turned holds which cells
// have turned, from the same spread the art view draws (WorldView2D.Spread),
// so the two views agree cell for cell. Nothing is recoloured on the CPU.
Shader "KingdomWatch/SeasonSpread"
{
    Properties
    {
        _MainTex ("This season", 2D) = "white" {}
        _Previous ("Last season", 2D) = "white" {}
        _Turned ("Which cells have turned", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Previous);
            TEXTURE2D(_Turned);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // All three textures are point-sampled and one texel a cell, so
            // one sampler serves them.
            half4 Fragment(Varyings input) : SV_Target
            {
                half4 colour = SAMPLE_TEXTURE2D(_Turned, sampler_MainTex, input.uv).r > 0.5
                    ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv)
                    : SAMPLE_TEXTURE2D(_Previous, sampler_MainTex, input.uv);
                return colour * input.color;
            }
            ENDHLSL
        }
    }
}

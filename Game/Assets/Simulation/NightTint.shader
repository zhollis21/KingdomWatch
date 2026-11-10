// Night over the map (#21): one quad across the camera's view, multiplying
// what is under it by its vertex colour - white by day, a soft blue at
// night (WorldView2D.DrawNight sets how far). Multiplied rather than laid
// over, so the dark stays dark and nothing turns grey.
Shader "KingdomWatch/NightTint"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend DstColor Zero
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(input.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}

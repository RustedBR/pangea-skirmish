// Low-poly sem luz de cena: sombreamento lambert fixo calculado no próprio shader.
// Existe porque o projeto usa o 2D Renderer da URP, que não ilumina malhas com URP/Lit.
// Escreve profundidade, então os sprites (que testam Z) ficam corretamente atrás/à frente.
Shader "Pangea/LowPoly"
{
    Properties
    {
        _BaseColor ("Cor", Color) = (1,1,1,1)
        _Tint ("Tint (multiplica)", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half   shade      : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                // Luz fixa: de cima, vindo da frente-esquerda (a câmera olha para +Z).
                float3 l = normalize(float3(-0.45, 1.0, -0.55));
                o.shade = 0.50 + 0.50 * saturate(dot(n, l));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(_BaseColor.rgb * _Tint.rgb * i.shade, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

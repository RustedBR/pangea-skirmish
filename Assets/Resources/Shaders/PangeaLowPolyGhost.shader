// Versão translúcida do Pangea/LowPoly para os "fantasmas" da projeção do turno (GDD-020 §5):
// mesmo sombreamento fixo, sem escrever profundidade, misturado por alfa e com uma borda clara
// (fresnel) para o contorno do modelo ler bem sobre o chão.
Shader "Pangea/LowPolyGhost"
{
    Properties
    {
        _BaseColor ("Cor", Color) = (1,1,1,1)
        _Alpha ("Opacidade", Range(0,1)) = 0.4
        _RimColor ("Borda", Color) = (0.75,0.88,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half  _Alpha;
                half4 _RimColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half shade : TEXCOORD0; half rim : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float3 l = normalize(float3(-0.45, 1.0, -0.55));
                o.shade = 0.50 + 0.50 * saturate(dot(n, l));
                float3 wpos = TransformObjectToWorld(v.positionOS.xyz);
                float3 viewDir = normalize(GetWorldSpaceViewDir(wpos));
                o.rim = pow(1.0 - saturate(dot(n, viewDir)), 2.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = lerp(_BaseColor.rgb * i.shade, _RimColor.rgb, i.rim * 0.6);
                return half4(c, saturate(_Alpha + i.rim * 0.25));
            }
            ENDHLSL
        }
    }
    Fallback Off
}

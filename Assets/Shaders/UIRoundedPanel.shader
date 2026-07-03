Shader "PangeaSkirmish/UIRoundedPanel"
{
    // Arredonda os 4 cantos de um Image de UI direto na tela via SDF (signed distance
    // field) — puro "border-radius", sem sprite gerado e sem 9-slice. O raio fica
    // sempre igual não importa o tamanho do painel, e não há "esticar borda" pra dar
    // bug de canto (era o problema da abordagem anterior com sprite procedural).
    // _Size/_Center vêm do RectTransform.rect (unidades locais = px) e são setados
    // uma vez por painel (ver BattleHUD.StyleFFTWindow / ApplyRoundedCorners).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Center ("Rect Center (local px)", Vector) = (0,0,0,0)
        _Size ("Rect Size (px)", Vector) = (100,100,0,0)
        _Radius ("Corner Radius (px)", Float) = 10

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _Center;
            float4 _Size;
            float _Radius;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex   = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color    = v.color * _Color;
                OUT.localPos = v.vertex.xy - _Center.xy;
                return OUT;
            }

            // SDF de retângulo arredondado: negativo = dentro, positivo = fora,
            // magnitude = distância até o contorno.
            float RoundedBoxSDF(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;

                float2 halfSize = _Size.xy * 0.5;
                float d = RoundedBoxSDF(IN.localPos, halfSize, _Radius);

                // Anti-aliasing só na borda externa (calculado por pixel de tela real,
                // não é uma textura pixel-art de baixa resolução).
                float aa = max(fwidth(d), 0.0001);
                col.a *= 1.0 - smoothstep(-aa, aa, d);
                return col;
            }
            ENDCG
        }
    }
}

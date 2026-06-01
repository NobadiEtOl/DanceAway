Shader "Custom/ColorRegionSwap"
{
    Properties
    {
        _MainTex             ("Sprite",            2D)    = "white" {}
        _MaskTex             ("Region Mask",       2D)    = "black" {}
        // ── Target recolor values ─────────────────────────────────────────────
        // Set each to the ORIGINAL sprite color of that region so nothing changes
        // at default. The color picker overwrites these at runtime.
        _Color1              ("Region 1 Color",    Color) = (0.6, 0.6, 0.6, 1)
        _Color2              ("Region 2 Color",    Color) = (0.6, 0.6, 0.6, 1)
        _Color3              ("Region 3 Color",    Color) = (0.6, 0.6, 0.6, 1)

        // ── Reference luminances ──────────────────────────────────────────────
        // Sample the BRIGHTEST pixel inside each masked region from your sprite
        // in Krita (e.g. R:200 G:200 B:200 sRGB → linear ≈ 0.58). Enter that
        // linear luminance here. Equal to dot(linearRGB, (0.299,0.587,0.114)).
        _RefLum1             ("Region 1 Ref Lum",  Range(0.001,1)) = 0.6
        _RefLum2             ("Region 2 Ref Lum",  Range(0.001,1)) = 0.6
        _RefLum3             ("Region 3 Ref Lum",  Range(0.001,1)) = 0.6

        // ── Mask identification colors ────────────────────────────────────────
        // Set each to the exact color you painted in your mask PNG (as sRGB).
        // The shader matches mask pixels to the nearest of these three colors.
        [Gamma] _MaskColor1  ("Mask Color 1",      Color) = (1, 0, 0, 1)
        [Gamma] _MaskColor2  ("Mask Color 2",      Color) = (0, 0, 1, 1)
        [Gamma] _MaskColor3  ("Mask Color 3",      Color) = (0, 1, 0, 1)
        // Pixels whose closest mask-color distance exceeds this are left unchanged.
        _MaskThreshold       ("Mask Threshold",    Range(0.05, 0.6)) = 0.3

        // Unity UI stencil support — set automatically by Unity masks/canvases.
        [HideInInspector] _StencilComp    ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil        ("Stencil ID",         Float) = 0
        [HideInInspector] _StencilOp      ("Stencil Operation",  Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask",Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask      ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "PreviewType"     = "Plane"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            fixed4    _Color1;
            fixed4    _Color2;
            fixed4    _Color3;
            float     _RefLum1;
            float     _RefLum2;
            float     _RefLum3;
            fixed4    _MaskColor1;
            fixed4    _MaskColor2;
            fixed4    _MaskColor3;
            float     _MaskThreshold;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Multiply by vertex color so Unity UI tint and CanvasGroup alpha work correctly.
                fixed4 main = tex2D(_MainTex, i.uv) * i.color;
                fixed4 mask = tex2D(_MaskTex, i.uv);

                float lum = dot(main.rgb, float3(0.299, 0.587, 0.114));

                // Find which mask region this pixel belongs to by nearest-color distance.
                float d1 = distance(mask.rgb, _MaskColor1.rgb);
                float d2 = distance(mask.rgb, _MaskColor2.rgb);
                float d3 = distance(mask.rgb, _MaskColor3.rgb);
                float minDist = min(d1, min(d2, d3));

                // Only recolor if the pixel is close enough to one of the mask colors.
                if (minDist < _MaskThreshold)
                {
                    if (d1 <= d2 && d1 <= d3)
                        return fixed4(_Color1.rgb * (lum / max(_RefLum1, 0.001)), main.a);
                    if (d2 <= d1 && d2 <= d3)
                        return fixed4(_Color2.rgb * (lum / max(_RefLum2, 0.001)), main.a);
                    return fixed4(_Color3.rgb * (lum / max(_RefLum3, 0.001)), main.a);
                }

                // Unmasked pixels (outlines, transparent areas) pass through unchanged.
                return main;
            }
            ENDCG
        }
    }
}

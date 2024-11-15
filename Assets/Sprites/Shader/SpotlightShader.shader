Shader "Custom/RadialGradientShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Radius", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            fixed4 _Color;
            float _Radius;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2 - 1; // Shift UVs from (0,1) to (-1,1)
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calculate distance from the center (0, 0)
                float dist = length(i.uv);

                // Create gradient with reversed opacity
                float alpha = smoothstep(_Radius, 0, dist);
                
                // Overlay color effect
                fixed3 overlayColor = lerp(_Color.rgb, _Color.rgb + (1.0 - _Color.rgb) * alpha, alpha);

                return fixed4(overlayColor, alpha);
            }
            ENDCG
        }
    }
}

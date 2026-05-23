Shader "dennokoworks/ColorChangeShader"
{
    Properties
    {
        [Header(HSV Settings)]
        _Hue ("Hue", Range(0, 1)) = 0.0
        _Saturation ("Saturation", Range(0, 1)) = 1.0
        _Value ("Value", Range(0, 1)) = 1.0
        
        [Header(Emission Settings)]
        _Emission ("Emission Intensity", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        LOD 100
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            // GPU Instancing Support
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // GPU Instancing properties buffer
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Hue)
                UNITY_DEFINE_INSTANCED_PROP(float, _Saturation)
                UNITY_DEFINE_INSTANCED_PROP(float, _Value)
                UNITY_DEFINE_INSTANCED_PROP(float, _Emission)
            UNITY_INSTANCING_BUFFER_END(Props)

            // Branchless, high-efficiency HSV to RGB conversion
            // input: float3(h, s, v) where each component is in range [0, 1]
            // output: float3(r, g, b)
            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // Fetch property values from the instance buffer (compatible with MaterialPropertyBlock)
                float h = UNITY_ACCESS_INSTANCED_PROP(Props, _Hue);
                float s = UNITY_ACCESS_INSTANCED_PROP(Props, _Saturation);
                float v = UNITY_ACCESS_INSTANCED_PROP(Props, _Value);
                float emission = UNITY_ACCESS_INSTANCED_PROP(Props, _Emission);

                float3 rgbColor = hsv2rgb(float3(h, s, v));
                
                // Apply emission as a post-process boost (Emission 0 = base color, >0 = HDR bloom boost)
                float3 finalColor = rgbColor * (1.0 + emission);

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

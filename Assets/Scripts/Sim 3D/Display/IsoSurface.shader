
// source https://gamedevbill.com/unity-vertex-shader-and-geometry-shader-tutorial/
Shader "Unlit/IsoSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        CGINCLUDE
            #include "UnityCG.cginc"



            struct v2g
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct g2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;		
            
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
			StructuredBuffer<float3> TrianglesPosTemp;
			StructuredBuffer<float> TriangleCount;
		#endif

        SamplerState linear_clamp_sampler;

        float scale;
        float3 colour;

        sampler2D ColourMap;

            v2g vert ( uint instanceID : SV_InstanceID)
            {
                v2g o;          
                
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                float2 texcoord = {TrianglesPosTemp[unity_InstanceID].w/1273,0};
                o.uv = texcoord;
                float4 position = TrianglesPosTemp[unity_InstanceID];
                position.w = 1
                o.vertex = UnityObjectToClipPos(position);
                o.normal = {0,0,0,0};
        #endif
                return o;
            }			
            
            void setup()
			{
			#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float3 pos = TrianglesPosTemp[2*unity_InstanceID].xyz;

				unity_ObjectToWorld._11_21_31_41 = float4(scale, 0, 0, 0);
				unity_ObjectToWorld._12_22_32_42 = float4(0, scale, 0, 0);
				unity_ObjectToWorld._13_23_33_43 = float4(0, 0, scale, 0);
				unity_ObjectToWorld._14_24_34_44 = float4(pos, 1);
				unity_WorldToObject = unity_ObjectToWorld;
				unity_WorldToObject._14_24_34 *= -1;
				unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;

			#endif
			}
            [maxvertexcount(15)]
            void geom(v2g input[15], inout TriangleStream<g2f> triStream)
            {
                
			#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED

                for(int i = 0; i < TriangleCount[unity_InstanceID]; i++)
                {
                    g2f o;
                    int tmp = i * 3;
                    float3 normal = normalize(cross(input[tmp + 1].vertex - input[tmp + 0].vertex, input[tmp + 2].vertex - input[tmp + 0].vertex));
                    
                    for(int ij = 0; j < 3; j++)
                    {
                        float4 vert = input[tmp + j].vertex;
                        o.vertex = UnityObjectToClipPos(vert);
                        UNITY_TRANSFER_FOG(o,o.vertex);
                        o.uv = input[tmp + j].uv;
                        o.normal = UnityObjectToWorldNormal((normal));

                        #if UNITY_PASS_SHADOWCASTER
                        o.vertex = UnityApplyLinearShadowBias(o.vertex);
                        #endif
                        triStream.Append(o);
                    }
    
                    triStream.RestartStrip();
                }
             #endif



            }
        ENDCG
        
        Pass
        {
            Tags { "RenderType"="Opaque" "LightMode" = "ForwardBase"}
            LOD 100
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase
            #pragma shader_feature IS_LIT

            fixed4 frag (g2f i) : SV_Target
            {
                // orangy color
                fixed4 col = fixed4(0.9,0.7,0.1,1);
                //lighting
                fixed light = saturate (dot (normalize(_WorldSpaceLightPos0), i.normal));
                col.rgb *= light;    
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
        
        Pass
        {
        Tags { "RenderType"="Opaque" "LightMode" = "ShadowCaster" }
        LOD 100
        CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment fragShadow
            #pragma target 4.6
            #pragma multi_compile_shadowcaster
            float4 fragShadow(g2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }   
        ENDCG
        }
    }
}

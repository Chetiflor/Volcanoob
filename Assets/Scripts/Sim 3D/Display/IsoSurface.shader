// source https://gamedevbill.com/unity-vertex-shader-and-geometry-shader-tutorial/
Shader "Unlit/IsoSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            
        CGINCLUDE
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

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
			StructuredBuffer<float4> Vertices;
			StructuredBuffer<float> Temperatures;
			StructuredBuffer<int> Mask;
		#endif

        SamplerState linear_clamp_sampler;

        float scale;
        float3 colour;

        sampler2D ColourMap;

            v2g vert ()
            {
                v2g o;          
                UNITY_INITIALIZE_OUTPUT(v2g, o);
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                float2 texcoord = float2 (Temperatures[unity_InstanceID],0);
                o.uv = texcoord;
                float4 position = Vertices[unity_InstanceID];
                position.w = 1;
                o.vertex = UnityObjectToClipPos(position);
                o.normal = float3(0,0,0);
        #endif
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }			

            void setup()
			{
			#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float3 pos = Vertices[unity_InstanceID].xyz;

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
            void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
            {

			#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED


                g2f o;
                float3 normal = normalize(cross(input[tmp + 1].vertex.xyz - input[tmp + 0].vertex.xyz, input[tmp + 2].vertex.xyz - input[tmp + 0].vertex.xyz));
                if(Mask[instanceID/3]==1)
                {
                    for(int j = 0; j < 3; j++)
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
        }

        


    }
}
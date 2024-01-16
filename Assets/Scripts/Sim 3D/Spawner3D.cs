using Unity.Mathematics;
using UnityEngine;

public class Spawner3D : MonoBehaviour
{
    public int numParticlesPerAxis;
    public Vector3 centre;
    public float size;
    public float3 initialVel;
    public float jitterStrength;
    public bool showSpawnBounds;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        int numPoints = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;
        float3[] points = new float3[numPoints];
        float3[] velocities = new float3[numPoints];
        float[] temperatures = new float[numPoints];
        float4[] predictedTemperaturesViscositiesConductivitiesCapacities = new float4[numPoints];
        float3[] zeros = new float3[numPoints];
        float3[] vdwValues = new float3[numPoints];

        int i = 0;

        for (int x = 0; x < numParticlesPerAxis; x++)
        {
            for (int y = 0; y < numParticlesPerAxis; y++)
            {
                for (int z = 0; z < numParticlesPerAxis; z++)
                {
                    float tx = x / (numParticlesPerAxis - 1f);
                    float ty = y / (numParticlesPerAxis - 1f);
                    float tz = z / (numParticlesPerAxis - 1f);

                    float px = (tx - 0.5f) * size + centre.x;
                    float py = (ty - 0.5f) * size + centre.y;
                    float pz = (tz - 0.5f) * size + centre.z;
                    float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                    points[i] = new float3(px, py, pz) + jitter;
                    velocities[i] = initialVel;
                    float viscosity = 0.1f;
                    temperatures[i] = 273+1000*ty;
                    float thermicConductivity = 1000f+tx*100f;
                    float thermicCapacity = 4.184f;
                    predictedTemperaturesViscositiesConductivitiesCapacities[i] = new float4(temperatures[i],viscosity,thermicConductivity,thermicCapacity);
                
                    zeros[i] = new float3(0,0,0);
                    float p = 22120000f;
                    float t = 374f;
                    float R =  	8.3f;
                    float a = 27 * R * R * t * t / (64 * p);
                    float b = 8 * R * t / p;
                    vdwValues[i] = new float3(0.018f, a, b);
                    i++;
                }
            }
        }

        return new SpawnData() { points = points, velocities = velocities, temperatures = temperatures, zeros=zeros, predictedTemperaturesViscositiesConductivitiesCapacities=predictedTemperaturesViscositiesConductivitiesCapacities, vdwValues=vdwValues};
    }

    public struct SpawnData
    {
        public float3[] points;
        public float3[] velocities;
        public float[] temperatures;
        public float[] thermicConductivities;
        public float[] viscosities;
        public float3[] zeros;
        public float4[] predictedTemperaturesViscositiesConductivitiesCapacities;
        public float3[] vdwValues;
    }

    void OnValidate()
    {
        debug_numParticles = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;
    }

    void OnDrawGizmos()
    {
        if (showSpawnBounds && !Application.isPlaying)
        {
            Gizmos.color = new Color(1, 1, 0, 0.5f);
            Gizmos.DrawWireCube(centre, Vector3.one * size);
        }
    }
}

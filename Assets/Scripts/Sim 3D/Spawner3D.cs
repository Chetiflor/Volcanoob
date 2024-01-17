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
    const float R = 8.3f;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        int numPoints = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;
        float3[] points = new float3[numPoints];
        float3[] velocities = new float3[numPoints];
        float3[] positionsVelocities =new float3[2*numPoints];

        float[] temperatures = new float[numPoints];
        float4[] predictedTemperaturesViscositiesConductivitiesCapacities = new float4[numPoints];
        float3[] accelerations = new float3[numPoints*4];
        int constantCount = 3;
        float[] constants = new float[numPoints*constantCount];

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

                    positionsVelocities[2*i] = new float3(px, py, pz) + jitter;
                    positionsVelocities[2*i+1] = initialVel;

                    float viscosity = 0.0f;
                    temperatures[i] = 273+1000*ty;
                    float thermicConductivity = 1000f+tx*100f;
                    float thermicCapacity = 4.184f;
                    predictedTemperaturesViscositiesConductivitiesCapacities[i] = new float4(temperatures[i],viscosity,thermicConductivity,thermicCapacity);

                    // water
                    float M = 0.018f;
                    float p = 22120000f;
                    float t = 374f;

                    float a = 27 * R * R * t * t / (64 * p);
                    float b = 8 * R * t / p;
                    constants[constantCount*i+0]=M;
                    constants[constantCount*i+1]=a;
                    constants[constantCount*i+2]=b;
                    i++;
                }
            }
        }

        return new SpawnData() { points = points, velocities = velocities, positionsVelocities=positionsVelocities, temperatures = temperatures, predictedTemperaturesViscositiesConductivitiesCapacities=predictedTemperaturesViscositiesConductivitiesCapacities, constants=constants};
    }

    public struct SpawnData
    {
        public float3[] points;
        public float3[] velocities;
        public float3[] positionsVelocities;
        public float[] temperatures;
        public float[] thermicConductivities;
        public float[] viscosities;
        public float3[] partialAccelerations;
        public float4[] predictedTemperaturesViscositiesConductivitiesCapacities;
        public float[] constants;
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

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
    public float R;
    public int stateVariablesStride;
    public int constantsStride;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        int numPoints = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;

        float3[] positionsVelocities =new float3[2*numPoints];
        float[] temperatures = new float[numPoints];
        float[] stateVariables = new float[numPoints * stateVariablesStride];
        float[] constants = new float[numPoints * constantsStride];

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
                    positionsVelocities[2*i] = new float3(px, py, pz) + jitter;
                    positionsVelocities[2*i+1] = initialVel;

                    temperatures[i] = 273+1000*ty;

                    // water

                    float viscosity = 0.0f;
                    float thermicConductivity = 598f;
                    float thermicCapacity = 4185f;

                    stateVariables[stateVariablesStride * i + 0] = 0;
                    stateVariables[stateVariablesStride * i + 1] = 0;
                    stateVariables[stateVariablesStride * i + 2] = viscosity;
                    stateVariables[stateVariablesStride * i + 3] = thermicConductivity;
                    stateVariables[stateVariablesStride * i + 4] = thermicCapacity;
                    float M = 0.018f;
                    float p = 22120000f;
                    float t = 374f;

                    float a = 27 * R * R * t * t / (64 * p);
                    float b = 8 * R * t / p;
                    constants[constantsStride * i + 0] = M;
                    constants[constantsStride * i + 1] = a;
                    constants[constantsStride * i + 2] = b;
                    i++;
                }
            }
        }

        return new SpawnData() {  positionsVelocities = positionsVelocities, temperatures = temperatures, stateVariables = stateVariables, constants = constants};
    }

    public struct SpawnData
    {
        public float3[] positionsVelocities;
        public float[] temperatures;
        public float[] stateVariables;
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

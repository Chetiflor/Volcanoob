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
    public float molesByParticle;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        int numPoints = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;

        float3[] positionsVelocities =new float3[2*numPoints];
        float[] temperatures = new float[numPoints];
        float[] stateVariables = new float[numPoints * stateVariablesStride];

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

                    float viscosity = 0f;
                    float thermicConductivity = 1000000f;
                    float thermicCapacity = 0.4185f;
                    float molarMass = 0.018f; // molar mass
                    float molarVolume = 55555;
                    float volumicMass = 1000;
                    float n = molarMass*molesByParticle; // matter quantity
                    float pCritic = 22120000f;
                    float tCritic = 374f;

                    float a = 27 * R * R * tCritic * tCritic / (64 * pCritic);
                    float b = 8 * R * tCritic / pCritic;

                    stateVariables[stateVariablesStride * i + 0] = 0;
                    stateVariables[stateVariablesStride * i + 1] = 0;
                    stateVariables[stateVariablesStride * i + 2] = viscosity;
                    stateVariables[stateVariablesStride * i + 3] = thermicConductivity;
                    stateVariables[stateVariablesStride * i + 4] = thermicCapacity;
                    stateVariables[stateVariablesStride * i + 5] = volumicMass;
                    stateVariables[stateVariablesStride * i + 6] = a;
                    stateVariables[stateVariablesStride * i + 7] = b;
                    stateVariables[stateVariablesStride * i + 8] = molarMass;
                    i++;
                }
            }
        }

        return new SpawnData() {  positionsVelocities = positionsVelocities, temperatures = temperatures, stateVariables = stateVariables};
    }

    public struct SpawnData
    {
        public float3[] positionsVelocities;
        public float[] temperatures;
        public float[] stateVariables;
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

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
    public float molesByParticle;
    public int constantsBufferStride;
    public int stateVariablesBufferStride;
    public int Nx,Ny,Nz;
    public Transform simTransform;

    [Header("Info")]
    public int debug_numParticles;

    public SpawnData GetSpawnData()
    {
        int numPoints = numParticlesPerAxis * numParticlesPerAxis * numParticlesPerAxis;

        float3[] gridPositions =new float3[Nx*Ny*Nz];
        float3[] positionsVelocities =new float3[2*numPoints];
        float[] temperatures = new float[numPoints];
        float[] constants = new float[numPoints * constantsBufferStride];
        float[] stateVariables = new float[numPoints * stateVariablesBufferStride];

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

                    // float px = (tx - 0.5f) * simTransform.localScale.x + simTransform.position.x;
                    // float py = (ty - 0.5f) * simTransform.localScale.y + simTransform.position.y;
                    // float pz = (tz - 0.5f) * simTransform.localScale.z + simTransform.position.z;

                    float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                    positionsVelocities[2*i] = new float3(px, py, pz) + jitter;
                    positionsVelocities[2*i+1] = initialVel;

                    temperatures[i] = 1273;

                    // water

                    float viscosity = 10000f;
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


                    constants[constantsBufferStride * i + 0] = a;
                    constants[constantsBufferStride * i + 1] = b;
                    constants[constantsBufferStride * i + 2] = viscosity;
                    constants[constantsBufferStride * i + 3] = thermicConductivity;
                    constants[constantsBufferStride * i + 4] = thermicCapacity;
                    constants[constantsBufferStride * i + 5] = volumicMass;
                    constants[constantsBufferStride * i + 6] = molarMass;
                    constants[constantsBufferStride * i + 7] = 0;

                    stateVariables[stateVariablesBufferStride * i + 0] = 0;
                    stateVariables[stateVariablesBufferStride * i + 1] = 0;
                    stateVariables[stateVariablesBufferStride * i + 2] = viscosity;
                    stateVariables[stateVariablesBufferStride * i + 3] = thermicConductivity;
                    stateVariables[stateVariablesBufferStride * i + 4] = thermicCapacity;
                    stateVariables[stateVariablesBufferStride * i + 5] = volumicMass;
                    stateVariables[stateVariablesBufferStride * i + 6] = a;
                    stateVariables[stateVariablesBufferStride * i + 7] = b;

                    i++;
                }
            }
        }
        
        for (int z = 0; z < Nz; z++)
        {
            for (int y = 0; y < Ny; y++)
            {
                for (int x = 0; x < Nx; x++)
                {
                    float tx = x / (Nx - 1f);
                    float ty = y / (Ny - 1f);
                    float tz = z / (Nz - 1f);

                    // float px = (tx - 0.5f) * simTransform.localScale.x + simTransform.position.x;
                    // float py = (ty - 0.5f) * simTransform.localScale.y + simTransform.position.y;
                    // float pz = (tz - 0.5f) * simTransform.localScale.z + simTransform.position.z;

                    float px = (tx - 0.5f) * size + centre.x;
                    float py = (ty - 0.5f) * size + centre.y;
                    float pz = (tz - 0.5f) * size + centre.z;

                    float3 pos = new float3(px,py,pz);

                    gridPositions[z*Nx*Ny + y*Nx + x] = pos;

                    i++;
                }
            }
        }

        return new SpawnData() { gridPositions = gridPositions, positionsVelocities = positionsVelocities, temperatures = temperatures, constants = constants, stateVariables = stateVariables};
    }

    public struct SpawnData
    {
        public float3[] positionsVelocities;
        public float3[] gridPositions;
        public float[] temperatures;
        public float[] constants;
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

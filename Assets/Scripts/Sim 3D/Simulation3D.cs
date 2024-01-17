using UnityEngine;
using Unity.Mathematics;

public class Simulation3D : MonoBehaviour
{
    public event System.Action SimulationStepCompleted;


    const float R = 8.3f;

    [Header("Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep;
    public int iterationsPerFrame;
    public float gravity = -10;
    [Range(0, 1)] public float collisionDamping = 0.05f;
    public float smoothingRadius = 0.2f;
    public float particleDimension = 0.002f;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;
    public Vector3 thermostatPosition;
    public float thermostatTemperature=1274;
    public float thermostatDensity=1000;
    public float thermostatConductivity=1000;
    public float molesBym3 = 55000;
    public float athmospherePressure = 100000;
    public float airDensity = 1.3f;
    float molesByParticule;

    [Header("References")]
    public ComputeShader compute;
    public Spawner3D spawner;
    public ParticleDisplay3D display;
    public Transform floorDisplay;
    public Transform thermostat;


    // Buffers
    public ComputeBuffer positionVelocityBuffer { get; private set; }
    public ComputeBuffer predictedPositionVelocityBuffer { get; private set; }
    public ComputeBuffer RK4AccelerationBuffer { get; private set; }
    public ComputeBuffer RK4HeatBuffer { get; private set; }
    public ComputeBuffer densityPressureBuffer { get; private set; }
    public ComputeBuffer temperatureBuffer { get; private set; }
    public ComputeBuffer deltaTemperatureBuffer { get; private set; }
    public ComputeBuffer predictedPositionBuffer;
    public ComputeBuffer predictedVelocityBuffer;
    public ComputeBuffer predictedTemperatureViscosityConductivityCapacityBuffer;
    public ComputeBuffer constantBuffer;// constant values particle-wise: M(molar mass), a(cohesion term), b(molar covolume)
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    // Kernel IDs
    const int predictionKernel = 0;
    const int externalForcesKernel = 1;
    const int spatialHashKernel = 2;
    const int densityKernel = 3;
    const int statteVariablesKernel = 4;
    const int pressureForcesKernel = 5;
    const int laplacianKernel = 6;
    const int updateSpatialKernel = 7;
    const int updateTemperatureKernel = 8;

    GPUSort gpuSort;

    // State
    bool isPaused;
    bool pauseNextFrame;
    Spawner3D.SpawnData spawnData;



    void Start()
    {
        Debug.Log("Controls: Space = Play/Pause, R = Reset");
        Debug.Log("Use transform tool in scene to scale/rotate simulation bounding box.");

        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;

        spawnData = spawner.GetSpawnData();

        // Create buffers
        int numParticles = spawnData.points.Length;
        positionVelocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*2);
        predictedPositionVelocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*2);

        RK4AccelerationBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*4);
        RK4HeatBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles*4);

        temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        deltaTemperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        densityPressureBuffer = ComputeHelper.CreateStructuredBuffer<float4>(numParticles);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
        
        predictedTemperatureViscosityConductivityCapacityBuffer = ComputeHelper.CreateStructuredBuffer<float4>(numParticles);
        constantBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles*3);
 
 
        // Set buffer data
        SetInitialBufferData(spawnData);

        // Init compute
        ComputeHelper.SetBuffer(compute, positionVelocityBuffer, "PositionsVelocities", predictionKernel,  updateSpatialKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionVelocityBuffer, "PredictedPositionsVelocities", predictionKernel, externalForcesKernel, spatialHashKernel, densityKernel, pressureForcesKernel, laplacianKernel, updateSpatialKernel);


        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureForcesKernel, laplacianKernel);

        ComputeHelper.SetBuffer(compute, densityPressureBuffer, "DensitiesPressures", externalForcesKernel, densityKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, constantBuffer, "VDWValues", densityKernel, pressureForcesKernel);

        ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", updateTemperatureKernel,predictionKernel);
        ComputeHelper.SetBuffer(compute, predictedTemperatureViscosityConductivityCapacityBuffer, "PredictedTemperaturesViscositiesConductivitiesCapacities", densityKernel, laplacianKernel,predictionKernel);

        ComputeHelper.SetBuffer(compute, RK4AccelerationBuffer, "RK4Accelerations", updateSpatialKernel, externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, RK4HeatBuffer, "RK4Heats", updateTemperatureKernel, laplacianKernel, predictionKernel);


        compute.SetInt("numParticles", positionVelocityBuffer.count/2);
        compute.SetFloat("R", R);

        gpuSort = new();
        gpuSort.SetBuffers(spatialIndices, spatialOffsets);


        // Init display
        display.Init(this);
    }

    void FixedUpdate()
    {
        // Run simulation if in fixed timestep mode
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        // Run simulation if not in fixed timestep mode
        // (skip running for first few frames as timestep can be a lot higher than usual)
        if (!fixedTimeStep && Time.frameCount > 10)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }
        floorDisplay.transform.localScale = new Vector3(1, 1 / transform.localScale.y * 0.1f, 1);
        thermostatPosition = thermostat.transform.position;

        HandleInput();
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!isPaused)
        {
            float timeStep = frameTime / iterationsPerFrame * timeScale;

            UpdateSettings();

            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep(timeStep);
                SimulationStepCompleted?.Invoke();
            }
        }
    }

    void RunSimulationStep(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime/2);
        compute.SetInt("RK4step",0);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: densityKernel);

        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: predictionKernel);

        compute.SetInt("RK4step",1);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: predictionKernel);

        compute.SetFloat("deltaTime", deltaTime);
        compute.SetInt("RK4step",2);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: predictionKernel);
        
        compute.SetInt("RK4step",3);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: laplacianKernel);

        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: updateSpatialKernel);
        ComputeHelper.Dispatch(compute, positionVelocityBuffer.count/2, kernelIndex: updateTemperatureKernel);

    }

    void UpdateSettings()
    {
        Vector3 simBoundsSize = transform.localScale;
        Vector3 simBoundsCentre = transform.position;

        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("particleDimension", particleDimension);
        molesByParticule = molesBym3*particleDimension*particleDimension*particleDimension;
        compute.SetFloat("molesByParticule", molesByParticule);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetFloat("thermostatTemperature", thermostatTemperature);
        compute.SetFloat("thermostatDensity", thermostatDensity);
        compute.SetFloat("thermostatConductivity", thermostatConductivity);
        compute.SetFloat("athmospherePressure", athmospherePressure);
        compute.SetFloat("airDensity", airDensity);
        compute.SetVector("boundsSize", simBoundsSize);
        compute.SetVector("centre", simBoundsCentre);
        compute.SetVector("thermostatPosition", thermostatPosition);

        compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
        compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);
    }

    void SetInitialBufferData(Spawner3D.SpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.points.Length];
        System.Array.Copy(spawnData.points, allPoints, spawnData.points.Length);

        positionVelocityBuffer.SetData(spawnData.positionsVelocities);
        predictedPositionVelocityBuffer.SetData(spawnData.positionsVelocities);
        
        temperatureBuffer.SetData(spawnData.temperatures);
        predictedTemperatureViscosityConductivityCapacityBuffer.SetData(spawnData.predictedTemperaturesViscositiesConductivitiesCapacities);
        constantBuffer.SetData(spawnData.constants);
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            pauseNextFrame = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
            SetInitialBufferData(spawnData);
        }
    }

    void OnDestroy()
    {
        ComputeHelper.Release(positionVelocityBuffer, predictedPositionVelocityBuffer, densityPressureBuffer, RK4AccelerationBuffer, temperatureBuffer, deltaTemperatureBuffer, spatialIndices, spatialOffsets); 
    }

    void OnDrawGizmos()
    {
        // Draw Bounds
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = m;

    }
}

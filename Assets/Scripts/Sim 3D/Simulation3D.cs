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
    public ComputeBuffer positionBuffer { get; private set; }

    public ComputeBuffer k1Buffer { get; private set; }
    public ComputeBuffer k2Buffer { get; private set; }
    public ComputeBuffer k3Buffer { get; private set; }
    public ComputeBuffer k4Buffer { get; private set; }

    public ComputeBuffer h1Buffer { get; private set; }
    public ComputeBuffer h2Buffer { get; private set; }
    public ComputeBuffer h3Buffer { get; private set; }
    public ComputeBuffer h4Buffer { get; private set; }

    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityPressureBuffer { get; private set; }
    public ComputeBuffer temperatureBuffer { get; private set; }
    public ComputeBuffer deltaTemperatureBuffer { get; private set; }
    public ComputeBuffer predictedPositionBuffer;
    public ComputeBuffer predictedVelocityBuffer;
    public ComputeBuffer predictedTemperatureViscosityConductivityCapacityBuffer;
    public ComputeBuffer vdwValueBuffer; // van der walls equation: M(molar mass), a(cohesion term), b(molar covolume)
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    // Kernel IDs
    const int predictionKernel = 0;
    const int externalForcesKernel = 1;
    const int spatialHashKernel = 2;
    const int densityPressureKernel = 3;
    const int pressureForcesKernel = 4;
    const int updatePositionKernel = 5;
    const int laplacianKernel = 6;
    const int updateTemperatureKernel = 7;

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
        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        predictedVelocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);

        k1Buffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        k2Buffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        k3Buffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        k4Buffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);

        h1Buffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        h2Buffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        h3Buffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        h4Buffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);

        temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        deltaTemperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        densityPressureBuffer = ComputeHelper.CreateStructuredBuffer<float4>(numParticles);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
        
        predictedTemperatureViscosityConductivityCapacityBuffer = ComputeHelper.CreateStructuredBuffer<float4>(numParticles);
        vdwValueBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);

 
        // Set buffer data
        SetInitialBufferData(spawnData);

        // Init compute
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", predictionKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", predictionKernel,  updatePositionKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", predictionKernel, spatialHashKernel, densityPressureKernel, pressureForcesKernel, laplacianKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, predictedVelocityBuffer, "PredictedVelocities", predictionKernel, externalForcesKernel, pressureForcesKernel, laplacianKernel, updatePositionKernel);

        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityPressureKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityPressureKernel, pressureForcesKernel, laplacianKernel);

        ComputeHelper.SetBuffer(compute, densityPressureBuffer, "DensitiesPressures", externalForcesKernel, densityPressureKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, vdwValueBuffer, "VDWValues", densityPressureKernel, pressureForcesKernel);

        ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", updateTemperatureKernel,predictionKernel);
        ComputeHelper.SetBuffer(compute, predictedTemperatureViscosityConductivityCapacityBuffer, "PredictedTemperaturesViscositiesConductivitiesCapacities", densityPressureKernel, laplacianKernel,predictionKernel);

        ComputeHelper.SetBuffer(compute, k1Buffer, "k1", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k2Buffer, "k2", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k3Buffer, "k3", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k4Buffer, "k4", updatePositionKernel);

        ComputeHelper.SetBuffer(compute, h1Buffer, "h1", updateTemperatureKernel);
        ComputeHelper.SetBuffer(compute, h2Buffer, "h2", updateTemperatureKernel);
        ComputeHelper.SetBuffer(compute, h3Buffer, "h3", updateTemperatureKernel);
        ComputeHelper.SetBuffer(compute, h4Buffer, "h4", updateTemperatureKernel);

        compute.SetInt("numParticles", positionBuffer.count);
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
        ComputeHelper.SetBuffer(compute, k1Buffer, "Accelerations", externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, h1Buffer, "DeltaTemperatures", laplacianKernel, predictionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityPressureKernel);

        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictionKernel);

        ComputeHelper.SetBuffer(compute, k2Buffer, "Accelerations", externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, h2Buffer, "DeltaTemperatures", laplacianKernel, predictionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityPressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictionKernel);

        compute.SetFloat("deltaTime", deltaTime);
        ComputeHelper.SetBuffer(compute, k3Buffer, "Accelerations", externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, h3Buffer, "DeltaTemperatures", laplacianKernel, predictionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityPressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictionKernel);
        
        ComputeHelper.SetBuffer(compute, k4Buffer, "Accelerations", externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, h4Buffer, "DeltaTemperatures", laplacianKernel, predictionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityPressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: laplacianKernel);

        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updateTemperatureKernel);

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

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
        predictedVelocityBuffer.SetData(spawnData.velocities);
        temperatureBuffer.SetData(spawnData.temperatures);
        predictedTemperatureViscosityConductivityCapacityBuffer.SetData(spawnData.predictedTemperaturesViscositiesConductivitiesCapacities);
        vdwValueBuffer.SetData(spawnData.vdwValues);
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
        ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, predictedVelocityBuffer, densityPressureBuffer, k1Buffer, k2Buffer, k3Buffer, k4Buffer, temperatureBuffer, deltaTemperatureBuffer, spatialIndices, spatialOffsets); 
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

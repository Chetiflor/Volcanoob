using UnityEngine;
using Unity.Mathematics;

public class Simulation3D : MonoBehaviour
{
    public event System.Action SimulationStepCompleted;

    [Header("Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep;
    public int iterationsPerFrame;
    public float gravity = -10;
    [Range(0, 1)] public float collisionDamping = 0.05f;
    public float smoothingRadius = 0.2f;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;
    public Vector3 thermostatPosition;
    public float thermostatTemperature=1274;
    public float thermostatDensity=1000;

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
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    public ComputeBuffer viscosityBuffer { get; private set; }
    public ComputeBuffer temperatureBuffer { get; private set; }
    public ComputeBuffer thermicConductivityBuffer { get; private set; }
    public ComputeBuffer deltaTemperatureBuffer { get; private set; }
    public ComputeBuffer predictedPositionBuffer;
    public ComputeBuffer predictedVelocityBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    // Kernel IDs
    const int predictPositionsKernel = 0;
    const int externalForcesKernel = 1;
    const int spatialHashKernel = 2;
    const int densityKernel = 3;
    const int pressureKernel = 4;
    const int viscosityKernel = 5;
    const int updatePositionKernel = 6;
    const int variableViscosityKernel = 7;
    const int temperatureKernel = 8;
    const int updateTemperatureKernel = 9;

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
        temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        thermicConductivityBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        deltaTemperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        viscosityBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
 
        // Set buffer data
        SetInitialBufferData(spawnData);

        // Init compute
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", predictPositionsKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", predictPositionsKernel,  updatePositionKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", predictPositionsKernel, temperatureKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, variableViscosityKernel, updatePositionKernel);
        ComputeHelper.SetBuffer(compute, predictedVelocityBuffer, "PredictedVelocities", predictPositionsKernel, externalForcesKernel, pressureKernel, viscosityKernel, variableViscosityKernel, updatePositionKernel);

        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, variableViscosityKernel, temperatureKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, variableViscosityKernel, temperatureKernel);

        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, variableViscosityKernel, temperatureKernel);
        ComputeHelper.SetBuffer(compute, viscosityBuffer, "Viscosities", viscosityKernel, variableViscosityKernel);

        ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", temperatureKernel, updateTemperatureKernel);
        ComputeHelper.SetBuffer(compute, deltaTemperatureBuffer, "DeltaTemperatures", temperatureKernel, updateTemperatureKernel);
        ComputeHelper.SetBuffer(compute, thermicConductivityBuffer, "ThermicConductivities", temperatureKernel);

        ComputeHelper.SetBuffer(compute, k1Buffer, "k1", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k2Buffer, "k2", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k3Buffer, "k3", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k4Buffer, "k4", updatePositionKernel);

        compute.SetInt("numParticles", positionBuffer.count);

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
        ComputeHelper.SetBuffer(compute, k1Buffer, "Accelerations", externalForcesKernel, pressureKernel, viscosityKernel, variableViscosityKernel, predictPositionsKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
// Calculate temperature here so hash is done on actual positions
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: temperatureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updateTemperatureKernel);

        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: variableViscosityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictPositionsKernel);

        ComputeHelper.SetBuffer(compute, k2Buffer, "Accelerations", externalForcesKernel, pressureKernel, viscosityKernel, variableViscosityKernel, predictPositionsKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: variableViscosityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictPositionsKernel);

        compute.SetFloat("deltaTime", deltaTime);
        ComputeHelper.SetBuffer(compute, k3Buffer, "Accelerations", externalForcesKernel, pressureKernel, viscosityKernel, variableViscosityKernel, predictPositionsKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: variableViscosityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: predictPositionsKernel);
        
        ComputeHelper.SetBuffer(compute, k4Buffer, "Accelerations", externalForcesKernel, pressureKernel, viscosityKernel, variableViscosityKernel, predictPositionsKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: variableViscosityKernel);

        ComputeHelper.SetBuffer(compute, k1Buffer, "k1", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k2Buffer, "k2", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k3Buffer, "k3", updatePositionKernel);
        ComputeHelper.SetBuffer(compute, k4Buffer, "k4", updatePositionKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionKernel);

    }

    void UpdateSettings()
    {
        Vector3 simBoundsSize = transform.localScale;
        Vector3 simBoundsCentre = transform.position;

        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetFloat("thermostatTemperature", thermostatTemperature);
        compute.SetFloat("thermostatDensity", thermostatDensity);
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
        viscosityBuffer.SetData(spawnData.viscosities);
        thermicConductivityBuffer.SetData(spawnData.thermicConductivities);
        k1Buffer.SetData(spawnData.zeros);
        k2Buffer.SetData(spawnData.zeros);
        k3Buffer.SetData(spawnData.zeros);
        k4Buffer.SetData(spawnData.zeros);
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
        ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer, viscosityBuffer, k1Buffer, temperatureBuffer, spatialIndices, spatialOffsets); 
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

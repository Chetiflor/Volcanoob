using System.IO.Pipes;
using System.Collections;
using UnityEngine;
using Unity.Mathematics;

public class Simulation3D : MonoBehaviour
{
    public event System.Action SimulationStepCompleted;


    const float R = 8.3f;
    const float PI = 3.14159f;

    [Header("Settings")]
    public float timeScale = 1;
    public bool fixedTimeStep;
    public int iterationsPerFrame;
    public float gravity = -10;
    [Range(0, 1)] public float collisionDamping = 0.05f;
    public float smoothingRadius = 0.2f;
    public float particleDimension = 0.002f;
    public float dx = 0.002f;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityStrength;
    public Vector3 thermostatPosition;
    public float thermostatTemperature=1274f;
    public float thermostatInfluenceRadius=0.5f;
    public float thermostatConductivity=1000f;
    float molesBym3 = 55555;
    public float athmospherePressure = 100f;
    public float airDensity = 1.3f;
    float molesByParticle;

    public int Nx,Ny,Nz,numVertices,numCubes;
    public bool drawGrid;

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


    public ComputeBuffer densityBuffer { get; private set; }
    public ComputeBuffer temperatureBuffer { get; private set; }
    public ComputeBuffer predictedTemperatureBuffer;
    public ComputeBuffer laplacianTemperatureBuffer;
    public ComputeBuffer stateVariableBuffer;//pressure, near pressure, viscosity, thermic conductivity, thermic capacity, rho(volumic mass), a(cohesion term), b(molar covolume), M(molar mass)
    const int stateVariablesBufferStride = 8;
    ComputeBuffer constantsBuffer;
    const int constantsBufferStride = 8;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    public ComputeBuffer gridVertexBuffer;
    public ComputeBuffer gridValueBuffer;
    public ComputeBuffer cubesTriangleVerticesBuffer;
    public ComputeBuffer cubesTriangleTemperaturesBuffer;
    public ComputeBuffer triangleMasksBuffer;
    public float isoDensity = 1;

    public Shader mcshader;
    Material cubesMat;
    Bounds bounds;

    // Kernel IDs
    const int predictionKernel = 0;
    const int externalForcesKernel = 1;
    const int spatialHashKernel = 2;
    const int densityKernel = 3;
    const int stateVariablesKernel = 4;
    const int pressureForcesKernel = 5;
    const int laplacianKernel = 6;
    const int updateSpatialKernel = 7;
    const int updateTemperatureKernel = 8;
    const int deltaTemperatureKernel = 9;
    const int evaluateGridKernel = 10;
    const int marchingCubesKernel = 11;

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

        molesByParticle = molesBym3 * particleDimension * particleDimension * particleDimension * 4 / 3 * PI;


        spawner.R = R;
        spawner.molesByParticle = molesByParticle;
        spawner.constantsBufferStride = constantsBufferStride;
        spawner.stateVariablesBufferStride = stateVariablesBufferStride;
        spawner.Nx = Nx;
        spawner.Ny = Ny;
        spawner.Nz = Nz;


        spawnData = spawner.GetSpawnData();

        // Create buffers
        int numParticles = spawnData.temperatures.Length;

        positionVelocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*2);
        predictedPositionVelocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*2);

        RK4AccelerationBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles*4);
        RK4HeatBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles*4);

        temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        predictedTemperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
        laplacianTemperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);

        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
        
        stateVariableBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles*stateVariablesBufferStride);
        constantsBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles*constantsBufferStride);

        numVertices = Nx * Ny * Nz;
        numCubes = (Nx -1) * (Ny - 1) * (Nz - 1);
        gridVertexBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numVertices);
        gridValueBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numVertices);
        cubesTriangleVerticesBuffer = ComputeHelper.CreateStructuredBuffer<float3>(15*numCubes);
        cubesTriangleTemperaturesBuffer = ComputeHelper.CreateStructuredBuffer<float>(15*numCubes);
        triangleMasksBuffer = ComputeHelper.CreateStructuredBuffer<int>(5*numCubes);
 
 
        // Set buffer data
        SetInitialBufferData(spawnData);

        // Init compute
        ComputeHelper.SetBuffer(compute, positionVelocityBuffer, "PositionsVelocities", evaluateGridKernel, predictionKernel,  updateSpatialKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionVelocityBuffer, "PredictedPositionsVelocities", deltaTemperatureKernel, stateVariablesKernel, predictionKernel, externalForcesKernel, spatialHashKernel, densityKernel, pressureForcesKernel, laplacianKernel, updateSpatialKernel);


        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", deltaTemperatureKernel, evaluateGridKernel, spatialHashKernel, stateVariablesKernel, densityKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", deltaTemperatureKernel, evaluateGridKernel, spatialHashKernel, stateVariablesKernel, densityKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, constantsBuffer, "Constants", stateVariablesKernel);

        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", deltaTemperatureKernel, evaluateGridKernel, externalForcesKernel, stateVariablesKernel, densityKernel, pressureForcesKernel, laplacianKernel);
        ComputeHelper.SetBuffer(compute, stateVariableBuffer, "StateVariables", deltaTemperatureKernel, evaluateGridKernel, externalForcesKernel, stateVariablesKernel, densityKernel, pressureForcesKernel, laplacianKernel);

        ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", updateTemperatureKernel, evaluateGridKernel, deltaTemperatureKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, predictedTemperatureBuffer, "PredictedTemperatures", deltaTemperatureKernel, densityKernel, stateVariablesKernel, laplacianKernel,predictionKernel);
        ComputeHelper.SetBuffer(compute, laplacianTemperatureBuffer, "TemperatureLaplacians", laplacianKernel);

        ComputeHelper.SetBuffer(compute, RK4AccelerationBuffer, "RK4Accelerations", updateSpatialKernel, externalForcesKernel, pressureForcesKernel, laplacianKernel, predictionKernel);
        ComputeHelper.SetBuffer(compute, RK4HeatBuffer, "RK4TemperatureDerivative", updateTemperatureKernel, deltaTemperatureKernel, predictionKernel);

        ComputeHelper.SetBuffer(compute, gridVertexBuffer, "GridVertices", evaluateGridKernel, marchingCubesKernel);
        ComputeHelper.SetBuffer(compute, gridValueBuffer, "GridValues", evaluateGridKernel, marchingCubesKernel);
        ComputeHelper.SetBuffer(compute, cubesTriangleVerticesBuffer, "CubesTrianglesTemperatures", marchingCubesKernel);
        ComputeHelper.SetBuffer(compute, cubesTriangleTemperaturesBuffer, "CubesTrianglesVertices", marchingCubesKernel);
        ComputeHelper.SetBuffer(compute, triangleMasksBuffer, "TriangleMasks", marchingCubesKernel);

        compute.SetInt("numParticles", temperatureBuffer.count);
        compute.SetFloat("R", R);

        gpuSort = new();
        gpuSort.SetBuffers(spatialIndices, spatialOffsets);


        // Init display
        display.Init(this);
        cubesMat = new Material(mcshader);
        cubesMat.SetBuffer("Vertices", cubesTriangleVerticesBuffer);
        cubesMat.SetBuffer("Temperatures", cubesTriangleTemperaturesBuffer);
        cubesMat.SetBuffer("Mask", triangleMasksBuffer);
        cubesMat.SetTexture("ColourMap", display.gradientTexture);
        bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
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
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: stateVariablesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: deltaTemperatureKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: predictionKernel);

        compute.SetInt("RK4step",1);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: stateVariablesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: deltaTemperatureKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: predictionKernel);

        compute.SetFloat("deltaTime", deltaTime);
        compute.SetInt("RK4step",2);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: stateVariablesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: deltaTemperatureKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: predictionKernel);
        
        compute.SetInt("RK4step",3);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: stateVariablesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: pressureForcesKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: laplacianKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: deltaTemperatureKernel);

        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: updateSpatialKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: updateTemperatureKernel);

        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, temperatureBuffer.count, kernelIndex: stateVariablesKernel);
        ComputeHelper.Dispatch(compute, numVertices, kernelIndex: evaluateGridKernel);
        ComputeHelper.Dispatch(compute, numCubes, kernelIndex: marchingCubesKernel);
        Graphics.DrawProcedural(cubesMat, bounds, MeshTopology.Triangles, numCubes*15);

    }

    void UpdateSettings()
    {
        Vector3 simBoundsSize = transform.localScale;
        Vector3 simBoundsCentre = transform.position;

        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("dx", dx);
        molesByParticle = molesBym3 * particleDimension * particleDimension * particleDimension * 4 / 3 * PI;
        compute.SetFloat("molesByParticle", molesByParticle);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetFloat("thermostatTemperature", thermostatTemperature);
        compute.SetFloat("thermostatInfluenceRadius", thermostatInfluenceRadius);
        compute.SetFloat("athmospherePressure", athmospherePressure);
        compute.SetFloat("airDensity", airDensity);
        compute.SetVector("boundsSize", simBoundsSize);
        compute.SetVector("centre", simBoundsCentre);
        compute.SetVector("thermostatPosition", thermostatPosition);
        compute.SetInt("stateVariablesStride",stateVariablesBufferStride);
        compute.SetInt("constantsBufferStride",constantsBufferStride);
        compute.SetInt("dimX",Nx);
        compute.SetInt("dimY",Ny);
        compute.SetInt("dimZ",Nz);
        compute.SetFloat("isoDensity",isoDensity);


        compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
        compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);
    }

    void SetInitialBufferData(Spawner3D.SpawnData spawnData)
    {
        // float3[] allPoints = new float3[spawnData.points.Length];
        // System.Array.Copy(spawnData.points, allPoints, spawnData.points.Length);


        positionVelocityBuffer.SetData(spawnData.positionsVelocities);
        predictedPositionVelocityBuffer.SetData(spawnData.positionsVelocities);
        
        temperatureBuffer.SetData(spawnData.temperatures);
        predictedTemperatureBuffer.SetData(spawnData.temperatures);

        constantsBuffer.SetData(spawnData.constants);
        stateVariableBuffer.SetData(spawnData.stateVariables);

        gridVertexBuffer.SetData(spawnData.gridPositions);
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
        ComputeHelper.Release(gridVertexBuffer, gridValueBuffer, cubesTriangleVerticesBuffer, cubesTriangleTemperaturesBuffer, triangleMasksBuffer, positionVelocityBuffer, predictedPositionVelocityBuffer, temperatureBuffer, laplacianTemperatureBuffer, constantsBuffer, predictedTemperatureBuffer, densityBuffer, stateVariableBuffer, RK4HeatBuffer, RK4AccelerationBuffer, spatialIndices, spatialOffsets); 
    }

    void OnDrawGizmos()
    {
        // Draw Bounds
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = m;
        if (!Application.isPlaying || !drawGrid) return;
        Gizmos.color = new Color(0, 0, 0, 0.5f);
        for(int i = 0; i < Nx; i++)
        {
            for(int j = 0; j < Ny; j++)
            {
                for(int k = 0; k < Nz; k++)
                {
                    float3 p = new float3();
                    p = spawnData.gridPositions[i + Nx * j + Nx * Ny * k];
                    Gizmos.DrawSphere(p, 0.03f);
                }
                
            }
        }

    }

}

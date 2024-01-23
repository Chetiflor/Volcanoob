using UnityEngine;
using UnityEngine.Rendering;

public class ParticleDisplay3D : MonoBehaviour
{

    public Shader shader;
    public Shader gridshader;
    public Shader mcshader;
    public float scale;
    Mesh mesh;
    public Color col;
    Material mat;
    Material gridMat;
    Material cubesMat;
    ComputeBuffer argsBuffer;
    ComputeBuffer gridArgsBuffer;
    Bounds bounds;

    public Gradient colourMap;
    public int gradientResolution;
    public float velocityDisplayMax;
    public Texture2D gradientTexture;
    bool needsUpdate;

    public bool marchingCubesRendering=true;
    public bool drawGridVertices=false;
    public int meshResolution;
    public int debug_MeshTriCount;
    int numTriangleVertices;

    public void Init(Simulation3D sim)
    {
        mat = new Material(shader);
        mat.SetBuffer("PositionsVelocities", sim.positionVelocityBuffer);
        mat.SetBuffer("Temperatures", sim.temperatureBuffer);
        mat.SetBuffer("StateVariables", sim.stateVariableBuffer);

        mesh = SebStuff.SphereGenerator.GenerateSphereMesh(meshResolution);
        debug_MeshTriCount = mesh.triangles.Length / 3;
        argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.positionVelocityBuffer.count/2);
        bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

        gridMat = new Material(gridshader);
        gridMat.SetBuffer("GridVertices", sim.gridVertexBuffer);
        mesh = SebStuff.SphereGenerator.GenerateSphereMesh(meshResolution);
        gridArgsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.gridVertexBuffer.count);

        numTriangleVertices = sim.numCubes*5*3;
        cubesMat = new Material(mcshader);
        cubesMat.SetBuffer("Vertices", sim.cubesTriangleVerticesBuffer);
        cubesMat.SetBuffer("Temperatures", sim.cubesTriangleTemperaturesBuffer);
        cubesMat.SetBuffer("Mask", sim.triangleMasksBuffer);


    }

    void LateUpdate()
    {

        UpdateSettings();
        if(!marchingCubesRendering)
        {
            Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
        }
            if(drawGridVertices)
            {
                Graphics.DrawMeshInstancedIndirect(mesh, 0, gridMat, bounds, gridArgsBuffer);
            }
    }


    void UpdateSettings()
    {
        if (needsUpdate)
        {
            needsUpdate = false;
            ParticleDisplay2D.TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
            mat.SetTexture("ColourMap", gradientTexture);
            cubesMat.SetTexture("ColourMap", gradientTexture);
        }
        mat.SetFloat("scale", scale);
        gridMat.SetFloat("scale", scale/5);
        mat.SetColor("colour", col);
        gridMat.SetColor("colour", col);
        mat.SetFloat("velocityMax", velocityDisplayMax);

        Vector3 s = transform.localScale;
        transform.localScale = Vector3.one;
        var localToWorld = transform.localToWorldMatrix;
        transform.localScale = s;

        mat.SetMatrix("localToWorld", localToWorld);
        gridMat.SetMatrix("localToWorld", localToWorld);
        cubesMat.SetMatrix("localToWorld", localToWorld);
    }

    // void OnDrawGizmos()
    // {
    //     if(marchingCubesRendering && Application.isPlaying)
    //     {
    //         cubesMat.SetPass(0);
    //         Graphics.DrawProceduralNow(MeshTopology.Triangles, numTriangleVertices);
    //     }
        
    // }

    private void OnValidate()
    {
        needsUpdate = true;
    }

    void OnDestroy()
    {
        ComputeHelper.Release(argsBuffer);
    }

    
}

using UnityEngine;
using UnityEngine.Rendering;

public class ParticleDisplay3D : MonoBehaviour
{

    public Shader shader;
    public Shader mcshader;
    public float scale;
    Mesh mesh;
    Mesh mcMesh;
    public Color col;
    Material mat;
    Material cubesMat;

    ComputeBuffer argsBuffer;
    Bounds bounds;
    CommandBuffer mcCommandBuffer;

    public Gradient colourMap;
    public int gradientResolution;
    public float velocityDisplayMax;
    Texture2D gradientTexture;
    bool needsUpdate;

    public bool marchingCubesRendering=true;
    public int meshResolution;
    public int debug_MeshTriCount;
    int numCubes;

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

        mcCommandBuffer = new CommandBuffer();
        numCubes = sim.numCubes;
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
        else
        {
            mcCommandBuffer.DrawProcedural(Matrix4x4.Translate(new Vector3(0,0,0)), cubesMat, 0, MeshTopology.Triangles,  numCubes);
        }
    }

    void UpdateSettings()
    {
        if (needsUpdate)
        {
            needsUpdate = false;
            ParticleDisplay2D.TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
            mat.SetTexture("ColourMap", gradientTexture);
        }
        mat.SetFloat("scale", scale);
        mat.SetColor("colour", col);
        mat.SetFloat("velocityMax", velocityDisplayMax);

        Vector3 s = transform.localScale;
        transform.localScale = Vector3.one;
        var localToWorld = transform.localToWorldMatrix;
        transform.localScale = s;

        mat.SetMatrix("localToWorld", localToWorld);
    }

    private void OnValidate()
    {
        needsUpdate = true;
    }

    void OnDestroy()
    {
        ComputeHelper.Release(argsBuffer);
    }
}

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using Unity.VisualScripting;
using Unity.Mathematics;
using System.Text;
using UnityEditor;
using System.Drawing;
using static GaussianViewer;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Assertions;


public struct PlyVertex
{
    public float3 xyz;
    public float3 normal;
    public float3 f_dc;     // 基础球谐系数
    public float[] f_rest;  // length = 45，9 个球谐系数
    public float opacity;   // Raw opacity. Don't use directly. Call GetOpacity() instead
    public float3 scale;    // Raw scaling. Don't use directly. Call GetScale() instead
    public float4 rotation; // x, y, z, w 右手系

    public const int bytesPerVertex3D = 62 * 4;     // 3D Gaussian splatting
    public const int bytesPerVertex2D = 61 * 4;   // 2D Gaussian splatting

    // 
    // Scale activation function
    //
    public Vector3 GetScale()
    {
        return new Vector3(
            Mathf.Exp(scale.x),
            Mathf.Exp(scale.y),
            Mathf.Exp(scale.z));
    }

    public float GetOpacity()
    {
        return 1f/(1f + Mathf.Exp(-opacity));
    }

    // Rotation matrix in Unity left hand system
    public Matrix4x4 Rotation()
    {
        float4 q = math.normalize(rotation);
        float x = q.x;
        float y = q.y;
        float z = q.z;
        float r = q.w;
        // 为了转换到左手系，对第三行和第四行取反（对角线上两次取反，不变）
        Matrix4x4 matrix = new Matrix4x4(
            new Vector4(1f - 2 * y * y - 2 * z * z,    2f * x * y + 2 * r * z,     -2f * x * z + 2 * r * y,     0), // column 0
            new Vector4(2f * x * y - 2 * r * z,        1f - 2 * x * x - 2 * z * z, -2f * y * z - 2 * r * x,     0), // column 1
            new Vector4(-2f * x * z - 2f * r * y,     -2f * y * z + 2f * r * x,     1f - 2 * x * x - 2 * y * y, 0), // column 2
            new Vector4(0, 0, 0, 1)
        );

        return matrix;
    }
};

[Serializable]
public struct CullBox
{
    public CullBox(
        float xMin, float xMax,
        float yMin, float yMax,
        float zMin, float zMax)
    {
        x1 = xMin;
        x2 = xMax;
        y1 = yMin;
        y2 = yMax;
        z1 = zMin;
        z2 = zMax;
        Debug.Assert(x1 <= x2 && y1 <= y2 && z1 <= z2);
    }

    [SerializeField]
    private float x1, x2;
    [SerializeField]
    private float y1, y2;
    [SerializeField]
    private float z1, z2;

    public float xMin
    {
        set { x1 = Math.Min(x2, value); }
        get { return x1; }
    }
    public float xMax
    {
        set { x2 = Math.Max(x1, value); }
        get { return x2; }
    }
    public float yMin
    {
        set { y1 = Math.Min(y2, value); }
        get { return y1; }
    }
    public float yMax
    {
        set { y2 = Math.Max(y1, value); }
        get { return y2; }
    }
    public float zMin
    {
        set { z1 = Math.Min(z2, value); }
        get { return z1; }
    }
    public float zMax
    {
        set { z2 = Math.Max(z1, value); }
        get { return z2; }
    }

    public Vector3 Center()
    {
        return new Vector3((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2);
    }

    public Vector3 Size()
    {
        return new Vector3(x2 - x1, y2 - y1, z2 - z1);
    }

    public bool Contains(float3 point)
    {
        return x1 <= point.x && point.x <= x2
            && y1 <= point.y && point.y <= y2
            && z1 <= point.z && point.z <= z2;
    }
}


// 不能在编辑模式下启动，会非常卡
public class GaussianViewer : MonoBehaviour
{
    // create a serialized field to store the file path
    [SerializeField]
    public string filePath = "3D-Gaussian-Splatting\\output\\garden";
    [SerializeField]
    private int maxGaussianCount = -1;
    [SerializeField]
    public Mesh mesh;
    [SerializeField]
    public Material material;
    [SerializeField]
    public CullBox cullBox = new CullBox(-1, 1, -1, 1, -1, 1);

    // Raw PLY data
    public List<PlyVertex> vertices; // 高斯点的数据

    // 用于渲染的数据
    private RenderParams renderParams;

    // Command buffer
    GraphicsBuffer commandBuf;
    GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;

    // Compute buffer
    private ComputeBuffer obj2WorldBuffer;
    private ComputeBuffer packedData0Buffer;
    private ComputeBuffer packedData1Buffer;

    // Start is called before the first frame update
    void Start()
    {
        // Command buffer
        commandBuf = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments,
            1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];

        LoadPointCloud();
        PreprocessData();
    }

    private void OnEnable()
    {
    }


    private void OnDisable()
    {
    }

    void OnDrawGizmos()
    {
        Gizmos.color = UnityEngine.Color.yellow;
        Gizmos.DrawWireCube(transform.position + cullBox.Center(), cullBox.Size());
    }


    // Update is called once per frame
    void Update()
    {
        //Debug.Log(camera.name);
        if (material == null || mesh == null)
            return;

        commandData[0].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[0].instanceCount = (uint)packedData0Buffer.count;
        commandBuf.SetData(commandData);

        Graphics.RenderMeshIndirect(renderParams, mesh, commandBuf, 1);
    }

    private void OnDestroy() 
    {
        obj2WorldBuffer?.Release();
        packedData0Buffer?.Release();
        packedData1Buffer?.Release();
        obj2WorldBuffer = null;
        packedData0Buffer = null;
        packedData1Buffer = null;

        commandBuf?.Release();
        commandBuf = null;
    }

    //void OnBeginFrameRendering(ScriptableRenderContext context, Camera[] cameras)
    //{
    //}


    public void PreprocessData()
    {
        if(vertices == null || vertices.Count == 0 || material == null || mesh == null)
        {
            return;
        }

        material.enableInstancing = true;
        Matrix4x4 parentRotation = Matrix4x4.Rotate(transform.rotation); ;

        // Compute Radius of the sphere mesh
        List<Vector3> verticesCache = new();
        mesh.GetVertices(verticesCache);
        double meshRadius = 0;
        foreach(Vector3 v in verticesCache)
        {
            meshRadius += v.magnitude;
        }
        meshRadius /= verticesCache.Count;
        
        // Prepare instance data
        int maxInstCount = maxGaussianCount < 0 ? vertices.Count : Math.Min(maxGaussianCount, vertices.Count);
        List<Matrix4x4> worldMatrixCache = new();
        List<Vector4> packedData0Cache = new();
        List<Vector4> packedData1Cache = new();
        for (int i = 0; i < vertices.Count; ++i)
        {
            PlyVertex vertex = vertices[i];

            // Scale must be greater than 0
            Vector3 scale = vertex.GetScale();   
            
            // Left coordinate system translate
            Matrix4x4 Translate = Matrix4x4.Translate(new Vector3(vertex.xyz.x, vertex.xyz.y, -vertex.xyz.z));

            // Object to world matrix
            // Object to parent object space
            Matrix4x4 obj2World = 
                Translate * vertex.Rotation() * Matrix4x4.Scale(scale); 
            obj2World = parentRotation * obj2World; // Apply parent object rotation

            // Cull
            if (!cullBox.Contains(new float3(obj2World.GetPosition()) - new float3(transform.position)))
                continue;

            // Random value for dither pattern offset
            int2 randomOffsets = new int2(UnityEngine.Random.Range(0, 8), UnityEngine.Random.Range(0, 8));
            float randomValue = (float)(randomOffsets.x * 8 + randomOffsets.y);

            // Smallest axis vector in world space
            Vector3 minAxisVec = new Vector3(0,0,0);
            if(scale.x < scale.y && scale.x < scale.z)
            {
                minAxisVec[0] = (float)meshRadius;
            }
            else if(scale.y < scale.z)
            {
                minAxisVec[1] = (float)meshRadius;
            }
            else
            {
                minAxisVec[2] = (float)meshRadius;
            }
            minAxisVec = obj2World.MultiplyVector(minAxisVec);

            // Add to instance data list
            worldMatrixCache.Add(obj2World);
            packedData0Cache.Add(new Vector4(randomValue, vertex.GetOpacity(), minAxisVec.x, minAxisVec.y));
            packedData1Cache.Add(new Vector4(scale.x, scale.y, scale.z, minAxisVec.z));
            
            // Limit the number of instances
            if(worldMatrixCache.Count >= maxInstCount)
            {
                break;
            }
        }

        // Render parameters
        renderParams = new(material);
        renderParams.receiveShadows = false;
        renderParams.lightProbeProxyVolume = null;
        renderParams.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderParams.motionVectorMode = UnityEngine.MotionVectorGenerationMode.ForceNoMotion;
        renderParams.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderParams.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderParams.matProps = new MaterialPropertyBlock();
        renderParams.worldBounds = new Bounds(cullBox.Center(), cullBox.Size());
        
        // Compute Buffer
        packedData0Buffer?.Release();
        packedData0Buffer = new ComputeBuffer(packedData0Cache.Count, sizeof(float) * 4);
        packedData0Buffer.SetData<Vector4>(packedData0Cache);
        material.SetBuffer("_PackedData0", packedData0Buffer);

        packedData1Buffer?.Release();
        packedData1Buffer = new ComputeBuffer(packedData1Cache.Count, sizeof(float) * 4);
        packedData1Buffer.SetData<Vector4>(packedData1Cache);
        material.SetBuffer("_PackedData1", packedData1Buffer);

        obj2WorldBuffer?.Release();
        obj2WorldBuffer = new ComputeBuffer(worldMatrixCache.Count, sizeof(float) * 16);
        obj2WorldBuffer.SetData<Matrix4x4>(worldMatrixCache);
        material.SetBuffer("_ObjectToWorldBuffer", obj2WorldBuffer);

        Debug.Log("PreprocessData Done: Rendering" + worldMatrixCache.Count + " objects");
    }

    public int GetRenderingGaussianCount()
    {
        return obj2WorldBuffer != null ? obj2WorldBuffer.count : 0;
    }

    // 读取 PLY 文件
    public void LoadPointCloud()
    {
        if(filePath == null)
        {
            Debug.LogWarning("File path is null");
            return;
        }

        // Load .ply file
        vertices = new();
        try
        {
            using (StreamReader file = new(filePath))
            using (BinaryReader bFile = new(file.BaseStream))
            {
                bool use2dGaussian= true; // 2D Gaussian splatting

                // Read header
                string line = file.ReadLine();
                int headerSize = line.Length + 1;
                int numVertices = 0;    // Number of vertices
                int vertexDataSize = 0; // Number of bytes per vertex element
                while (!line.Contains("end_header"))
                {
                    line = file.ReadLine();
                    headerSize += line.Length + 1;
                    if (line.Contains("element vertex"))
                    {
                        string[] tokens = line.Split(' ');
                        numVertices = int.Parse(tokens[2]);
                    }
                    if (line.Contains("property float"))
                    {
                        vertexDataSize += sizeof(float);
                        if (line.Contains("scale_2"))
                        {
                            use2dGaussian = false;
                        }
                    }
                }
                int validBytesPerVertex = use2dGaussian ? PlyVertex.bytesPerVertex2D : PlyVertex.bytesPerVertex3D;
                Assert.IsTrue(vertexDataSize >= validBytesPerVertex, "Vertex data size is too small");
                int restBytes = vertexDataSize - validBytesPerVertex;

                // Read vertices
                bFile.BaseStream.Seek(headerSize, SeekOrigin.Begin);
                vertices.Capacity = numVertices;
                for (int i = 0; i < numVertices; ++i)
                {
                    PlyVertex vertex = new PlyVertex();
                    vertex.xyz.x = bFile.ReadSingle();
                    vertex.xyz.y = bFile.ReadSingle();
                    vertex.xyz.z = bFile.ReadSingle();
                    vertex.normal.x = bFile.ReadSingle();
                    vertex.normal.y = bFile.ReadSingle();
                    vertex.normal.z = bFile.ReadSingle();
                    vertex.f_dc.x = bFile.ReadSingle();
                    vertex.f_dc.y = bFile.ReadSingle();
                    vertex.f_dc.z = bFile.ReadSingle();
                    vertex.f_rest = new float[45];
                    for (int j = 0; j < 45; ++j)
                    {
                        vertex.f_rest[j] = bFile.ReadSingle();
                    }
                    vertex.opacity = bFile.ReadSingle();
                    vertex.scale.x = bFile.ReadSingle();
                    vertex.scale.y = bFile.ReadSingle();
                    if (use2dGaussian)
                        vertex.scale.z = -10f;
                    else
                        vertex.scale.z = bFile.ReadSingle();

                    // 注意，PLY 文件里的存储顺序是 w, x, y, z，而且是右手坐标系
                    vertex.rotation.w = bFile.ReadSingle();
                    vertex.rotation.x = bFile.ReadSingle();
                    vertex.rotation.y = bFile.ReadSingle();
                    vertex.rotation.z = bFile.ReadSingle();

                    // Skip the rest of the vertex data
                    bFile.ReadBytes(restBytes);

                    vertices.Add(vertex);
                }
                Debug.Log("Vertices loaded: " + vertices.Count);
            }
        }
        catch(Exception ex)
        {
            // File not found
            Debug.LogWarning(ex.Message);
        }

    }
}

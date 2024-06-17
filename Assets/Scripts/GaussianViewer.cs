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


public struct PlyVertex
{
    public float3 xyz;
    public float3 normal;
    public float3 f_dc;     // 基础球谐系数
    public float[] f_rest;  // length = 45，9 个球谐系数
    public float opacity;
    public float3 scale;
    public float4 rotation;

    public const int bytesPerVertex = 62 * 4;
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

public class GaussianViewer : MonoBehaviour
{
    // create a serialized field to store the file path
    [SerializeField]
    private string filePath = "3D-Gaussian-Splatting\\output\\garden";
    [SerializeField]
    private int iteration = -1;
    [SerializeField]
    private int maxGaussianCount = -1;
    [SerializeField]
    public Mesh mesh;
    [SerializeField]
    public Material material;
    [SerializeField]
    public float scaleModifier = 0.01f;
    [SerializeField]
    public CullBox cullBox = new CullBox(-1, 1, -1, 1, -1, 1);

    // Raw PLY data
    public List<PlyVertex> vertices;

    // 用于分批渲染的数据
    private List<RenderParams> renderParamList;
    public List<Matrix4x4[]> objectToWorldArray;

    public const int maxInstPerBatch = 1024 * 30;

    // Start is called before the first frame update
    void Start()
    {
        renderParamList = new();
        objectToWorldArray = new();
        LoadPointCloud();
        PreprocessData();
    }

    private void OnEnable()
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
        if (material == null || mesh == null)
            return;

        for (int i = 0; i < renderParamList.Count; i += 1)
        {
            Graphics.RenderMeshInstanced(renderParamList[i], mesh, 0, objectToWorldArray[i]);
        }
    }

    private void OnDestroy() {}

    public void PreprocessData()
    {
        if(vertices == null || vertices.Count == 0 || material == null)
        {
            return;
        }

        material.enableInstancing = true;

        Matrix4x4 parentRotation = Matrix4x4.TRS(Vector3.zero, transform.rotation, new Vector3(1, 1, 1));
        
        // Prepare instance data
        int maxInstCount = maxGaussianCount < 0 ? vertices.Count : Math.Min(maxGaussianCount, vertices.Count);
        List<Matrix4x4> worldMatrixCache = new();
        List<Vector4> packedData0Cache = new();
        List<Vector4> packedData1Cache = new();
        for (int i = 0; i < vertices.Count; ++i)
        {
            PlyVertex vertex = vertices[i];

            // Normalize quaternion
            float4 normQ = math.normalize(vertex.rotation);

            // Scale Modifier
            Vector3 scale = new Vector3(vertex.scale.x, vertex.scale.y, vertex.scale.z);   
            scale = vertex.scale.x > 0 ? scale : -scale;
            scale *= scaleModifier;

            // To Left coordinate system
            // https://blog.csdn.net/weixin_40277515/article/details/89323615
            Vector3 pos = new Vector3(vertex.xyz.x, vertex.xyz.y, -vertex.xyz.z);
            Quaternion qot = new Quaternion(normQ.x, -normQ.y, -normQ.z, normQ.w);

            // Object to world matrix
            Matrix4x4 obj2World = new Matrix4x4();
            obj2World.SetTRS(pos, qot, scale );     // Object to parent object space 
            obj2World = parentRotation * obj2World; // Apply parent object rotation

            // Cull
            if (!cullBox.Contains(new float3(obj2World.GetPosition()) - new float3(transform.position)))
                continue;

            // Random value for dither pattern offset
            int2 randomOffsets = new int2(UnityEngine.Random.Range(0, 8), UnityEngine.Random.Range(0, 8));
            float randomValue = (float)(randomOffsets.x * 8 + randomOffsets.y);

            // Smallest axis dir in world space
            Vector3 axisDir = Vector3.zero;
            if(scale.x < scale.y && scale.x < scale.z)
            {
                axisDir[0] = 1;
            }
            else if(scale.y < scale.z)
            {
                axisDir[1] = 1;
            }
            else
            {
                axisDir[2] = 1;
            }
            axisDir = obj2World * axisDir;
            axisDir.Normalize();

            // Add to instance data list
            worldMatrixCache.Add(obj2World);
            packedData0Cache.Add(new Vector4(randomValue, vertex.opacity, axisDir.x, axisDir.y));
            packedData1Cache.Add(new Vector4(vertex.f_dc.x, vertex.f_dc.y, vertex.f_dc.z, axisDir.z));
            
            // Limit the number of instances
            if(worldMatrixCache.Count >= maxInstCount)
            {
                break;
            }
        }

        // --------------------- Split into batches ---------------------
        int batchCount = (worldMatrixCache.Count + maxInstPerBatch - 1) / maxInstPerBatch;
        int resCount = worldMatrixCache.Count % maxInstPerBatch;
        if (resCount == 0)
            resCount = maxInstPerBatch;

        objectToWorldArray.Clear();
        renderParamList.Clear();
        for (int i = 0; i < batchCount; i++)
        {
            int instCount = i == batchCount - 1 ? resCount : maxInstPerBatch;

            // Obj2World matrix
            objectToWorldArray.Add(new Matrix4x4[instCount]);
            worldMatrixCache.CopyTo(i * maxInstPerBatch, objectToWorldArray[i], 0, instCount);

            // Render parameters
            Vector4[] vectorCache = new Vector4[instCount];
            RenderParams renderParam = new RenderParams(material);
            renderParam.receiveShadows = false;
            renderParam.lightProbeProxyVolume = null;
            renderParam.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderParam.motionVectorMode = UnityEngine.MotionVectorGenerationMode.ForceNoMotion;
            renderParam.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderParam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderParam.matProps = new MaterialPropertyBlock();
            packedData0Cache.CopyTo(i * maxInstPerBatch, vectorCache, 0, instCount);
            renderParam.matProps.SetVectorArray("_PackedData0", vectorCache);
            packedData1Cache.CopyTo(i * maxInstPerBatch, vectorCache, 0, instCount);
            renderParam.matProps.SetVectorArray("_PackedData1", vectorCache);
            renderParamList.Add(renderParam);
        }
        // --------------------------------------------------------------

        int totalInstForRender = (batchCount - 1) * maxInstPerBatch + resCount;
        Debug.Log("PreprocessData Done: " + totalInstForRender + " objects");
    }

    // 读取 PLY 文件
    public void LoadPointCloud()
    {
        if(filePath == null)
        {
            Debug.LogWarning("File path is null");
            return;
        }

        // 
        string pointCloudDirPath = Path.Combine(filePath, "point_cloud\\");
        string[] pointCloudDirs = Directory.GetDirectories(pointCloudDirPath);
        if (pointCloudDirs.Length == 0) 
        {
            Debug.LogWarning("No point cloud data found in " + pointCloudDirPath);
            return;
        }
        Array.Sort(pointCloudDirs);

        // Get the .ply file path
        string plyFilePath = "";
        if(iteration < 0)
        {   // Max iteration
            plyFilePath = Path.Combine(
                pointCloudDirs[pointCloudDirs.Length - 1], "point_cloud.ply");
        }
        else
        {
            foreach (string dir in pointCloudDirs)
            {
                int iter = Int32.Parse(dir.Substring("iteration_".Length));
                if (iter == iteration)
                {
                    plyFilePath = Path.Combine(dir, "point_cloud.ply");
                    break;
                }
            }
            if(plyFilePath == "")
            {
                Debug.LogWarning("Iteration " + iteration + " not found");
                return;
            }
        }

        // Load .ply file
        vertices = new();
        try
        {
            using (StreamReader file = new(plyFilePath))
            using (BinaryReader bFile = new(file.BaseStream))
            {
                // Read header
                string line = file.ReadLine();
                int headerSize = line.Length + 1;
                int numVertices = 0; // Number of vertices
                while (!line.Contains("end_header"))
                {
                    line = file.ReadLine();
                    headerSize += line.Length + 1;
                    if (line.Contains("element vertex"))
                    {
                        string[] tokens = line.Split(' ');
                        numVertices = int.Parse(tokens[2]);
                    }
                }

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
                    vertex.scale.z = bFile.ReadSingle();
                    vertex.rotation.x = bFile.ReadSingle();
                    vertex.rotation.y = bFile.ReadSingle();
                    vertex.rotation.z = bFile.ReadSingle();
                    vertex.rotation.w = bFile.ReadSingle();

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

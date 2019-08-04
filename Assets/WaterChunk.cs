using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Threading.Tasks;
using System.Threading;

[ExecuteInEditMode]
public class WaterChunk : MonoBehaviour
{
    /// <summary>
    /// Static method <c>BuildMesh</c> takes the prepared <c>VoxelUtilities.MeshData</c>
    /// and creates a <c>Mesh</c>. This is necessary because the Unity API only allows
    /// meshes to be created on the main thread.
    /// </summary>
    /// <param name="chunk">The <c>WaterChunk</c> which has requested a <c>Mesh</c>.</param>
    private static void BuildMesh(WaterChunk chunk)
    {
        // Clears the old mesh
        chunk.mesh.Clear();

        // Sets the vertices, triangles, and colors.
        chunk.mesh.vertices = chunk.meshData.vertices;
        chunk.mesh.triangles = chunk.meshData.triangles;
        chunk.mesh.colors = chunk.meshData.colors;

        // Optimizes the mesh for faster rendering and CollisionMesh baking
        chunk.mesh.Optimize();

        // Calculates the normals of the mesh
        chunk.mesh.RecalculateNormals();

        // Assigns the mesh to the meshFilter and meshCollider to update it.
        chunk.meshFilter.sharedMesh = chunk.mesh;
        chunk.meshCollider.sharedMesh = chunk.mesh;

        // Calls the chunk's MeshBuilt callback, to allow for post-generation steps.
        chunk.MeshBuilt();
    }

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    private float[] points;
    public bool pointsHaveBeenGenerated;

    private VoxelUtilities.MeshData meshData;

    private Mesh mesh;

    private Chunk parentChunk;

    private void Awake()
    {
        // Gets a reference to the MeshFilter, MeshRenderer, and MeshCollider components attached to the GameObject
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        // Gets a reference to the parent Chunk of this WaterChunk
        parentChunk = transform.GetComponentInParent<Chunk>();

        // Instantiates an empty mesh.
        mesh = new Mesh();

        pointsHaveBeenGenerated = false;
    }

    /// <summary>
    /// Starts a task on a worker thread to update the points,
    /// thereby simulating the motion of the water in the chunk.
    /// 
    /// Requests a mesh upon completion of the update.
    /// </summary>
    public void BeginPointsUpdate()
    {
        Task task = Task.Run(() =>
           {
               UpdatePoints();
           });
        task.ContinueWith(prevTask =>
            {
                GetMesh();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Simulates the motion of the water, by applying rules to each point.
    /// 
    /// Doesn't quite work properly yet.
    /// </summary>
    void UpdatePoints()
    {
        // Initializes, or regenerates the points array
        if (!pointsHaveBeenGenerated)
        {
            GetPoints();
            pointsHaveBeenGenerated = true;
        }

        for (int y = 1; y < VoxelUtilities.pointsPerAxis; y++)
        {
            for (int x = 0; x < VoxelUtilities.pointsPerAxis; x++)
            {
                for (int z = 0; z < VoxelUtilities.pointsPerAxis; z++)
                {
                    float v = GetValueAtPoint(x, y, z);
                    float amountTaken = 0;
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j < 1; j++)
                        {
                            for (int k = -1; k <= 1; k++)
                            {
                                if (i == 0 && j == 0 && k == 0) continue;
                                float v1 = GetValueAtPoint(x + i, y + j, z + k);
                                float t1 = 1f - parentChunk.GetValueAtPoint(x + i, y + j, z + k);
                                float take = (v * (-j + 1)) / 17 * t1;
                                SetValueAtPoint(x + i, y + j, z + k, take + v1 * t1);
                                amountTaken += Mathf.Min(1 - v1, take);
                            }
                        }
                    }
                    if (amountTaken > 0)
                        SetValueAtPoint(x, y, z, (GetValueAtPoint(x, y, z) - amountTaken));
                }
            }
        }

        // Limit the maximum number of updates per second to 30
        Thread.Sleep(33);
    }

    /// <summary>
    /// Generates the initial density values representing the terrain.
    /// </summary>
    void GetPoints()
    {
        points = new float[VoxelUtilities.pointsPerAxis * VoxelUtilities.pointsPerAxis * VoxelUtilities.pointsPerAxis];
        //for (int z = 6; z < pointsPerAxis - 6; z++)
        //{
        //    for (int y = 14; y < pointsPerAxis -1; y++)
        //    {
        //        for (int x = 6; x < pointsPerAxis - 6; x++)
        //        {
        //            points[z * pointsPerAxisSqr + y * pointsPerAxis + x] = y < pointsPerAxis - 3 ? 0 : 1f;
        //        }
        //    }
        //}
        points[VoxelUtilities.GetIndex(8, 14, 8)] = 1f;
    }

    /// <summary>
    /// Begin a new Task to prepare the MeshData for the chunk,
    /// then call BuildMesh from the main thread upon completion.
    /// </summary>
    public void GetMesh()
    {
        Task<VoxelUtilities.MeshData> task = Task.Run(() =>
        {
            return GenerateChunk();
        });

        // Once the Task completes, if it has not been cancelled,
        // call BuildMesh from the main thread.
        task.ContinueWith(prevTask =>
        {
            if (!prevTask.IsCanceled)
            {
                meshData = prevTask.Result;
                BuildMesh(this);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Runs on a worker thread. This method applies the Marching Cubes algorithm to
    /// extract a surface at the specified ISO-value.
    /// </summary>
    /// <param name="isoValue">Default is 0.5f</param>
    /// <returns>Returns prepared MeshData.</returns>
    VoxelUtilities.MeshData GenerateChunk(float isoValue = 0.5f)
    {
        // If points have not been generated, or the points are to be reset,
        // then call the GetPoints method.
        if (!pointsHaveBeenGenerated)
        {
            GetPoints();
            pointsHaveBeenGenerated = true;
        }

        // A list to store the Triangles
        List<VoxelUtilities.Triangle> triangles = new List<VoxelUtilities.Triangle>();

        // March through the 3D density values grid.
        for (int z = 0; z < VoxelUtilities.chunkSize; z++)
        {
            for (int y = 0; y < VoxelUtilities.chunkSize; y++)
            {
                for (int x = 0; x < VoxelUtilities.chunkSize; x++)
                {
                    // Get the vertices and values at the 8 corners of the cube.
                    VoxelUtilities.CubeCorner[] cubeCorners =
                    {
                            VoxelUtilities.GetCubeCorner(x, y, z, points),
                            VoxelUtilities.GetCubeCorner(x+1, y, z, points),
                            VoxelUtilities.GetCubeCorner(x+1, y, z+1, points),
                            VoxelUtilities.GetCubeCorner(x, y, z+1, points),
                            VoxelUtilities.GetCubeCorner(x, y+1, z, points),
                            VoxelUtilities.GetCubeCorner(x+1, y+1, z, points),
                            VoxelUtilities.GetCubeCorner(x+1, y+1, z+1, points),
                            VoxelUtilities.GetCubeCorner(x, y+1, z+1, points)
                        };

                    // Calculate the configuration number (256 possibilities)
                    int configNumber = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (cubeCorners[i].value > isoValue)
                        {
                            configNumber |= (int)Mathf.Pow(2, i);
                        }
                    }

                    // Add triangles according to the predefined triangulation for the specified configuration
                    for (int i = 0; VoxelUtilities.triangulation[configNumber][i] != -1; i += 3)
                    {
                        int a0 = VoxelUtilities.cornerIndexAFromEdge[VoxelUtilities.triangulation[configNumber][i]];
                        int b0 = VoxelUtilities.cornerIndexBFromEdge[VoxelUtilities.triangulation[configNumber][i]];

                        int a1 = VoxelUtilities.cornerIndexAFromEdge[VoxelUtilities.triangulation[configNumber][i + 1]];
                        int b1 = VoxelUtilities.cornerIndexBFromEdge[VoxelUtilities.triangulation[configNumber][i + 1]];

                        int a2 = VoxelUtilities.cornerIndexAFromEdge[VoxelUtilities.triangulation[configNumber][i + 2]];
                        int b2 = VoxelUtilities.cornerIndexBFromEdge[VoxelUtilities.triangulation[configNumber][i + 2]];

                        VoxelUtilities.Triangle tri;
                        tri.a = VoxelUtilities.InterpolateVerts(cubeCorners[a0], cubeCorners[b0], isoValue);
                        tri.b = VoxelUtilities.InterpolateVerts(cubeCorners[a1], cubeCorners[b1], isoValue);
                        tri.c = VoxelUtilities.InterpolateVerts(cubeCorners[a2], cubeCorners[b2], isoValue);
                        tri.color = Color.white;
                        triangles.Add(tri);
                    }
                }
            }
        }

        // Prepare MeshData from the triangles List

        int triangleCount = triangles.Count;


        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        int[] tris = new int[triangleCount * 3];

        //Smooth shading
        Dictionary<Vector3, int> vertexToIndex = new Dictionary<Vector3, int>();


        for (int i = 0; i < triangleCount; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                //Smooth shading
                Vector3 vertex = triangles[i][j];
                Color color = triangles[i].color;
                if (vertexToIndex.ContainsKey(vertex))
                {
                    tris[i * 3 + j] = vertexToIndex[vertex];
                }
                else
                {
                    vertices.Add(vertex);
                    colors.Add(color);
                    int index = vertexToIndex.Count;
                    vertexToIndex.Add(vertex, index);
                    tris[i * 3 + j] = index;
                }

                //// Flat shading
                //tris[i * 3 + j] = i * 3 + j;
                //vertices.Add(triangles[i][j]);
                //colors.Add(triangles[i].color);
            }
        }

        VoxelUtilities.MeshData meshData;
        meshData.vertices = vertices.ToArray();
        meshData.colors = colors.ToArray();
        meshData.triangles = tris;

        return meshData;
    }

    /// <summary>
    /// Callback for the BuildMesh method. Updates the points array.
    /// </summary>
    private void MeshBuilt()
    {
        BeginPointsUpdate();
    }

    /// <summary>
    /// Used for debugging. Visualizes the density values of the chunk in the editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        for (int z = 0; z < VoxelUtilities.pointsPerAxis; z++)
        {
            for (int y = 0; y < VoxelUtilities.pointsPerAxis; y++)
            {
                for (int x = 0; x < VoxelUtilities.pointsPerAxis; x++)
                {
                    float value = 1f-GetValueAtPoint(x, y, z);
                    Gizmos.color = new Color(value, value, value, 0.3f);
                    Gizmos.DrawCube(new Vector3(x, y, z), Vector3.one * 0.5f);
                }
            }
        }
    }

    /// <summary>
    /// Returns the density value at a given point. If the point is outside the chunk,
    /// then we return 1 (i.e solid). TODO: Allow Chunks to retrieve values from neigh-
    /// bouring chunks.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns>Density value at the specified point.</returns>
    public float GetValueAtPoint(int x, int y, int z)
    {
        if (0 <= x && x < VoxelUtilities.pointsPerAxis && 0 <= y && y < VoxelUtilities.pointsPerAxis && 0 <= z && z < VoxelUtilities.pointsPerAxis)
            return points[VoxelUtilities.GetIndex(x, y, z)];
        else
            return 1f;
    }

    /// <summary>
    /// Sets the density value at a given point, if it is inside the chunk.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="value"></param>
    public void SetValueAtPoint(int x, int y, int z, float value)
    {
        if (0 <= x && x < VoxelUtilities.pointsPerAxis && 0 <= y && y < VoxelUtilities.pointsPerAxis && 0 <= z && z < VoxelUtilities.pointsPerAxis)
            points[VoxelUtilities.GetIndex(x, y, z)] = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Adds to the density value at a given point, if it is inside the chunk.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="amount"></param>
    public void AddValueAtPoint(int x, int y, int z, float amount)
    {
        if (0 <= x && x < VoxelUtilities.pointsPerAxis && 0 <= y && y < VoxelUtilities.pointsPerAxis && 0 <= z && z < VoxelUtilities.pointsPerAxis)
            points[VoxelUtilities.GetIndex(x, y, z)] += amount;
    }
}

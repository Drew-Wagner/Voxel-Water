using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;

/// <summary>
/// The class <c>Chunk</c> represents a chunk of voxel terrain.
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    /// <summary>
    /// Static method <c>BuildMesh</c> takes the prepared <c>VoxelUtilities.MeshData</c>
    /// and creates a <c>Mesh</c>. This is necessary because the Unity API only allows
    /// meshes to be created on the main thread.
    /// </summary>
    /// <param name="chunk">The <c>Chunk</c> which has requested a <c>Mesh</c>.</param>
    private static void BuildMesh(Chunk chunk)
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
    private bool pointsHaveBeenGenerated;

    private VoxelUtilities.MeshData meshData;

    private Mesh mesh;

    private WaterChunk waterChunk;

    private void Awake()
    {
        // Gets a reference to the MeshFilter, MeshRenderer, and MeshCollider components attached to the GameObject
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();

        // Gets a reference to the WaterChunk associated with this chunk.
        waterChunk = GetComponentInChildren<WaterChunk>();

        // Instantiates an empty mesh.
        mesh = new Mesh();

        // Initializes the points array to the appropriate size.
        points = new float[VoxelUtilities.pointsPerAxis * VoxelUtilities.pointsPerAxis * VoxelUtilities.pointsPerAxis];
        pointsHaveBeenGenerated = false;

        // Requests that a new mesh be created.
        GetMesh();
    }

    /// <summary>
    /// Regenerates the Chunk on recompile.
    /// </summary>
    private void OnValidate()
    {
        pointsHaveBeenGenerated = false;
        waterChunk = GetComponentInChildren<WaterChunk>();
        waterChunk.pointsHaveBeenGenerated = false;
        GetMesh();
    }

    /// <summary>
    /// Callback for the BuildMesh method. Requests a mesh for the WaterChunk.
    /// </summary>
    private void MeshBuilt() {
        waterChunk.GetMesh();
    }

    /// <summary>
    /// Generates the density values representing the terrain.
    /// </summary>
    void GetPoints()
    {
        for (int y = 0; y < VoxelUtilities.pointsPerAxis; y++)
        {
            for (int z = 0; z < VoxelUtilities.pointsPerAxis; z++)
            {
                for (int x = 0; x < VoxelUtilities.pointsPerAxis; x++)
                {
                    //// Creates a circular crater.
                    //int cx = x - 8;
                    //int cy = y - 8;
                    //int cz = z - 8;
                    //SetValueAtPoint(x, y, z, y > 8 || Mathf.Sqrt(cx * cx + cy * cy + cz * cz) < 8 ? 0 : 1f);

                    // Creates a flat plane at height 3.
                    SetValueAtPoint(x, y, z, y > 3 ? 0 : 1f);
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
    /// Runs on a worker thread. This method applies the Marching Cubes algorithm to
    /// extract a surface at the specified ISO-value.
    /// </summary>
    /// <param name="isoValue"></param>
    /// <returns>Returns prepared MeshData.</returns>
    VoxelUtilities.MeshData GenerateChunk(float isoValue=0.5f)
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

        ////Smooth shading
        //Dictionary<Vector3, int> vertexToIndex = new Dictionary<Vector3, int>();


        for (int i = 0; i < triangleCount; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                ////Smooth shading
                //Vector3 vertex = triangles[i][j];
                //Color color = triangles[i].color;
                //if (vertexToIndex.ContainsKey(vertex))
                //{
                //    tris[i * 3 + j] = vertexToIndex[vertex];
                //} else
                //{
                //    vertices.Add(vertex);
                //    colors.Add(color);
                //    int index = vertexToIndex.Count;
                //    vertexToIndex.Add(vertex, index);
                //    tris[i * 3 + j] = index;
                //}

                // Flat shading
                tris[i * 3 + j] = i * 3 + j;
                vertices.Add(triangles[i][j]);
                colors.Add(triangles[i].color);
            }
        }

        VoxelUtilities.MeshData meshData;
        meshData.vertices = vertices.ToArray();
        meshData.colors = colors.ToArray();
        meshData.triangles = tris;

        return meshData;
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
}

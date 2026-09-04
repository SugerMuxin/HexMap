using System;
using System.Collections.Generic;
using UnityEngine;


public struct EdgeVertices
{
    public Vector3 v1, v2,v3, v4, v5;

	public EdgeVertices(Vector3 corner1, Vector3 corner2)
    {
        v1 = corner1;
        v2 = Vector3.Lerp(corner1, corner2, 1f / 4f);
        v3 = Vector3.Lerp(corner1, corner2, 0.5f);
        v4 = Vector3.Lerp(corner1, corner2, 3f / 4f);
        v5 = corner2;
    }

    public static EdgeVertices TerraceLerp(EdgeVertices a, EdgeVertices b, int step)
    {
        EdgeVertices result;
        result.v1 = HexMetrics.TerraceLerp(a.v1, b.v1, step);
        result.v2 = HexMetrics.TerraceLerp(a.v2, b.v2, step);
        result.v3 = HexMetrics.TerraceLerp(a.v3, b.v3, step);
        result.v4 = HexMetrics.TerraceLerp(a.v4, b.v4, step);
        result.v5 = HexMetrics.TerraceLerp(a.v5, b.v5, step);
        return result;
    }

    public EdgeVertices(Vector3 corner1, Vector3 corner2, float outerStep)
    {
        v1 = corner1;
        v2 = Vector3.Lerp(corner1, corner2, outerStep);
        v3 = Vector3.Lerp(corner1, corner2, 0.5f);
        v4 = Vector3.Lerp(corner1, corner2, 1f - outerStep);
        v5 = corner2;
    }

}


[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexMesh : MonoBehaviour {

    public bool useCollider,useColors, useUVCoordinates;
    Mesh hexMesh;
	MeshCollider meshCollider;
    [NonSerialized] List<Vector3> vertices;
    [NonSerialized] List<Color> colors;
    [NonSerialized] List<int> triangles;
    [NonSerialized] List<Vector2> uvs;
    void Awake () {
		GetComponent<MeshFilter>().mesh = hexMesh = new Mesh();
        if (useCollider)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        hexMesh.name = "Hex Mesh";
	}


    public void Clear()
    {
        hexMesh.Clear();
        vertices = ListPool<Vector3>.Get();
        if (useColors)
        {
            colors = ListPool<Color>.Get();
        }
        if (useUVCoordinates)
        {
            uvs = ListPool<Vector2>.Get();
        }
        triangles = ListPool<int>.Get();
    }

    public void Apply()
    {
        hexMesh.SetVertices(vertices);
        ListPool<Vector3>.Add(vertices);
        if (useColors)
        {
            hexMesh.SetColors(colors);
            ListPool<Color>.Add(colors);
        }
        if (useUVCoordinates)
        {
            hexMesh.SetUVs(0, uvs);
            ListPool<Vector2>.Add(uvs);
        }
        hexMesh.SetTriangles(triangles, 0);
        ListPool<int>.Add(triangles);
        hexMesh.RecalculateNormals();

        if (useCollider)
        {
            meshCollider.sharedMesh = hexMesh;
        }
    }


    public void AddTriangle (Vector3 v1, Vector3 v2, Vector3 v4) {
		int vertexIndex = vertices.Count;
		vertices.Add(HexMetrics.Perturb(v1));
		vertices.Add(HexMetrics.Perturb(v2));
		vertices.Add(HexMetrics.Perturb(v4));
		triangles.Add(vertexIndex);
		triangles.Add(vertexIndex + 1);
		triangles.Add(vertexIndex + 2);
	}

    public void AddTriangleUnpertured(Vector3 v1, Vector3 v2, Vector3 v4)
    {
        int vertexIndex = vertices.Count;
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v4);
        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 2);
    }


    public void AddTriangleColor (Color color) {
		colors.Add(color);
		colors.Add(color);
		colors.Add(color);
	}

    public void AddTriangleColor (Color c1, Color c2, Color c3) {
		colors.Add(c1);
		colors.Add(c2);
		colors.Add(c3);
	}

    public void AddQuad (Vector3 v1, Vector3 v2, Vector3 v4, Vector3 v5) {
		int vertexIndex = vertices.Count;
		vertices.Add(HexMetrics.Perturb(v1));
		vertices.Add(HexMetrics.Perturb(v2));
		vertices.Add(HexMetrics.Perturb(v4));
		vertices.Add(HexMetrics.Perturb(v5));
		triangles.Add(vertexIndex);
		triangles.Add(vertexIndex + 2);
		triangles.Add(vertexIndex + 1);
		triangles.Add(vertexIndex + 1);
		triangles.Add(vertexIndex + 2);
		triangles.Add(vertexIndex + 3);
	}

    public void AddQuadColor (Color c1, Color c2, Color c3, Color c4) {
		colors.Add(c1);
		colors.Add(c2);
		colors.Add(c3);
		colors.Add(c4);
	}

    public void AddQuadColor(Color color)
    {
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
    }

    public void AddQuadColor (Color c1, Color c2) {
		colors.Add(c1);
		colors.Add(c1);
		colors.Add(c2);
		colors.Add(c2);
    }


    public void AddTriangleUV(Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);
    }

    public void AddQuadUV(Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 uv4)
    {
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);
        uvs.Add(uv4);
    }

    public void AddQuadUV(float uMin, float uMax, float vMin, float vMax)
    {
        uvs.Add(new Vector2(uMin, vMin));
        uvs.Add(new Vector2(uMax, vMin));
        uvs.Add(new Vector2(uMin, vMax));
        uvs.Add(new Vector2(uMax, vMax));
    }

    public void AddQuadUnperturbed(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4){
        int vertexIndex = vertices.Count;
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);
        vertices.Add(v4);
        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 3);
    }

}
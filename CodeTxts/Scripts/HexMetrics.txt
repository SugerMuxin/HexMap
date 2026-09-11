
using UnityEngine;

public enum HexEdgeType
{
    Flat, Slope, Cliff
}


public struct HexHash {
    public float a, b, c, d, e;

    public static HexHash Create()
    {
        HexHash hash;
        hash.a = Random.value;
        hash.b = Random.value;
        hash.c = Random.value;
        hash.d = Random.value;
        hash.e = Random.value;
        return hash;
    }
}


public enum GroundKind1
{
    Grass = 0,
    Stone,
    Sand,
    Water,
    /// <summary>未分类的地面。脚步等默认按 Grass 处理。</summary>
    Default
}




public class HexMetrics
{
    /// <summary>
    /// 特征放置密度：true = 教程密集模式(中心+6方向, 每格最多7候选位)；
    /// false = 稀疏模式(仅每格中心1位)。运行时由 UI Toggle 切换。
    /// </summary>
    public static bool denseFeatures;

    public const int chunkSizeX = 5, chunkSizeZ = 5;

    public const float outerToInner = 0.866025404f;
    public const float innerToOuter = 1f / outerToInner;

    public const float outerRadius = 5f;

    public const float innerRadius = outerRadius * outerToInner;

    public const float solidFactor = 0.75f;

    public const float blendFactor = 1f - solidFactor;

    public const float riverSurfaceElevationOffset = -0.5f;

    public const float waterElevationOffset = -0.5f;

    public const int hashGridSize = 256;

    static HexHash[] hashGrid;


    #region 高度相关
    public const float elevationStep = 5.0f;

    public const int terracesPerSlope = 2;

    public const int terraceSteps = terracesPerSlope * 2 + 1;

    public static Texture2D noiseSource;

    public const float cellPerturbStrength = 0f; //扰动程度 4 越小越规则

    public const float noiseScale = 0.003f;
    
    public const float elevationPerturbStrength = 1.5f;  //垂直扰动//

    public const float streamBedElevationOffset = -1f;  //河床偏移

    public const float waterFactor = 0.6f;  //水面高度因子

    #endregion
    public static Vector3[] corners = {
        new Vector3(0f, 0f, outerRadius),
        new Vector3(innerRadius, 0f, 0.5f * outerRadius),
        new Vector3(innerRadius, 0f, -0.5f * outerRadius),
        new Vector3(0f, 0f, -outerRadius),
        new Vector3(-innerRadius, 0f, -0.5f * outerRadius),
        new Vector3(-innerRadius, 0f, 0.5f * outerRadius),
        new Vector3(0f, 0f, outerRadius)
    };

    public static void InitializeHashGrid(int seed){
        hashGrid = new HexHash[hashGridSize * hashGridSize];
        Random.State currentState = Random.state;
        Random.InitState(seed);
        for (int i = 0; i < hashGrid.Length; i++)
        {
            hashGrid[i] = HexHash.Create();
        }
        Random.state = currentState;
    }


    public static Vector3 GetFirstCorner(HexDirection direction)
    {
        return corners[(int)direction];
    }

    public static Vector3 GetFirstSolidCorner(HexDirection direction)
    {
        return corners[(int)direction] * solidFactor;
    }

    public static Vector3 GetSecondCorner(HexDirection direction)
    {
        return corners[(int)direction + 1];
    }

    public static Vector3 GetSecondSolidCorner(HexDirection direction)
    {
        return corners[(int)direction + 1] * solidFactor;
    }

    public static Vector3 GetBridge(HexDirection direction)
    {
        return (corners[(int)direction] + corners[(int)direction + 1]) * blendFactor;
    }

    public const float horizontalTerraceStepSize = 1f / terraceSteps;
    public const float verticalTerraceStepSize = 1f / (terracesPerSlope + 1);
    public static Vector3 TerraceLerp(Vector3 a, Vector3 b, int step)
    {
        float h = step * HexMetrics.horizontalTerraceStepSize;
        a.x += (b.x - a.x) * h;
        a.z += (b.z - a.z) * h;
        float v = ((step + 1) / 2) * HexMetrics.verticalTerraceStepSize;
        a.y += (b.y - a.y) * v;
        return a;
    }

    public static Color TerraceLerp(Color a, Color b, int step)
    {
        float h = step * HexMetrics.horizontalTerraceStepSize;
        return Color.Lerp(a, b, h);
    }

    public static HexEdgeType GetEdgeType(int elevation1, int elevation2)
    {
        if (elevation1 == elevation2)
        {
            return HexEdgeType.Flat;
        }
        int delta = elevation2 - elevation1;
        if (delta == 1 || delta == -1)
        {
            return HexEdgeType.Slope;
        }
        return HexEdgeType.Cliff;
    }

    public static Vector4 SampleNoise(Vector3 position)
    {
        return noiseSource.GetPixelBilinear(
            position.x * 1f * noiseScale,
            position.z * 1f * noiseScale
        );
    }

    public static Vector3 GetSolidEdgeMiddle(HexDirection direction)
    {
        return
            (corners[(int)direction] + corners[(int)direction + 1]) *
            (0.5f * solidFactor);
    }


    /// <summary>
    /// 顶点扰动//
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public static Vector3 Perturb(Vector3 position)
    {
        Vector4 sample = SampleNoise(position);
        position.x += (sample.x * 2f - 1f) * cellPerturbStrength;
        //position.y += (sample.y * 2f - 1f)*HexMetrics.cellPerturbStrength;
        position.z += (sample.z * 2f - 1f) * cellPerturbStrength;
        return position;
    }

    public static Vector3 GetFirstWaterCorner(HexDirection direction)
    {
        return corners[(int)direction] * waterFactor;
    }

    public static Vector3 GetSecondWaterCorner(HexDirection direction)
    {
        return corners[(int)direction + 1] * waterFactor;
    }

    public const float waterBlendFactor = 1f - waterFactor;

    public static Vector3 GetWaterBridge(HexDirection direction)
    {
        return (corners[(int)direction] + corners[(int)direction + 1]) *
            waterBlendFactor;
    }
    public const float hashGridScale = 0.25f;

    public static HexHash SampleHashGrid(Vector3 position)
    {
        int x = (int)(position.x * hashGridScale) % hashGridSize;
        if (x < 0)
        {
            x += hashGridSize;
        }
        int z = (int)(position.z * hashGridScale) % hashGridSize;
        if (z < 0)
        {
            z += hashGridSize;
        }
        return hashGrid[x + z * hashGridSize];
    }


}


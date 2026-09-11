using UnityEngine;

[System.Serializable]
public class HexCoordinates {

    [SerializeField]
    private int x, z;

    public int X { get { return x; } } 
    public int Z { get { return z; } }

    public int Y
    {
        get
        {
            return -X - Z;
        }
    }

    public HexCoordinates(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    /// <summary>
    /// 两套 cube 坐标之间的六边形距离（= 需要走多少格，Catlike HexMap Part15 §3.2）：
    ///   (|Δx| + |Δy| + |Δz|) / 2
    /// 由于 Y 由 X/Z 推出（Y = -X-Z），三项绝对差之和恒为最大绝对差的两倍。
    /// 与地形/障碍无关，是"直线距离"；带障碍的最短路径见 HexGrid.FindPath。
    /// </summary>
    public int DistanceTo(HexCoordinates other)
    {
        if (other == null)
        {
            return 0;
        }
        return
            ((x < other.x ? other.x - x : x - other.x) +
            (Y < other.Y ? other.Y - Y : Y - other.Y) +
            (z < other.z ? other.z - z : z - other.z)) / 2;
    }


    public static HexCoordinates FromOffsetCoordinates(int x, int z)
    {
        return new HexCoordinates(x - z / 2, z);
    }

    public override string ToString()
    {
        return "(" + X.ToString() + ", " + Y.ToString() + ", " + Z.ToString() + ")";
    }

    public string ToStringOnSeparateLines()
    {
        return X.ToString() + "\n" + Y.ToString() + "\n" + Z.ToString();
    }

    public static HexCoordinates FromPosition(Vector3 position) { 
        float x = position.x / (HexMetrics.innerRadius * 2f);
        float y = -x;
        float offset = position.z / (HexMetrics.outerRadius * 3f);
        x -= offset;
        y-= offset;
        int iX = Mathf.RoundToInt(x);
        int iY = Mathf.RoundToInt(y);
        int iZ = Mathf.RoundToInt(-x - y);
        if (iX + iY + iZ != 0)
        {
            float dX = Mathf.Abs(x - iX);
            float dY = Mathf.Abs(y - iY);
            float dZ = Mathf.Abs(-x - y - iZ);

            if (dX > dY && dX > dZ)
            {
                iX = -iY - iZ;
            }
            else if (dZ > dY)
            {
                iZ = -iX - iY;
            }
            Debug.LogWarning("rounding error!");
            
        }
        return new HexCoordinates(iX, iZ);
    }

}

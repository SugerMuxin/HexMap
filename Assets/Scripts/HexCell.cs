
using System.IO;
using UnityEngine;

public class HexCell : MonoBehaviour
{

    public HexCoordinates coordinates;

    public RectTransform uiRect;

    public HexGridChunk chunk;
    //Color _color;
    int terrainTypeIndex;

    public int TerrainTypeIndex
    {
        get
        {
            return terrainTypeIndex;
        }
        set
        {
            if (terrainTypeIndex != value)
            {
                terrainTypeIndex = value;
                Refresh();
            }
        }
    }

    public Color color
    { 
        get
        {
            return HexMetrics.colors[terrainTypeIndex]; ;
        }
    }

    [SerializeField]
    HexCell[] neighbors;

    private int elevation = int.MinValue;

    public int Elevation
    {
        get
        {
            return elevation;
        }
        set
        {
            if (elevation == value)
                return;
            elevation = value;

            RefreshPosition();

            ValidateRivers();
            Refresh();
        }
    }

    void RefreshPosition() {
        Vector3 position = transform.localPosition;
        position.y = elevation * HexMetrics.elevationStep;
        position.y += (HexMetrics.SampleNoise(position).y * 2f - 1f) *
            HexMetrics.elevationPerturbStrength;
        transform.localPosition = position;

        Vector3 uiPosition = uiRect.localPosition;
        uiPosition.z = elevation * -HexMetrics.elevationStep;
        uiRect.localPosition = uiPosition;
    }

    void ValidateRivers() {
        if (hasOutgoingRiver && elevation < GetNeighbor(outgoingRiver).elevation)
        {
            RemoveOutgoingRiver();
        }
        if (hasIncomingRiver && elevation > GetNeighbor(incomingRiver).elevation)
        {
            RemoveIncomingRiver();
        }
    }


    public Vector3 Position
    {
        get
        {
            return transform.localPosition;
        }
    }

    public float RiverSurfaceY
    {
        get
        {
            return
                (elevation + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
        }
    }

    public float WaterSurfaceY
    {
        get
        {
            return
                (waterLevel + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
        }
    }


    public HexCell GetNeighbor(HexDirection direction) { 
        return neighbors[(int)direction];
    }

    public void SetNeighbor(HexDirection direction, HexCell cell)
    {
        neighbors[(int)direction] = cell;
        cell.neighbors[(int)direction.Opposite()] = this;
    }

    public HexEdgeType GetEdgeType(HexDirection direction)
    {
        return HexMetrics.GetEdgeType(elevation, neighbors[(int)direction].elevation);
    }

    public HexEdgeType GetEdgeType(HexCell otherCell)
    {
        return HexMetrics.GetEdgeType(
            elevation, otherCell.elevation
        );
    }

    void Refresh(){
        if (chunk) {
            chunk.Refresh();
            for (int i = 0; i < neighbors.Length; i++) {
                HexCell neighbor = neighbors[i];
                if (neighbor != null && neighbor.chunk != chunk)
                {
                    neighbor.chunk.Refresh();
                }
            }
        }
    }


    #region River About

    public float StreamBedY
    {
        get
        {
            return (elevation + HexMetrics.streamBedElevationOffset) * HexMetrics.elevationStep;
        }
    }


    bool hasIncomingRiver, hasOutgoingRiver;

    HexDirection incomingRiver, outgoingRiver;

    public bool HasIncomingRiver
    {
        get
        {
            return hasIncomingRiver;
        }
    }

    public bool HasOutgoingRiver
    {
        get
        {
            return hasOutgoingRiver;
        }
    }

    public HexDirection IncomingRiver
    {
        get
        {
            return incomingRiver;
        }
    }

    public HexDirection OutgoingRiver
    {
        get
        {
            return outgoingRiver;
        }
    }

    public bool HasRiver
    {
        get
        {
            return hasIncomingRiver || hasOutgoingRiver;
        }
    } 

    public bool HasRiverBeginOrEnd
    {
        get
        {
            return hasIncomingRiver != hasOutgoingRiver;
        }
    }

    public bool HasRiverThroughEdge(HexDirection direction)
    {
        return hasIncomingRiver && incomingRiver == direction ||
            hasOutgoingRiver && outgoingRiver == direction;
    }


    public void RemoveIncomingRiver()
    {
        if (!hasIncomingRiver)
        {
            return;
        }
        hasIncomingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(incomingRiver);
        if ((neighbor))
        {
            neighbor.hasOutgoingRiver = false;
            neighbor.RefreshSelfOnly();
        }

    }

    public void RemoveOutgoingRiver()
    {
        if (!hasOutgoingRiver)
        {
            return;
        }
        hasOutgoingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(outgoingRiver);
        if (neighbor != null)
        {
            neighbor.hasIncomingRiver = false;
            neighbor.RefreshSelfOnly();
        }
    }

    public void RefreshSelfOnly() {
        chunk.Refresh();
    }

    public void RemoveRiver() {
        RemoveIncomingRiver();
        RemoveOutgoingRiver();
    }

    public void SetOutgoingRiver(HexDirection direction) {
        if (hasOutgoingRiver && outgoingRiver == direction) {
            return;
        }
        HexCell neighbor = GetNeighbor(direction);
        if(!neighbor || elevation < neighbor.elevation)
        {
            return;
        }
        RemoveOutgoingRiver();
        if (hasIncomingRiver && incomingRiver == direction) {
            RemoveIncomingRiver();
        }

        hasOutgoingRiver = true;
        outgoingRiver = direction;
        RefreshSelfOnly();

        neighbor.RemoveIncomingRiver();
        neighbor.hasIncomingRiver = true;
        neighbor.incomingRiver = direction.Opposite();
        neighbor.RefreshSelfOnly();
    }
    #endregion



    #region Water Area
    int waterLevel;

    public int WaterLevel
    {
        get {
            return waterLevel;
        }
        set {
            if (waterLevel == value) {
                return;
            }
            waterLevel = value;
            Refresh();
        }
    }

    public bool IsUnderwater
    {
        get {
            return waterLevel > elevation;
        }
    }



    #endregion

    #region Data

    public void Save(BinaryWriter writer) {
        writer.Write((byte)terrainTypeIndex);
        writer.Write((byte)elevation);
        writer.Write((byte)waterLevel);
        //writer.Write((byte)urbanLevel);
        //writer.Write((byte)farmLevel);
        //writer.Write((byte)plantLevel);
        //writer.Write((byte)specialIndex);
        //writer.Write(walled);

        writer.Write(hasIncomingRiver);
        writer.Write((byte)incomingRiver);
        writer.Write(hasOutgoingRiver);
        writer.Write((byte)outgoingRiver);
        /*for (int i = 0; i < roads.Length; i++)
        {
            writer.Write(roads[i]);
        }*/
    }

    public void Load(BinaryReader reader) {
        terrainTypeIndex = reader.ReadByte();
        elevation = reader.ReadByte();
        waterLevel = reader.ReadByte();
        //urbanLevel = reader.ReadByte();
        //farmLevel = reader.ReadByte();
        //plantLevel = reader.ReadByte();
        //specialIndex = reader.ReadByte();
        //walled = reader.ReadBoolean();

        hasIncomingRiver = reader.ReadBoolean();
        incomingRiver = (HexDirection)reader.ReadByte();

        hasOutgoingRiver = reader.ReadBoolean();
        outgoingRiver = (HexDirection)reader.ReadByte();

        /*for (int i = 0; i < roads.Length; i++)
        {
            roads[i] = reader.ReadBoolean();
        }*/
        RefreshPosition();
    }

    #endregion

}

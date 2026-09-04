using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HexGridChunk : MonoBehaviour
{

    HexCell[] cells;

    public HexMesh terrain;

    public HexMesh rivers,water,waterShore;


    Canvas gridCanvas;


    private void Awake()
    {
        gridCanvas = GetComponentInChildren<Canvas>();

        //hexMesh = GetComponentInChildren<HexMesh>();

        cells = new HexCell[HexMetrics.chunkSizeX * HexMetrics.chunkSizeZ];
    }

    public void AddCell(int index, HexCell cell){
        cells[index] = cell;
        cell.chunk = this;
        cell.transform.SetParent(transform, false);
        cell.uiRect.SetParent(gridCanvas.transform, false);
    }

    public void Refresh(){
        //hexMesh.Triangulate(cells);
        enabled = true;
    }

    private void LateUpdate()
    {
        Triangulate();
        //hexMesh.Triangulate(cells);
        enabled = false;
    }

    public void ShowUI(bool visible)
    {
        gridCanvas.gameObject.SetActive(visible);
    }

    public void Triangulate()
    {
        terrain.Clear();
        rivers.Clear();
        water.Clear();
        waterShore.Clear();
        for (int i = 0; i < cells.Length; i++)
        {
            Triangulate(cells[i]);
        }
        terrain.Apply();
        rivers.Apply();
        water.Apply();
        waterShore.Apply();
    }

    void Triangulate(HexCell cell)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(d, cell);
        }
    }


    void Triangulate(HexDirection direction, HexCell cell)
    {
        Vector3 center = cell.Position;

        EdgeVertices e = new EdgeVertices(
            center + HexMetrics.GetFirstSolidCorner(direction),
            center + HexMetrics.GetSecondSolidCorner(direction)
        );
        if (cell.HasRiver)
        {
            if (cell.HasRiverThroughEdge(direction))
            {
                e.v3.y = cell.StreamBedY;
                if (cell.HasRiverBeginOrEnd)
                {
                    TriangulateWithRiverBeginOrEnd(direction, cell, center, e);
                }
                else
                {
                    TriangulateWithRiver(direction, cell, center, e);
                }
            }
            else
            {
                TriangulateAdjacentToRiver(direction, cell, center, e);
            }
        }
        else
        {
            TruangulateEdgeFan(center, e, cell.color);
        }
        if (direction <= HexDirection.SE)
        {
            TriangulateConnection(direction, cell, e);
        }
        if (cell.IsUnderwater) {
            TriangulateWater(direction, cell, center);
        }
    }

    void TriangulateWater(HexDirection direction, HexCell cell, Vector3 center) {
        center.y = cell.WaterSurfaceY;
        HexCell neighbor = cell.GetNeighbor(direction);

        if (neighbor != null && !neighbor.IsUnderwater)
        {
            TriangulateWaterShore(direction, cell, neighbor, center);
        }
        else
        {
            TriangulateOpenWater(direction, cell, neighbor, center);
        }

    }

    void TriangulateOpenWater(
        HexDirection direction, HexCell cell, HexCell neighbor, Vector3 center
    )
    {
        Vector3 c1 = center + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = center + HexMetrics.GetSecondWaterCorner(direction);

        water.AddTriangle(center, c1, c2);

        if (direction <= HexDirection.SE)
        {
            /*
             * HexCell neighbor = cell.GetNeighbor(direction);
            if (neighbor == null || !neighbor.IsUnderwater)
            {
                return;
            }
            */

            Vector3 bridge = HexMetrics.GetWaterBridge(direction);
            Vector3 e1 = c1 + bridge;
            Vector3 e2 = c2 + bridge;

            water.AddQuad(c1, c2, e1, e2);

            if (direction <= HexDirection.E)
            {
                HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
                if (nextNeighbor == null || !nextNeighbor.IsUnderwater)
                {
                    return;
                }
                water.AddTriangle(
                    c2, e2, c2 + HexMetrics.GetWaterBridge(direction.Next())
                );
            }
        }
    }

    void TriangulateWaterShore(
        HexDirection direction, HexCell cell, HexCell neighbor, Vector3 center
    )
    {
        EdgeVertices e1 = new EdgeVertices(
            center + HexMetrics.GetFirstWaterCorner(direction),
            center + HexMetrics.GetSecondWaterCorner(direction)
        );
        water.AddTriangle(center, e1.v1, e1.v2);
        water.AddTriangle(center, e1.v2, e1.v3);
        water.AddTriangle(center, e1.v3, e1.v4);
        water.AddTriangle(center, e1.v4, e1.v5);

        Vector3 center2 = neighbor.Position;
        center2.y = center.y;
        EdgeVertices e2 = new EdgeVertices(
            center2 + HexMetrics.GetSecondSolidCorner(direction.Opposite()),
            center2 + HexMetrics.GetFirstSolidCorner(direction.Opposite())
        );

        waterShore.AddQuad(e1.v1, e1.v2, e2.v1, e2.v2);
        waterShore.AddQuad(e1.v2, e1.v3, e2.v2, e2.v3);
        waterShore.AddQuad(e1.v3, e1.v4, e2.v3, e2.v4);
        waterShore.AddQuad(e1.v4, e1.v5, e2.v4, e2.v5);

        waterShore.AddQuadUV(0f, 0f, 0f, 1f);
        waterShore.AddQuadUV(0f, 0f, 0f, 1f);
        waterShore.AddQuadUV(0f, 0f, 0f, 1f);
        waterShore.AddQuadUV(0f, 0f, 0f, 1f);

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (nextNeighbor != null)
        {
            Vector3 v3 = nextNeighbor.Position + (nextNeighbor.IsUnderwater ?
                            HexMetrics.GetFirstWaterCorner(direction.Previous()) :
                            HexMetrics.GetFirstSolidCorner(direction.Previous()));
            v3.y = center.y;
            waterShore.AddTriangle(e1.v5, e2.v5, v3);
            waterShore.AddTriangleUV(
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, nextNeighbor.IsUnderwater ? 0f : 1f)
            );
        }
    }


    void TriangulateWithRiver(HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        Vector3 centerL = Vector3.zero;
        Vector3 centerR = Vector3.zero;
        if (cell.HasRiverThroughEdge(direction.Opposite()))
        {
            centerL = center +
                HexMetrics.GetFirstSolidCorner(direction.Previous()) * 0.25f;
            centerR = center +
                HexMetrics.GetSecondSolidCorner(direction.Next()) * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Next()))
        {
            centerL = center;
            centerR = Vector3.Lerp(center, e.v5, 2f / 3f);
        }
        else if (cell.HasRiverThroughEdge(direction.Previous()))
        {
            centerL = Vector3.Lerp(center, e.v1, 2f / 3f);
            centerR = center;
        }
        else if (cell.HasRiverThroughEdge(direction.Next2()))
        {
            centerL = center;
            centerR = center +
                HexMetrics.GetSolidEdgeMiddle(direction.Next()) *
                (0.5f * HexMetrics.innerToOuter);
        }
        else
        {
            centerL = center +
                HexMetrics.GetSolidEdgeMiddle(direction.Previous()) *
                (0.5f * HexMetrics.innerToOuter);
            centerR = center;
        }

        center = Vector3.Lerp(centerL, centerR, 0.5f);

        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(centerL, e.v1, 0.5f),
            Vector3.Lerp(centerR, e.v5, 0.5f),
            1f / 6f
        );


        m.v3.y = center.y = e.v3.y;

        TriangulateEdgeStrip(m, cell.color, e, cell.color);

        terrain.AddTriangle(centerL, m.v1, m.v2);
        terrain.AddTriangleColor(cell.color);
        terrain.AddQuad(centerL, center, m.v2, m.v3);
        terrain.AddQuadColor(cell.color);
        terrain.AddQuad(center, centerR, m.v3, m.v4);
        terrain.AddQuadColor(cell.color);
        terrain.AddTriangle(centerR, m.v4, m.v5);
        terrain.AddTriangleColor(cell.color);
        if (!cell.IsUnderwater)  /*这个判断是为了当河流在水下时不去绘制在水下的部分河流的mesh*/
        {
            bool reversed = cell.IncomingRiver == direction;
            TriangulateRiverQuad(centerL, centerR, m.v2, m.v4, cell.RiverSurfaceY, 0.4f, reversed);
            TriangulateRiverQuad(m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed);
        }

    }

    /// <summary>
    /// 绘制河流的起始或重点的各种情况
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="cell"></param>
    /// <param name="center"></param>
    /// <param name="e"></param>
    void TriangulateWithRiverBeginOrEnd(
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
    )
    {
        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(center, e.v1, 0.5f),
            Vector3.Lerp(center, e.v5, 0.5f)
        );

        m.v3.y = e.v3.y;

        TriangulateEdgeStrip(m, cell.color, e, cell.color);
        TruangulateEdgeFan(center, m, cell.color);

        /*river*/
        if (!cell.IsUnderwater) { /*这个判断是为了当河流在水下时不去绘制在水下的部分河流的mesh*/
            bool reversed = cell.HasIncomingRiver;
            TriangulateRiverQuad(
                m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed
            );
            center.y = m.v2.y = m.v4.y = cell.RiverSurfaceY;
            rivers.AddTriangle(center, m.v2, m.v4);
            if (reversed)
            {
                rivers.AddTriangleUV(
                    new Vector2(0.5f, 0.4f), new Vector2(1f, 0.2f), new Vector2(0f, 0.2f)
                );
            }
            else
            {
                rivers.AddTriangleUV(
                    new Vector2(0.5f, 0.4f), new Vector2(0f, 0.6f), new Vector2(1f, 0.6f)
                );
            }
        }      
    }

    //没有水面边缘，纯纯水面的部分mesh构造//
    void TriangulateAdjacentToRiver(
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
    )
    {
        if (cell.HasRiverThroughEdge(direction.Next()))
        {
            if (cell.HasRiverThroughEdge(direction.Previous()))
            {
                center += HexMetrics.GetSolidEdgeMiddle(direction) *
                    (HexMetrics.innerToOuter * 0.5f);
            }
            else if (
                cell.HasRiverThroughEdge(direction.Previous2())
            )
            {
                center += HexMetrics.GetFirstSolidCorner(direction) * 0.25f;
            }
        }
        else if (
            cell.HasRiverThroughEdge(direction.Previous()) &&
            cell.HasRiverThroughEdge(direction.Next2())
        )
        {
            center += HexMetrics.GetSecondSolidCorner(direction) * 0.25f;
        }
        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(center, e.v1, 0.5f),
            Vector3.Lerp(center, e.v5, 0.5f)
        );

        TriangulateEdgeStrip(m, cell.color, e, cell.color);
        TruangulateEdgeFan(center, m, cell.color);
    }

    /// <summary>
    /// 瀑布到水面的衔接//
    /// </summary>
    void TriangulateWaterfallInWater(Vector3 v1,Vector3 v2, Vector3 v3,Vector3 v4,float y1,float y2,float waterY) {
        v1.y = v2.y = y1;
        v3.y = v4.y = y2;
        v1 = HexMetrics.Perturb(v1);
        v2 = HexMetrics.Perturb(v2);
        v3 = HexMetrics.Perturb(v3);
        v4 = HexMetrics.Perturb(v4);
        float t = (waterY - y2) / (y1 - y2);
        v3 = Vector3.Lerp(v3, v1, t);
        v4 = Vector3.Lerp(v4, v2, t);
        rivers.AddQuadUnperturbed(v1, v2, v3, v4);
        rivers.AddQuadUV(0f, 1f, 0.8f, 1f);
    }



    //细分中心三角形面的扇区//
    void TruangulateEdgeFan(Vector3 center, EdgeVertices edge, Color color)
    {
        terrain.AddTriangle(center, edge.v1, edge.v2);
        terrain.AddTriangleColor(color);

        terrain.AddTriangle(center, edge.v2, edge.v3);
        terrain.AddTriangleColor(color);

        terrain.AddTriangle(center, edge.v3, edge.v4);
        terrain.AddTriangleColor(color);

        terrain.AddTriangle(center, edge.v4, edge.v5);
        terrain.AddTriangleColor(color);
    }




    //细分两个三角形面之间的四边形带//
    void TriangulateEdgeStrip(EdgeVertices edge1, Color color1, EdgeVertices edge2, Color color2)
    {
        terrain.AddQuad(edge1.v1, edge1.v2, edge2.v1, edge2.v2);
        terrain.AddQuadColor(color1, color2);
        terrain.AddQuad(edge1.v2, edge1.v3, edge2.v2, edge2.v3);
        terrain.AddQuadColor(color1, color2);
        terrain.AddQuad(edge1.v3, edge1.v4, edge2.v3, edge2.v4);
        terrain.AddQuadColor(color1, color2);
        terrain.AddQuad(edge1.v4, edge1.v5, edge2.v4, edge2.v5);
        terrain.AddQuadColor(color1, color2);
    }



    void TriangulateConnection(
        HexDirection direction, HexCell cell, EdgeVertices e1
    )
    {
        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor == null)
        {
            return;
        }

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.y = neighbor.Position.y - cell.Position.y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v5 + bridge
        );
        if (cell.HasRiver)
        {
            if (cell.HasRiverThroughEdge(direction))
            {
                e2.v3.y = neighbor.StreamBedY;
            }
        }


        if (cell.HasRiverThroughEdge(direction))
        {
            e2.v3.y = neighbor.StreamBedY;
            if (!cell.IsUnderwater )
            {
                if (!neighbor.IsUnderwater) {
                    TriangulateRiverQuad(
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, 0.8f,
                        cell.HasIncomingRiver && cell.IncomingRiver == direction
                    );
                } else if (cell.Elevation > neighbor.WaterLevel) {
                    TriangulateWaterfallInWater(
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY,
                        neighbor.WaterSurfaceY
                    );
                }
            }
            else if (
                !neighbor.IsUnderwater &&
                neighbor.Elevation > cell.WaterLevel
            ){
                TriangulateWaterfallInWater(
                    e2.v4, e2.v2, e1.v4, e1.v2,
                    neighbor.RiverSurfaceY, cell.RiverSurfaceY,
                    cell.WaterSurfaceY
                );
            }
        }

        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(e1, cell, e2, neighbor);
        }
        else
        {
            TriangulateEdgeStrip(e1, cell.color, e2, neighbor.color);
        }

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (direction <= HexDirection.E && nextNeighbor != null)
        {
            Vector3 v5 = e1.v5 + HexMetrics.GetBridge(direction.Next());
            v5.y = nextNeighbor.Position.y;

            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(
                        e1.v5, cell, e2.v5, neighbor, v5, nextNeighbor
                    );
                }
                else
                {
                    TriangulateCorner(
                        v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor
                    );
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(
                    e2.v5, neighbor, v5, nextNeighbor, e1.v5, cell
                );
            }
            else
            {
                TriangulateCorner(
                    v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor
                );
            }
        }
    }

    //梯田连接边缘
    void TriangulateEdgeTerraces(EdgeVertices begin, HexCell beginCell,
        EdgeVertices end, HexCell endCell)
    {

        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color c2 = HexMetrics.TerraceLerp(beginCell.color, endCell.color, 1);

        TriangulateEdgeStrip(begin, beginCell.color, e2, c2);

        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color c1 = c2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            c2 = HexMetrics.TerraceLerp(beginCell.color, endCell.color, i);
            TriangulateEdgeStrip(e1, c1, e2, c2);
        }

        TriangulateEdgeStrip(e2, c2, end, endCell.color);

    }

    void TriangulateCorner(Vector3 bottom, HexCell bottomCell,
        Vector3 left, HexCell leftCell, Vector3 right, HexCell rightCell)
    {

        HexEdgeType leftEdgeType = bottomCell.GetEdgeType(leftCell);
        HexEdgeType rightEdgeType = bottomCell.GetEdgeType(rightCell);

        if (leftEdgeType == HexEdgeType.Slope)
        {
            if (rightEdgeType == HexEdgeType.Slope)
            {
                TriangulateCornerTerraces(bottom, bottomCell, left, leftCell, right, rightCell);
                return;
            }
            if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(left, leftCell, right, rightCell, bottom, bottomCell);
                return;
            }
            TriangulateCornerTerracesCliff(bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        else if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerCliffTerraces(bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        else if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(
                    right, rightCell, bottom, bottomCell, left, leftCell
                );
            }
            else
            {
                TriangulateCornerTerracesCliff(
                    left, leftCell, right, rightCell, bottom, bottomCell
                );
            }
            return;
        }
        else
        {
            terrain.AddTriangle(bottom, left, right);
            terrain.AddTriangleColor(bottomCell.color, leftCell.color, rightCell.color);
        }
    }

    void TriangulateCornerTerraces(Vector3 begin, HexCell beginCell, Vector3 left, HexCell leftCell, Vector3 right, HexCell rightCell)
    {

        Vector3 v4 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v5 = HexMetrics.TerraceLerp(begin, right, 1);
        Color c3 = HexMetrics.TerraceLerp(beginCell.color, leftCell.color, 1);
        Color c4 = HexMetrics.TerraceLerp(beginCell.color, rightCell.color, 1);

        terrain.AddTriangle(begin, v4, v5);
        terrain.AddTriangleColor(beginCell.color, c3, c4);
        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            Vector3 v1 = v4;
            Vector3 v2 = v5;
            Color c1 = c3;
            Color c2 = c4;
            v4 = HexMetrics.TerraceLerp(begin, left, i);
            v5 = HexMetrics.TerraceLerp(begin, right, i);
            c3 = HexMetrics.TerraceLerp(beginCell.color, leftCell.color, i);
            c4 = HexMetrics.TerraceLerp(beginCell.color, rightCell.color, i);
            terrain.AddQuad(v1, v2, v4, v5);
            terrain.AddQuadColor(c1, c2, c3, c4);
        }
        terrain.AddQuad(v4, v5, left, right);
        terrain.AddQuadColor(c3, c4, leftCell.color, rightCell.color);
    }



    /// <summary>
    /// Triangulates a corner where one edge is a terrace and the other is a cliff.
    /// </summary>
    /// <param name="begin"></param>
    /// <param name="beginCell"></param>
    /// <param name="left"></param>
    /// <param name="leftCell"></param>
    /// <param name="right"></param>
    /// <param name="rightCell"></param>
    void TriangulateCornerTerracesCliff(
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell
    )
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Vector3.Lerp(HexMetrics.Perturb(begin), HexMetrics.Perturb(right), b);
        Color boundaryColor = Color.Lerp(beginCell.color, rightCell.color, b);

        TriangulateBoundaryTriangle(begin, beginCell, left, leftCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            terrain.AddTriangleUnpertured(HexMetrics.Perturb(left), HexMetrics.Perturb(right), boundary);
            terrain.AddTriangleColor(leftCell.color, rightCell.color, boundaryColor);
        }
    }

    /// <summary>
    /// Triangulates a corner where one edge is a cliff and the other is a terrace.
    /// </summary>
    /// <param name="begin"></param>
    /// <param name="beginCell"></param>
    /// <param name="left"></param>
    /// <param name="leftCell"></param>
    /// <param name="right"></param>
    /// <param name="rightCell"></param>
    void TriangulateCornerCliffTerraces(
    Vector3 begin, HexCell beginCell,
    Vector3 left, HexCell leftCell,
    Vector3 right, HexCell rightCell)
    {
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);
        Vector3 boundary = Vector3.Lerp(HexMetrics.Perturb(begin), HexMetrics.Perturb(left), b);
        Color boundaryColor = Color.Lerp(beginCell.color, leftCell.color, b);

        TriangulateBoundaryTriangle(
            right, rightCell, begin, beginCell, boundary, boundaryColor
        );

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(
                left, leftCell, right, rightCell, boundary, boundaryColor
            );
        }
        else
        {
            terrain.AddTriangleUnpertured(HexMetrics.Perturb(left), HexMetrics.Perturb(right), boundary);
            terrain.AddTriangleColor(leftCell.color, rightCell.color, boundaryColor);
        }
    }



    void TriangulateBoundaryTriangle(Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 boundary, Color boundaryColor)
    {

        Vector3 v2 = HexMetrics.Perturb(HexMetrics.TerraceLerp(begin, left, 1));
        Color c2 = HexMetrics.TerraceLerp(beginCell.color, leftCell.color, 1);

        terrain.AddTriangleUnpertured(HexMetrics.Perturb(begin), v2, boundary);
        terrain.AddTriangleColor(beginCell.color, c2, boundaryColor);

        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color c1 = c2;
            v2 = HexMetrics.Perturb(HexMetrics.TerraceLerp(begin, left, i));
            c2 = HexMetrics.TerraceLerp(beginCell.color, leftCell.color, i);
            terrain.AddTriangleUnpertured(v1, v2, boundary);
            terrain.AddTriangleColor(c1, c2, boundaryColor);
        }

        terrain.AddTriangleUnpertured(v2, HexMetrics.Perturb(left), boundary);
        terrain.AddTriangleColor(c2, leftCell.color, boundaryColor);
    }


    //-----------------------

    void TriangulateRiverQuad(
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1,float y2,float v,bool reversed
    )
    {
        v1.y = v2.y = y1;
        v3.y = v4.y = y2;
        rivers.AddQuad(v1, v2, v3, v4);
        if (reversed)
        {
            rivers.AddQuadUV(1f, 0f, 0.8f - v, 0.6f - v);
        }
        else {
            rivers.AddQuadUV(0f, 1f, v, v + 0.2f);
        }
        /*if (reversed)
        {
            rivers.AddQuadUV(1f, 0f, 1f, 0f);
        }
        else {
            rivers.AddQuadUV(0f, 1f, 0f, 1f);
        }*/

    }

    void TriangulateRiverQuad(
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y, float v,bool reversed
    )
    {
        TriangulateRiverQuad(v1, v2, v3, v4, y, y, v, reversed);
    }


}

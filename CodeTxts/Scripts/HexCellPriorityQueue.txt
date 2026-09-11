using System.Collections.Generic;

/// <summary>
/// 六边形 A* 用的优先级队列（Catlike HexMap Part16 §4 的"按优先级分桶 + 同优先级链表"实现）。
///
/// 与教科书二叉堆的差异：不做 O(log n) 上浮/下沉，而是
///   - 用 list[priority] 存该优先级的链表头，格子之间靠 HexCell.NextWithSamePriority 串联；
///   - 维护 minimum 记住当前最小非空优先级，Dequeue 从 minimum 开始扫。
/// 搜索优先级是「移动成本 5/10 量级的整数」且沿路径单调上升，因此入队/出队摊销接近 O(1)，
/// 并且零堆分配（Clear() 后复用）。
///
/// 使用约定：同一格子在队内时不要重复 Enqueue（优先级变化请用 Change(cell, oldPriority)）。
/// </summary>
public class HexCellPriorityQueue
{
    readonly List<HexCell> list = new List<HexCell>();

    int count;
    int minimum = int.MaxValue;

    /// <summary>队列中的格子数。</summary>
    public int Count { get { return count; } }

    public void Enqueue(HexCell cell)
    {
        if (cell == null) return;

        count += 1;
        int priority = cell.SearchPriority;
        if (priority < minimum) minimum = priority;

        while (priority >= list.Count) list.Add(null);
        cell.NextWithSamePriority = list[priority];
        list[priority] = cell;
    }

    public HexCell Dequeue()
    {
        if (count <= 0) return null;

        for (; minimum < list.Count; minimum++)
        {
            HexCell cell = list[minimum];
            if (cell != null)
            {
                list[minimum] = cell.NextWithSamePriority;
                cell.NextWithSamePriority = null;    // 出队即摘链，避免脏引用
                count -= 1;
                return cell;
            }
        }

        count = 0;   // 防御式复位（正常流程不会走到）
        return null;
    }

    /// <summary>
    /// 格子优先级变化后调用：先用 oldPriority 把它从原链表摘出来，再按新优先级入队。
    /// 找不到时（状态不一致 / 已被出队）直接按新优先级入队，不抛异常。
    /// </summary>
    public void Change(HexCell cell, int oldPriority)
    {
        if (cell == null) return;

        bool unlinked = false;
        if (oldPriority >= 0 && oldPriority < list.Count)
        {
            HexCell current = list[oldPriority];
            if (current == cell)
            {
                list[oldPriority] = current.NextWithSamePriority;
                unlinked = true;
            }
            else if (current != null)
            {
                HexCell next = current.NextWithSamePriority;
                while (next != null)
                {
                    if (next == cell)
                    {
                        current.NextWithSamePriority = next.NextWithSamePriority;
                        unlinked = true;
                        break;
                    }
                    current = next;
                    next = current.NextWithSamePriority;
                }
            }
        }

        if (unlinked) count -= 1;   // 摘链成功 = 已出队，再入队后 count 净不变
        Enqueue(cell);
    }

    /// <summary>是否在队列中（O(n) 线性扫描，仅供调试 / 断言）。</summary>
    public bool Contains(HexCell cell)
    {
        if (cell == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            for (HexCell c = list[i]; c != null; c = c.NextWithSamePriority)
            {
                if (c == cell) return true;
            }
        }
        return false;
    }

    /// <summary>清空并断开所有链表（下轮搜索复用同一实例）。</summary>
    public void Clear()
    {
        int guard = count + 1;   // 异常链表（若有）也不会死循环
        for (int i = 0; i < list.Count && guard > 0; i++)
        {
            HexCell c = list[i];
            while (c != null && guard > 0)
            {
                HexCell next = c.NextWithSamePriority;
                c.NextWithSamePriority = null;
                c = next;
                guard--;
            }
            list[i] = null;
        }
        count = 0;
        minimum = int.MaxValue;
    }
}

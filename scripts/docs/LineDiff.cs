namespace DressSharp.Docs;

enum DiffKind
{
    Equal,
    Delete,
    Insert
}

/// <summary>A line-level diff for showing what one preference value changed in an example.</summary>
static class LineDiff
{
    /// <summary>Returns the diff as a sequence of operations; the index points into <paramref name="before"/> for deletions and <paramref name="after"/> otherwise.</summary>
    internal static List<(DiffKind Kind, int Index)> Compute(string[] before, string[] after)
    {
        var lcs = new int[before.Length + 1, after.Length + 1];
        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
                lcs[i, j] = before[i] == after[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        var result = new List<(DiffKind, int)>();
        int x = 0, y = 0;
        while (x < before.Length && y < after.Length)
        {
            if (before[x] == after[y])
            {
                result.Add((DiffKind.Equal, y));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add((DiffKind.Delete, x++));
            }
            else
            {
                result.Add((DiffKind.Insert, y++));
            }
        }
        while (x < before.Length)
            result.Add((DiffKind.Delete, x++));
        while (y < after.Length)
            result.Add((DiffKind.Insert, y++));
        return result;
    }
}

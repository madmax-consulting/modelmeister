using System.IO;
using Shouldly;
using ModelMeister.Excel;
using ModelMeister.Inriver.WorkAreas;
using Xunit;

namespace ModelMeister.Excel.Tests;

/// <summary>A WorkArea workbook must round-trip folder tree + flags + opaque query JSON value-for-value.</summary>
public class WorkAreaWorkbookRoundTripTests
{
    [Fact]
    public void Save_then_load_preserves_folders()
    {
        var folders = new List<WorkAreaFolderDto>
        {
            new() { Path = "Marketing", Name = "Marketing", Index = 0 },
            new() { Path = "Marketing/Launch 2026", Name = "Launch 2026", Index = 1, IsQuery = true, QueryJson = "{\"EntityTypeId\":\"Product\"}" },
            new() { Path = "Syndication", Name = "Syndication", Index = 2, IsSyndication = true },
        };

        var path = Path.Combine(Path.GetTempPath(), "mm-workarea-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            WorkAreaWorkbook.Save(folders, path);
            var loaded = WorkAreaWorkbook.Load(path);

            loaded.Count.ShouldBe(3);
            var launch = loaded.Single(f => f.Path == "Marketing/Launch 2026");
            launch.Name.ShouldBe("Launch 2026");
            launch.Index.ShouldBe(1);
            launch.IsQuery.ShouldBeTrue();
            launch.QueryJson.ShouldBe("{\"EntityTypeId\":\"Product\"}");
            loaded.Single(f => f.Path == "Syndication").IsSyndication.ShouldBeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Round_trips_username_and_oversize_query_via_sidecar()
    {
        // A GUI-built query can exceed Excel's 32k cell cap — it must spill to the sidecar and resolve back.
        var bigQuery = "{\"EntityTypeId\":\"Product\",\"pad\":\"" + new string('x', 40_000) + "\"}";
        var folders = new List<WorkAreaFolderDto>
        {
            new() { Path = "Alice/Pinned", Name = "Pinned", Index = 0, Username = "alice" },
            new() { Path = "Alice/Big query", Name = "Big query", Index = 1, IsQuery = true, Username = "alice", QueryJson = bigQuery },
        };

        var path = Path.Combine(Path.GetTempPath(), "mm-workarea-big-" + Guid.NewGuid().ToString("N") + ".xlsx");
        var sidecar = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_files");
        try
        {
            WorkAreaWorkbook.Save(folders, path);
            var loaded = WorkAreaWorkbook.Load(path);

            loaded.Single(f => f.Path == "Alice/Pinned").Username.ShouldBe("alice");
            var big = loaded.Single(f => f.Path == "Alice/Big query");
            big.Username.ShouldBe("alice");
            big.QueryJson.ShouldBe(bigQuery);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(sidecar)) Directory.Delete(sidecar, recursive: true);
        }
    }

    [Fact]
    public void Load_missing_sheet_returns_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "mm-workarea-empty-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            WorkAreaWorkbook.Save([], path);
            WorkAreaWorkbook.Load(path).ShouldBeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

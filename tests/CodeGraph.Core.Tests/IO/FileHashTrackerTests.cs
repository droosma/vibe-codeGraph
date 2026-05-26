using System.Globalization;
using System.Reflection;
using CodeGraph.Core.IO;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.Tests.IO;

public class FileHashTrackerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _graphDir;

    public FileHashTrackerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-hash-{Guid.NewGuid():N}");
        _graphDir = Path.Combine(_testDir, "graph");
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private string CreateTestFile(string name, string content)
    {
        var path = Path.Combine(_testDir, name);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ComputeHash_ReturnsConsistentHash()
    {
        var path = CreateTestFile("test.cs", "class Foo {}");

        var hash1 = FileHashTracker.ComputeHash(path);
        var hash2 = FileHashTracker.ComputeHash(path);

        Assert.NotNull(hash1);
        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentContent_ReturnsDifferentHash()
    {
        var path1 = CreateTestFile("a.cs", "class A {}");
        var path2 = CreateTestFile("b.cs", "class B {}");

        var hash1 = FileHashTracker.ComputeHash(path1);
        var hash2 = FileHashTracker.ComputeHash(path2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var tracker = new FileHashTracker(_graphDir);
        var hashes = new Dictionary<string, string>
        {
            ["src/Foo.cs"] = "AABB",
            ["src/Bar.cs"] = "CCDD"
        };

        await tracker.SaveHashesAsync(hashes);
        var loaded = await tracker.LoadHashesAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("AABB", loaded["src/Foo.cs"]);
        Assert.Equal("CCDD", loaded["src/Bar.cs"]);
    }

    [Fact]
    public async Task SaveHashes_UpdatesExistingEntry()
    {
        var tracker = new FileHashTracker(_graphDir);

        var initial = new Dictionary<string, string> { ["src/Foo.cs"] = "AABB" };
        await tracker.SaveHashesAsync(initial);

        var updated = new Dictionary<string, string> { ["src/Foo.cs"] = "EEFF" };
        await tracker.SaveHashesAsync(updated);

        var loaded = await tracker.LoadHashesAsync();
        Assert.Equal("EEFF", loaded["src/Foo.cs"]);
    }

    [Fact]
    public async Task LoadHashes_MissingDbFile_ReturnsEmpty()
    {
        var tracker = new FileHashTracker(_graphDir);

        var loaded = await tracker.LoadHashesAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DetectChanges_EmptyInitialState_AllFilesAreAdded()
    {
        var tracker = new FileHashTracker(_graphDir);
        var file1 = CreateTestFile("src/A.cs", "class A {}");
        var file2 = CreateTestFile("src/B.cs", "class B {}");

        var changes = await tracker.DetectChangesAsync(new[] { file1, file2 });

        Assert.Equal(2, changes.Added.Count);
        Assert.Empty(changes.Modified);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public async Task DetectChanges_DetectsAddedFiles()
    {
        var tracker = new FileHashTracker(_graphDir);
        var file1 = CreateTestFile("src/Existing.cs", "class Existing {}");

        // Store hash for file1
        var hashes = new Dictionary<string, string>
        {
            [file1.Replace('\\', '/')] = FileHashTracker.ComputeHash(file1)
        };
        await tracker.SaveHashesAsync(hashes);

        // Now detect with file1 + a new file2
        var file2 = CreateTestFile("src/NewFile.cs", "class NewFile {}");
        var changes = await tracker.DetectChangesAsync(new[] { file1, file2 });

        Assert.Single(changes.Added);
        Assert.Contains(file2, changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public async Task DetectChanges_DetectsModifiedFiles()
    {
        var tracker = new FileHashTracker(_graphDir);
        var filePath = CreateTestFile("src/Mod.cs", "class Mod { }");

        // Store the original hash
        var hashes = new Dictionary<string, string>
        {
            [filePath.Replace('\\', '/')] = FileHashTracker.ComputeHash(filePath)
        };
        await tracker.SaveHashesAsync(hashes);

        // Modify the file
        File.WriteAllText(filePath, "class Mod { int X; }");

        var changes = await tracker.DetectChangesAsync(new[] { filePath });

        Assert.Empty(changes.Added);
        Assert.Single(changes.Modified);
        Assert.Contains(filePath, changes.Modified);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public async Task DetectChanges_DetectsRemovedFiles()
    {
        var tracker = new FileHashTracker(_graphDir);
        var file1 = CreateTestFile("src/Keep.cs", "class Keep {}");
        var file2 = CreateTestFile("src/Remove.cs", "class Remove {}");

        // Store hashes for both files
        var hashes = new Dictionary<string, string>
        {
            [file1.Replace('\\', '/')] = FileHashTracker.ComputeHash(file1),
            [file2.Replace('\\', '/')] = FileHashTracker.ComputeHash(file2)
        };
        await tracker.SaveHashesAsync(hashes);

        // Detect with only file1 (file2 is "removed")
        var changes = await tracker.DetectChangesAsync(new[] { file1 });

        Assert.Empty(changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Single(changes.Removed);
        Assert.Contains(file2.Replace('\\', '/'), changes.Removed);
    }

    [Fact]
    public async Task DetectChanges_UnchangedFiles_NotReported()
    {
        var tracker = new FileHashTracker(_graphDir);
        var filePath = CreateTestFile("src/Stable.cs", "class Stable {}");

        var hashes = new Dictionary<string, string>
        {
            [filePath.Replace('\\', '/')] = FileHashTracker.ComputeHash(filePath)
        };
        await tracker.SaveHashesAsync(hashes);

        var changes = await tracker.DetectChangesAsync(new[] { filePath });

        Assert.Empty(changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public void Constructor_NullGraphDir_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FileHashTracker(null!));
    }

    [Fact]
    public void ComputeHash_NullFilePath_Throws()
    {
        Assert.Throws<ArgumentException>(() => FileHashTracker.ComputeHash(null!));
    }

    [Fact]
    public void Constructor_WhitespaceGraphDir_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new FileHashTracker("   "));
        Assert.Contains("Graph directory must not be null or empty.", ex.Message);
    }

    [Fact]
    public void ComputeHash_WhitespaceFilePath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => FileHashTracker.ComputeHash(" "));
        Assert.Contains("File path must not be null or empty.", ex.Message);
    }

    [Fact]
    public async Task SaveHashesAsync_BackslashPaths_LoadsNormalizedPaths()
    {
        var tracker = new FileHashTracker(_graphDir);

        await tracker.SaveHashesAsync(new Dictionary<string, string>
        {
            ["src\\Nested\\File.cs"] = "AABB"
        });

        var loaded = await tracker.LoadHashesAsync();

        var entry = Assert.Single(loaded);
        Assert.Equal("src/Nested/File.cs", entry.Key);
        Assert.Equal("AABB", entry.Value);
    }

    [Fact]
    public async Task LoadHashesAsync_DatabaseWithoutFileHashesTable_ReturnsEmptyDictionary()
    {
        var dbPath = Path.Combine(_graphDir, "filehashes.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE other_table (id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var tracker = new FileHashTracker(_graphDir);
        var loaded = await tracker.LoadHashesAsync();

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task DetectChangesAsync_NormalizedPathAndHashCaseMatch_DoesNotReportChange()
    {
        var tracker = new FileHashTracker(_graphDir);
        var filePath = CreateTestFile("src/CaseMatch.cs", "class CaseMatch {} ");
        var currentHash = FileHashTracker.ComputeHash(filePath);

        await tracker.SaveHashesAsync(new Dictionary<string, string>
        {
            [filePath.Replace('/', '\\')] = currentHash.ToLowerInvariant()
        });

        var changes = await tracker.DetectChangesAsync(new[] { filePath.Replace('\\', '/') });

        Assert.Empty(changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public async Task DetectChangesAsync_EmptyCurrentFiles_ReturnsAllStoredFilesAsRemoved()
    {
        var tracker = new FileHashTracker(_graphDir);
        var file1 = CreateTestFile("src/Keep.cs", "class Keep {} ");
        var file2 = CreateTestFile("src/Remove.cs", "class Remove {} ");

        await tracker.SaveHashesAsync(new Dictionary<string, string>
        {
            [file1] = FileHashTracker.ComputeHash(file1),
            [file2] = FileHashTracker.ComputeHash(file2)
        });

        var changes = await tracker.DetectChangesAsync(Array.Empty<string>());

        Assert.Empty(changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Equal(2, changes.Removed.Count);
        Assert.Contains(file1.Replace('\\', '/'), changes.Removed);
        Assert.Contains(file2.Replace('\\', '/'), changes.Removed);
    }

    [Fact]
    public async Task SaveHashesAsync_StoresLastIndexedUsingRoundTripIso8601Format()
    {
        var tracker = new FileHashTracker(_graphDir);

        await tracker.SaveHashesAsync(new Dictionary<string, string>
        {
            ["src/Foo.cs"] = "AABB"
        });

        var dbPath = Path.Combine(_graphDir, "filehashes.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_indexed FROM file_hashes WHERE file_path = 'src/Foo.cs'";
        var storedTimestamp = (string)(await command.ExecuteScalarAsync())!;

        var parsedTimestamp = DateTimeOffset.ParseExact(
            storedTimestamp,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        Assert.Equal(parsedTimestamp.ToString("o"), storedTimestamp);
    }

    [Fact]
    public void SetParameterValue_NullValue_StoresDBNull()
    {
        using var command = new SqliteCommand();
        command.Parameters.Add("$value", SqliteType.Text);

        var method = typeof(FileHashTracker).GetMethod(
            "SetParameterValue",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, new object?[] { command, "$value", null });

        Assert.IsType<DBNull>(command.Parameters["$value"].Value);
    }
}

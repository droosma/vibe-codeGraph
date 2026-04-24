using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

public class SolutionParserTests
{
    private const string SampleSlnContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{2150E333-8FDC-42A3-9474-1A3956D46DE8}"") = ""src"", ""src"", ""{5FB4A249-187F-44B1-B65D-9E0BCD3CA926}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MyApp.Core"", ""src\MyApp.Core\MyApp.Core.csproj"", ""{BAC2CC74-6527-42D2-B3CA-EECFC96E1CDD}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MyApp.Services"", ""src/MyApp.Services/MyApp.Services.csproj"", ""{CDDF3089-5BAB-4530-AAFE-10BDE585286F}""
EndProject
Global
EndGlobal
";

    [Fact]
    public void ParseContent_ExtractsCSharpProjects()
    {
        var entries = SolutionParser.ParseContent(SampleSlnContent);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseContent_ExtractsProjectNames()
    {
        var entries = SolutionParser.ParseContent(SampleSlnContent);

        Assert.Equal("MyApp.Core", entries[0].Name);
        Assert.Equal("MyApp.Services", entries[1].Name);
    }

    [Fact]
    public void ParseContent_ExtractsRelativePaths()
    {
        var entries = SolutionParser.ParseContent(SampleSlnContent);

        // Paths should be normalized to OS separator
        Assert.Contains("MyApp.Core.csproj", entries[0].RelativePath);
        Assert.Contains("MyApp.Services.csproj", entries[1].RelativePath);
    }

    [Fact]
    public void ParseContent_ExtractsGuids()
    {
        var entries = SolutionParser.ParseContent(SampleSlnContent);

        Assert.Equal("BAC2CC74-6527-42D2-B3CA-EECFC96E1CDD", entries[0].ProjectGuid);
        Assert.Equal("CDDF3089-5BAB-4530-AAFE-10BDE585286F", entries[1].ProjectGuid);
    }

    [Fact]
    public void ParseContent_FiltersSolutionFolders()
    {
        // The "src" entry is a solution folder (type GUID 2150E333), not a csproj
        var entries = SolutionParser.ParseContent(SampleSlnContent);

        Assert.DoesNotContain(entries, e => e.Name == "src");
    }

    [Fact]
    public void ParseContent_EmptyContent_ReturnsEmpty()
    {
        var entries = SolutionParser.ParseContent("");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseContent_VbprojProjects_FilteredOut()
    {
        var content = @"
Project(""{F184B08F-C81C-45F6-A57F-5ABD9991F28F}"") = ""LegacyApp"", ""src\LegacyApp\LegacyApp.vbproj"", ""{11111111-2222-3333-4444-555555555555}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ModernApp"", ""src\ModernApp\ModernApp.csproj"", ""{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}""
EndProject
";

        var entries = SolutionParser.ParseContent(content);

        Assert.Single(entries);
        Assert.Equal("ModernApp", entries[0].Name);
    }

    [Fact]
    public void ParseContent_NormalizesForwardSlashesToSystemSeparator()
    {
        var content = @"
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MyLib"", ""src/MyLib/MyLib.csproj"", ""{12345678-1234-1234-1234-123456789012}""
EndProject
";

        var entries = SolutionParser.ParseContent(content);

        Assert.Single(entries);
        var expected = $"src{Path.DirectorySeparatorChar}MyLib{Path.DirectorySeparatorChar}MyLib.csproj";
        Assert.Equal(expected, entries[0].RelativePath);
    }

    [Fact]
    public void ParseContent_MalformedLines_SkippedGracefully()
    {
        var content = @"
This is not a valid project line
Project(
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ValidProject"", ""src\Valid\Valid.csproj"", ""{ABCDEF01-2345-6789-ABCD-EF0123456789}""
EndProject
Some other garbage
";

        var entries = SolutionParser.ParseContent(content);

        Assert.Single(entries);
        Assert.Equal("ValidProject", entries[0].Name);
    }

    [Fact]
    public void ParseContent_MultipleProjectTypes_AllCsprojParsed()
    {
        var content = @"
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ClassLib"", ""src\ClassLib\ClassLib.csproj"", ""{11111111-1111-1111-1111-111111111111}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""WebApp"", ""src\WebApp\WebApp.csproj"", ""{22222222-2222-2222-2222-222222222222}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""Tests"", ""tests\Tests\Tests.csproj"", ""{33333333-3333-3333-3333-333333333333}""
EndProject
";

        var entries = SolutionParser.ParseContent(content);

        Assert.Equal(3, entries.Count);
        Assert.Equal("ClassLib", entries[0].Name);
        Assert.Equal("WebApp", entries[1].Name);
        Assert.Equal("Tests", entries[2].Name);
    }

    #region Parse from file (kills NoCoverage on Parse method)

    [Fact]
    public void Parse_FromFile_ExtractsProjects()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_sln_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var slnPath = Path.Combine(testDir, "Test.sln");
            File.WriteAllText(slnPath, SampleSlnContent);

            var entries = SolutionParser.Parse(slnPath);

            Assert.Equal(2, entries.Count);
            Assert.Equal("MyApp.Core", entries[0].Name);
            Assert.Equal("MyApp.Services", entries[1].Name);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = @"C:\nonexistent\path\fake.sln";
        var ex = Assert.Throws<FileNotFoundException>(() =>
            SolutionParser.Parse(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Parse_FromFile_NormalizesPathSeparators()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_sln2_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var content = @"
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""MyLib"", ""src/MyLib/MyLib.csproj"", ""{12345678-1234-1234-1234-123456789012}""
EndProject
";
            var slnPath = Path.Combine(testDir, "Test.sln");
            File.WriteAllText(slnPath, content);

            var entries = SolutionParser.Parse(slnPath);

            Assert.Single(entries);
            var expected = $"src{Path.DirectorySeparatorChar}MyLib{Path.DirectorySeparatorChar}MyLib.csproj";
            Assert.Equal(expected, entries[0].RelativePath);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Slnx format

    private const string SampleSlnxContent = @"<Solution>
  <Folder Name=""src"">
    <Project Path=""src/MyApp.Core/MyApp.Core.csproj"" />
    <Project Path=""src/MyApp.Services/MyApp.Services.csproj"" />
  </Folder>
</Solution>";

    [Fact]
    public void ParseSlnxContent_ExtractsCSharpProjects()
    {
        var entries = SolutionParser.ParseSlnxContent(SampleSlnxContent);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseSlnxContent_ExtractsProjectNames()
    {
        var entries = SolutionParser.ParseSlnxContent(SampleSlnxContent);

        Assert.Equal("MyApp.Core", entries[0].Name);
        Assert.Equal("MyApp.Services", entries[1].Name);
    }

    [Fact]
    public void ParseSlnxContent_ExtractsRelativePaths()
    {
        var entries = SolutionParser.ParseSlnxContent(SampleSlnxContent);

        Assert.Contains("MyApp.Core.csproj", entries[0].RelativePath);
        Assert.Contains("MyApp.Services.csproj", entries[1].RelativePath);
    }

    [Fact]
    public void ParseSlnxContent_ProjectGuid_IsEmpty()
    {
        var entries = SolutionParser.ParseSlnxContent(SampleSlnxContent);

        Assert.All(entries, e => Assert.Equal(string.Empty, e.ProjectGuid));
    }

    [Fact]
    public void ParseSlnxContent_FiltersFolders()
    {
        var content = @"<Solution>
  <Project Path=""Docs"" Type=""Folder"" />
  <Project Path=""src/MyApp/MyApp.csproj"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        Assert.Equal("MyApp", entries[0].Name);
    }

    [Fact]
    public void ParseSlnxContent_EmptyContent_ReturnsEmpty()
    {
        var entries = SolutionParser.ParseSlnxContent("<Solution />");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseSlnxContent_InvalidXml_ReturnsEmpty()
    {
        var entries = SolutionParser.ParseSlnxContent("this is not xml");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseSlnxContent_VbprojProjects_FilteredOut()
    {
        var content = @"<Solution>
  <Project Path=""src/LegacyApp/LegacyApp.vbproj"" />
  <Project Path=""src/ModernApp/ModernApp.csproj"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        Assert.Equal("ModernApp", entries[0].Name);
    }

    [Fact]
    public void ParseSlnxContent_NormalizesPathSeparators()
    {
        var content = @"<Solution>
  <Project Path=""src/MyLib/MyLib.csproj"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        var expected = $"src{Path.DirectorySeparatorChar}MyLib{Path.DirectorySeparatorChar}MyLib.csproj";
        Assert.Equal(expected, entries[0].RelativePath);
    }

    [Fact]
    public void ParseSlnxContent_ExplicitNameAttribute_UsesIt()
    {
        var content = @"<Solution>
  <Project Path=""src/MyLib/MyLib.csproj"" Name=""CustomName"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        Assert.Equal("CustomName", entries[0].Name);
    }

    [Fact]
    public void ParseSlnxContent_NoNameAttribute_DerivesFromPath()
    {
        var content = @"<Solution>
  <Project Path=""src/MyLib/MyLib.csproj"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        Assert.Equal("MyLib", entries[0].Name);
    }

    [Fact]
    public void ParseSlnxContent_NestedFolders_FindsProjects()
    {
        var content = @"<Solution>
  <Folder Name=""src"">
    <Folder Name=""Core"">
      <Project Path=""src/Core/MyApp.Core/MyApp.Core.csproj"" />
    </Folder>
  </Folder>
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
        Assert.Equal("MyApp.Core", entries[0].Name);
    }

    [Fact]
    public void ParseSlnxContent_ProjectWithEmptyPath_Skipped()
    {
        var content = @"<Solution>
  <Project Path="""" />
  <Project Path=""src/MyApp/MyApp.csproj"" />
</Solution>";

        var entries = SolutionParser.ParseSlnxContent(content);

        Assert.Single(entries);
    }

    [Fact]
    public void Parse_SlnxFile_ExtractsProjects()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_slnx_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var slnxPath = Path.Combine(testDir, "Test.slnx");
            File.WriteAllText(slnxPath, SampleSlnxContent);

            var entries = SolutionParser.Parse(slnxPath);

            Assert.Equal(2, entries.Count);
            Assert.Equal("MyApp.Core", entries[0].Name);
            Assert.Equal("MyApp.Services", entries[1].Name);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_SlnxFile_NonExistent_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".slnx");
        var ex = Assert.Throws<FileNotFoundException>(() =>
            SolutionParser.Parse(path));
        Assert.Contains(path, ex.Message);
    }

    #endregion
}

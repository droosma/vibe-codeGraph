using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

public class ProjectParserTests
{
    private const string SdkStyleCsproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>MyApp.Core</AssemblyName>
    <RootNamespace>MyApp.Core</RootNamespace>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\MyApp.Shared\MyApp.Shared.csproj"" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
    <PackageReference Include=""Serilog"" Version=""3.1.1"" />
  </ItemGroup>
</Project>";

    private const string LegacyStyleCsproj = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>LegacyApp</AssemblyName>
    <RootNamespace>LegacyNs</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Program.cs"" />
    <Compile Include=""Utils\Helper.cs"" />
  </ItemGroup>
</Project>";

    private const string MultiTargetCsproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net7.0;net8.0</TargetFrameworks>
    <AssemblyName>MultiTarget.Lib</AssemblyName>
  </PropertyGroup>
</Project>";

    #region IsSdkStyle / Sdk attribute detection

    [Fact]
    public void ParseContent_SdkStyle_DetectsIsSdkStyle()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.True(info.IsSdkStyle);
    }

    [Fact]
    public void ParseContent_LegacyStyle_DetectsNotSdkStyle()
    {
        var info = ProjectParser.ParseContent(LegacyStyleCsproj, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.IsSdkStyle);
    }

    #endregion

    #region PropertyGroup / TargetFramework

    [Fact]
    public void ParseContent_SdkStyle_ReadsTargetFramework()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_NoFramework_DefaultsToNet100()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <AssemblyName>NoTfm</AssemblyName>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "NoTfm.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net10.0", info.TargetFramework);
    }

    #endregion

    #region TargetFrameworks (multi-target) picks highest version

    [Fact]
    public void ParseContent_MultiTarget_PicksHighestFramework()
    {
        var info = ProjectParser.ParseContent(MultiTargetCsproj, "multi.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_MultiTarget_OutOfOrder_PicksHighest()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net6.0;net7.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "m.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_MultiTarget_SingleInList()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net7.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "m.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net7.0", info.TargetFramework);
    }

    #endregion

    #region AssemblyName from XML and default fallback

    [Fact]
    public void ParseContent_SdkStyle_ReadsAssemblyName()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("MyApp.Core", info.AssemblyName);
    }

    [Fact]
    public void ParseContent_DefaultsAssemblyName_ToFileName()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "FallbackName.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("FallbackName", info.AssemblyName);
    }

    #endregion

    #region RootNamespace from XML and default fallback

    [Fact]
    public void ParseContent_SdkStyle_ReadsRootNamespace()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("MyApp.Core", info.RootNamespace);
    }

    [Fact]
    public void ParseContent_LegacyStyle_ReadsRootNamespace()
    {
        var info = ProjectParser.ParseContent(LegacyStyleCsproj, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("LegacyNs", info.RootNamespace);
    }

    [Fact]
    public void ParseContent_DefaultsRootNamespace_ToFileName()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "FallbackNs.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("FallbackNs", info.RootNamespace);
    }

    #endregion

    #region LangVersion

    [Fact]
    public void ParseContent_SdkStyle_ReadsLangVersion()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("12.0", info.LangVersion);
    }

    [Fact]
    public void ParseContent_NoLangVersion_ReturnsNull()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Null(info.LangVersion);
    }

    #endregion

    #region Nullable / NullableEnabled

    [Fact]
    public void ParseContent_NullableEnable_ReturnsTrue()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.True(info.NullableEnabled);
    }

    [Fact]
    public void ParseContent_NullableEnable_CaseInsensitive()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>Enable</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.True(info.NullableEnabled);
    }

    [Fact]
    public void ParseContent_NullableDisable_ReturnsFalse()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.NullableEnabled);
    }

    [Fact]
    public void ParseContent_NullableMissing_ReturnsFalse()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.NullableEnabled);
    }

    #endregion

    #region ProjectReference / Include attribute

    [Fact]
    public void ParseContent_SdkStyle_ExtractsProjectReferences()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.ProjectReferences);
        Assert.Contains("MyApp.Shared.csproj", info.ProjectReferences[0]);
    }

    [Fact]
    public void ParseContent_NoProjectReferences_ReturnsEmpty()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Empty(info.ProjectReferences);
    }

    [Fact]
    public void ParseContent_ProjectReference_WithoutInclude_IsSkipped()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference />
    <ProjectReference Include=""..\Real\Real.csproj"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.ProjectReferences);
        Assert.Contains("Real.csproj", info.ProjectReferences[0]);
    }

    #endregion

    #region PackageReference / Include + Version attributes

    [Fact]
    public void ParseContent_SdkStyle_ExtractsPackageReferences()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal(2, info.PackageReferences.Count);
        Assert.Contains(info.PackageReferences, p => p.Name == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(info.PackageReferences, p => p.Name == "Serilog" && p.Version == "3.1.1");
    }

    [Fact]
    public void ParseContent_PackageReference_VersionInChildElement()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""ChildVer.Pkg"">
      <Version>2.0.0</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.PackageReferences);
        Assert.Equal("ChildVer.Pkg", info.PackageReferences[0].Name);
        Assert.Equal("2.0.0", info.PackageReferences[0].Version);
    }

    [Fact]
    public void ParseContent_PackageReference_EmptyName_IsFiltered()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="""" Version=""1.0.0"" />
    <PackageReference Version=""2.0.0"" />
    <PackageReference Include=""ValidPkg"" Version=""3.0.0"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.PackageReferences);
        Assert.Equal("ValidPkg", info.PackageReferences[0].Name);
        Assert.Equal("3.0.0", info.PackageReferences[0].Version);
    }

    [Fact]
    public void ParseContent_PackageReference_NoVersion_ReturnsNull()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""NoVerPkg"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.PackageReferences);
        Assert.Equal("NoVerPkg", info.PackageReferences[0].Name);
        Assert.Null(info.PackageReferences[0].Version);
    }

    #endregion

    #region Legacy Compile element parsing

    [Fact]
    public void ParseContent_LegacyStyle_ParsesCompileItems()
    {
        var info = ProjectParser.ParseContent(LegacyStyleCsproj, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Equal(2, info.SourceFiles.Count);
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Program.cs"));
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Helper.cs"));
    }

    [Fact]
    public void ParseContent_LegacyStyle_OnlyCsFilesFromCompile()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Code.cs"" />
    <Compile Include=""Resources.resx"" />
    <Compile Include=""Image.png"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.SourceFiles);
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Code.cs"));
    }

    [Fact]
    public void ParseContent_LegacyStyle_CsExtension_CaseInsensitive()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Upper.CS"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.SourceFiles);
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Upper.CS"));
    }

    #endregion

    #region ProjectPath and ProjectDirectory are set

    [Fact]
    public void ParseContent_SetsProjectPath()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, @"C:\repos\MyApp\MyApp.csproj", @"C:\repos\MyApp");
        Assert.Equal(@"C:\repos\MyApp\MyApp.csproj", info.ProjectPath);
    }

    [Fact]
    public void ParseContent_SetsProjectDirectory()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, @"C:\repos\MyApp\MyApp.csproj", @"C:\repos\MyApp");
        Assert.Equal(@"C:\repos\MyApp", info.ProjectDirectory);
    }

    #endregion

    #region LegacyStyle extracts all properties

    [Fact]
    public void ParseContent_LegacyStyle_ExtractsTargetFramework()
    {
        var info = ProjectParser.ParseContent(LegacyStyleCsproj, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net472", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_LegacyStyle_ExtractsAssemblyName()
    {
        var info = ProjectParser.ParseContent(LegacyStyleCsproj, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("LegacyApp", info.AssemblyName);
    }

    #endregion

    #region Edge cases for TargetFramework resolution

    [Fact]
    public void ParseContent_TfmWithWhitespace_IsTrimmed()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>  net7.0  </TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net7.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_TfmTakesPrecedenceOverTfms()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <TargetFrameworks>net7.0;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net6.0", info.TargetFramework);
    }

    #endregion

    #region Multiple ProjectReferences

    [Fact]
    public void ParseContent_MultipleProjectReferences()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\A\A.csproj"" />
    <ProjectReference Include=""..\B\B.csproj"" />
    <ProjectReference Include=""..\C\C.csproj"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal(3, info.ProjectReferences.Count);
        Assert.Contains(info.ProjectReferences, r => r.Contains("A.csproj"));
        Assert.Contains(info.ProjectReferences, r => r.Contains("B.csproj"));
        Assert.Contains(info.ProjectReferences, r => r.Contains("C.csproj"));
    }

    #endregion

    #region Version attribute takes precedence over child element

    [Fact]
    public void ParseContent_PackageReference_AttributeVersionOverChildElement()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Both.Pkg"" Version=""1.0.0"">
      <Version>2.0.0</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("1.0.0", info.PackageReferences[0].Version);
    }

    #endregion

    #region Whitespace-only property is ignored

    [Fact]
    public void ParseContent_WhitespaceOnlyProperty_FallsBack()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>   </AssemblyName>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "FallbackAsm.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("FallbackAsm", info.AssemblyName);
    }

    #endregion

    #region Full round-trip: all fields set in one test

    [Fact]
    public void ParseContent_FullProject_AllFieldsPopulated()
    {
        var info = ProjectParser.ParseContent(SdkStyleCsproj, @"C:\src\test.csproj", @"C:\src");

        Assert.Equal(@"C:\src\test.csproj", info.ProjectPath);
        Assert.Equal(@"C:\src", info.ProjectDirectory);
        Assert.Equal("MyApp.Core", info.AssemblyName);
        Assert.Equal("net8.0", info.TargetFramework);
        Assert.Equal("MyApp.Core", info.RootNamespace);
        Assert.Equal("12.0", info.LangVersion);
        Assert.True(info.NullableEnabled);
        Assert.True(info.IsSdkStyle);
        Assert.Single(info.ProjectReferences);
        Assert.Equal(2, info.PackageReferences.Count);
    }

    #endregion

    #region Legacy namespace (xmlns) support

    [Fact]
    public void ParseContent_LegacyNamespace_ReadsAllProperties()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>LegNs</AssemblyName>
    <RootNamespace>LegNsRoot</RootNamespace>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.cs"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Dep\Dep.csproj"" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include=""Pkg1"" Version=""1.0.0"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "leg.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.IsSdkStyle);
        Assert.Equal("net48", info.TargetFramework);
        Assert.Equal("LegNs", info.AssemblyName);
        Assert.Equal("LegNsRoot", info.RootNamespace);
        Assert.Equal("9.0", info.LangVersion);
        Assert.True(info.NullableEnabled);
        Assert.Single(info.SourceFiles);
        Assert.Single(info.ProjectReferences);
        Assert.Single(info.PackageReferences);
        Assert.Equal("Pkg1", info.PackageReferences[0].Name);
        Assert.Equal("1.0.0", info.PackageReferences[0].Version);
    }

    #endregion

    #region NullableEnabled string comparisons

    [Fact]
    public void ParseContent_NullableWarnings_ReturnsFalse()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>warnings</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.NullableEnabled);
    }

    [Fact]
    public void ParseContent_NullableENABLE_AllCaps_ReturnsTrue()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>ENABLE</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.True(info.NullableEnabled);
    }

    #endregion

    #region ExtractVersion ordering

    [Fact]
    public void ParseContent_MultiTarget_PicksNetstandard21OverNetstandard20()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;netstandard2.1</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("netstandard2.1", info.TargetFramework);
    }

    #endregion

    #region Compile element without Include is skipped

    [Fact]
    public void ParseContent_LegacyCompile_NoInclude_IsSkipped()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile />
    <Compile Include=""Valid.cs"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.SourceFiles);
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Valid.cs"));
    }

    #endregion

    #region PackageRef record structure

    [Fact]
    public void PackageRef_RecordEquality()
    {
        var a = new PackageRef("Pkg", "1.0.0");
        var b = new PackageRef("Pkg", "1.0.0");
        Assert.Equal(a, b);
    }

    [Fact]
    public void PackageRef_NullVersion()
    {
        var p = new PackageRef("Pkg", null);
        Assert.Equal("Pkg", p.Name);
        Assert.Null(p.Version);
    }

    #endregion

    #region Parse method validates file existence

    [Fact]
    public void Parse_NonExistentFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ProjectParser.Parse(@"C:\nonexistent\path\fake.csproj"));
    }

    #endregion

    #region Empty project

    [Fact]
    public void ParseContent_EmptyPropertyGroup_DefaultsEverything()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "Empty.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net10.0", info.TargetFramework);
        Assert.Equal("Empty", info.AssemblyName);
        Assert.Equal("Empty", info.RootNamespace);
        Assert.Null(info.LangVersion);
        Assert.False(info.NullableEnabled);
        Assert.True(info.IsSdkStyle);
        Assert.Empty(info.ProjectReferences);
        Assert.Empty(info.PackageReferences);
    }

    #endregion

    #region Multiple PropertyGroups - first non-empty wins

    [Fact]
    public void ParseContent_MultiplePropertyGroups_FirstNonEmptyWins()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
  </PropertyGroup>
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net7.0", info.TargetFramework);
    }

    #endregion

    #region Mixed package references (attribute + child version + no version + empty name)

    [Fact]
    public void ParseContent_MixedPackageReferences()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""AttrVer"" Version=""1.0.0"" />
    <PackageReference Include=""ChildVer"">
      <Version>2.0.0</Version>
    </PackageReference>
    <PackageReference Include=""NoVer"" />
    <PackageReference Include="""" Version=""9.9.9"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal(3, info.PackageReferences.Count);

        var attrVer = info.PackageReferences.First(p => p.Name == "AttrVer");
        Assert.Equal("1.0.0", attrVer.Version);

        var childVer = info.PackageReferences.First(p => p.Name == "ChildVer");
        Assert.Equal("2.0.0", childVer.Version);

        var noVer = info.PackageReferences.First(p => p.Name == "NoVer");
        Assert.Null(noVer.Version);
    }

    #endregion

    #region Conditional false mutation on isSdkStyle (L77)

    [Fact]
    public void ParseContent_SdkStyle_UsesGlobSourceFiles_NotLegacy()
    {
        // SDK style project uses GlobSourceFiles, legacy uses ParseLegacySourceFiles
        // Mutating the condition to false would use legacy parsing for SDK projects
        const string sdkProject = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(sdkProject, "t.csproj", Directory.GetCurrentDirectory());
        // SDK style project should NOT parse Compile items (because there are none in XML)
        // but should still work - source files come from globbing
        Assert.True(info.IsSdkStyle);
    }

    [Fact]
    public void ParseContent_LegacyStyle_UsesCompileItems_NotGlob()
    {
        const string legacyXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Foo.cs"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(legacyXml, "legacy.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.IsSdkStyle);
        // Legacy should parse Compile items
        Assert.Single(info.SourceFiles);
        Assert.Contains(info.SourceFiles, f => f.EndsWith("Foo.cs"));
    }

    #endregion

    #region Version != null mutation (L96)

    [Fact]
    public void ParseContent_PackageVersion_NullVsNotNull()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""HasVer"" Version=""1.0.0"" />
    <PackageReference Include=""NoVer"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());

        var hasVer = info.PackageReferences.First(p => p.Name == "HasVer");
        Assert.Equal("1.0.0", hasVer.Version);
        Assert.NotNull(hasVer.Version); // Explicitly check not null

        var noVer = info.PackageReferences.First(p => p.Name == "NoVer");
        Assert.Null(noVer.Version); // Must be null, not some value
    }

    #endregion

    #region StringSplitOptions bitwise mutation (L127)

    [Fact]
    public void ParseContent_MultiTarget_TrimsWhitespace()
    {
        // Tests that TrimEntries is active (bitwise mutation would break this)
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>  net6.0 ; net8.0  </TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_MultiTarget_RemovesEmptyEntries()
    {
        // Tests that RemoveEmptyEntries is active
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;;net8.0;</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    #endregion

    #region First() to FirstOrDefault() mutation (L129)

    [Fact]
    public void ParseContent_MultiTarget_SingleFramework_StillWorks()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("net8.0", info.TargetFramework);
    }

    #endregion

    #region ProjectPath and ProjectDirectory propagation

    [Fact]
    public void ParseContent_SetsProjectPath_Correctly()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "my/project.csproj", "my");
        Assert.Equal("my/project.csproj", info.ProjectPath);
        Assert.Equal("my", info.ProjectDirectory);
    }

    #endregion

    #region while loop dir == null mutations (L182, L216)

    [Fact]
    public void ParseContent_NoDirectoryBuildProps_StillWorks()
    {
        // With no Directory.Build.props in parent dirs, parsing should still work
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Test</AssemblyName>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "test.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("Test", info.AssemblyName);
    }

    #endregion

    #region Integration: Directory.Build.props and Directory.Packages.props

    [Fact]
    public void Parse_WithDirectoryBuildProps_InheritsProperties()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_pp_" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(testDir, "src", "MyApp");
        Directory.CreateDirectory(subDir);
        try
        {
            // Create Directory.Build.props in parent
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            // Create .csproj without LangVersion or Nullable (should inherit)
            var csprojPath = Path.Combine(subDir, "MyApp.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            // Create a source file so globbing finds something
            File.WriteAllText(Path.Combine(subDir, "Program.cs"), "public class Program { }");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Equal("net8.0", info.TargetFramework);
            Assert.Equal("12.0", info.LangVersion);
            Assert.True(info.NullableEnabled);
            Assert.Equal("MyApp", info.AssemblyName); // Default from filename
            Assert.True(info.IsSdkStyle);
            Assert.NotEmpty(info.SourceFiles); // SDK style globs .cs files
            Assert.Contains(info.SourceFiles, f => f.EndsWith("Program.cs"));
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_WithDirectoryPackagesProps_ResolvesCentralVersions()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_pp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            // Create Directory.Packages.props
            File.WriteAllText(Path.Combine(testDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>");

            // Create .csproj with PackageReference without Version (should get from central)
            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" />
  </ItemGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Single(info.PackageReferences);
            var pkg = info.PackageReferences[0];
            Assert.Equal("Newtonsoft.Json", pkg.Name);
            Assert.Equal("13.0.3", pkg.Version); // Resolved from central
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_SdkStyle_GlobsSourceFiles_ExcludingBin()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_pp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>");

            // Create source files
            File.WriteAllText(Path.Combine(testDir, "A.cs"), "public class A {}");
            File.WriteAllText(Path.Combine(testDir, "B.cs"), "public class B {}");

            // Create bin directory with a .cs file (should be excluded)
            var binDir = Path.Combine(testDir, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "Generated.cs"), "public class Gen {}");

            var info = ProjectParser.Parse(csprojPath);

            // Should find A.cs and B.cs but NOT bin/Generated.cs
            Assert.Equal(2, info.SourceFiles.Count);
            Assert.Contains(info.SourceFiles, f => f.EndsWith("A.cs"));
            Assert.Contains(info.SourceFiles, f => f.EndsWith("B.cs"));
            Assert.DoesNotContain(info.SourceFiles, f => f.Contains("Generated.cs"));
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: XML element name specificity

    [Fact]
    public void ParseContent_AllPropertyElementNames_AreCorrectlyMapped()
    {
        // Each property has a UNIQUE value so that if any XML element name string
        // is mutated (e.g., "TargetFramework" -> "AssemblyName"), the wrong value
        // would be returned and the test fails.
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>UniqueAssembly</AssemblyName>
    <RootNamespace>UniqueNamespace</RootNamespace>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "mapped.csproj", Directory.GetCurrentDirectory());

        Assert.Equal("net9.0", info.TargetFramework);
        Assert.Equal("UniqueAssembly", info.AssemblyName);
        Assert.Equal("UniqueNamespace", info.RootNamespace);
        Assert.Equal("preview", info.LangVersion);
        Assert.True(info.NullableEnabled);

        // Verify defaults are NOT used (would indicate element name wasn't found)
        Assert.NotEqual("mapped", info.AssemblyName);
        Assert.NotEqual("mapped", info.RootNamespace);
        Assert.NotEqual("net10.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_PropertyGroupElementName_IsRequired()
    {
        // If "PropertyGroup" string is mutated, properties won't be found
        // and defaults will be used instead of the real values
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>RealName</AssemblyName>
    <RootNamespace>RealNs</RootNamespace>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "pg.csproj", Directory.GetCurrentDirectory());

        // These must come from the XML, not from defaults
        Assert.Equal("net7.0", info.TargetFramework);
        Assert.Equal("RealName", info.AssemblyName);
        Assert.Equal("RealNs", info.RootNamespace);
    }

    #endregion

    #region Mutation killers: PackageReference element/attribute names

    [Fact]
    public void ParseContent_PackageReference_ElementAndAttributeNames_AreCorrect()
    {
        // Verifies "PackageReference", "Include", "Version" strings are correct
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Exact.Pkg.Name"" Version=""4.5.6"" />
    <PackageReference Include=""Another.Pkg"" Version=""7.8.9"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());

        Assert.Equal(2, info.PackageReferences.Count);

        var pkg1 = info.PackageReferences.First(p => p.Name == "Exact.Pkg.Name");
        Assert.Equal("4.5.6", pkg1.Version);

        var pkg2 = info.PackageReferences.First(p => p.Name == "Another.Pkg");
        Assert.Equal("7.8.9", pkg2.Version);
    }

    [Fact]
    public void ParseContent_PackageReference_VersionChildElement_NameMatters()
    {
        // The child element <Version> must specifically be named "Version"
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""ChildOnly"">
      <Version>3.2.1</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());

        Assert.Single(info.PackageReferences);
        Assert.Equal("ChildOnly", info.PackageReferences[0].Name);
        Assert.Equal("3.2.1", info.PackageReferences[0].Version);
    }

    #endregion

    #region Mutation killers: First() to FirstOrDefault() on framework selection

    [Fact]
    public void ParseContent_MultiTarget_ResultIsNeverNull()
    {
        // If First() is mutated to FirstOrDefault(), with valid entries it still works,
        // but we must verify the result is a real non-null, non-empty string
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());

        Assert.NotNull(info.TargetFramework);
        Assert.NotEmpty(info.TargetFramework);
        Assert.Equal("net8.0", info.TargetFramework);
    }

    [Fact]
    public void ParseContent_MultiTarget_AllSemicolons_ThrowsOrDefaults()
    {
        // With ";;;" after RemoveEmptyEntries, array is empty.
        // First() throws; FirstOrDefault() returns null.
        // Actually, ";;;" is not whitespace-only, so the code enters the branch.
        // This tests the First() behavior specifically.
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>;;;</TargetFrameworks>
  </PropertyGroup>
</Project>";
        Assert.ThrowsAny<InvalidOperationException>(() =>
            ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory()));
    }

    #endregion

    #region Mutation killers: Source file discovery pattern

    [Fact]
    public void Parse_SdkStyle_FindsCsFiles_NotOtherExtensions()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_glob_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var csprojPath = Path.Combine(testDir, "GlobTest.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>");

            // Create various file types
            File.WriteAllText(Path.Combine(testDir, "Real.cs"), "class Real {}");
            File.WriteAllText(Path.Combine(testDir, "Style.css"), "body {}");
            File.WriteAllText(Path.Combine(testDir, "Config.json"), "{}");
            File.WriteAllText(Path.Combine(testDir, "Script.js"), "var x;");
            File.WriteAllText(Path.Combine(testDir, "Data.txt"), "data");

            var info = ProjectParser.Parse(csprojPath);

            // Must find .cs files only
            Assert.Single(info.SourceFiles);
            Assert.Contains(info.SourceFiles, f => f.EndsWith("Real.cs"));
            Assert.DoesNotContain(info.SourceFiles, f => f.EndsWith(".css"));
            Assert.DoesNotContain(info.SourceFiles, f => f.EndsWith(".json"));
            Assert.DoesNotContain(info.SourceFiles, f => f.EndsWith(".js"));
            Assert.DoesNotContain(info.SourceFiles, f => f.EndsWith(".txt"));
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_SdkStyle_FindsCsFiles_InSubdirectories()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_glob2_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var csprojPath = Path.Combine(testDir, "SubDir.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>");

            File.WriteAllText(Path.Combine(testDir, "Root.cs"), "class Root {}");
            var subDir = Path.Combine(testDir, "Models");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "Model.cs"), "class Model {}");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Equal(2, info.SourceFiles.Count);
            Assert.Contains(info.SourceFiles, f => f.EndsWith("Root.cs"));
            Assert.Contains(info.SourceFiles, f => f.EndsWith("Model.cs"));
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: Directory.Build.props property deduplication

    [Fact]
    public void Parse_DirectoryBuildProps_LocalPropertyOverridesInherited()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_dedup_" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(testDir, "src");
        Directory.CreateDirectory(subDir);
        try
        {
            // Directory.Build.props sets LangVersion=10.0
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>10.0</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>");

            // .csproj overrides with its own values
            var csprojPath = Path.Combine(subDir, "Dedup.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            // Local values must win over inherited
            Assert.Equal("12.0", info.LangVersion);
            Assert.True(info.NullableEnabled);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_DirectoryBuildProps_DuplicateKeys_FirstValueWins()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_firstval_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            // Directory.Build.props with same key in two PropertyGroups
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>11.0</LangVersion>
  </PropertyGroup>
  <PropertyGroup>
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>
</Project>");

            // .csproj without LangVersion should inherit first value
            var csprojPath = Path.Combine(testDir, "First.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            // First non-empty value (11.0) should win because of ContainsKey check
            Assert.Equal("11.0", info.LangVersion);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: Directory.Packages.props null checks and break

    [Fact]
    public void Parse_DirectoryPackagesProps_NullIncludeOrVersion_IsSkipped()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_cpmnull_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            // PackageVersion elements: one valid, one missing Include, one missing Version
            File.WriteAllText(Path.Combine(testDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""ValidPkg"" Version=""1.0.0"" />
    <PackageVersion Version=""2.0.0"" />
    <PackageVersion Include=""NoVer"" />
  </ItemGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "NullCheck.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""ValidPkg"" />
    <PackageReference Include=""NoVer"" />
    <PackageReference Include=""NotInCentral"" />
  </ItemGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Equal(3, info.PackageReferences.Count);

            var valid = info.PackageReferences.First(p => p.Name == "ValidPkg");
            Assert.Equal("1.0.0", valid.Version); // Resolved from central

            var noVer = info.PackageReferences.First(p => p.Name == "NoVer");
            Assert.Null(noVer.Version); // No version in central (missing Version attr)

            var notCentral = info.PackageReferences.First(p => p.Name == "NotInCentral");
            Assert.Null(notCentral.Version); // Not in central at all
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_DirectoryPackagesProps_TryAdd_FirstVersionWins()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_tryadd_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            // Duplicate package versions - TryAdd means first wins
            File.WriteAllText(Path.Combine(testDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""DupPkg"" Version=""1.0.0"" />
    <PackageVersion Include=""DupPkg"" Version=""2.0.0"" />
  </ItemGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "TryAdd.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""DupPkg"" />
  </ItemGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Single(info.PackageReferences);
            Assert.Equal("1.0.0", info.PackageReferences[0].Version); // First wins via TryAdd
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: central version NOT used when attribute version exists

    [Fact]
    public void Parse_CentralVersion_DoesNotOverrideAttributeVersion()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_centralattr_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            File.WriteAllText(Path.Combine(testDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Pkg"" Version=""9.9.9"" />
  </ItemGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Pkg"" Version=""1.0.0"" />
  </ItemGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Single(info.PackageReferences);
            Assert.Equal("1.0.0", info.PackageReferences[0].Version);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: Directory.Build.props break (only nearest used)

    [Fact]
    public void Parse_DirectoryBuildProps_OnlyNearestIsUsed()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_nearest_" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(testDir, "src");
        Directory.CreateDirectory(subDir);
        try
        {
            // Grandparent has LangVersion=9.0
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>9.0</LangVersion>
  </PropertyGroup>
</Project>");

            // Parent (closer to csproj) has LangVersion=12.0
            File.WriteAllText(Path.Combine(subDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
</Project>");

            var csprojPath = Path.Combine(subDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            // Nearest Directory.Build.props wins (break after first found)
            Assert.Equal("12.0", info.LangVersion);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: Directory.Packages.props break (only nearest used)

    [Fact]
    public void Parse_DirectoryPackagesProps_OnlyNearestIsUsed()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_nearestpkg_" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(testDir, "src");
        Directory.CreateDirectory(subDir);
        try
        {
            // Grandparent has older version
            File.WriteAllText(Path.Combine(testDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Pkg"" Version=""1.0.0"" />
  </ItemGroup>
</Project>");

            // Parent (closer) has newer version
            File.WriteAllText(Path.Combine(subDir, "Directory.Packages.props"),
                @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Pkg"" Version=""2.0.0"" />
  </ItemGroup>
</Project>");

            var csprojPath = Path.Combine(subDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Pkg"" />
  </ItemGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Single(info.PackageReferences);
            Assert.Equal("2.0.0", info.PackageReferences[0].Version);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: GlobSourceFiles with nonexistent dir

    [Fact]
    public void ParseContent_SdkStyle_NonexistentDir_ReturnsEmptySourceFiles()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        // Use a directory that does not exist for globbing
        var info = ProjectParser.ParseContent(xml, "fake.csproj",
            Path.Combine(Directory.GetCurrentDirectory(), "nonexistent_dir_" + Guid.NewGuid().ToString("N")));
        Assert.Empty(info.SourceFiles);
    }

    #endregion

    #region Mutation killers: ??= inheritance from Directory.Build.props

    [Fact]
    public void Parse_DirectoryBuildProps_InheritsTargetFramework()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_tfminh_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>InheritedAsm</AssemblyName>
    <RootNamespace>InheritedNs</RootNamespace>
  </PropertyGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Equal("net7.0", info.TargetFramework);
            Assert.Equal("InheritedAsm", info.AssemblyName);
            Assert.Equal("InheritedNs", info.RootNamespace);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Parse_DirectoryBuildProps_InheritsTargetFrameworks()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_tfmsinh_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Equal("net8.0", info.TargetFramework);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: whitespace-only property in Directory.Build.props

    [Fact]
    public void Parse_DirectoryBuildProps_WhitespaceProperty_NotInherited()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_wsprop_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            File.WriteAllText(Path.Combine(testDir, "Directory.Build.props"),
                @"<Project>
  <PropertyGroup>
    <LangVersion>   </LangVersion>
  </PropertyGroup>
</Project>");

            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var info = ProjectParser.Parse(csprojPath);

            // Whitespace-only value should be skipped by !string.IsNullOrWhiteSpace check
            Assert.Null(info.LangVersion);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Mutation killers: ExtractVersion edge cases

    [Fact]
    public void ParseContent_MultiTarget_UnparsableVersion_TreatedAsZero()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>noversion;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        // "noversion" has no digits → ExtractVersion returns 0, net8.0 → 8.0
        Assert.Equal("net8.0", info.TargetFramework);
    }

    #endregion

    #region Mutation killers: bin exclusion uses OrdinalIgnoreCase

    [Fact]
    public void Parse_SdkStyle_ExcludesBinCaseInsensitive()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_bincase_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var csprojPath = Path.Combine(testDir, "Test.csproj");
            File.WriteAllText(csprojPath,
                @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>");

            File.WriteAllText(Path.Combine(testDir, "Good.cs"), "class Good {}");
            var binDir = Path.Combine(testDir, "BIN");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "Bad.cs"), "class Bad {}");

            var info = ProjectParser.Parse(csprojPath);

            Assert.Single(info.SourceFiles);
            Assert.Contains(info.SourceFiles, f => f.EndsWith("Good.cs"));
            Assert.DoesNotContain(info.SourceFiles, f => f.Contains("Bad.cs"));
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    #endregion

    #region Exact property values kill string-constant mutants

    [Fact]
    public void ParseContent_ExactTargetFramework_NotDefault()
    {
        // If "TargetFramework" string constant is mutated, the value won't be found
        // and we'll get the default "net10.0" — but we're using a DIFFERENT value here.
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>SpecificName</AssemblyName>
    <RootNamespace>My.Specific.Ns</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "Fallback.csproj", Directory.GetCurrentDirectory());

        // These must match XML values, NOT fallback defaults
        Assert.Equal("net7.0", info.TargetFramework);
        Assert.Equal("SpecificName", info.AssemblyName);
        Assert.Equal("My.Specific.Ns", info.RootNamespace);
        Assert.True(info.NullableEnabled);
        Assert.Equal("11", info.LangVersion);
    }

    [Fact]
    public void ParseContent_NullableDisableExact_ReturnsFalse()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.False(info.NullableEnabled);
    }

    [Fact]
    public void ParseContent_AssemblyNameFromXml_NotFilenameFallback()
    {
        // If "AssemblyName" constant is mutated, it won't find the element
        // and will fall back to the filename "WrongFallback" — our assertion catches that.
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>CorrectAssembly</AssemblyName>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "WrongFallback.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("CorrectAssembly", info.AssemblyName);
    }

    [Fact]
    public void ParseContent_RootNamespaceFromXml_NotFilenameFallback()
    {
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Correct.Namespace</RootNamespace>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "WrongFallback.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("Correct.Namespace", info.RootNamespace);
    }

    [Fact]
    public void ParseContent_PackageReferenceIncludeAttribute_Parsed()
    {
        // If "PackageReference" or "Include" string is mutated, no packages are found
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""My.Package"" Version=""1.2.3"" />
  </ItemGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Single(info.PackageReferences);
        Assert.Equal("My.Package", info.PackageReferences[0].Name);
        Assert.Equal("1.2.3", info.PackageReferences[0].Version);
    }

    [Fact]
    public void ParseContent_LangVersionFromXml_NotNull()
    {
        // If "LangVersion" constant is mutated, the property won't be found and result is null
        const string xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
</Project>";
        var info = ProjectParser.ParseContent(xml, "t.csproj", Directory.GetCurrentDirectory());
        Assert.Equal("preview", info.LangVersion);
    }

    #endregion

}
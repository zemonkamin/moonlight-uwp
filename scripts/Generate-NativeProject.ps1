$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = [System.Collections.Generic.List[string]]::new()
Get-ChildItem "$root/moonlight-common-c/src/*.c" | ForEach-Object { $sources.Add('../moonlight-common-c/src/' + $_.Name) }
Get-ChildItem "$root/moonlight-common-c/enet/*.c" | Where-Object Name -ne 'unix.c' | ForEach-Object { $sources.Add('../moonlight-common-c/enet/' + $_.Name) }
foreach ($file in @('rs.c','deps/obl/oblas_common.c','deps/obl/oblas_lite.c')) { $sources.Add('../moonlight-common-c/nanors/' + $file) }
Get-ChildItem "$root/third_party/mbedtls/library/*.c" | ForEach-Object { $sources.Add('../third_party/mbedtls/library/' + $_.Name) }
foreach ($spec in @(@('opus','OPUS_SOURCES'), @('opus','OPUS_SOURCES_FLOAT'), @('celt','CELT_SOURCES'), @('silk','SILK_SOURCES'), @('silk','SILK_SOURCES_FLOAT'))) {
    $content = Get-Content "$root/third_party/opus/$($spec[0])_sources.mk" -Raw
    $match = [regex]::Match($content, '(?m)^' + $spec[1] + ' = ([\s\S]*?)(?:\r?\n\r?\n|\z)')
    if (!$match.Success) { throw "Source list missing: $($spec[1])" }
    foreach ($m in [regex]::Matches($match.Groups[1].Value, '[\w/]+\.c')) { $sources.Add('../third_party/opus/' + $m.Value) }
}
$directories = [System.Collections.Generic.HashSet[string]]::new()
$items = foreach ($source in $sources | Sort-Object -Unique) {
    $dir = (($source.Substring(0, $source.LastIndexOf('/'))) -replace '^\.\./','' -replace '[/.]','_') + '\'
    $null = $directories.Add('$(IntDir)' + $dir)
    '    <ClCompile Include="' + $source.Replace('/','\') + '"><CompileAsWinRT>false</CompileAsWinRT><ObjectFileName>$(IntDir)' + $dir + '</ObjectFileName></ClCompile>'
}
$configs = foreach ($config in @('Debug','Release')) { foreach ($platform in @('ARM','Win32','x64')) {
    "    <ProjectConfiguration Include=`"$config|$platform`"><Configuration>$config</Configuration><Platform>$platform</Platform></ProjectConfiguration>"
} }
$project = @'
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup Label="ProjectConfigurations">
__CONFIGS__
  </ItemGroup>
  <PropertyGroup Label="Globals">
    <ProjectGuid>{31A0B5D1-AFFA-4436-86C6-6508CEF0800B}</ProjectGuid>
    <RootNamespace>MoonlightNative</RootNamespace><ProjectName>Moonlight.Native</ProjectName>
    <DefaultLanguage>en-US</DefaultLanguage><MinimumVisualStudioVersion>15.0</MinimumVisualStudioVersion>
    <AppContainerApplication>true</AppContainerApplication><ApplicationType>Windows Store</ApplicationType><ApplicationTypeRevision>10.0</ApplicationTypeRevision>
    <WindowsTargetPlatformVersion>10.0.15063.0</WindowsTargetPlatformVersion><WindowsTargetPlatformMinVersion>10.0.14393.0</WindowsTargetPlatformMinVersion>
  </PropertyGroup>
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props" />
  <PropertyGroup Label="Configuration">
    <ConfigurationType>DynamicLibrary</ConfigurationType><PlatformToolset>v141</PlatformToolset>
    <UseDebugLibraries Condition="'$(Configuration)'=='Debug'">true</UseDebugLibraries>
    <UseDebugLibraries Condition="'$(Configuration)'=='Release'">false</UseDebugLibraries>
  </PropertyGroup>
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props" />
  <PropertyGroup><OutDir>$(ProjectDir)..\bin\$(Platform)\$(Configuration)\Native\</OutDir><IntDir>$(ProjectDir)..\obj\native\$(Platform)\$(Configuration)\</IntDir><GenerateManifest>false</GenerateManifest><TargetName>MoonlightNative</TargetName></PropertyGroup>
  <ItemDefinitionGroup>
    <ClCompile>
      <PrecompiledHeader>NotUsing</PrecompiledHeader><CompileAsWinRT>true</CompileAsWinRT>
      <WarningLevel>Level3</WarningLevel><SDLCheck>false</SDLCheck><MultiProcessorCompilation>true</MultiProcessorCompilation><ExceptionHandling>Sync</ExceptionHandling>
      <PreprocessorDefinitions>NOMINMAX;_WINRT_DLL;_CRT_SECURE_NO_WARNINGS=1;_WINSOCK_DEPRECATED_NO_WARNINGS;USE_MBEDTLS;HAS_SOCKLEN_T;OPUS_BUILD;USE_ALLOCA;HAVE_LRINTF;MBEDTLS_CONFIG_FILE="MoonlightMbedTlsConfig.h";%(PreprocessorDefinitions)</PreprocessorDefinitions>
      <AdditionalIncludeDirectories>$(ProjectDir);$(ProjectDir)..\moonlight-common-c\src;$(ProjectDir)..\moonlight-common-c\enet\include;$(ProjectDir)..\moonlight-common-c\nanors;$(ProjectDir)..\moonlight-common-c\nanors\deps;$(ProjectDir)..\moonlight-common-c\nanors\deps\obl;$(ProjectDir)..\third_party\mbedtls\include;$(ProjectDir)..\third_party\opus\include;$(ProjectDir)..\third_party\opus;$(ProjectDir)..\third_party\opus\celt;$(ProjectDir)..\third_party\opus\silk;$(ProjectDir)..\third_party\opus\silk\float;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
      <AdditionalOptions>/bigobj %(AdditionalOptions)</AdditionalOptions>
    </ClCompile>
    <Link><AdditionalDependencies>ws2_32.lib;bcrypt.lib;windowsapp.lib;%(AdditionalDependencies)</AdditionalDependencies><GenerateWindowsMetadata>true</GenerateWindowsMetadata><SubSystem>Console</SubSystem></Link>
  </ItemDefinitionGroup>
  <ItemDefinitionGroup Condition="'$(Configuration)'=='Release'"><ClCompile><Optimization>MaxSpeed</Optimization><PreprocessorDefinitions>NDEBUG;%(PreprocessorDefinitions)</PreprocessorDefinitions></ClCompile><Link><AdditionalOptions>/nodefaultlib:vccorlib /nodefaultlib:msvcrt vccorlib.lib msvcrt.lib %(AdditionalOptions)</AdditionalOptions></Link></ItemDefinitionGroup>
  <ItemDefinitionGroup Condition="'$(Configuration)'=='Debug'"><ClCompile><Optimization>Disabled</Optimization></ClCompile><Link><AdditionalOptions>/nodefaultlib:vccorlibd /nodefaultlib:msvcrtd vccorlibd.lib msvcrtd.lib %(AdditionalOptions)</AdditionalOptions></Link></ItemDefinitionGroup>
  <ItemGroup>
__SOURCES__
    <ClCompile Include="Runtime.cpp" />
    <ClInclude Include="Runtime.h" /><ClInclude Include="MoonlightMbedTlsConfig.h" /><ClInclude Include="threading_alt.h" />
  </ItemGroup>
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
  <Target Name="CreateNativeObjectDirectories" BeforeTargets="ClCompile"><MakeDir Directories="__DIRECTORIES__"/></Target>
</Project>
'@
$project.Replace('__CONFIGS__',($configs -join "`n")).Replace('__SOURCES__',($items -join "`n")).Replace('__DIRECTORIES__',($directories -join ';')) | Set-Content "$root/native/Moonlight.Native.vcxproj" -Encoding UTF8

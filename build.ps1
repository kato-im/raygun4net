properties {
    $root =                        $psake.build_script_dir
    $solution_file =                 "$root/Mindscape.Raygun4Net.sln"
    $solution_file2 =                "$root/Mindscape.Raygun4Net2.sln"
    $solution_file4 =                "$root/Mindscape.Raygun4Net4.sln"
    $solution_file_mvc =             "$root/Mindscape.Raygun4Net.Mvc.sln"
    $solution_file_webapi =          "$root/Mindscape.Raygun4Net.WebApi.sln"
    $solution_file_winrt =           "$root/Mindscape.Raygun4Net.WinRT.sln"
    $solution_file_windows_phone =   "$root/Mindscape.Raygun4Net.WindowsPhone.sln"
    $configuration =                 "Release"
    $build_dir =                     "$root\build\"
    $build_dir2 =                    "$build_dir\Net2"
    $build_dir4 =                    "$build_dir\Net4"
    $build_dir_mvc =                 "$build_dir\Mvc"
    $build_dir_webapi =              "$build_dir\WebApi"
    $nunit_dir =                     "$root\packages\NUnit.Runners.2.6.2\tools\"
    $tools_dir =                     "$root\tools"
    $nuget_dir =                     "$root\.nuget"
    $env:Path +=                     ";$nunit_dir;$tools_dir;$nuget_dir"
}

task default -depends Compile, CompileWinRT, CompileWindowsPhone

task Clean {
    remove-item -force -recurse $build_dir -ErrorAction SilentlyContinue | Out-Null
}

task Init -depends Clean {
    new-item $build_dir -itemType directory | Out-Null
}

task Compile -depends Init {
    exec { msbuild "$solution_file" /m /p:OutDir=$build_dir /p:Configuration=$configuration }
    exec { msbuild "$solution_file2" /m /p:OutDir=$build_dir2 /p:Configuration=$configuration }
    exec { msbuild "$solution_file4" /m /p:OutDir=$build_dir4 /p:Configuration=$configuration }
    exec { msbuild "$solution_file_mvc" /m /p:OutDir=$build_dir_mvc /p:Configuration=$configuration }
    exec { msbuild "$solution_file_webapi" /m /p:OutDir=$build_dir_webapi /p:Configuration=$configuration }
}

task CompileWinRT -depends Init {
    exec { msbuild "$solution_file_winrt" /m /p:OutDir=$build_dir /p:Configuration=$configuration }
    move-item $build_dir/Mindscape.Raygun4Net.WinRT/Mindscape.Raygun4Net.WinRT.dll $build_dir
    move-item $build_dir/Mindscape.Raygun4Net.WinRT/Mindscape.Raygun4Net.WinRT.pdb $build_dir
    move-item $build_dir/Mindscape.Raygun4Net.WinRT.Tests/Mindscape.Raygun4Net.WinRT.Tests.dll $build_dir
    move-item $build_dir/Mindscape.Raygun4Net.WinRT.Tests/Mindscape.Raygun4Net.WinRT.Tests.pdb $build_dir
    remove-item -force -recurse $build_dir/Mindscape.Raygun4Net.WinRT -ErrorAction SilentlyContinue | Out-Null
    remove-item -force -recurse $build_dir/Mindscape.Raygun4Net.WinRT.Tests -ErrorAction SilentlyContinue | Out-Null
}

task CompileWindowsPhone -depends Init {
    exec { msbuild "$solution_file_windows_phone" /m /p:OutDir=$build_dir /p:Configuration=$Configuration }
}

task Test -depends Compile, CompileWinRT, CompileWindowsPhone, CompileWindowsStore {
    $test_assemblies = Get-ChildItem $build_dir -Include *Tests.dll -Name

    Push-Location -Path $build_dir

    exec { nunit-console.exe $test_assemblies }

    Pop-Location
}

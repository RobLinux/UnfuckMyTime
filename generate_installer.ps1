$ErrorActionPreference = "Stop"

Write-Host "Building UnfuckMyTime..."

# Publish the application
dotnet publish src/UnfuckMyTime.UI/UnfuckMyTime.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/win-x64

# Check (simple) if we can determine version
$version = "1.0.0"
# Try to get it from the assembly info or MinVer if possible, but for now simple fallback.
# In a real CI env, we'd pass this in.

# Check for Inno Setup Compiler
if (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue) {
    Write-Host "Inno Setup Compiler found. Generating installer..."
    
    # We can try to grab the version from git tags if available
    try {
        $gitVersion = git describe --tags --abbrev=0
        if ($gitVersion) { 
            $version = $gitVersion -replace "^v",""
            Write-Host "Detected version from git: $version"
        }
    } catch {
        Write-Host "Could not detect version from git, using default: $version"
    }

    ISCC.exe "/DMyAppVersion=$version" installer/setup.iss
    Write-Host "Installer generated in dist/installer/"
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not found in PATH."
    Write-Warning "The application was published to dist/win-x64/ but the installer was not generated."
    Write-Warning "To generate the installer locally, please install Inno Setup 6."
}

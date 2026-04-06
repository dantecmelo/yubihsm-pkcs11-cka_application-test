# Part 1 — Install .NET 6 SDK
Ubuntu 20.04 does not ship .NET in its default apt repositories, so you add Microsoft's feed manually.

## Install prerequisites for adding a new apt source
`sudo apt-get update`

`sudo apt-get install -y wget apt-transport-https software-properties-common`

## Download and register Microsoft's package signing key
`wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb`

`sudo dpkg -i packages-microsoft-prod.deb`

`rm packages-microsoft-prod.deb`

## Update apt and install the SDK
`sudo apt-get update`

`sudo apt-get install -y dotnet-sdk-6.0`

## Verify the installation — you should see something like "6.0.x"
`dotnet --version`

If you see a version number, .NET is ready.

# Part 2 — Create the C# Project
## Make a project directory
`mkdir -p ~/yubihsm-pkcs11-cka_application-test`

`cd ~/yubihsm-pkcs11-cka_application-test`

## Create the project file
```cat > YubiHsmPkcs11Test.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <AssemblyName>YubiHsmPkcs11Test</AssemblyName>
    <RootNamespace>YubiHsmCkaApplicationTest</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Net.Pkcs11Interop" Version="5.1.3" />
  </ItemGroup>
</Project>
EOF```

## Save the C# source file
Create Program.cs and paste the full source code from the previous response into it:

nano Program.cs

Paste the code, then save with Ctrl+O, Enter, Ctrl+X.

## Edit the configuration constants
Open Program.cs and locate the Config class near the top. Change the following values to match your environment:

private static class Config
{
    // Full path to the .so file — confirm this with:
    // find /usr -name "yubihsm_pkcs11.so" 2>/dev/null
    public const string Pkcs11LibraryPath =
        "/usr/local/lib/pkcs11/yubihsm_pkcs11.so";

    // Slot 0 corresponds to the first connector entry in the config
    public const uint SlotId = 0;

    // Key ID (4 hex digits) concatenated with the password
    // Default factory auth key: ID = 0x0001, password = "password"
    public const string Pin = "0001password";

    // These two should stay as-is for the test
    public const string ApplicationTag = "SmartcryptWrappedBinary";
    public const string ObjectLabel    = "Custom-CKA_APPLICATION-Opaque-Object";
    public const string PayloadText    = "This is a custom CKA_APPLICATION Opaque Object";
}

Save and close the file.

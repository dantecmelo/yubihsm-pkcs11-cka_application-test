# What this test application does

This is a .NET C# application that tests the patched YubiHSM PKCS#11 library available at:
https://github.com/dantecmelo/yubihsm-shell_272_opaque.

An unpatched YubiHSM PKCS#11 library always reports `CKA_APPLICATION = "Opaque object"` for
`CKO_DATA` objects, discarding whatever the caller supplied. The patched library preserves
the caller's value. This test proves which one you have.

The test runs in three fully logged phases.

* **Phase 1 — Create.** It builds a `C_CreateObject` template with:
  - `CKO_DATA`
  - `CKA_TOKEN = true`
  - `CKA_LABEL = "Custom-CKA_APPLICATION-Opq-Obj"`
  - `CKA_APPLICATION = "SmartcryptWrappedBinary"`
  - A small UTF-8 payload as `CKA_VALUE`.

  Every attribute in the template is printed to the console before the call.

  > Labels are kept short deliberately. The YubiHSM caps object labels at 40 bytes, so the
  > longer names used in earlier drafts of this README are not usable.

* **Phase 2 — Find.** It issues `C_FindObjects` filtered by `CKO_DATA` and the label. It fails
  hard if the object is not found, which catches cases where `C_CreateObject` silently
  succeeded but didn't actually persist anything.

* **Phase 3 — Verify.** It calls `C_GetAttributeValue` for `CKA_CLASS`, `CKA_TOKEN`, `CKA_LABEL`,
  `CKA_APPLICATION`, and `CKA_VALUE`, then asserts each one against the expected value. The
  critical assertion is `CKA_APPLICATION == "SmartcryptWrappedBinary"`. If the library is not
  patched, you'll see:

~~~
*** TEST FAILED: CKA_APPLICATION: expected [SmartcryptWrappedBinary]
                 but got [Opaque object] ***
~~~

  If it is patched correctly, you'll see:

~~~
  ✓ CKA_CLASS       = CKO_DATA
  ✓ CKA_TOKEN       = true
  ✓ CKA_LABEL       = "Custom-CKA_APPLICATION-Opq-Obj"
  ✓ CKA_APPLICATION = "SmartcryptWrappedBinary"
  ✓ CKA_VALUE       = "Custom CKA_APPLICATION Opq Obj"

*** ALL CHECKS PASSED ***
~~~

The test deletes any leftover objects with the same label at startup, so repeated runs are
idempotent. Exit code is 0 on pass, 1 on assertion failure, 2 on unexpected exception.

### Note on end-of-run cleanup

The final `DestroyObject` call is currently **commented out** in `Program.cs` (see the
`// Cleanup` block in `Run()`), so the test object is intentionally **left on the device**
after a run. This is what allows you to inspect it with external tools such as `pkcs11-tool`
— see [Verifying with pkcs11-tool](#part-5--verifying-independently-with-pkcs11-tool).

Re-running the test is still safe: the startup sweep removes the previous object before
creating a new one. To restore self-cleaning behaviour, uncomment those two lines.

# How to Build and Run

## Part 1 — Install the .NET SDK

Ubuntu does not ship .NET in its default apt repositories, so you add Microsoft's feed
manually. Adjust the `/ubuntu/20.04/` path below to match your release.

### Install prerequisites for adding a new apt source
`sudo apt-get update`

`sudo apt-get install -y wget apt-transport-https software-properties-common`

### Download and register Microsoft's package signing key
`wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb`

`sudo dpkg -i packages-microsoft-prod.deb`

`rm packages-microsoft-prod.deb`

### Update apt and install the SDK
`sudo apt-get update`

`sudo apt-get install -y dotnet-sdk-8.0`

### Verify the installation — you should see something like "8.0.x"
`dotnet --version`

If you see a version number, .NET is ready.

> The project targets `net8.0`. To build against .NET 6 instead, install `dotnet-sdk-6.0` and
> change `<TargetFramework>` in the `.csproj` to `net6.0`; the source itself is compatible
> with both.

## Part 2 — Get the project

`Program.cs` and `YubiHsmPkcs11Test.csproj` are both included in this repository, so just
clone it:

~~~
git clone https://github.com/dantecmelo/yubihsm-pkcs11-cka_application-test.git
cd yubihsm-pkcs11-cka_application-test
~~~

For reference, the project file is:

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <AssemblyName>YubiHsmPkcs11Test</AssemblyName>
    <RootNamespace>YubiHsmCkaApplicationTest</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Pkcs11Interop" Version="5.2.0" />
  </ItemGroup>
</Project>
~~~

> Use `Pkcs11Interop` **5.2.0**. Version 5.1.3 is not published on nuget.org; requesting it
> produces an `NU1603` warning and silently resolves to 5.2.0 anyway.

### Edit the configuration constants

Open `Program.cs` and locate the `Config` class near the top. Change the following values to
match your environment:

~~~csharp
private static class Config
{
    // Full path to the .so file — confirm this with:
    // find /usr -name "yubihsm_pkcs11.so" 2>/dev/null
    public const string Pkcs11LibraryPath =
        "/usr/local/lib/pkcs11/yubihsm_pkcs11.so";

    // Slot 0 corresponds to the first connector entry in the config
    public const ulong SlotId = 0;

    // Key ID (4 hex digits) concatenated with the password
    // Default factory auth key: ID = 0x0001, password = "password"
    public const string Pin = "0001password";

    // These three should stay as-is for the test
    public const string ApplicationTag = "SmartcryptWrappedBinary";
    public const string ObjectLabel    = "Custom-CKA_APPLICATION-Opq-Obj";
    public const string PayloadText    = "Custom CKA_APPLICATION Opq Obj";
}
~~~

Save and close the file.

## Part 3 — Build the Project

### Change to the project directory
`cd ~/yubihsm-pkcs11-cka_application-test`

### Restore the NuGet package (downloads Net.Pkcs11Interop)
`dotnet restore`

### Build in Release configuration
`dotnet build --configuration Release`

### Confirm the binary was produced
`ls -la bin/Release/net8.0/YubiHsmPkcs11Test`

If the build succeeds, you will see:
~~~
Build succeeded.
    0 Warning(s)
    0 Error(s)
~~~

## Part 4 — Run the Test

### Prerequisites
Make sure:
* The YubiHSM Connector is running (`yubihsm-connector`, listening on port 12345 by default).
* The `YUBIHSM_PKCS11_CONF` variable is set and points to the YubiHSM PKCS#11 configuration file.
* The YubiHSM 2 is plugged in.

~~~
cd ~/yubihsm-pkcs11-cka_application-test

# The environment variable must be set in this shell.
# Note the filename varies by installation — check which one exists:
#   ls /etc/yubihsm*pkcs11*.conf
export YUBIHSM_PKCS11_CONF=/etc/yubihsm-pkcs11.conf

dotnet run --configuration Release
~~~

You can confirm the connector is up with `pgrep -a yubihsm-connector`.

#### Expected output when the YubiHSM PKCS#11 library is **not** yet patched
~~~
=== YubiHSM CKA_APPLICATION Round-Trip Test ===

[OK] Library loaded — /usr/local/lib/pkcs11/yubihsm_pkcs11.so
[OK] Slot found — slot id = 0
[OK] Session opened and logged in

── Phase 1: Creating CKO_DATA object ──────────────────
  Template:
    CKA_CLASS       = CKO_DATA
    CKA_TOKEN       = true
    CKA_LABEL       = "Custom-CKA_APPLICATION-Opq-Obj"
    CKA_APPLICATION = "SmartcryptWrappedBinary"
    CKA_VALUE       = "Custom CKA_APPLICATION Opq Obj"
[OK] CKO_DATA object created — handle = 0x1B445

── Phase 2: Finding object by CKA_LABEL ───────────────
[OK] Object located by label — handle = 0x1B445

── Phase 3: Reading and verifying attributes ───────────
  Retrieved attributes:
    CKA_CLASS                 = 0x0 (0)
    CKA_TOKEN                 = True
    CKA_LABEL                 = "Custom-CKA_APPLICATION-Opq-Obj"
    CKA_APPLICATION           = "Opaque object"
    CKA_VALUE                 = "Custom CKA_APPLICATION Opq Obj"

*** TEST FAILED: CKA_APPLICATION: expected [SmartcryptWrappedBinary]
                 but got [Opaque object] ***
~~~

This is the baseline failure that confirms the YubiHSM PKCS#11 library is not patched and
always uses `CKA_APPLICATION="Opaque object"` for `CKO_DATA` objects.

#### Expected output when the YubiHSM PKCS#11 library **is** patched
~~~
=== YubiHSM CKA_APPLICATION Round-Trip Test ===

[OK] Library loaded — /usr/local/lib/pkcs11/yubihsm_pkcs11.so
[OK] Slot found — slot id = 0
[OK] Session opened and logged in

── Phase 1: Creating CKO_DATA object ──────────────────
  Template:
    CKA_CLASS       = CKO_DATA
    CKA_TOKEN       = true
    CKA_LABEL       = "Custom-CKA_APPLICATION-Opq-Obj"
    CKA_APPLICATION = "SmartcryptWrappedBinary"
    CKA_VALUE       = "Custom CKA_APPLICATION Opq Obj"
[OK] CKO_DATA object created — handle = 0x1537B

── Phase 2: Finding object by CKA_LABEL ───────────────
[OK] Object located by label — handle = 0x1537B

── Phase 3: Reading and verifying attributes ───────────
  Retrieved attributes:
    CKA_CLASS                 = 0x0 (0)
    CKA_TOKEN                 = True
    CKA_LABEL                 = "Custom-CKA_APPLICATION-Opq-Obj"
    CKA_APPLICATION           = "SmartcryptWrappedBinary"
    CKA_VALUE                 = "Custom CKA_APPLICATION Opq Obj"

  ✓ CKA_CLASS       = CKO_DATA
  ✓ CKA_TOKEN       = true
  ✓ CKA_LABEL       = "Custom-CKA_APPLICATION-Opq-Obj"
  ✓ CKA_APPLICATION = "SmartcryptWrappedBinary"
  ✓ CKA_VALUE       = "Custom CKA_APPLICATION Opq Obj"

*** ALL CHECKS PASSED ***
~~~

Exit code is 0 on pass, 1 on assertion failure, 2 on unexpected exception. You can check it
with:

`echo "Exit code: $?"`

## Part 5 — Verifying independently with pkcs11-tool

Because the end-of-run cleanup is disabled (see above), the object stays on the device and
you can confirm the attribute survived using a completely separate PKCS#11 client. This rules
out the possibility that the round-trip only succeeded because of in-session caching:

~~~
pkcs11-tool --module /usr/local/lib/pkcs11/yubihsm_pkcs11.so \
            -l --pin 0001password -O --type data
~~~

With the patched library you will see:

~~~
Data object 86907
  label:          'Custom-CKA_APPLICATION-Opq-Obj'
  application:    'SmartcryptWrappedBinary'
  app_id:         <empty>
  flags:          <empty>
~~~

If `application:` comes back empty or as `Opaque object`, the tag did not persist.

### Two traps to avoid

* **Run the test first, and leave cleanup disabled.** If `DestroyObject` is active, the object
  is gone before `pkcs11-tool` ever runs, and you will only see unrelated objects already on
  the token — each showing `application: ''`. That is not a failure of the patch.

* **Do not use `pkcs11-tool` to _write_ the tag.** Its `--application-label` option does not
  take effect here: creating an object with `--label foo --application-label bar` yields
  `application: 'foo'`, silently substituting the label. `pkcs11-tool` is reliable for
  *reading* `CKA_APPLICATION`, but will mislead you if you try to *set* it. Use this test
  application or `yubihsm-shell` to write it.

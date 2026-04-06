# Part 1 — Install .NET 6 SDK
Ubuntu 20.04 does not ship .NET in its default apt repositories, so you add Microsoft's feed manually.
bash
## Install prerequisites for adding a new apt source
sudo apt-get update

sudo apt-get install -y wget apt-transport-https software-properties-common

## Download and register Microsoft's package signing key
wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb

sudo dpkg -i packages-microsoft-prod.deb

rm packages-microsoft-prod.deb

## Update apt and install the SDK
sudo apt-get update

sudo apt-get install -y dotnet-sdk-6.0

# Verify the installation — you should see something like "6.0.x"
dotnet --version

If you see a version number, .NET is ready.

dotnet publish  ..\src\CrowdSecManager\CrowdSecManager.csproj  --os linux --arch x64 /t:PublishContainer -p:ContainerArchiveOutputPath=..\..\deploy\images\crowdsec-manager.tar.gz

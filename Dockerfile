FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /workspace

ENV DOTNET_NOLOGO=true
ENV ASPNETCORE_URLS=http://0.0.0.0:8090

EXPOSE 8090

ENTRYPOINT ["sh", "-c", "dotnet restore /workspace/src/TestTask.Api/TestTask.Api.csproj && dotnet build /workspace/src/TestTask.Api/TestTask.Api.csproj --no-restore && dotnet run --project /workspace/src/TestTask.Api/TestTask.Api.csproj --no-build --no-restore --urls http://0.0.0.0:8090"]

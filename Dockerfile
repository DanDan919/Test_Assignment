FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get upgrade -y && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

ENV DOTNET_NOLOGO=true
ENV ASPNETCORE_URLS=http://0.0.0.0:8090
ENV DOTNET_CLI_HOME=/tmp
ENV NUGET_PACKAGES=/tmp/nuget
ENV HOME=/tmp

USER $APP_UID

EXPOSE 8090

ENTRYPOINT ["sh", "-c", "dotnet restore /workspace/src/TestTask.Api/TestTask.Api.csproj --locked-mode --artifacts-path /tmp/artifacts && dotnet build /workspace/src/TestTask.Api/TestTask.Api.csproj --no-restore --artifacts-path /tmp/artifacts && dotnet run --project /workspace/src/TestTask.Api/TestTask.Api.csproj --no-build --no-restore --artifacts-path /tmp/artifacts --urls http://0.0.0.0:8090"]

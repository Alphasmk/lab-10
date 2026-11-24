FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Server/Server.csproj Server/
COPY Messaging/Messaging.csproj Messaging/
RUN dotnet restore Server/Server.csproj
COPY Server/ Server/
COPY Messaging/ Messaging/
RUN dotnet publish Server/Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["RunScope.Api/RunScope.Api.csproj", "RunScope.Api/"]
COPY ["RunScope.Core/RunScope.Core.csproj", "RunScope.Core/"]
RUN dotnet restore "RunScope.Api/RunScope.Api.csproj"
COPY . .
WORKDIR "/src/RunScope.Api"
RUN dotnet build "RunScope.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RunScope.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RunScope.Api.dll"]
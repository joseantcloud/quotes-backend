# build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# copy solution and projects
COPY *.sln ./
COPY AzureQuotes.Api/*.csproj ./AzureQuotes.Api/

# restore
RUN dotnet restore

# copy everything and publish
COPY . .
WORKDIR /src/AzureQuotes.Api
RUN dotnet publish -c Release -o /app/publish /p:PublishTrimmed=true

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "AzureQuotes.Api.dll"]

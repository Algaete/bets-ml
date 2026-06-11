FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CornersPrediction.sln ./
COPY CornersPrediction.Domain/CornersPrediction.Domain.csproj CornersPrediction.Domain/
COPY CornersPrediction.Application/CornersPrediction.Application.csproj CornersPrediction.Application/
COPY CornersPrediction.Infrastructure/CornersPrediction.Infrastructure.csproj CornersPrediction.Infrastructure/
COPY CornersPredictionApi/CornersPredictionApi.csproj CornersPredictionApi/
COPY CornersPrediction.Web/CornersPrediction.Web.csproj CornersPrediction.Web/
RUN dotnet restore CornersPrediction.Web/CornersPrediction.Web.csproj

COPY . .
RUN dotnet publish CornersPrediction.Web/CornersPrediction.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CornersPrediction.Web.dll"]
